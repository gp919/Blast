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
        private const float PanelWidth = 240f;
        private const float PanelHeight = 220f;
        private const float PanelMargin = 8f;

        // 2주차 문제 상황 영상 촬영 때 화면을 가리지 않도록 끌 수 있게 둡니다.
        private const KeyCode ToggleKey = KeyCode.F1;

        // 소유권 검사를 눈으로 확인하기 위한 키입니다. 남의 플레이어에 입력을
        // 보내면 NGO 가 거부하고 경고를 남깁니다. 이것이 서버 권위의 최소 방어선이라
        // 동작을 한 번은 직접 보고 넘어가야 합니다.
        private const KeyCode OwnershipTestKey = KeyCode.F2;

        private readonly ConnectionLauncher _launcher = new ConnectionLauncher();
        private readonly StringBuilder _statusBuilder = new StringBuilder(64);

        private string _statusText = string.Empty;
        private bool _isVisible = true;

        private void Start()
        {
            _launcher.Attach();
            _launcher.StateChanged += RebuildStatusText;

            // 스폰과 디스폰도 표시가 바뀌는 시점입니다. 이벤트로 받아야 OnGUI 에서
            // 매 프레임 레지스트리를 들여다보지 않습니다.
            PlayerRegistry.PlayerSpawned += HandleRegistryChanged;
            PlayerRegistry.PlayerDespawned += HandleRegistryChanged;

            RebuildStatusText();
        }

        private void OnDestroy()
        {
            _launcher.StateChanged -= RebuildStatusText;
            PlayerRegistry.PlayerSpawned -= HandleRegistryChanged;
            PlayerRegistry.PlayerDespawned -= HandleRegistryChanged;
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

            if (!_isVisible)
            {
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

        // 상태 변화 시에만 호출됩니다. OnGUI 안에서 문자열을 조립하면 초당 수백 번
        // 같은 문자열을 새로 만들게 됩니다.
        private void RebuildStatusText()
        {
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

            _statusBuilder.Append("\nF1 표시 전환  F2 소유권 테스트");

            _statusText = _statusBuilder.ToString();
        }
    }
}
