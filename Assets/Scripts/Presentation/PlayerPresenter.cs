using Blast.Core;
using Blast.Simulation;
using UnityEngine;

namespace Blast.Presentation
{
    // 계층: Presentation. 보간된 위치를 Transform 에 반영하고, 스프라이트를 뒤집고,
    // 시뮬레이션 상태에서 Animator 파라미터를 파생시킵니다.
    //
    // 이 계층만 Transform 과 Animator 를 만집니다. Simulation 은 위치를 Vector2
    // 상태로만 들고 있고 Transform 도 Animator 도 존재를 모릅니다.
    //
    // 애니메이션은 상태에서 파생시키고 트리거를 쏘지 않습니다. 3주차 재조정이
    // 들어오면 서버 보정을 받을 때마다 과거 틱부터 다시 시뮬레이션되는데,
    // 이벤트 기반이면 되감기 구간의 점프마다 트리거가 다시 발사되어 한 번 뛴
    // 점프에 모션과 효과음이 여러 번 나옵니다. RTT 150ms 에 60Hz 면 매 보정마다
    // 9틱이 재생되므로 상시 발생합니다. 상태에서 파생시키면 되감기가 몇 번이든
    // 최종 결과가 같습니다. 근거는 docs/project_context.md 3번.
    public sealed class PlayerPresenter : MonoBehaviour, IPlayerPresenter
    {
        // 캐릭터 그림입니다. 자식 오브젝트에 두는 것을 전제로 합니다.
        //
        // 루트에 두면 그림을 충돌 박스에 맞춰 위아래로 옮길 수가 없습니다.
        // 루트의 위치는 시뮬레이션이 쓰는 값이라 매 프레임 덮어써지기 때문입니다.
        // 스프라이트 원본에 투명 여백이 있으면 발끝이 박스 바닥과 어긋나는데,
        // 자식이면 그 차이를 자식 위치로 흡수할 수 있습니다.
        [SerializeField] private SpriteRenderer _spriteRenderer;

        // 없어도 동작합니다. Animator Controller 를 붙이기 전에도 이동과 기즈모는
        // 그대로 확인할 수 있어야 하므로 필수 참조로 두지 않습니다.
        // 스프라이트를 교체하는 클립이므로 SpriteRenderer 와 같은 오브젝트에 둡니다.
        [SerializeField] private Animator _animator;

        // 시뮬레이션 충돌 박스와 그림의 실제 크기를 Scene 뷰에 겹쳐 그립니다.
        [SerializeField] private bool _drawCollisionBox = true;

        // Animator 파라미터 이름입니다. 문자열을 매 프레임 넘기면 Animator 가
        // 내부에서 해시를 다시 계산하고 문자열 비교까지 겪습니다. 이름은 컴파일
        // 시점에 정해져 있으므로 해시를 한 번만 구해 둡니다.
        //
        // Animator Controller 쪽 파라미터 이름이 이 셋과 정확히 같아야 합니다.
        // 이름이 틀리면 예외 없이 조용히 무시되므로, 애니메이션이 안 바뀌면
        // 여기부터 확인하세요.
        private static readonly int _speedParameter = Animator.StringToHash("Speed");
        private static readonly int _groundedParameter = Animator.StringToHash("IsGrounded");
        private static readonly int _verticalVelocityParameter = Animator.StringToHash("VerticalVelocity");

        // 원격 캐릭터 속도 추정에 쓰는 시간 창입니다.
        //
        // 원격 위치는 30Hz 로 도착하는데 렌더는 매 프레임 돕니다. 프레임마다
        // 위치 차이를 dt 로 나누면 값이 도착한 프레임에만 큰 수가 나오고 나머지
        // 프레임은 0 이라, 초당 30번 idle 과 run 을 오갑니다. 한 창만큼 변위를
        // 모아서 걸린 시간으로 나누면 그 구간의 평균 속도가 나옵니다.
        //
        // 창이 길수록 안정적이지만 애니메이션 반응이 그만큼 늦습니다. 0.1초면
        // 30Hz 기준 세 번의 갱신이 들어옵니다.
        private const float EstimationWindowSeconds = 0.1f;

