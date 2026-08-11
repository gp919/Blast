using System.Collections.Generic;
using Blast.Core;
using Blast.Input;
using Blast.Network;
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
        // 플레이어는 씬에 미리 있지 않고 접속 후 NGO 가 스폰합니다. 그래서 프리젠터를
        // [SerializeField] 로 물어둘 수 없고, PlayerRegistry 등록 시점에 받습니다.
        [SerializeField] private Vector2 _spawnPosition = new Vector2(0f, 2f);

        // 두 캐릭터가 겹쳐 스폰되면 둘 다 있는지 눈으로 확인할 수 없습니다.
        // SpawnIndex 로 옆으로 밀어 놓습니다. 모든 피어가 같은 식으로 계산하므로
        // 위치를 주고받지 않아도 원격 캐릭터가 제자리에 그려집니다.
        private const float SpawnSpacing = 2f;

        // 충돌 대상 레이어입니다. Simulation 은 설정을 들고 있지 않으므로
        // 합성 루트가 주입합니다.
        [SerializeField] private LayerMask _groundLayer;

        // 캐릭터 튜닝 애셋입니다. 시뮬레이션 상태와 달리 [SerializeField] 로
        // 노출해도 됩니다. 시뮬레이션이 쓰는 값이 아니라 사람이 정하는 설계
        // 데이터라 씬이 오염될 여지가 없습니다.
        [SerializeField] private CharacterTuningAsset _tuningAsset;

        private readonly IPlayerInputSource _inputSource = new KeyboardInputSource();

        private int _groundLayerMask;

        private PlayerState _previousState;
        private PlayerState _currentState;
        private uint _tick;

        // 이 피어가 구동하는 플레이어입니다. 접속 전과 디스폰 후에는 null 이며,
        // 그동안에는 시뮬레이션할 대상 자체가 없습니다.
        private PlayerHandle _localPlayer;
        private PlayerPresenter _localPresenter;

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
        public bool HasLocalPlayer => _localPlayer != null;
        public float Alpha => _accumulator.Alpha;
        public float AccumulatorRemainder => _accumulator.Remainder;

        // 애셋이 비어 있으면 기본값으로 돌아갑니다. 캐릭터가 아예 못 움직이는
        // 것보다 낫고, Awake 에서 경고를 한 번 남기므로 놓칠 일도 없습니다.
        //
        // Awake 에서 캐싱하지 않는 이유가 이 이슈의 요점입니다. 매 프레임 애셋을
        // 다시 읽어야 Play 중에 인스펙터에서 바꾼 값이 즉시 반영됩니다.
        // 값 8 개짜리 struct 복사라 프레임당 한 번은 무시할 수 있는 비용입니다.
        public CharacterTuning CurrentTuning =>
            _tuningAsset != null ? _tuningAsset.ToTuning() : CharacterTuning.Default;

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

            if (_tuningAsset == null)
            {
                Debug.LogWarning(
                    "TickDriver 의 Tuning Asset 이 비어 있어 CharacterTuning.Default 를 씁니다. "
                    + "Project 창에서 Create > Blast > Character Tuning 으로 만들어 지정하세요.",
                    this);
            }

            _currentState = CreateSpawnState(0);
            _previousState = _currentState;
        }

        private void OnEnable()
        {
            PlayerRegistry.PlayerSpawned += HandlePlayerSpawned;
            PlayerRegistry.PlayerDespawned += HandlePlayerDespawned;

            // 드라이버가 뒤늦게 켜지는 경우에도 이미 스폰된 플레이어를 놓치지 않습니다.
            IReadOnlyList<PlayerHandle> players = PlayerRegistry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                HandlePlayerSpawned(players[i]);
            }
        }

        private void OnDisable()
        {
            PlayerRegistry.PlayerSpawned -= HandlePlayerSpawned;
            PlayerRegistry.PlayerDespawned -= HandlePlayerDespawned;
        }

        // 스폰 위치는 SpawnIndex 의 결정적 함수입니다. 서버가 위치를 보내주지 않아도
        // 모든 피어가 같은 값을 계산합니다. 이것이 3주차 이후 움직이는 플랫폼을
        // 대역폭 0 으로 동기화하는 것과 같은 원리입니다.
        private PlayerState CreateSpawnState(int spawnIndex)
        {
            Vector2 position = _spawnPosition;
            position.x += spawnIndex * SpawnSpacing;

            return new PlayerState
            {
                Tick = 0,
                Position = position,
                Velocity = Vector2.zero,
                IsGrounded = false,
                CoyoteTicksRemaining = 0,

                // 0 은 유효한 방향이 아닙니다. default 로 두면 스프라이트 방향이
                // 첫 입력 전까지 정의되지 않습니다.
                FacingDirection = 1
            };
        }

        private void HandlePlayerSpawned(PlayerHandle player)
        {
            // 스폰 시점에 한 번만 찾습니다. 매 프레임 GetComponent 를 부르면 안 됩니다.
            PlayerPresenter presenter = player.GameObject.GetComponent<PlayerPresenter>();
            if (presenter == null)
            {
                Debug.LogWarning(
                    "스폰된 플레이어에 PlayerPresenter 가 없습니다. 프리팹 구성을 확인하세요.",
                    player.GameObject);
                return;
            }

            PlayerState spawnState = CreateSpawnState(player.SpawnIndex);

            if (player.IsLocalOwner)
            {
                _localPlayer = player;
                _localPresenter = presenter;

                // 시뮬레이션 상태를 스폰 위치로 되돌립니다. 틱 카운터는 건드리지
                // 않습니다. 틱은 월드 전체의 것이지 플레이어의 것이 아닙니다.
                _currentState = spawnState;
                _previousState = spawnState;
                return;
            }

            // 원격 캐릭터는 이번 이슈에서 움직이지 않습니다. 한 번만 제자리에 놓습니다.
            // 다음 이슈에서 이 자리에 스냅샷 수신과 보간이 들어갑니다.
            presenter.SetTuning(CurrentTuning);
            presenter.Render(spawnState, spawnState, 0f);
        }

        private void HandlePlayerDespawned(PlayerHandle player)
        {
            if (_localPlayer != player)
            {
                return;
            }

            _localPlayer = null;
            _localPresenter = null;
        }

        private void Update()
        {
            // 계층: Input. 에지 입력 래치는 프레임 단위로 걷어야 합니다.
            _inputSource.Poll();

            // 접속 전에는 구동할 대상이 없습니다. 누산기를 돌리지 않으므로 틱도
            // 진행하지 않습니다. 3주차에 서버 틱과 맞추면서 이 부분이 바뀝니다.
            if (_localPlayer == null)
            {
                _lastFrameTickCount = 0;
                return;
            }

            // 벽시계를 읽는 것은 합성 루트의 몫입니다.
            // 누산 로직 자체는 FixedTickAccumulator 가 들고 있어 테스트가 가능합니다.
            int ticksThisFrame = _accumulator.Advance(Time.deltaTime);

            // 튜닝은 프레임 진입 시 한 번만 읽어 이번 프레임의 모든 틱에 같은 값을
            // 씁니다. 틱마다 다시 읽으면 한 프레임 안에서도 값이 갈릴 수 있고,
            // 재조정 루프가 같은 입력에 다른 결과를 내게 됩니다.
            CharacterTuning tuning = CurrentTuning;

            // 계층: Simulation.
            for (int i = 0; i < ticksThisFrame; i++)
            {
                InputCommand input = _inputSource.Sample(_tick);

                // 이 틱이 에지 입력을 가져갔으므로 래치를 지웁니다.
                // 한 프레임에 틱이 여러 번 돌아도 점프는 첫 틱에서만 발동합니다.
                _inputSource.ConsumeEdges();

                _previousState = _currentState;
                _currentState = SimulationWorld.Step(
                    _currentState, input, tuning,
                    SimulationConstants.FixedDeltaTime, _groundLayerMask);

                _tick++;
            }

            _lastFrameTickCount = ticksThisFrame;

            // 계층: Presentation. 누산기에 남은 시간이 곧 다음 틱까지의 진행률입니다.
            if (_localPresenter != null)
            {
                // 기즈모가 실제 충돌 박스를 그리도록 이번 프레임 튜닝을 넘깁니다.
                _localPresenter.SetTuning(tuning);
                _localPresenter.Render(_previousState, _currentState, _accumulator.Alpha);
            }
        }
    }
}
