using Blast.Core;
using UnityEngine.InputSystem;

namespace Blast.Input
{
    // 계층: Input. 키보드를 폴링해 틱 단위 입력으로 변환합니다.
    //
    // 이벤트 콜백이 아니라 폴링인 이유는 고정 틱 시뮬레이션이기 때문입니다.
    // 근거는 docs/project_context.md 2번 참조.
    public sealed class KeyboardInputSource : IPlayerInputSource
    {
        // 아직 어떤 틱도 소비하지 않은 점프 누름입니다.
        private bool _jumpLatched;

        public void Poll()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            // wasPressedThisFrame 은 프레임 단위 신호입니다. 이 프레임에 틱이 돌지
            // 않으면 그대로 사라지므로, 소비될 때까지 여기에 붙잡아 둡니다.
            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                _jumpLatched = true;
            }
        }

        public InputCommand Sample(uint tick)
        {
            sbyte moveX = 0;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                // 키보드는 디지털이라 -1, 0, 1 로 떨어집니다.
                // 나중에 게임패드 아날로그를 붙이면 여기서 고정 단계로 양자화해야
                // 클라이언트와 서버가 같은 입력 비트를 먹습니다.
                if (keyboard.leftArrowKey.isPressed)
                {
                    moveX -= 1;
                }
                if (keyboard.rightArrowKey.isPressed)
                {
                    moveX += 1;
                }
            }

            return new InputCommand
            {
                Tick = tick,
                MoveX = moveX,
                JumpPressed = _jumpLatched
            };
        }

        public void ConsumeEdges()
        {
            _jumpLatched = false;
        }
    }
}
