using System.Collections.Generic;
using System.Text;
using Blast.Network;
using UnityEngine;

namespace Blast.Game
{
    // 계층: Game (합성 루트). Host / Client 시작 버튼만 있는 임시 개발용 UI 입니다.
    //
    // uGUI 대신 IMGUI 입니다. Canvas 와 Button 을 씬에 만들면 배선이 전부 씬 YAML 로
    // 들어가는데, 며칠 뒤 지울 UI 에 그 비용을 쓸 이유가 없습니다. 이쪽은 컴포넌트
    // 하나 붙이면 끝이고 삭제도 파일 하나입니다. 로비 UI 를 만드는 것이 아닙니다.
    //
    // IMGUI 는 레이아웃 계산에서 자체 할당이 있어 매 프레임 GC 를 완전히 없앨 수 없습니다.
    // 개발 중에만 쓰는 UI 라 허용하되, 우리가 만드는 문자열까지 매 프레임 다시 만들지는
    // 않도록 상태가 바뀔 때만 갱신합니다. 이 규칙은 나중에 만들 런타임 오버레이에
    // 그대로 적용됩니다.
    [DisallowMultipleComponent]
    public sealed class ConnectionHud : MonoBehaviour
    {
        private const float PanelWidth = 260f;
        private const float PanelHeight = 270f;
        private const float PanelMargin = 8f;

        // 패널을 감췄을 때 남기는 자리입니다. 권한 모드 한 줄과 네트워크 조건 한 줄을
        // 위아래로 놓습니다. 줄바꿈 없이 한 줄에 들어가야 하므로 패널보다 넓게 잡습니다.
        // 잘리면 영상에 수치가 반쪽만 남습니다.
        private const float ConditionStripWidth = 420f;
        private const float ConditionStripHeight = 18f;

        // 2주차 문제 상황 영상 촬영 때 화면을 가리지 않도록 끌 수 있게 둡니다.
        private const KeyCode ToggleKey = KeyCode.F1;

        // 소유권 검사를 눈으로 확인하기 위한 키입니다. 남의 플레이어에 입력을
        // 보내면 NGO 가 거부하고 경고를 남깁니다. 이것이 서버 권위의 최소 방어선이라
        // 동작을 한 번은 직접 보고 넘어가야 합니다.
        private const KeyCode OwnershipTestKey = KeyCode.F2;

        // 서버 권위와 클라 권위를 같은 세션 안에서 번갈아 보기 위한 키입니다.
        // 2주차 C 비교 촬영에서 한 번에 두 조건을 담기 위한 개발 기능입니다.
        private const KeyCode AuthorityToggleKey = KeyCode.F3;

        // 정상 조건과 지연 조건을 같은 세션 안에서 번갈아 보기 위한 키입니다.
        private const KeyCode ConditionToggleKey = KeyCode.F4;

        // 권한 모드 표시는 세 가지 상태뿐이라 문자열을 미리 만들어 둡니다.
        // StringBuilder 로 조립할 것이 없으므로 할당도 없습니다.
        private const string AuthorityUnknownText = "권위 미확인 (접속 전)";
        private const string AuthorityServerText = "권위 서버";
        private const string AuthorityClientText = "권위 클라";

        private readonly ConnectionLauncher _launcher = new ConnectionLauncher();
        private readonly StringBuilder _statusBuilder = new StringBuilder(64);
        private readonly StringBuilder _conditionBuilder = new StringBuilder(48);

        private string _statusText = string.Empty;

        // 패널을 감춰도 남는 한 줄입니다. 영상에 조건 수치가 찍혀 있지 않으면 나중에
        // 그 조건에서 찍었다고 주장할 근거가 없습니다.
        private string _conditionText = string.Empty;

