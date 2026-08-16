using Blast.Core;
using UnityEngine;

namespace Blast.Network
{
    // 계층: Network. 스폰된 플레이어 하나에 대한 계층 경계용 핸들입니다.
    //
    // 왜 NetworkPlayer 를 그대로 내보내지 않는가:
    //   NetworkPlayer 는 NetworkBehaviour 를 상속합니다. 상위 계층이 그 타입을
    //   변수로 잡기만 해도 C# 컴파일러는 상속 체인을 해석하려고 Unity.Netcode.Runtime
    //   참조를 요구합니다 (CS0012). 멤버를 하나도 쓰지 않아도 마찬가지입니다.
    //   그러면 NGO 의존이 Blast.Game 까지 새어 나가고, 트랜스포트 교체와 테스트
    //   대역 삽입 경로가 막힙니다.
    //
    //   핸들은 NGO 타입을 상속하지 않는 평범한 클래스라 이 문제가 없습니다.
    //   NGO 가 아는 값을 복사해 담아 두는 것이 전부입니다.
    //
    // 참고: asmdef 참조 방향 검사로는 이 문제가 잡히지 않습니다. 참조 그래프는
    // 정상이었고 깨진 것은 컴파일이었습니다.
    //
    // 식별자만 들던 것에서 상태 운반체로 커졌습니다. 의도한 방향입니다.
    // 3주차 스냅샷 링버퍼도 결국 플레이어별로 붙어야 하고, 그 자리가 여기입니다.
    public sealed class PlayerHandle
    {
        private readonly IPlayerLink _link;

        // 생성자가 internal 입니다. 핸들을 만드는 것은 NGO 스폰 경로뿐이어야 합니다.
        // 상위 계층이 임의로 만들 수 있으면 링크가 없는 핸들이 레지스트리에 섞입니다.
        internal PlayerHandle(
            GameObject gameObject, ulong ownerId, bool isLocalOwner, int spawnIndex, IPlayerLink link)
        {
            GameObject = gameObject;
            OwnerId = ownerId;
            IsLocalOwner = isLocalOwner;
            SpawnIndex = spawnIndex;
            _link = link;
            NetFacing = 1;
        }

        // 상위 계층이 여기서 PlayerPresenter 를 찾습니다. Blast.Network 는
        // Blast.Presentation 을 참조하지 않으므로 프리젠터 타입을 직접 들 수 없습니다.
        public GameObject GameObject { get; }

        // NGO 가 부여한 소유자 식별자입니다.
        public ulong OwnerId { get; }

        // 이 피어가 입력으로 조종하는 대상인가.
        public bool IsLocalOwner { get; }

        // 결정적 스폰 위치를 계산하기 위한 인덱스입니다.
        //
        // 임시 구현입니다. OwnerClientId 는 ulong 이고 희소해서 배열 인덱스로 쓸 수
        // 없다는 것을 project_context.md 8번에 적어뒀습니다. 2 인 로컬 테스트에서는
        // 0 과 1 이 나오므로 지금은 이대로 쓰고, 다중 플레이어 구조를 확정할 때
        // 서버가 부여하는 조밀한 인덱스로 교체합니다.
        public int SpawnIndex { get; }

        // 서버가 발행한 최신 상태입니다. 클라이언트에게는 이것이 유일한 진실입니다.
        // 보간도 예측도 없이 도착한 값을 그대로 그립니다. 이번 이슈의 의도된 결함입니다.
        public Vector2 NetPosition { get; private set; }
        public sbyte NetFacing { get; private set; }

        // 한 번이라도 서버 값을 받았는가. 받기 전에는 결정적 스폰 위치를 씁니다.
        public bool HasNetState { get; private set; }

        // 관측용입니다. 서버가 마지막으로 받은 입력의 클라이언트 틱 번호입니다.
        // 이 값이 멈춰 있으면 입력이 서버에 도달하지 않고 있다는 뜻입니다.
        public uint LastReceivedInputTick => _link.LastReceivedInputTick;

        // 소유 클라이언트에서만 의미가 있습니다. 남의 플레이어에 보내면 NGO 가
        // 소유권 검사에서 거부하고 경고를 남깁니다.
        public void SendInput(in InputCommand command)
        {
            _link.SubmitInput(command);
        }

        public bool TryConsumeInput(out InputCommand command)
        {
            return _link.TryConsumeInput(out command);
        }

        // 서버에서만 호출합니다. 클라이언트가 부르면 NGO 가 쓰기 권한 위반으로 막습니다.
        public void PublishState(in PlayerState state)
        {
            _link.PublishState(state.Position, state.FacingDirection);
        }

        internal void ApplyNetState(Vector2 position, sbyte facing)
        {
            NetPosition = position;
            NetFacing = facing;
            HasNetState = true;
        }
    }
}
