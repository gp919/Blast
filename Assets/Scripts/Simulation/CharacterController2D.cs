using Blast.Core;
using UnityEngine;

namespace Blast.Simulation
{
    // 계층: Simulation. BoxCast 기반 커스텀 kinematic 캐릭터 컨트롤러입니다.
    //
    // 플레이어에게는 Collider2D 가 붙어 있지 않습니다. 충돌 박스는 PlayerState.Position
    // 에서 파생됩니다. 콜라이더를 붙이면 캐스트 전에 그것을 시뮬레이션 위치로 옮겨야
    // 하는데, 그러려면 Transform 을 써야 하고 이 계층은 그것이 금지입니다.
    // 재조정으로 한 프레임에 여러 틱을 재생할 때 중간 상태가 물리 월드에 새는 문제도
    // 같은 이유로 사라집니다. 부수 효과로 자기 자신을 때리는 일이 불가능해집니다.
    //
    // 틱당 순서는 이렇습니다.
    //   1. 속도 결정 (입력, 중력, 점프)
    //   2. 수평 이동과 캐스트
    //   3. 수직 이동과 캐스트
    //   4. 접지 프로브
    //   5. 코요테 타임 갱신
    public static class CharacterController2D
    {
        // 튜닝 값은 상수가 아니라 인자로 받습니다. 상수로 박아두면 이동 속도나
        // 점프 속도를 만질 때마다 Play 중지, 재컴파일, 재실행을 반복해야 합니다.
        // 값의 출처와 편집 수단은 CharacterTuning 주석을 참조하세요.
        public static PlayerState Step(
            in PlayerState previous, in InputCommand input, in CharacterTuning tuning,
            float fixedDt, int groundLayerMask)
        {
            PlayerState next = previous;
            next.Tick = input.Tick;

            // --- 1. 속도 결정 ---
            next.Velocity.x = input.MoveX * tuning.MoveSpeedPerSecond;

            next.Velocity.y += tuning.GravityPerSecond * fixedDt;
            if (next.Velocity.y < tuning.TerminalFallSpeed)
            {
                next.Velocity.y = tuning.TerminalFallSpeed;
            }

            bool canJump = next.IsGrounded || next.CoyoteTicksRemaining > 0;
            if (input.JumpPressed && canJump)
            {
                // 중력을 적용한 뒤 덮어씁니다. 그래야 점프 틱만 중력이 빠지는
                // 특수 케이스가 생기지 않습니다.
                next.Velocity.y = tuning.JumpSpeedPerSecond;
                next.IsGrounded = false;

                // 코요테 창 안에서 두 번 뛰는 것을 막기 위해 즉시 소진합니다.
                next.CoyoteTicksRemaining = 0;
            }

            // --- 2. 수평 이동 ---
            MoveAxis(ref next.Position, ref next.Velocity.x,
                next.Velocity.x * fixedDt, true, tuning, groundLayerMask);

            // --- 3. 수직 이동 ---
            MoveAxis(ref next.Position, ref next.Velocity.y,
                next.Velocity.y * fixedDt, false, tuning, groundLayerMask);

            // --- 4. 접지 프로브 ---
            // 정지 상태에서는 수직 이동량이 0 이라 수직 캐스트가 지면에 닿지 않습니다.
            // 접지는 위치의 함수로 매 틱 따로 구합니다. 그래야 재조정으로 위치가
            // 보정돼도 다음 틱에 자동으로 정합해집니다.
            next.IsGrounded = ProbeGround(next.Position, tuning, groundLayerMask);

            // --- 5. 코요테 타임 ---
            if (next.IsGrounded)
            {
                next.CoyoteTicksRemaining = tuning.CoyoteTicks;
            }
            else if (next.CoyoteTicksRemaining > 0)
            {
                next.CoyoteTicksRemaining--;
            }

            if (input.MoveX != 0)
            {
                next.FacingDirection = input.MoveX > 0 ? (sbyte)1 : (sbyte)-1;
            }

            return next;
        }

        // 한 축으로 스윕하고 충돌하면 표면 앞에서 멈춥니다.
        // 맞으면 남은 이동량은 버리고 해당 축 속도를 0 으로 만듭니다.
        // 속도를 남겨두면 벽에 붙어 있는 동안 계속 쌓여 재조정 때 갈라집니다.
        private static void MoveAxis(
            ref Vector2 position, ref float velocity, float delta, bool horizontal,
            in CharacterTuning tuning, int groundLayerMask)
        {
            if (delta == 0f)
            {
                return;
            }

            Vector2 direction;
            if (horizontal)
            {
                direction = delta > 0f ? Vector2.right : Vector2.left;
            }
            else
            {
                direction = delta > 0f ? Vector2.up : Vector2.down;
            }

            float distance = Mathf.Abs(delta);

            RaycastHit2D hit = Physics2D.BoxCast(
                position, tuning.BoxSize, 0f, direction, distance, groundLayerMask);

            if (hit.collider != null)
            {
                // Max 가 필수입니다. hit.distance 가 스킨보다 작으면 뺄셈이 음수가 되어
                // 뒤로 밀리고, 매 틱 앞뒤로 떠는 지터가 생깁니다.
                distance = Mathf.Max(0f, hit.distance - tuning.SkinWidth);
                velocity = 0f;
            }

            position += direction * distance;
        }

        private static bool ProbeGround(
            Vector2 position, in CharacterTuning tuning, int groundLayerMask)
        {
            RaycastHit2D hit = Physics2D.BoxCast(
                position, tuning.BoxSize, 0f, Vector2.down,
                tuning.GroundProbeDistance, groundLayerMask);

            // TODO: 캐릭터가 지오메트리 안에 박히면 hit.distance 가 0 이고 법선이
            // 영벡터로 나와 접지 판정에 실패합니다. 밀어내기 보정이 필요해지면
            // 여기에 추가합니다. 지금은 스킨 두께로 그 상황 자체를 예방합니다.
            return hit.collider != null && hit.normal.y >= tuning.MinGroundNormalY;
        }
    }
}
