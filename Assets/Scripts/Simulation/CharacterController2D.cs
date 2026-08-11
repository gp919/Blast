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
        // 튜닝 값은 전부 초당 단위입니다. 틱당 단위로 박으면 틱레이트를 바꿀 때
        // 전부 다시 튜닝해야 합니다. docs/project_context.md 3번 참조.
        public const float MoveSpeedPerSecond = 8f;
        public const float GravityPerSecond = -30f;
        public const float TerminalFallSpeed = -20f;

        // sqrt(2 * 30 * 2.8) 를 반올림한 값입니다. 대략 2.8 유닛 높이까지 오릅니다.
        public const float JumpSpeedPerSecond = 13f;

        // 충돌 박스 크기입니다. Inspector 노출값이 아니라 상수인 이유는
        // 클라이언트와 서버가 반드시 같은 크기를 써야 하기 때문입니다.
        // 직렬화 값으로 두면 씬이나 프리팹에 따라 달라질 수 있고, 그 순간
        // 예측 결과와 서버 결과가 갈라집니다.
        public static readonly Vector2 BoxSize = new Vector2(0.4f, 0.5f);

        // 표면에서 띄워둘 여유입니다. 히트 지점에 정확히 붙이면 부동소수점 오차로
        // 지오메트리 안쪽에 들어가고, 다음 틱 캐스트가 콜라이더 내부에서 시작해
        // 거리 0 에 쓰레기 법선을 받거나 아예 놓칩니다. 끼거나 뚫거나 둘 중 하나입니다.
        public const float SkinWidth = 0.02f;

        // 캐릭터는 항상 스킨만큼 떠 있으므로 접지 판정은 그보다 멀리 봐야 합니다.
        public const float GroundProbeDistance = SkinWidth * 2f;

        // 벽을 바닥으로 오인하지 않기 위한 최소 법선 y 입니다.
        public const float MinGroundNormalY = 0.5f;

        // 지면에서 벗어난 뒤 점프를 허용하는 틱 수입니다. 60Hz 기준 100ms.
        public const byte CoyoteTicks = 6;

        public static PlayerState Step(
            in PlayerState previous, in InputCommand input, float fixedDt, int groundLayerMask)
        {
            PlayerState next = previous;
            next.Tick = input.Tick;

            // --- 1. 속도 결정 ---
            next.Velocity.x = input.MoveX * MoveSpeedPerSecond;

            next.Velocity.y += GravityPerSecond * fixedDt;
            if (next.Velocity.y < TerminalFallSpeed)
            {
                next.Velocity.y = TerminalFallSpeed;
            }

            bool canJump = next.IsGrounded || next.CoyoteTicksRemaining > 0;
            if (input.JumpPressed && canJump)
            {
                // 중력을 적용한 뒤 덮어씁니다. 그래야 점프 틱만 중력이 빠지는
                // 특수 케이스가 생기지 않습니다.
                next.Velocity.y = JumpSpeedPerSecond;
                next.IsGrounded = false;

                // 코요테 창 안에서 두 번 뛰는 것을 막기 위해 즉시 소진합니다.
                next.CoyoteTicksRemaining = 0;
            }

            // --- 2. 수평 이동 ---
            MoveAxis(ref next.Position, ref next.Velocity.x,
                next.Velocity.x * fixedDt, true, groundLayerMask);

            // --- 3. 수직 이동 ---
            MoveAxis(ref next.Position, ref next.Velocity.y,
                next.Velocity.y * fixedDt, false, groundLayerMask);

            // --- 4. 접지 프로브 ---
            // 정지 상태에서는 수직 이동량이 0 이라 수직 캐스트가 지면에 닿지 않습니다.
            // 접지는 위치의 함수로 매 틱 따로 구합니다. 그래야 재조정으로 위치가
            // 보정돼도 다음 틱에 자동으로 정합해집니다.
            next.IsGrounded = ProbeGround(next.Position, groundLayerMask);

            // --- 5. 코요테 타임 ---
            if (next.IsGrounded)
            {
                next.CoyoteTicksRemaining = CoyoteTicks;
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
            ref Vector2 position, ref float velocity, float delta, bool horizontal, int groundLayerMask)
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
                position, BoxSize, 0f, direction, distance, groundLayerMask);

            if (hit.collider != null)
            {
                // Max 가 필수입니다. hit.distance 가 스킨보다 작으면 뺄셈이 음수가 되어
                // 뒤로 밀리고, 매 틱 앞뒤로 떠는 지터가 생깁니다.
                distance = Mathf.Max(0f, hit.distance - SkinWidth);
                velocity = 0f;
            }

            position += direction * distance;
        }

        private static bool ProbeGround(Vector2 position, int groundLayerMask)
        {
            RaycastHit2D hit = Physics2D.BoxCast(
                position, BoxSize, 0f, Vector2.down, GroundProbeDistance, groundLayerMask);

            // TODO: 캐릭터가 지오메트리 안에 박히면 hit.distance 가 0 이고 법선이
            // 영벡터로 나와 접지 판정에 실패합니다. 밀어내기 보정이 필요해지면
            // 여기에 추가합니다. 지금은 스킨 두께로 그 상황 자체를 예방합니다.
            return hit.collider != null && hit.normal.y >= MinGroundNormalY;
        }
    }
}
