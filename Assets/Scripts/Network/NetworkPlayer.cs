using Blast.Core;
using Unity.Netcode;
using UnityEngine;

namespace Blast.Network
{
    // 계층: Network. 스폰된 플레이어 프리팹에 붙는 컴포넌트입니다.
    //
    // 역할 세 가지입니다.
    //   1. 스폰 시점에 PlayerHandle 을 만들어 레지스트리에 넣는다
    //   2. 소유 클라이언트의 입력을 서버로 나른다 (ServerRpc)
    //   3. 서버가 계산한 위치를 전 피어에 발행한다 (NetworkVariable)
    //
    // 이 컴포넌트는 시뮬레이션을 돌리지 않습니다. 틱 드라이버는 씬에 하나뿐이고,
    // 여기서 각자 Update 를 돌리면 누산기와 틱 카운터가 엔티티마다 갈라집니다.
    // Game/TickDriver 주석 참조.
    public sealed class NetworkPlayer : NetworkBehaviour, IPlayerLink
    {
        // 서버 쓰기, 전원 읽기. 이번 이슈의 나이브함이 여기 다 들어 있습니다.
        // float32 그대로, 델타 압축 없음, 보간 없음. NGO 전송 틱(30Hz)에 실려 나가므로
        // 클라이언트는 초당 30번 갱신되는 위치를 계단처럼 받습니다.
        //
        // 위치와 방향만 보냅니다. PlayerState 를 통째로 보내려면 Core 에 NGO
        // 직렬화 인터페이스를 붙여야 하는데 그건 하지 않습니다. NetworkInputCommand
        // 주석 참조. 그리는 데 실제로 필요한 값도 이 둘뿐입니다.
        private readonly NetworkVariable<Vector2> _position = new NetworkVariable<Vector2>(
            Vector2.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<sbyte> _facing = new NetworkVariable<sbyte>(
            1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private PlayerHandle _handle;

        // 서버가 이 플레이어로부터 마지막으로 받은 입력 하나입니다.
        //
        // 큐가 아니라 슬롯 한 개인 것은 의도입니다. 지터가 끼면 같은 입력이 두 틱
        // 먹히거나 한 틱이 통째로 유실됩니다. 그 증상을 눈으로 본 뒤 3주차에
        // 입력 버퍼를 넣는 순서입니다. 지금 버퍼를 넣으면 왜 필요한지 설명할 근거가
        // 사라집니다.
        private InputCommand _latestInput;
        private bool _hasInput;

        public uint LastReceivedInputTick { get; private set; }

        public override void OnNetworkSpawn()
        {
            // 구독이 핸들 생성보다 먼저여야 합니다. 아래 Register 가 동기적으로
            // 틱 드라이버의 스폰 처리를 부르고, 서버라면 그 안에서 스폰 상태를
            // 발행합니다. 구독이 늦으면 그 첫 발행을 놓칩니다.
            _position.OnValueChanged += HandlePositionChanged;
            _facing.OnValueChanged += HandleFacingChanged;

            // IsOwner 와 OwnerClientId 는 이 시점에 확정되어 있습니다.
            _handle = new PlayerHandle(gameObject, OwnerClientId, IsOwner, (int)OwnerClientId, this);

            // 뒤늦게 접속한 피어는 스폰 메시지에 실려온 현재 값을 여기서 받습니다.
            // 이것이 없으면 이미 이동해 있는 원격 캐릭터가 다음 값 변경 전까지
            // 스폰 위치에 서 있는 것으로 보입니다.
            _handle.ApplyNetState(_position.Value, _facing.Value);

            PlayerRegistry.Register(_handle);
        }

        public override void OnNetworkDespawn()
        {
            _position.OnValueChanged -= HandlePositionChanged;
            _facing.OnValueChanged -= HandleFacingChanged;

            PlayerRegistry.Unregister(_handle);
            _handle = null;
        }

        private void HandlePositionChanged(Vector2 previous, Vector2 current)
        {
            _handle?.ApplyNetState(current, _facing.Value);
        }

        private void HandleFacingChanged(sbyte previous, sbyte current)
        {
            _handle?.ApplyNetState(_position.Value, current);
        }

        void IPlayerLink.SubmitInput(in InputCommand command)
        {
            SubmitInputRpc(NetworkInputCommand.From(command));
        }

        // 통합 RPC 입니다. NGO 2.x 에서 ServerRpc 와 ClientRpc 는 하나로 합쳐졌고,
        // 예전 [ServerRpc(RequireOwnership = true)] 는 아래 두 축으로 나뉘었습니다.
        //   SendTo        - 어디서 실행되는가
        //   InvokePermission - 누가 호출할 수 있는가
        // 이름이 Rpc 로 끝나야 하는 것도 통합 RPC 의 규칙입니다.
        //
        // Owner 권한이 서버 권위의 최소 방어선입니다. 남의 플레이어를 조종하려는
        // 호출은 서버에 도달하기 전에 NGO 가 거부하고 경고를 남깁니다.
        //
        // Host 에서는 이 호출이 네트워크를 타지 않고 즉시 로컬 실행됩니다.
        // 그래서 Host 는 입력 지연이 0 이고 Client 만 RTT 만큼 밀립니다.
        // 리슨 서버의 구조적 비대칭이며, 2주차 C 영상의 소재가 바로 이것입니다.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitInputRpc(NetworkInputCommand command)
        {
            _latestInput = command.ToCommand();
            _hasInput = true;
            LastReceivedInputTick = command.Tick;
        }

        bool IPlayerLink.TryConsumeInput(out InputCommand command)
        {
            if (!_hasInput)
            {
                command = default;
                return false;
            }

            command = _latestInput;

            // 에지 입력만 소비 즉시 지웁니다. 이동은 다음 입력이 올 때까지 이어서
            // 쓰는 것이 자연스럽지만, 점프가 슬롯에 남으면 매 틱 다시 발동해
            // 캐릭터가 공중에 떠버립니다. 그건 관측 가치가 있는 결함이 아니라
            // 그냥 못 쓰게 되는 버그입니다.
            _latestInput.JumpPressed = false;
            return true;
        }

        void IPlayerLink.PublishState(Vector2 position, sbyte facing)
        {
            if (!IsServer)
            {
                return;
            }

            // NetworkVariable 은 값이 같으면 더티로 표시하지 않습니다.
            // 가만히 서 있는 플레이어는 대역폭을 쓰지 않습니다.
            _position.Value = position;
            _facing.Value = facing;
        }
    }
}
