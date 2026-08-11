using Blast.Core;
using Blast.Simulation;
using UnityEngine;

namespace Blast.Presentation
{
    // 계층: Presentation. 보간된 위치를 Transform 에 반영하고 스프라이트를 뒤집습니다.
    //
    // 이 계층만 Transform 을 만집니다. Simulation 은 위치를 Vector2 상태로만 들고 있고
    // Transform 의 존재를 모릅니다.
    public sealed class PlayerPresenter : MonoBehaviour, IPlayerPresenter
    {
        [SerializeField] private SpriteRenderer _spriteRenderer;

        // 시뮬레이션 충돌 박스와 스프라이트 실제 크기를 Scene 뷰에 겹쳐 그립니다.
        // 둘을 맞추는 방향은 스프라이트 쪽입니다. 시뮬레이션이 진실이고 그림이 따라옵니다.
        [SerializeField] private bool _drawCollisionBox = true;

        private void Reset()
        {
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        public void Render(in PlayerState previous, in PlayerState current, float alpha)
        {
            // 틱 사이를 보간해 프레임레이트와 무관하게 부드럽게 그립니다.
            // 이 보간이 없으면 144fps 화면에서 초당 60번만 위치가 갱신되어 끊겨 보입니다.
            Vector2 position = Vector2.Lerp(previous.Position, current.Position, alpha);

            Vector3 renderPosition = transform.position;
            renderPosition.x = position.x;
            renderPosition.y = position.y;
            transform.position = renderPosition;

            // 방향은 보간하지 않습니다. 이산값이라 마지막 틱 값을 그대로 씁니다.
            if (_spriteRenderer != null)
            {
                _spriteRenderer.flipX = current.FacingDirection < 0;
            }
        }

        private void OnDrawGizmos()
        {
            if (!_drawCollisionBox)
            {
                return;
            }

            // 초록: 시뮬레이션이 실제로 쓰는 충돌 박스입니다.
            // Position 은 캐릭터의 중심이므로 박스도 중심 기준입니다.
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(transform.position, CharacterController2D.BoxSize);

            // 노랑: 스프라이트가 실제로 차지하는 크기입니다.
            // 초록보다 조금 큰 정도가 정상입니다. 팔이나 머리카락은 충돌하지 않아야 합니다.
            // 노랑이 초록보다 작으면 캐릭터가 지면에 잠겨 보입니다.
            if (_spriteRenderer != null)
            {
                Gizmos.color = Color.yellow;
                Bounds bounds = _spriteRenderer.bounds;
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }
        }
    }
}