        // 추정 수직 속도가 이보다 크면 공중으로 봅니다.
        private const float AirborneSpeedThreshold = 0.5f;

        // 점프 정점에서는 수직 속도가 순간적으로 0 을 지납니다. 그 순간 접지로
        // 판정하면 정점마다 착지 모션이 한 번씩 스칩니다. 중력 30 units/s^2 에서
        // 임계값 0.5 를 통과하는 데 걸리는 시간이 약 33ms 이므로 그보다 길게 잡습니다.
        private const float AirborneHoldSeconds = 0.08f;

        // 합성 루트가 매 프레임 밀어넣습니다. 기즈모 그리기 외에는 쓰지 않습니다.
        // [SerializeField] 가 아니라 런타임 주입인 이유는 값의 출처가 애셋 하나로
        // 유지되어야 하기 때문입니다. 여기 직렬화하면 프리팹마다 다른 박스 크기가
        // 생기고, 기즈모가 시뮬레이션의 진실이 아니라 거짓말을 하게 됩니다.
        private CharacterTuning _tuning = CharacterTuning.Default;

        // 원격 캐릭터 속도 추정 상태입니다. 시뮬레이션 상태가 아니라 화면에서
        // 되읽은 파생값이므로 여기 두는 것이 맞습니다. 이 값은 시뮬레이션으로
        // 되돌아가지 않으며, 틀려도 캐릭터 위치는 달라지지 않습니다.
        private Vector2 _estimatedVelocity;
        private Vector2 _accumulatedDisplacement;
        private float _accumulatedSeconds;
        private Vector2 _lastRenderPosition;
        private bool _hasLastRenderPosition;
        private float _airborneHoldRemaining;

        // 커스텀 인스펙터가 읽는 관측 창구입니다. 상태를 [SerializeField] 로
        // 노출하면 씬과 프리팹에 런타임 값이 저장되므로 프로퍼티로만 내보냅니다.
        public Vector2 EstimatedVelocity => _estimatedVelocity;
        public bool IsAnimationEstimated { get; private set; }
        public bool HasAnimator => _animator != null;
        public bool HasSpriteRenderer => _spriteRenderer != null;

        // 그림이 자식에 있는가. 루트에 있으면 발밑 정렬이 불가능합니다.
        public bool IsSpriteOnChild =>
            _spriteRenderer != null && _spriteRenderer.transform != transform;

        private void Reset()
        {
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            _animator = GetComponentInChildren<Animator>();
        }

        public void SetTuning(in CharacterTuning tuning)
        {
            _tuning = tuning;
        }

        public void Render(
            in PlayerState previous, in PlayerState current, float alpha, bool stateIsSimulatedHere)
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

            // 추정은 시뮬레이션 여부와 무관하게 항상 돌립니다. 권한 모드가 바뀌어
            // 갑자기 추정 경로로 넘어갈 때 창이 비어 있으면 한 창만큼 idle 로 굳습니다.
            UpdateVelocityEstimate(position);
            ApplyAnimatorParameters(current, stateIsSimulatedHere);
        }

        // 렌더 위치의 변위를 시간 창 단위로 모아 평균 속도를 구합니다.
        // GC 할당이 없고 분기 몇 개뿐이라 매 프레임 경로에 두어도 됩니다.
        private void UpdateVelocityEstimate(Vector2 renderPosition)
        {
            if (!_hasLastRenderPosition)
            {
                _lastRenderPosition = renderPosition;
                _hasLastRenderPosition = true;
                return;
            }

            _accumulatedDisplacement += renderPosition - _lastRenderPosition;
            _accumulatedSeconds += Time.deltaTime;
            _lastRenderPosition = renderPosition;

            if (_accumulatedSeconds < EstimationWindowSeconds)
            {
                return;
            }

            _estimatedVelocity = _accumulatedDisplacement / _accumulatedSeconds;
            _accumulatedDisplacement = Vector2.zero;
            _accumulatedSeconds = 0f;
        }