        // 같은 이유로 권한 모드도 남깁니다. 영상 02 는 권한 전환이 주제인데, 전환이
        // 화면에 기록되지 않으면 어느 구간이 어느 모드인지 나중에 알 수 없습니다.
        // 메인 에디터의 TickDriver 인스펙터에도 같은 값이 있지만 그쪽은 호스트
        // 인스턴스만 보여 주므로, 클라이언트 창에는 이 표시가 유일한 근거입니다.
        private string _authorityText = AuthorityUnknownText;

        private NetworkConditionController _conditions;
        private bool _isVisible = true;

        private void Start()
        {
            _launcher.Attach();
            _launcher.StateChanged += RebuildStatusText;

            // 같은 오브젝트에 붙어 있는 조건 컨트롤러를 찾습니다. 인스펙터 참조로 두면
            // 씬 YAML 에 배선이 하나 더 생기는데, 개발용 HUD 와 개발용 조건 전환은
            // 어차피 같은 오브젝트에 함께 있습니다.
            _conditions = GetComponent<NetworkConditionController>();
            if (_conditions != null)
            {
                _conditions.StateChanged += RebuildStatusText;
            }

            // 스폰과 디스폰도 표시가 바뀌는 시점입니다. 이벤트로 받아야 OnGUI 에서
            // 매 프레임 레지스트리를 들여다보지 않습니다.
            PlayerRegistry.PlayerSpawned += HandleRegistryChanged;
            PlayerRegistry.PlayerDespawned += HandleRegistryChanged;

            // 권한 모드는 서버가 방송하므로 F3 를 누른 순간이 아니라 값이 돌아온
            // 순간에 바뀝니다. 그 시점을 알려주는 것이 이 이벤트입니다.
            PlayerRegistry.PlayerAuthorityChanged += HandleRegistryChanged;

            RebuildStatusText();
        }

        private void OnDestroy()
        {
            _launcher.StateChanged -= RebuildStatusText;

            if (_conditions != null)
            {
                _conditions.StateChanged -= RebuildStatusText;
            }

            PlayerRegistry.PlayerSpawned -= HandleRegistryChanged;
            PlayerRegistry.PlayerDespawned -= HandleRegistryChanged;
            PlayerRegistry.PlayerAuthorityChanged -= HandleRegistryChanged;
            _launcher.Detach();
        }

        private void HandleRegistryChanged(PlayerHandle player)
        {
            RebuildStatusText();
        }

        private void OnGUI()
        {
            // 표시 전환은 IMGUI 자체 이벤트로 처리합니다. Input System 을 쓰면 Blast.Game 이
            // Unity.InputSystem 을 참조해야 하는데, 개발용 토글 하나 때문에 합성 루트의
            // 의존을 늘릴 이유가 없습니다. 시뮬레이션 입력 경로와도 무관합니다.
            Event current = Event.current;
            if (current.type == EventType.KeyDown && current.keyCode == ToggleKey)
            {
                _isVisible = !_isVisible;
                current.Use();
            }
            else if (current.type == EventType.KeyDown && current.keyCode == OwnershipTestKey)
            {
                TrySendInputToRemotePlayer();
                current.Use();
            }
            else if (current.type == EventType.KeyDown && current.keyCode == AuthorityToggleKey)
            {
                RequestAuthorityToggle();
                current.Use();
            }
            else if (current.type == EventType.KeyDown && current.keyCode == ConditionToggleKey)
            {
                if (_conditions != null)
                {
                    _conditions.Toggle();
                }

                current.Use();
            }

            if (!_isVisible)
            {
                // 패널을 감춰도 권한과 조건 두 줄은 남깁니다. 촬영할 때 화면을 비우려고
                // F1 을 누르는데, 그 순간 이 표시까지 사라지면 영상만 남고 근거가
                // 사라집니다.
                GUI.Label(
                    new Rect(PanelMargin, PanelMargin, ConditionStripWidth, ConditionStripHeight),
                    _authorityText);
                GUI.Label(
                    new Rect(
                        PanelMargin, PanelMargin + ConditionStripHeight,
                        ConditionStripWidth, ConditionStripHeight),
                    _conditionText);
                return;
            }

            GUILayout.BeginArea(new Rect(PanelMargin, PanelMargin, PanelWidth, PanelHeight), GUI.skin.box);

            if (_launcher.IsRunning)
            {
                GUILayout.Label(_statusText);
                if (GUILayout.Button("Shutdown"))
                {
                    _launcher.Shutdown();
                }
            }
            else
            {
                GUILayout.Label(_statusText);
                if (GUILayout.Button("Start Host"))
                {
                    _launcher.StartHost();
                }

                if (GUILayout.Button("Start Client"))
                {
                    _launcher.StartClient();
                }
            }

            GUILayout.EndArea();
        }

