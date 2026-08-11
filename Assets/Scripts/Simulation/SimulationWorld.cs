using Blast.Core;

namespace Blast.Simulation
{
    // 계층: Simulation. 한 틱 분량의 월드 상태 전이입니다.
    //
    // 이 타입에는 시간 개념이 없습니다. 벽시계를 읽지 않고 dt 를 인자로 받습니다.
    // 상수를 직접 읽지 않는 이유는 테스트에서 임의 dt 로 돌려보기 위해서입니다.
    //
    // Step 은 사실상 순수 함수입니다. 같은 이전 상태와 같은 입력을 주면 항상 같은
    // 결과가 나와야 합니다. 3주차 재조정이 이 성질 위에 얹힙니다.
    //
    // 지금은 캐릭터 하나로 위임만 하지만, 여기가 여러 플레이어를 정해진 순서로
    // 진행시키는 자리입니다. 그 순서 역시 결정성의 일부라 Dictionary 순회 같은
    // 비결정적 순서에 의존하면 안 됩니다.
    //
    // 주의: 나중에 움직이는 발판 같은 동적 지오메트리를 추가하면, 그 지오메트리의
    // 상태도 스냅샷에 포함되어야 합니다. 재시뮬레이션 시점의 충돌 월드가 원래와
    // 달라지면 결과가 갈라집니다. 지금은 정적 타일맵뿐이라 문제되지 않습니다.
    public static class SimulationWorld
    {
        public static PlayerState Step(
            in PlayerState previous, in InputCommand input, float fixedDt, int groundLayerMask)
        {
            return CharacterController2D.Step(previous, input, fixedDt, groundLayerMask);
        }
    }
}
