using Blast.Core;

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
        void Render(in PlayerState previous, in PlayerState current, float alpha);
    }
}
