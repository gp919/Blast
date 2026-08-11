namespace Blast.Core
{
    // 계층: Core. 고정 틱 시뮬레이션의 시간 상수입니다.
    public static class SimulationConstants
    {
        // 확정값입니다. 근거는 docs/project_context.md 2번과 6번 참조.
        // 근접 전투 판정과 랙 보상 rewind 의 시간 해상도가 이 값에 직접 묶입니다.
        public const int TickRate = 60;

        // 16.667ms. float 로 정확히 표현되지 않으므로(0.016666668) 이 값을 계속 더해
        // 누적한 시간을 상태 식별자로 쓰면 안 됩니다. 틱은 정수 카운터가 진실입니다.
        public const float FixedDeltaTime = 1f / TickRate;

        // 한 프레임에 돌릴 수 있는 최대 틱 수입니다.
        // 프레임이 길어지면 밀린 틱이 늘고, 그걸 다 돌리면 다음 프레임이 더 길어지는
        // death spiral 이 생깁니다. 상한에 걸리면 남은 누산 시간은 버립니다.
        public const int MaxTicksPerFrame = 5;

        // 누산기에 더하기 전에 프레임 시간을 자르는 상한입니다.
        // 에디터에서 브레이크포인트에 걸리거나 씬 로드로 프레임이 튀었을 때
        // 수백 틱이 한꺼번에 밀려드는 것을 막습니다.
        public const float MaxFrameTime = 0.25f;
    }
}
