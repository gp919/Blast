using Blast.Core;
using UnityEngine;

namespace Blast.Network
{
    // 계층: Network. 핸들이 실제 NGO 컴포넌트를 부르기 위한 통로입니다.
    //
    // internal 인 것이 요점입니다. PlayerHandle 은 상위 계층에 공개되지만 이 인터페이스는
    // 어셈블리 밖으로 나가지 않습니다. 상위 계층은 핸들의 메서드만 부르고, 그 뒤에
    // NetworkBehaviour 가 있다는 사실 자체를 모릅니다.
    //
    // 델리게이트(Action) 대신 인터페이스인 이유:
    //   핸들 하나가 세 방향(입력 송신, 입력 소비, 상태 발행)으로 통신하는데 델리게이트
    //   세 개를 따로 물리면 어느 하나가 null 인 상태가 생길 수 있습니다.
    //   인터페이스는 구현체가 셋을 전부 제공하는 것을 컴파일 시점에 보장합니다.
    internal interface IPlayerLink
    {
        // 소유 클라이언트가 서버로 입력을 보냅니다.
        void SubmitInput(in InputCommand command);

        // 서버가 이번 틱에 쓸 입력을 꺼냅니다. 아직 한 번도 받지 못했으면 false 입니다.
        bool TryConsumeInput(out InputCommand command);

        // 서버가 시뮬레이션 결과를 전 피어에 발행합니다.
        void PublishState(Vector2 position, sbyte facing);

        // 클라 권위 모드에서 소유 클라이언트가 자기 위치를 서버에 통보합니다.
        // 서버는 검증하지 않고 그대로 발행합니다. 그것이 클라 권위의 정의입니다.
        void SubmitState(Vector2 position, sbyte facing);

        // 이 플레이어를 누가 시뮬레이션하는가. 서버가 방송하는 값입니다.
        bool IsClientAuthority { get; }

        // 권한 모드 변경을 서버에 요청합니다. 비교 촬영용 개발 기능입니다.
        void RequestAuthorityMode(bool clientAuthority);

        // 관측용입니다. 서버가 마지막으로 받은 입력의 클라이언트 틱 번호입니다.
        uint LastReceivedInputTick { get; }
    }
}
