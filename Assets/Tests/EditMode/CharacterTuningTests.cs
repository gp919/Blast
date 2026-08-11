using Blast.Core;
using Blast.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Blast.Tests
{
    // 튜닝 값이 실제로 시뮬레이션에 반영되는지 검증합니다.
    //
    // 이 테스트가 성립한다는 사실 자체가 리팩터링의 목적입니다. 튜닝이 const 였을
    // 때는 "점프 속도 13 이면 2.8 유닛까지 오른다" 를 코드를 고치지 않고 확인할
    // 방법이 없었습니다.
    //
    // 애셋(CharacterTuningAsset)은 Game 어셈블리에 있고 테스트는 그것을 참조하지
    // 않습니다. 참조하려는 순간 tools/check-layering.ps1 이 막습니다.
    // 검증 대상은 순수 구조체이고, 애셋은 그 값을 채워주는 껍데기일 뿐입니다.
    public sealed class CharacterTuningTests
    {
        private const int NoCollisionMask = 0;
        private const float Dt = SimulationConstants.FixedDeltaTime;

        // 접지 상태에서 한 번 점프시키고 도달 최고점을 돌려줍니다.
        // 충돌 마스크가 0 이라 씬 없이 헤드리스로 돕니다.
        private static float SimulateJumpApex(in CharacterTuning tuning)
        {
            PlayerState state = new PlayerState
            {
                Tick = 0,
                Position = Vector2.zero,
                Velocity = Vector2.zero,

                // 캐스트가 아무것도 맞히지 않으므로 접지를 직접 세워줍니다.
                // 이것이 없으면 점프 자체가 발동하지 않습니다.
                IsGrounded = true,
                CoyoteTicksRemaining = 0,
                FacingDirection = 1
            };

            float apex = state.Position.y;

            // 상승 구간을 충분히 덮는 틱 수입니다. 최고점 이후는 낙하라 결과에
            // 영향을 주지 않습니다.
            for (uint tick = 0; tick < 300; tick++)
            {
                InputCommand input = new InputCommand
                {
                    Tick = tick,
                    MoveX = 0,

                    // 첫 틱에만 누릅니다. 코요테 잔여가 점프 시 0 으로 소진되므로
                    // 계속 눌러도 두 번 뛰지는 않지만, 의도를 분명히 둡니다.
                    JumpPressed = tick == 0
                };

                state = SimulationWorld.Step(state, input, tuning, Dt, NoCollisionMask);

                if (state.Position.y > apex)
                {
                    apex = state.Position.y;
                }
            }

            return apex;
        }

        // 이산 적분의 최고점은 해석해 v^2 / (2g) 와 정확히 일치하지 않습니다.
        // semi-implicit Euler 는 속도를 갱신한 뒤 위치에 더하므로 매 틱 일정량을
        // 더 올라가고, 누적 오차는 정확히 v * dt / 2 입니다.
        // 기본 튜닝 기준 2.81667 이 아니라 2.925 가 나옵니다.
        //
        // 허용 오차를 한 틱 이동량으로 잡으면 이 오버슛을 덮으면서도, 중력이나
        // 점프 속도가 엉뚱하게 적용되는 경우는 걸러냅니다.
        private static void AssertApexMatchesAnalytic(in CharacterTuning tuning)
        {
            float analytic = (tuning.JumpSpeedPerSecond * tuning.JumpSpeedPerSecond)
                / (2f * Mathf.Abs(tuning.GravityPerSecond));

            float apex = SimulateJumpApex(tuning);
            float maxTravelPerTick = tuning.JumpSpeedPerSecond * Dt;

            Assert.That(apex, Is.EqualTo(analytic).Within(maxTravelPerTick + 1e-4f),
                $"점프 최고점이 해석해와 한 틱 이상 어긋납니다. 실측 {apex}, 해석해 {analytic}");
        }

        [Test]
        public void Jump_DefaultTuning_ApexMatchesAnalyticHeight()
        {
            AssertApexMatchesAnalytic(CharacterTuning.Default);
        }

        // 핵심 검증입니다. 주입한 값이 무시되고 옛 상수가 쓰이면 최고점이 그대로라
        // 여기서 터집니다.
        [Test]
        public void Jump_InjectedTuning_ChangesApexAccordingly()
        {
            CharacterTuning baseline = CharacterTuning.Default;
            CharacterTuning stronger = new CharacterTuning(
                baseline.MoveSpeedPerSecond,
                baseline.GravityPerSecond,
                baseline.TerminalFallSpeed,

                // 높이는 속도의 제곱에 비례하므로 두 배면 대략 네 배 오릅니다.
                baseline.JumpSpeedPerSecond * 2f,
                baseline.BoxSize,
                baseline.SkinWidth,
                baseline.MinGroundNormalY,
                baseline.CoyoteTicks);

            float baselineApex = SimulateJumpApex(baseline);
            float strongerApex = SimulateJumpApex(stronger);

            Assert.That(strongerApex, Is.GreaterThan(baselineApex),
                "점프 속도를 올렸는데 최고점이 그대로라면 튜닝이 주입되지 않은 것입니다.");

            AssertApexMatchesAnalytic(stronger);
        }

        [Test]
        public void Tuning_MoveSpeed_ScalesHorizontalVelocity()
        {
            CharacterTuning baseline = CharacterTuning.Default;
            PlayerState state = new PlayerState { FacingDirection = 1 };
            InputCommand input = new InputCommand { Tick = 0, MoveX = 1, JumpPressed = false };

            PlayerState next = SimulationWorld.Step(
                state, input, baseline, Dt, NoCollisionMask);

            Assert.That(next.Velocity.x, Is.EqualTo(baseline.MoveSpeedPerSecond).Within(1e-5f));
        }

        // 접지 프로브 거리는 독립 필드가 아니라 스킨에서 파생됩니다.
        // 따로 두면 스킨을 바꿨을 때 조용히 어긋나 접지가 깨집니다.
        [Test]
        public void Tuning_GroundProbeDistance_DerivesFromSkinWidth()
        {
            CharacterTuning tuning = new CharacterTuning(
                8f, -30f, -20f, 13f, new Vector2(0.4f, 0.5f), 0.05f, 0.5f, 6);

            Assert.That(tuning.GroundProbeDistance, Is.EqualTo(0.1f).Within(1e-6f));
        }
    }
}
