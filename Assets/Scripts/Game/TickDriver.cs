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
    // 이번 이슈에서 역할이 피어마다 갈라졌습니다.
    //   서버: 모든 플레이어를 시뮬레이션하고 결과를 발행한다
    //   클라이언트: 자기 입력을 보내고, 받은 위치를 그대로 그린다
    //
    // 클라이언트는 시뮬레이션을 한 줄도 돌리지 않습니다. 자기 캐릭터조차 서버가
    // 보내준 위치를 따릅니다. 그래서 입력에서 반응까지 RTT 만큼 밀립니다.
    // 의도된 결함이고, 이것을 없애는 것이 3주차 클라이언트 예측입니다.
    //
    // 조건 분기가 IsServer 뿐이고 IsHost 가 없다는 점이 중요합니다. Host 는
    // "서버이면서 클라이언트"일 뿐 별도의 경우가 아닙니다. IsHost 로 분기하기
    // 시작하면 데디케이티드 서버로 돌릴 수 없게 됩니다.
    public sealed class TickDriver : MonoBehaviour
    {
        // 플레이어 하나에 대한 구동 정보입니다. struct 가 아니라 class 인 것은
        // List 안에서 상태를 갱신하기 때문입니다. struct 로 두면 인덱서가 복사본을
        // 돌려주어 갱신이 조용히 사라집니다. 스폰 시 한 번만 할당됩니다.
        private sealed class PlayerSimEntry
        {
            public PlayerHandle Handle;
            public PlayerPresenter Presenter;
            public PlayerState State;

            // 직전 틱의 상태입니다. 이 피어가 직접 시뮬레이션하는 캐릭터를
            // 프레임레이트와 무관하게 그리기 위한 것이며, 네트워크와 무관합니다.
            public PlayerState PreviousState;
        }

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

        // SpawnIndex 오름차순으로 유지합니다. 등록 순서는 곧 접속 순서라 피어마다
        // 갈릴 수 있습니다. 지금은 플레이어끼리 상호작용이 없어 순서가 결과를 바꾸지
        // 않지만, 밀치기나 충돌이 들어오는 순간 순회 순서가 곧 결정성입니다.
        // 그때 가서 고치면 원인을 찾기 어려운 종류의 버그가 됩니다.
        private readonly List<PlayerSimEntry> _entries = new List<PlayerSimEntry>(4);

        private int _groundLayerMask;
        private uint _tick;

        // 이 피어가 입력으로 조종하는 플레이어입니다. 데디케이티드 서버에서는
        // 끝까지 null 이며, 그래도 서버 시뮬레이션은 정상적으로 돕니다.
        private PlayerSimEntry _localEntry;

        // 절대 readonly 로 바꾸지 마세요. 분석기가 그렇게 제안하지만 따르면 깨집니다.
        // readonly struct 필드에 비-readonly 메서드를 호출하면 C# 이 방어적 복사본을
        // 만들어서, Advance 는 복사본을 증가시키고 이 필드는 영원히 0 에 머뭅니다.
        // 60fps 에서는 우연히 맞고 다른 프레임레이트에서만 틀리는 형태가 됩니다.
        private FixedTickAccumulator _accumulator;
        private int _lastFrameTickCount;
        private uint _lastSentInputTick;

        // 관측용 읽기 전용 창구입니다. 커스텀 인스펙터와 디버그 오버레이가 씁니다.
        // 상태를 [SerializeField] 로 노출하면 씬 파일에 런타임 값이 저장되므로
        // 프로퍼티로만 내보냅니다. struct 라 반환값은 복사본이고 수정해도 무의미합니다.
        public uint CurrentTick => _tick;
        public int LastFrameTickCount => _lastFrameTickCount;
        public bool HasLocalPlayer => _localEntry != null;
        public int TrackedPlayerCount => _entries.Count;
        public bool IsServerPeer => NetworkPeer.IsServer;
        public uint LastSentInputTick => _lastSentInputTick;

        // 현재 권한 모드입니다. 서버가 방송하므로 전 피어가 같은 값을 봅니다.
        public bool IsClientAuthorityMode =>
            _localEntry != null && _localEntry.Handle.IsClientAuthority;

        // 이 피어가 이번 프레임에 실제로 시뮬레이션하는 캐릭터 수입니다.
        // 서버 권위면 서버에서 전원, 클라 권위면 각 피어에서 1 이 나옵니다.
        public int SimulatedHereCount
        {
            get
            {
                bool isServer = NetworkPeer.IsServer;
                int count = 0;
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (IsSimulatedHere(_entries[i], isServer))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        // 서버에서만 갱신됩니다. 클라이언트에서는 0 에 머뭅니다.
        // 서버에서 이 값이 멈춰 있으면 입력 RPC 가 도달하지 못하고 있다는 뜻입니다.
        public uint LocalReceivedInputTick =>
            _localEntry != null ? _localEntry.Handle.LastReceivedInputTick : 0u;

        public PlayerState CurrentState =>
            _localEntry != null ? _localEntry.State : default;

        // 이 피어가 직접 시뮬레이션하는 캐릭터의 렌더 보간에 쓰입니다.
        // 수신 상태를 그리는 경로에는 쓰이지 않습니다. 둘은 다른 문제를 풉니다.
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
            if (FindEntry(player) != null)
            {
                return;
            }

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
            PlayerSimEntry entry = new PlayerSimEntry
            {
                Handle = player,
                Presenter = presenter,
                State = spawnState,
                PreviousState = spawnState
            };

            InsertOrdered(entry);

            if (player.IsLocalOwner)
            {
                _localEntry = entry;
            }

            // 서버는 스폰 상태를 즉시 발행합니다. 이 호출이 NGO 가 스폰 메시지를
            // 내보내기 전에 일어나므로, 클라이언트는 스폰과 동시에 올바른 위치를
            // 받습니다. 이것이 없으면 첫 값 변경 전까지 원점에 서 있게 됩니다.
            if (NetworkPeer.IsServer)
            {
                player.PublishState(entry.State);
            }
        }

        private void HandlePlayerDespawned(PlayerHandle player)
        {
            PlayerSimEntry entry = FindEntry(player);
            if (entry == null)
            {
                return;
            }

            _entries.Remove(entry);

            if (_localEntry == entry)
            {
                _localEntry = null;
            }
        }

        private PlayerSimEntry FindEntry(PlayerHandle player)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Handle == player)
                {
                    return _entries[i];
                }
            }

            return null;
        }

        // SpawnIndex 오름차순 삽입입니다. 인원이 한 자릿수라 선형 탐색으로 충분하고,
        // 정렬 함수를 부르는 것보다 순서 규칙이 코드에 그대로 드러납니다.
        private void InsertOrdered(PlayerSimEntry entry)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (entry.Handle.SpawnIndex < _entries[i].Handle.SpawnIndex)
                {
                    _entries.Insert(i, entry);
                    return;
                }
            }

            _entries.Add(entry);
        }

        private void Update()
        {
            // 계층: Input. 에지 입력 래치는 프레임 단위로 걷어야 합니다.
            _inputSource.Poll();

            CharacterTuning tuning = CurrentTuning;

            // 스폰된 플레이어가 없으면 진행시킬 것이 없습니다. 누산기를 돌리지 않으므로
            // 틱도 멈춥니다. 3주차에 서버 틱과 맞추면서 이 부분이 바뀝니다.
            if (_entries.Count == 0)
            {
                _lastFrameTickCount = 0;
                return;
            }

            bool isServer = NetworkPeer.IsServer;
            int ticksThisFrame = _accumulator.Advance(Time.deltaTime);

            // 튜닝은 프레임 진입 시 한 번만 읽어 이번 프레임의 모든 틱에 같은 값을
            // 씁니다. 틱마다 다시 읽으면 한 프레임 안에서도 값이 갈릴 수 있고,
            // 재조정 루프가 같은 입력에 다른 결과를 내게 됩니다.
            for (int i = 0; i < ticksThisFrame; i++)
            {
                // 계층: Input. 이번 틱의 입력을 한 번만 뽑습니다.
                InputCommand localInput = default;
                bool hasLocalInput = false;

                if (_localEntry != null)
                {
                    localInput = _inputSource.Sample(_tick);

                    // 이 틱이 에지 입력을 가져갔으므로 래치를 지웁니다.
                    // 한 프레임에 틱이 여러 번 돌아도 점프는 첫 틱에서만 발동합니다.
                    _inputSource.ConsumeEdges();
                    hasLocalInput = true;

                    // 계층: Network. 서버 권위에서만 입력을 보냅니다.
                    // 클라 권위에서는 여기서 시뮬레이션하고 결과 위치를 보내므로
                    // 입력까지 보내면 같은 것을 두 번 보내는 셈입니다.
                    //
                    // Host 에서는 이 호출이 곧바로 로컬 실행되어 지연이 0 입니다.
                    if (!_localEntry.Handle.IsClientAuthority)
                    {
                        _localEntry.Handle.SendInput(localInput);
                        _lastSentInputTick = _tick;
                    }
                }

                // 계층: Simulation.
                StepSimulatedHere(tuning, isServer, localInput, hasLocalInput);

                _tick++;
            }

            _lastFrameTickCount = ticksThisFrame;

            // 계층: Presentation. 틱이 한 번도 돌지 않은 프레임에도 그립니다.
            // 서버 값은 우리 틱과 무관하게 30Hz 로 도착하기 때문입니다.
            RenderAll(tuning, isServer, _accumulator.Alpha);
        }

        // 이 플레이어를 이 피어가 시뮬레이션하는가.
        //
        // 서버 권위: 서버만 돈다. 소유 클라이언트도 자기 캐릭터를 돌리지 않는다
        // 클라 권위: 소유 클라이언트만 돈다. 서버조차 남의 캐릭터를 돌리지 않는다
        //
        // 두 모드가 정확히 하나의 시뮬레이터를 지정한다는 것이 요점입니다. 둘이 되면
        // 서로 위치를 덮어쓰며 캐릭터가 떨고, 영이 되면 아무도 안 움직입니다.
        private static bool IsSimulatedHere(PlayerSimEntry entry, bool isServer)
        {
            return entry.Handle.IsClientAuthority ? entry.Handle.IsLocalOwner : isServer;
        }

        private void StepSimulatedHere(
            in CharacterTuning tuning, bool isServer, in InputCommand localInput, bool hasLocalInput)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                PlayerSimEntry entry = _entries[i];
                if (!IsSimulatedHere(entry, isServer))
                {
                    continue;
                }

                InputCommand input;
                if (entry.Handle.IsClientAuthority)
                {
                    // 클라 권위에서는 자기 캐릭터만 돌리므로 입력이 이미 손에 있습니다.
                    // 네트워크를 타지 않아 지연이 0 이고, 그것이 이 모드의 전부입니다.
                    if (!hasLocalInput)
                    {
                        continue;
                    }

                    input = localInput;
                }
                else if (!entry.Handle.TryConsumeInput(out input))
                {
                    // 입력이 한 번도 도착하지 않았으면 아무것도 누르지 않은 것으로 봅니다.
                    // 중력과 관성은 그대로 적용되어야 하므로 시뮬레이션은 계속 돕니다.
                    input = default;
                }

                // 클라이언트가 붙여 보낸 틱 번호는 지금 쓰지 않습니다. 입력을 정렬해
                // 보관하는 버퍼가 있어야 의미가 생기는데 그건 3주차입니다.
                // 지금은 자기 틱으로 덮어 한 시간축 위에서만 시뮬레이션합니다.
                input.Tick = _tick;

                // 한 프레임에 틱이 여러 번 돌면 마지막 틱의 직전 상태가 남습니다.
                // 알파 보간이 그리는 구간이 곧 그 마지막 틱 구간입니다.
                entry.PreviousState = entry.State;
                entry.State = SimulationWorld.Step(
                    entry.State, input, tuning,
                    SimulationConstants.FixedDeltaTime, _groundLayerMask);

                // 서버는 발행하고, 클라 권위의 소유자는 통보합니다.
                // 통보받은 서버는 검증 없이 그대로 발행합니다.
                if (entry.Handle.IsClientAuthority)
                {
                    entry.Handle.SubmitState(entry.State);
                }
                else
                {
                    entry.Handle.PublishState(entry.State);
                }
            }
        }

        private void RenderAll(in CharacterTuning tuning, bool isServer, float alpha)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                PlayerSimEntry entry = _entries[i];
                bool simulatedHere = IsSimulatedHere(entry, isServer);

                if (!simulatedHere && entry.Handle.HasNetState)
                {
                    // 내가 안 돌리는 캐릭터는 수신값이 곧 상태입니다. 속도나 접지 같은
                    // 나머지 필드는 받지 않으므로 갱신되지 않습니다. 그리는 데
                    // 쓰이지 않아 문제되지 않지만, 인스펙터에서 그 값들을 볼 때는
                    // 시뮬레이션하는 쪽에서 봐야 한다는 뜻이기도 합니다.
                    //
                    // 권한 모드를 전환하면 여기서 넘겨받은 상태로 시뮬레이션이
                    // 시작됩니다. 속도가 0 으로 초기화되므로 낙하 중에 전환하면
                    // 한 번 튑니다. 비교 촬영용 기능이라 그대로 둡니다.
                    PlayerState received = entry.State;
                    received.Position = entry.Handle.NetPosition;
                    received.FacingDirection = entry.Handle.NetFacing;
                    entry.State = received;

                    // 권한 모드가 바뀌어 이 캐릭터를 갑자기 시뮬레이션하게 될 때
                    // 직전 상태가 옛날 값이면 한 프레임 크게 튑니다.
                    entry.PreviousState = received;
                }

                // 기즈모가 실제 충돌 박스를 그리도록 이번 프레임 튜닝을 넘깁니다.
                entry.Presenter.SetTuning(tuning);

                if (simulatedHere)
                {
                    // 틱 알파 보간입니다. 스냅샷 보간과 혼동하면 안 됩니다.
                    // 이쪽은 60Hz 틱과 화면 프레임레이트가 맞지 않는 문제를 푸는 것이고,
                    // 네트워크와 무관합니다. 이것까지 없애면 한 프레임에 틱이
                    // 1 번, 2 번, 0 번 도는 편차가 그대로 화면에 떨림으로 나옵니다.
                    entry.Presenter.Render(entry.PreviousState, entry.State, alpha);
                }
                else
                {
                    // 이쪽이 이번 이슈에서 일부러 비워둔 자리입니다. 30Hz 로 도착한
                    // 위치를 보간 없이 그대로 그립니다. 같은 상태를 두 번 넘겨
                    // 알파를 무의미하게 만듭니다. 3주차 스냅샷 보간이 여기 들어옵니다.
                    entry.Presenter.Render(entry.State, entry.State, 0f);
                }
            }
        }
    }
}
