using Blast.Core;
using Blast.Simulation;

namespace Blast.Presentation
{
    // 계층: Presentation. 시뮬레이션 상태를 화면에 반영합니다.
    //
    // 참조 방향이 Game -> Presentation 이라 Presentation 은 틱 드라이버를 볼 수 없습니다.
    // 따라서 드라이버가 밀어넣는 push 구조이고, 그 덕에 이 계층은
    // (상태, 알파) -> 화면 의 순수 함수가 됩니다. 상태를 되읽거나 바꾸지 않습니다.
    public interface IPlayerPresenter
    {
        // previous 는 직전 틱, current 는 마지막으로 완료된 틱의 상태입니다.
        // alpha 는 누산기에 남은 시간을 고정 dt 로 나눈 0 이상 1 미만의 값입니다.
        //
        // 둘 사이를 보간하므로 화면은 항상 최대 한 틱만큼 과거를 그립니다.
        // 60Hz 기준 16.7ms 이고, 이것이 틱레이트를 30Hz 로 낮추지 않은 이유 중 하나입니다.
        //
        // stateIsSimulatedHere 는 넘긴 상태의 어느 필드까지 믿어도 되는지를 알립니다.
        // 참이면 이 피어가 직접 돌린 결과라 Velocity 와 IsGrounded 가 진실입니다.
        // 거짓이면 서버에서 받은 위치와 방향만 채워져 있고 나머지 필드는 스폰 시점
        // 값에 멈춰 있습니다. 그 상태로 애니메이션을 파생시키면 원격 캐릭터가
        // 움직이면서도 idle 로 굳으므로, 프리젠터가 다른 경로를 타야 합니다.
        //
        // 상태를 두 종류로 나누는 대신 플래그 하나로 알리는 이유는, 이 구분이
        // 상태의 성질이 아니라 "지금 이 피어에서 누가 시뮬레이션하는가"라는
        // 호출 시점의 사정이기 때문입니다. 권한 모드를 바꾸면 같은 캐릭터가
        // 같은 프레임에 반대쪽이 됩니다.
        void Render(in PlayerState previous, in PlayerState current, float alpha, bool stateIsSimulatedHere);

        // 디버그 표시 전용입니다. Presentation 은 튜닝 값으로 아무것도 계산하지
        // 않습니다. 충돌 박스 기즈모를 시뮬레이션의 실제 크기로 그리기 위해서만
        // 필요하고, 튜닝 애셋은 Game 계층에 있어 여기서 직접 읽을 수 없습니다.
        void SetTuning(in CharacterTuning tuning);
    }
}
