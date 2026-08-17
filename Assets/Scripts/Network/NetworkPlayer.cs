using System.Collections.Generic;
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

        // 이 플레이어를 누가 시뮬레이션하는가. 서버가 방송해 전 피어가 같은 값을 봅니다.
        //
        // 월드 단위 설정을 플레이어마다 두는 것이 낭비로 보이지만, 씬에 전용
        // NetworkObject 를 새로 만들면 프리팹과 씬 배선이 늘어납니다. 며칠 뒤 지울
        // 비교용 기능에 그 비용을 쓰지 않습니다. 실제로 플레이어별로 권한이 다른
        // 구성도 존재하므로 위치 자체가 틀린 것도 아닙니다.
        //
        // 쓰기 권한을 Owner 로 열지 않은 것에 주의하세요. 그렇게 하면 클라이언트가
        // 자기 권한을 스스로 올릴 수 있게 됩니다. 모드 전환조차 서버를 거칩니다.
        private readonly NetworkVariable<bool> _clientAuthority = new NetworkVariable<bool>(
            false,
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
            InputCommand received = command.ToCommand();

            // 에지 입력만 덮어쓰지 않고 합칩니다.
            //
            // 입력은 60Hz 로 만들어지는데 NGO 전송 틱은 30Hz 라, 서버는 한 프레임에
            // RPC 를 두 개씩 연달아 받습니다. 그냥 대입하면 뒤엣것이 앞엣것을 덮어쓰고,
            // 그 사이에 틱이 돌지 않았으면 점프가 소비되기 전에 사라집니다.
            // 이동은 연속값이라 마지막 값만 써도 티가 안 나지만 점프는 에지라 증발합니다.
            //
            // 이것은 근본 해결이 아닙니다. 순서와 시각 정보는 여전히 버립니다.
            // 한 틱에 두 번 누른 것은 한 번으로 합쳐지고, 어느 틱의 입력인지도
            // 잃습니다. 제대로 하려면 입력을 틱 번호로 정렬해 보관해야 하고
            // 그것이 3주차 입력 버퍼입니다. 지금은 조작이 불가능해지지 않을 만큼만
            // 막아둡니다.
            if (_hasInput)
            {
                received.JumpPressed |= _latestInput.JumpPressed;
            }

            _latestInput = received;
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

        void IPlayerLink.SubmitState(Vector2 position, sbyte facing)
        {
            SubmitStateRpc(position, facing);
        }

        bool IPlayerLink.IsClientAuthority => _clientAuthority.Value;

        void IPlayerLink.RequestAuthorityMode(bool clientAuthority)
        {
            RequestAuthorityModeRpc(clientAuthority);
        }

        // 클라 권위 모드의 핵심입니다. 서버는 받은 위치를 검증 없이 그대로 발행합니다.
        // 벽 안쪽 좌표를 보내도, 한 틱에 화면 밖으로 이동한 좌표를 보내도 통과합니다.
        // 이것이 클라 권위가 경쟁 게임에서 쓰이지 않는 이유이고, 촬영에서 보여줄
        // 대비이기도 합니다.
        //
        // 서버 권위 모드에서 온 호출은 버립니다. 모드 전환 직후 아직 예전 모드로
        // 돌고 있던 클라이언트의 패킷이 도착할 수 있는데, 그것까지 반영하면
        // 서버가 계산한 위치를 클라이언트가 덮어쓰게 됩니다.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitStateRpc(Vector2 position, sbyte facing)
        {
            if (!_clientAuthority.Value)
            {
                return;
            }

            _position.Value = position;
            _facing.Value = facing;
        }

        // 비교 촬영용 개발 기능이라 누구나 호출할 수 있게 열어둡니다.
        // 실제 게임 기능이라면 이런 것을 클라이언트가 부를 수 있으면 안 됩니다.
        //
        // 한 플레이어만 바꾸면 두 캐릭터가 서로 다른 권한으로 돌아 비교가 성립하지
        // 않습니다. 서버가 전원에게 같은 값을 씁니다.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestAuthorityModeRpc(bool clientAuthority)
        {
            IReadOnlyList<PlayerHandle> players = PlayerRegistry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                NetworkPlayer player = players[i].GameObject.GetComponent<NetworkPlayer>();
                if (player != null)
                {
                    player._clientAuthority.Value = clientAuthority;
                }
            }

            Debug.Log($"[Net] 권한 모드 변경: {(clientAuthority ? "클라 권위" : "서버 권위")}");
        }
    }
}