        // 남의 플레이어에 입력을 보내 봅니다. ServerRpc 가 RequireOwnership 이라
        // 서버에 도달하기 전에 NGO 가 막고 콘솔에 경고를 남깁니다.
        // 캐릭터가 움직이지 않는 것이 정상 동작입니다.
        private static void TrySendInputToRemotePlayer()
        {
            IReadOnlyList<PlayerHandle> players = PlayerRegistry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerHandle player = players[i];
                if (player.IsLocalOwner)
                {
                    continue;
                }

                Debug.Log($"[Net] 소유권 검사 테스트: ownerId {player.OwnerId} 의 플레이어에 입력 전송 시도");
                player.SendInput(default);
                return;
            }

            Debug.Log("[Net] 소유권 검사 테스트: 남의 플레이어가 없습니다. 접속 후 다시 시도하세요.");
        }

        // 권한 모드 전환을 서버에 요청합니다. 요청 자체는 아무 플레이어를 통해 보내도
        // 되지만, 소유하지 않은 플레이어로 보내면 Everyone 권한이라 통과하긴 해도
        // 의도가 흐려집니다. 로컬 플레이어를 씁니다.
        private static void RequestAuthorityToggle()
        {
            PlayerHandle localPlayer = PlayerRegistry.LocalPlayer;
            if (localPlayer == null)
            {
                Debug.Log("[Net] 권한 모드 전환: 로컬 플레이어가 없습니다. 접속 후 다시 시도하세요.");
                return;
            }

            localPlayer.RequestAuthorityMode(!localPlayer.IsClientAuthority);
        }

        // 상태 변화 시에만 호출됩니다. OnGUI 안에서 문자열을 조립하면 초당 수백 번
        // 같은 문자열을 새로 만들게 됩니다.
        private void RebuildStatusText()
        {
            RebuildAuthorityText();
            RebuildConditionText();

            _statusBuilder.Clear();
            _statusBuilder.Append(_launcher.Mode.ToString());

            if (_launcher.Mode == ConnectionMode.Host)
            {
                _statusBuilder.Append("  clients ");
                _statusBuilder.Append(_launcher.ConnectedClientCount);
            }
            else if (_launcher.Mode == ConnectionMode.Client)
            {
                _statusBuilder.Append(_launcher.IsConnectedClient ? "  연결됨" : "  연결 시도 중");
            }

            _statusBuilder.Append('\n');
            _statusBuilder.Append(_launcher.LastMessage);

            if (_launcher.IsRunning)
            {
                // 소유권 값 관측용입니다. Host 에서 IsServer 와 IsClient 가 동시에
                // 참이라는 것, 그리고 로컬 소유 플레이어의 OwnerClientId 가 무엇인지가
                // 여기서 눈으로 확인됩니다.
                _statusBuilder.Append("\nserver ");
                _statusBuilder.Append(_launcher.IsServer ? "O" : "X");
                _statusBuilder.Append("  client ");
                _statusBuilder.Append(_launcher.IsClient ? "O" : "X");
                _statusBuilder.Append("  host ");
                _statusBuilder.Append(_launcher.IsHost ? "O" : "X");

                _statusBuilder.Append("\nlocalClientId ");
                _statusBuilder.Append(_launcher.LocalClientId);

                _statusBuilder.Append("\nplayers ");
                _statusBuilder.Append(PlayerRegistry.Players.Count);

                PlayerHandle localPlayer = PlayerRegistry.LocalPlayer;
                _statusBuilder.Append("  ownerId ");
                _statusBuilder.Append(localPlayer != null ? localPlayer.OwnerId.ToString() : "없음");
            }

            _statusBuilder.Append('\n');
            _statusBuilder.Append(_authorityText);

            _statusBuilder.Append('\n');
            _statusBuilder.Append(_conditionText);

            _statusBuilder.Append("\nF1 표시  F2 소유권  F3 권한  F4 지연");

            _statusText = _statusBuilder.ToString();
        }

