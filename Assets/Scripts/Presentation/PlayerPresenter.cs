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

        // 합성 루트가 매 프레임 밀어넣습니다. 기즈모 그리기 외에는 쓰지 않습니다.
        // [SerializeField] 가 아니라 런타임 주입인 이유는 값의 출처가 애셋 하나로
        // 유지되어야 하기 때문입니다. 여기 직렬화하면 프리팹마다 다른 박스 크기가
        // 생기고, 기즈모가 시뮬레이션의 진실이 아니라 거짓말을 하게 됩니다.
        //
        // Play 중이 아니면 주입해줄 드라이버가 없으므로 기본값으로 그립니다.
        // 애셋에서 박스 크기를 바꿨다면 편집 중 기즈모는 그것을 반영하지 못합니다.
        // Play 를 눌러 확인하세요.
        private CharacterTuning _tuning = CharacterTuning.Default;

        private void Reset()
        {
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        public void SetTuning(in CharacterTuning tuning)
        {
            _tuning = tuning;
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
            //
            // 편집 중에는 튜닝을 주입해줄 드라이버가 없어 기본값을 그립니다.
            // 실제 값과 다를 수 있으므로 색을 어둡게 해서 구분합니다.
            Gizmos.color = Application.isPlaying ? Color.green : new Color(0f, 0.5f, 0f);
            Gizmos.DrawWireCube(transform.position, _tuning.BoxSize);

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
