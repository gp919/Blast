using Blast.Core;
using Blast.Input;
using Blast.Presentation;
using Blast.Simulation;
using UnityEngine;

namespace Blast.Game
{
    // 계층: Game (합성 루트). 고정 틱 누산기 루프입니다.
    //
    // 왜 각 MonoBehaviour 가 아니라 여기 한 곳에서 도는가:
    //   1. MonoBehaviour 간 Update 호출 순서는 보장되지 않습니다. 엔티티마다
    //      누산기를 두면 그 순서가 시뮬레이션 결과에 섞여 결정성이 깨집니다
    //   2. 틱 카운터가 하나여야 "틱 N 의 월드 상태"라는 개념이 성립합니다
    //   3. 재조정은 보정 틱부터 현재 틱까지 다시 돌리는 루프인데, 누산기가
    //      엔티티마다 흩어져 있으면 그 루프를 표현할 방법이 없습니다
    //
    // 드라이버가 입력 폴링까지 직접 소유하므로 Script Execution Order 설정이
    // 필요 없습니다. 순서가 이 함수 안에 그대로 드러납니다.
    public sealed class TickDriver : MonoBehaviour
    {
        [SerializeField] private PlayerPresenter _presenter;
        [SerializeField] private Vector2 _spawnPosition = new Vector2(0f, 2f);

        // 충돌 대상 레이어입니다. Simulation 은 설정을 들고 있지 않으므로
        // 합성 루트가 주입합니다.
        [SerializeField] private LayerMask _groundLayer;

        private readonly IPlayerInputSource _inputSource = new KeyboardInputSource();

        private int _groundLayerMask;

        private PlayerState _previousState;
        private PlayerState _currentState;
        private uint _tick;

        // 절대 readonly 로 바꾸지 마세요. 분석기가 그렇게 제안하지만 따르면 깨집니다.
        // readonly struct 필드에 비-readonly 메서드를 호출하면 C# 이 방어적 복사본을
        // 만들어서, Advance 는 복사본을 증가시키고 이 필드는 영원히 0 에 머뭅니다.
        // 60fps 에서는 우연히 맞고 다른 프레임레이트에서만 틀리는 형태가 됩니다.
        private FixedTickAccumulator _accumulator;
        private int _lastFrameTickCount;

        // 관측용 읽기 전용 창구입니다. 커스텀 인스펙터와 디버그 오버레이가 씁니다.
        // 상태를 [SerializeField] 로 노출하면 씬 파일에 런타임 값이 저장되므로
        // 프로퍼티로만 내보냅니다. struct 라 반환값은 복사본이고 수정해도 무의미합니다.
        public uint CurrentTick => _tick;
        public int LastFrameTickCount => _lastFrameTickCount;
        public PlayerState CurrentState => _currentState;
        public float Alpha => _accumulator.Alpha;
        public float AccumulatorRemainder => _accumulator.Remainder;

        private void Awake()
        {
            // 레이어를 지정하지 않으면 캐스트가 아무것도 맞히지 못해 캐릭터가
            // 바닥을 그대로 통과합니다. 설정 누락은 조용히 넘기지 않고 알립니다.
            _groundLayerMask = _groundLayer.value;
            if (_groundLayerMask == 0)
            {
                _groundLayerMask = Physics2D.AllLayers;
                Debug.LogWarning(
                    "TickDriver 의 Ground Layer 가 비어 있어 모든 레이어를 충돌 대상으로 씁니다. "
                    + "Inspector 에서 지정하세요.", this);
            }

            _currentState = new PlayerState
            {
                Tick = 0,
                Position = _spawnPosition,
                Velocity = Vector2.zero,
                IsGrounded = false,
                CoyoteTicksRemaining = 0,

                // 0 은 유효한 방향이 아닙니다. default 로 두면 스프라이트 방향이
                // 첫 입력 전까지 정의되지 않습니다.
                FacingDirection = 1
            };
            _previousState = _currentState;
        }

        private void Update()
        {
            // 계층: Input. 에지 입력 래치는 프레임 단위로 걷어야 합니다.
            _inputSource.Poll();

            // 벽시계를 읽는 것은 합성 루트의 몫입니다.
            // 누산 로직 자체는 FixedTickAccumulator 가 들고 있어 테스트가 가능합니다.
            int ticksThisFrame = _accumulator.Advance(Time.deltaTime);

            // 계층: Simulation.
            for (int i = 0; i < ticksThisFrame; i++)
            {
                InputCommand input = _inputSource.Sample(_tick);

                // 이 틱이 에지 입력을 가져갔으므로 래치를 지웁니다.
                // 한 프레임에 틱이 여러 번 돌아도 점프는 첫 틱에서만 발동합니다.
                _inputSource.ConsumeEdges();

                _previousState = _currentState;
                _currentState = SimulationWorld.Step(
                    _currentState, input, SimulationConstants.FixedDeltaTime, _groundLayerMask);

                _tick++;
            }

            _lastFrameTickCount = ticksThisFrame;

            // 계층: Presentation. 누산기에 남은 시간이 곧 다음 틱까지의 진행률입니다.
            if (_presenter != null)
            {
                _presenter.Render(_previousState, _currentState, _accumulator.Alpha);
            }
        }
    }
}
