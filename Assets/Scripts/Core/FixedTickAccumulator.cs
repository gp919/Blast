namespace Blast.Core
{
    // 계층: Core. 고정 틱 누산기입니다.
    //
    // 벽시계 시간을 읽지 않습니다. 프레임 시간을 받기만 하므로 순수하고,
    // 테스트에서 임의의 프레임 시퀀스를 직접 먹여 검증할 수 있습니다.
    // Time.deltaTime 을 읽는 것은 호출자인 합성 루트의 몫입니다.
    //
    // 불변식: Advance 가 반환한 뒤 남은 시간은 항상 한 틱 미만입니다.
    // 따라서 Alpha 는 0 이상 1 미만입니다.
    public struct FixedTickAccumulator
    {
        private float _accumulator;

        // 다음 틱까지의 진행률입니다. 렌더 보간에 씁니다.
        public float Alpha => _accumulator / SimulationConstants.FixedDeltaTime;

        // 테스트와 디버그 오버레이용입니다.
        public float Remainder => _accumulator;

        // 이번 프레임에 돌려야 할 틱 수를 반환합니다.
        public int Advance(float frameTime)
        {
            // 음수 프레임 시간은 들어올 일이 없지만, 들어오면 누산기가 음수가 되어
            // Alpha 가 음수로 나가므로 막습니다.
            if (frameTime < 0f)
            {
                frameTime = 0f;
            }

            // 에디터에서 브레이크포인트에 걸리거나 씬 로드로 프레임이 튀었을 때
            // 수백 틱이 한꺼번에 밀려드는 것을 막습니다.
            if (frameTime > SimulationConstants.MaxFrameTime)
            {
                frameTime = SimulationConstants.MaxFrameTime;
            }

            _accumulator += frameTime;

            // 반복 감산입니다. (int)(누산기 / dt) 로 한 번에 구하면 누산기가 정확히
            // 2*dt 일 때 float 나눗셈이 1.9999999 를 뱉어 1틱만 도는 경우가 생깁니다.
            int ticks = 0;
            while (_accumulator >= SimulationConstants.FixedDeltaTime
                   && ticks < SimulationConstants.MaxTicksPerFrame)
            {
                _accumulator -= SimulationConstants.FixedDeltaTime;
                ticks++;
            }

            // 상한에 걸렸다면 남은 시간을 버립니다.
            // 루프 횟수만 제한하고 누산기를 그대로 두면 잔여 시간이 프레임마다
            // 쌓여서 상한이 무의미해지고 결국 death spiral 이 됩니다.
            // 서버는 계속 틱을 돌리므로, 클라이언트는 시간 부채를 갚으며 뒤처지는 것보다
            // 건너뛰는 쪽이 맞습니다.
            if (ticks == SimulationConstants.MaxTicksPerFrame
                && _accumulator >= SimulationConstants.FixedDeltaTime)
            {
                _accumulator = 0f;
            }

            return ticks;
        }
    }
}
