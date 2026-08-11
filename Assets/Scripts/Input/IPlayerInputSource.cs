using Blast.Core;

namespace Blast.Input
{
    // 계층: Input. 틱 드라이버가 입력을 끌어가는 통로입니다.
    //
    // 호출 순서가 계약의 일부입니다.
    //   1. 매 프레임 Poll 한 번. 에지 입력을 래치합니다
    //   2. 틱마다 Sample 한 번
    //   3. 그 틱이 입력을 소비했으면 ConsumeEdges
    //
    // Poll 과 ConsumeEdges 가 나뉜 이유는 프레임과 틱이 1대1이 아니기 때문입니다.
    // 틱이 0번 도는 프레임에 눌린 점프는 래치에 남아 다음 틱이 가져가야 하고,
    // 한 프레임에 틱이 여러 번 돌면 그 누름은 첫 틱만 소비해야 합니다.
    public interface IPlayerInputSource
    {
        void Poll();

        InputCommand Sample(uint tick);

        void ConsumeEdges();
    }
}