        // 권한 모드 한 줄입니다. 값은 로컬 소유 플레이어에서 읽습니다.
        //
        // 현재 구현에서 권한은 월드 단위입니다. 서버가 F3 요청을 받으면 레지스트리에
        // 있는 모든 플레이어에 같은 값을 씁니다. NetworkPlayer 의
        // RequestAuthorityModeRpc 주석 참조. 따라서 로컬 플레이어의 값 하나로 전체
        // 상태를 대표할 수 있습니다. 플레이어마다 권한이 갈리는 구성이 생기면 이
        // 표시부터 바꿔야 합니다.
        private void RebuildAuthorityText()
        {
            PlayerHandle localPlayer = PlayerRegistry.LocalPlayer;
            if (localPlayer == null)
            {
                // 접속 전이거나, 끊긴 뒤이거나, 플레이어 오브젝트가 없는 순수 서버입니다.
                // 여기에 서버 권위라고 적으면 조건이 확정된 것처럼 보여서 그 상태로
                // 촬영하게 됩니다.
                _authorityText = AuthorityUnknownText;
                return;
            }

            _authorityText = localPlayer.IsClientAuthority ? AuthorityClientText : AuthorityServerText;
        }

        // 네트워크 조건 한 줄입니다. 패널 안과 패널을 감췄을 때 양쪽에서 같은 문자열을
        // 씁니다. 표시하는 값은 이 피어가 시뮬레이터에 요청한 조건이며, 실제로 그만큼
        // 걸렸는지는 RNSM 의 RTT 로 확인해야 합니다.
        private void RebuildConditionText()
        {
            _conditionBuilder.Clear();
            _conditionBuilder.Append("net ");

            if (_conditions == null)
            {
                _conditionBuilder.Append("조건 컴포넌트 없음");
            }
            else if (!_conditions.IsSimulatorAttached)
            {
                _conditionBuilder.Append("시뮬레이터 없음");
            }
            else if (_conditions.IsActive)
            {
                _conditionBuilder.Append("지연 ");
                _conditionBuilder.Append(NetworkConditionController.PacketDelayMs);
                _conditionBuilder.Append("ms  지터 ");
                _conditionBuilder.Append(NetworkConditionController.PacketJitterMs);
                _conditionBuilder.Append("ms  손실 ");
                _conditionBuilder.Append(NetworkConditionController.PacketLossPercent);
                _conditionBuilder.Append("%  RTT ");
                _conditionBuilder.Append(NetworkConditionController.TargetRoundTripMs);
                _conditionBuilder.Append("ms");
            }
            else if (_conditions.IsRequested)
            {
                // 요청은 켜져 있지만 이 피어에는 걸리지 않는 상태입니다. 서버 피어이거나
                // 아직 접속 전입니다. 조건이 걸린 줄 알고 촬영하는 것을 막는 표시입니다.
                _conditionBuilder.Append("정상 (서버 피어 또는 접속 전)");
            }
            else
            {
                _conditionBuilder.Append("정상");
            }

            _conditionText = _conditionBuilder.ToString();
        }
    }
}