        private void ApplyAnimatorParameters(in PlayerState current, bool stateIsSimulatedHere)
        {
            IsAnimationEstimated = !stateIsSimulatedHere;

            if (_animator == null)
            {
                return;
            }

            float horizontalSpeed;
            float verticalVelocity;
            bool isGrounded;

            if (stateIsSimulatedHere)
            {
                // 이 피어가 직접 돌린 상태라 속도와 접지가 진실입니다.
                horizontalSpeed = Mathf.Abs(current.Velocity.x);
                verticalVelocity = current.Velocity.y;
                isGrounded = current.IsGrounded;

                // 추정 경로로 넘어갈 때 지난 판정이 남아 있으면 안 됩니다.
                _airborneHoldRemaining = 0f;
            }
            else
            {
                // 원격 캐릭터는 위치와 방향만 도착합니다. Velocity 와 IsGrounded 는
                // 스폰 시점 값에 멈춰 있으므로 그대로 쓰면 움직이면서도 idle 입니다.
                //
                // 여기서 전송 필드를 늘리지 않는 것이 이번 이슈의 판단입니다.
                // 이슈 #10 이 기준선 대역폭을 재기 직전이라, 지금 필드를 늘리면
                // 3주차 최적화 전후 비교의 기준이 오염됩니다. 애니메이션은 원래
                // Presentation 파생물이고 시뮬레이션 결과를 바꾸지 않으므로,
                // 화면에서 되읽어 추정하는 쪽이 계층 관점에서도 맞습니다.
                horizontalSpeed = Mathf.Abs(_estimatedVelocity.x);
                verticalVelocity = _estimatedVelocity.y;
                isGrounded = !UpdateAirborneEstimate(verticalVelocity);
            }

            _animator.SetFloat(_speedParameter, horizontalSpeed);
            _animator.SetFloat(_verticalVelocityParameter, verticalVelocity);
            _animator.SetBool(_groundedParameter, isGrounded);
        }

        // 접지 여부는 받지 않으므로 수직 속도로 추정합니다. 정점에서 한 번
        // 튀는 것을 막기 위해 공중 판정을 잠깐 붙잡아 둡니다.
        private bool UpdateAirborneEstimate(float verticalVelocity)
        {
            if (Mathf.Abs(verticalVelocity) > AirborneSpeedThreshold)
            {
                _airborneHoldRemaining = AirborneHoldSeconds;
            }
            else if (_airborneHoldRemaining > 0f)
            {
                _airborneHoldRemaining -= Time.deltaTime;
            }

            return _airborneHoldRemaining > 0f;
        }

        // 그림이 실제로 차지하는 사각형입니다. 에디터 도구와 기즈모가 같은 값을
        // 봐야 "맞췄는데 노란 상자가 안 맞는" 상태가 생기지 않습니다.
        //
        // 주의: 스프라이트 바운드는 잘라낸 픽셀이 아니라 슬라이스한 사각형 전체입니다.
        // 32x32 칸에 캐릭터가 24 픽셀만 차 있으면 나머지 투명 여백도 여기 포함됩니다.
        // 그래서 충돌 박스를 이 값에 1:1 로 맞추면 실제 캐릭터보다 큰 박스가 됩니다.
        public bool TryGetVisualBounds(out Bounds bounds)
        {
            if (_spriteRenderer == null || _spriteRenderer.sprite == null)
            {
                bounds = default;
                return false;
            }

            bounds = _spriteRenderer.bounds;
            return bounds.size.x > 0f && bounds.size.y > 0f;
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
            // 커스텀 인스펙터를 열어두면 애셋의 실제 값이 들어옵니다.
            Gizmos.color = Application.isPlaying ? Color.green : new Color(0f, 0.5f, 0f);
            Gizmos.DrawWireCube(transform.position, _tuning.BoxSize);

            // 노랑: 스프라이트가 차지하는 사각형입니다. 투명 여백을 포함하므로
            // 초록보다 조금 큰 것이 정상입니다.
            if (TryGetVisualBounds(out Bounds bounds))
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }
        }
    }
}
