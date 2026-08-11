using Blast.Core;
using NUnit.Framework;

namespace Blast.Tests
{
    // 고정 틱 누산기가 프레임 분할과 무관하게 같은 양의 시뮬레이션 시간을
    // 만들어내는지 검증합니다. 육안으로는 절대 잡히지 않는 종류의 오류입니다.
    public sealed class FixedTickAccumulatorTests
    {
        private const float Dt = SimulationConstants.FixedDeltaTime;

        // 한 틱보다 훨씬 작은 허용 오차입니다. 틱 하나를 잃거나 더 만들면
        // 16.7ms 차이가 나므로 반드시 실패합니다.
        private const float TimeTolerance = 0.005f;

        private static int RunFrames(ref FixedTickAccumulator accumulator, float frameTime, int frameCount)
        {
            int total = 0;
            for (int i = 0; i < frameCount; i++)
            {
                total += accumulator.Advance(frameTime);
            }
            return total;
        }

        // 프레임 분할이 달라도 시간은 생기지도 사라지지도 않아야 합니다.
        // 돌린 틱의 시간과 누산기에 남은 시간의 합이 먹인 총 시간과 같아야 합니다.
        // 프레임당 틱 수가 상한(5)을 넘지 않는 분할만 사용합니다.
        [TestCase(60)]
        [TestCase(30)]
        [TestCase(20)]
        [TestCase(15)]
        public void Advance_WithoutClamping_ConservesTime(int frameCount)
        {
            FixedTickAccumulator accumulator = default;
            float frameTime = 1f / frameCount;

            int ticks = RunFrames(ref accumulator, frameTime, frameCount);
            float simulated = ticks * Dt + accumulator.Remainder;

            Assert.That(simulated, Is.EqualTo(1f).Within(TimeTolerance),
                "돌린 틱 시간과 남은 시간의 합이 먹인 총 시간과 일치해야 합니다.");
        }

        // 1초를 어떻게 쪼개 먹이든 총 틱 수는 같아야 합니다.
        // 잔여 시간이 한 틱 미만이므로 1틱 차이까지만 허용됩니다.
        [TestCase(60)]
        [TestCase(30)]
        [TestCase(20)]
        [TestCase(15)]
        public void Advance_DifferentFrameChunkings_ProduceSameTickCount(int frameCount)
        {
            FixedTickAccumulator accumulator = default;

            int ticks = RunFrames(ref accumulator, 1f / frameCount, frameCount);

            Assert.That(ticks, Is.EqualTo(SimulationConstants.TickRate).Within(1),
                "프레임레이트가 달라도 1초에 도는 틱 수는 같아야 합니다.");
        }

        // 긴 히치가 들어와도 한 프레임에 도는 틱 수는 상한을 넘지 않고,
        // 남은 시간은 버려져야 합니다. 버리지 않으면 잔여 시간이 계속 쌓여
        // 상한이 무의미해지고 death spiral 이 됩니다.
        [Test]
        public void Advance_LongHitch_ClampsToMaxTicksAndDiscardsLeftover()
        {
            FixedTickAccumulator accumulator = default;

            int ticks = accumulator.Advance(5f);

            Assert.That(ticks, Is.EqualTo(SimulationConstants.MaxTicksPerFrame),
                "히치가 아무리 길어도 프레임당 틱 수는 상한까지입니다.");
            Assert.That(accumulator.Remainder, Is.LessThan(Dt),
                "상한에 걸렸다면 잔여 시간을 버려야 합니다.");
        }

        // 히치가 반복돼도 부채가 누적되지 않아야 합니다.
        [Test]
        public void Advance_RepeatedHitches_DoNotAccumulateDebt()
        {
            FixedTickAccumulator accumulator = default;

            for (int i = 0; i < 20; i++)
            {
                accumulator.Advance(1f);
                Assert.That(accumulator.Remainder, Is.LessThan(Dt),
                    "반복된 히치 후에도 잔여 시간은 한 틱 미만이어야 합니다.");
            }
        }

        // 렌더 보간에 그대로 쓰이는 값이라 범위를 벗어나면 화면이 튑니다.
        [Test]
        public void Alpha_StaysInUnitRange_AcrossIrregularFrames()
        {
            FixedTickAccumulator accumulator = default;
            float[] frameTimes = { 0.001f, 0.016f, 0.0333f, 0.4f, 0.008f, 0.05f, 0f, 0.25f, 0.017f };

            foreach (float frameTime in frameTimes)
            {
                accumulator.Advance(frameTime);

                Assert.That(accumulator.Alpha, Is.GreaterThanOrEqualTo(0f));
                Assert.That(accumulator.Alpha, Is.LessThan(1f));
            }
        }

        [Test]
        public void Advance_NegativeFrameTime_DoesNotRewind()
        {
            FixedTickAccumulator accumulator = default;
            accumulator.Advance(0.01f);
            float before = accumulator.Remainder;

            int ticks = accumulator.Advance(-1f);

            Assert.That(ticks, Is.Zero);
            Assert.That(accumulator.Remainder, Is.EqualTo(before));
        }

        // 정확히 두 틱 분량이 쌓이면 두 틱이 나와야 합니다.
        // (int)(누산기 / dt) 로 구현하면 float 나눗셈이 1.9999999 를 뱉어 1틱만 도는데,
        // 이런 오류는 실행 화면에서 절대 보이지 않습니다.
        [Test]
        public void Advance_ExactlyTwoTicksWorth_ProducesTwoTicks()
        {
            FixedTickAccumulator accumulator = default;

            int ticks = accumulator.Advance(Dt * 2f);

            Assert.That(ticks, Is.EqualTo(2));
        }
    }
}
