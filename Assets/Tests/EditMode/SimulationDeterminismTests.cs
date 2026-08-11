using Blast.Core;
using Blast.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Blast.Tests
{
    // 시뮬레이션이 결정적인지, 그리고 프레임레이트와 무관한 결과를 내는지 검증합니다.
    //
    // 충돌 마스크를 0 으로 주면 어떤 레이어도 맞지 않아 자유낙하만 일어납니다.
    // 덕분에 씬이나 타일맵 없이 헤드리스로 돌아갑니다. 고정 틱 루프의 정확성을
    // 확인하는 데는 자유낙하만으로 충분합니다.
    public sealed class SimulationDeterminismTests
    {
        private const int NoCollisionMask = 0;
        private const float Dt = SimulationConstants.FixedDeltaTime;

        // 애셋 없이 코드로 튜닝을 주입합니다. 테스트가 프로젝트 애셋의 현재 값에
        // 의존하면, 감각 조정하려고 점프 속도를 만졌을 뿐인데 테스트가 빨개집니다.
        private static readonly CharacterTuning Tuning = CharacterTuning.Default;

        private static PlayerState CreateInitial()
        {
            return new PlayerState
            {
                Tick = 0,
                Position = new Vector2(0f, 10f),
                Velocity = Vector2.zero,
                IsGrounded = false,
                CoyoteTicksRemaining = 0,
                FacingDirection = 1
            };
        }

        private static InputCommand MakeInput(uint tick)
        {
            return new InputCommand
            {
                Tick = tick,
                MoveX = (sbyte)(tick % 3 == 0 ? 1 : -1),
                JumpPressed = tick % 17 == 0
            };
        }

        private static PlayerState RunTicks(int tickCount)
        {
            PlayerState state = CreateInitial();
            for (uint tick = 0; tick < tickCount; tick++)
            {
                state = SimulationWorld.Step(state, MakeInput(tick), Tuning, Dt, NoCollisionMask);
            }
            return state;
        }

        // 프레임 시퀀스를 실제 드라이버와 같은 방식으로 먹여 1초간 시뮬레이션합니다.
        private static (int Ticks, PlayerState State) DriveForOneSecond(int frameCount)
        {
            FixedTickAccumulator accumulator = default;
            PlayerState state = CreateInitial();
            uint tick = 0;
            float frameTime = 1f / frameCount;

            for (int frame = 0; frame < frameCount; frame++)
            {
                int ticksThisFrame = accumulator.Advance(frameTime);
                for (int i = 0; i < ticksThisFrame; i++)
                {
                    state = SimulationWorld.Step(state, MakeInput(tick), Tuning, Dt, NoCollisionMask);
                    tick++;
                }
            }

            return ((int)tick, state);
        }

        // 같은 입력 시퀀스를 두 번 돌리면 비트 단위로 같아야 합니다.
        // 어긋나면 어딘가에 정적 상태나 프레임 종속이 숨어 있다는 뜻이고,
        // 그 상태로는 3주차 재조정이 성립하지 않습니다.
        [Test]
        public void Step_SameInputSequence_ProducesBitIdenticalState()
        {
            PlayerState first = RunTicks(120);
            PlayerState second = RunTicks(120);

            Assert.That(second.Position.x, Is.EqualTo(first.Position.x));
            Assert.That(second.Position.y, Is.EqualTo(first.Position.y));
            Assert.That(second.Velocity.x, Is.EqualTo(first.Velocity.x));
            Assert.That(second.Velocity.y, Is.EqualTo(first.Velocity.y));
            Assert.That(second.Tick, Is.EqualTo(first.Tick));
            Assert.That(second.IsGrounded, Is.EqualTo(first.IsGrounded));
            Assert.That(second.CoyoteTicksRemaining, Is.EqualTo(first.CoyoteTicksRemaining));
            Assert.That(second.FacingDirection, Is.EqualTo(first.FacingDirection));
        }

        // 틱 수가 같으면 프레임 분할과 무관하게 결과가 같아야 합니다.
        // Step 이 dt 를 인자로 받는 순수 함수라 성립하는 성질입니다.
        [Test]
        public void Step_SameTickCount_IsIndependentOfHowTicksWereBatched()
        {
            PlayerState direct = RunTicks(60);
            (int Ticks, PlayerState State) driven = DriveForOneSecond(60);

            Assume.That(driven.Ticks, Is.EqualTo(60), "이 테스트는 정확히 60틱이 돌았을 때만 유효합니다.");
            Assert.That(driven.State.Position.y, Is.EqualTo(direct.Position.y));
            Assert.That(driven.State.Velocity.y, Is.EqualTo(direct.Velocity.y));
        }

        [Test]
        public void Drive_SameWallTime_TickCountsAgreeWithinOne()
        {
            int at60 = DriveForOneSecond(60).Ticks;
            int at30 = DriveForOneSecond(30).Ticks;
            int at15 = DriveForOneSecond(15).Ticks;

            Assert.That(at30, Is.EqualTo(at60).Within(1));
            Assert.That(at15, Is.EqualTo(at60).Within(1));
        }

        // 이번 단계의 핵심 검증입니다. 30fps 와 60fps 로 같은 벽시계 시간을 돌렸을 때
        // 낙하 거리가 같아야 합니다. 틱 수가 최대 1 다를 수 있으므로 허용 오차는
        // 한 틱 동안 떨어질 수 있는 최대 거리입니다.
        [Test]
        public void Drive_SameWallTime_FallDistanceMatchesWithinOneTick()
        {
            PlayerState at60 = DriveForOneSecond(60).State;
            PlayerState at30 = DriveForOneSecond(30).State;

            float maxTravelPerTick = Mathf.Abs(Tuning.TerminalFallSpeed) * Dt;
            float difference = Mathf.Abs(at30.Position.y - at60.Position.y);

            Assert.That(difference, Is.LessThanOrEqualTo(maxTravelPerTick + 1e-4f),
                "프레임레이트가 낙하 거리에 영향을 주면 고정 틱 루프가 잘못된 것입니다.");
        }
    }
}
