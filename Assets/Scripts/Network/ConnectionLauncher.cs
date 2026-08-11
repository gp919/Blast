using System;
using Unity.Netcode;
using UnityEngine;

namespace Blast.Network
{
    // 계층: Network. 접속 모드입니다.
    // NGO 타입을 상위 계층에 노출하지 않기 위해 자체 열거형으로 둡니다.
    public enum ConnectionMode
    {
        None,
        Host,
        Client
    }

    // 계층: Network. NGO 접속 시작과 종료를 감싸는 얇은 래퍼입니다.
    //
    // 이 타입이 NetworkManager 를 만지는 유일한 곳입니다. Blast.Game 은 NGO 어셈블리를
    // 참조하지 않고 이 클래스만 씁니다. 지금은 참조 한 줄 차이지만, NGO API 가 합성 루트
    // 전역에 퍼지면 트랜스포트 교체나 테스트 대역 삽입 경로가 막힙니다.
    //
    // MonoBehaviour 가 아닙니다. 씬에 붙일 것이 늘면 배선이 늘고 씬 YAML 이 더러워집니다.
    // 수명은 소유자(ConnectionHud)가 Attach / Detach 로 관리합니다.
    public sealed class ConnectionLauncher
    {
        // 상태가 바뀔 때만 통지합니다. 표시 문자열을 매 프레임 다시 만들지 않기 위한 것입니다.
        public event Action StateChanged;

        public ConnectionMode Mode { get; private set; } = ConnectionMode.None;

        // 서버 관점의 접속 클라이언트 수입니다. Host 는 자기 자신을 포함해 1부터 셉니다.
        public int ConnectedClientCount { get; private set; }

        // 클라이언트 관점의 접속 성립 여부입니다. 서버 응답을 받아야 true 가 됩니다.
        public bool IsConnectedClient { get; private set; }

        public string LastMessage { get; private set; } = "대기 중";

        // NGO 의 역할 플래그입니다. Host 에서는 IsServer 와 IsClient 가 동시에 참이라는
        // 것이 소유권 이해의 출발점이라, 값을 그대로 관측할 수 있게 노출합니다.
        public bool IsServer { get; private set; }
        public bool IsClient { get; private set; }
        public bool IsHost { get; private set; }
        public ulong LocalClientId { get; private set; }

        public bool IsRunning => Mode != ConnectionMode.None;

        private bool _isSubscribed;

        // NetworkManager.Singleton 은 자신의 Awake 에서 채워지므로 실행 순서에 의존하지
        // 않도록 Start 시점에 호출하게 합니다.
        public void Attach()
        {
            if (_isSubscribed)
            {
                return;
            }

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null)
            {
                LastMessage = "NetworkManager 가 씬에 없습니다";
                RaiseStateChanged();
                return;
            }

            manager.OnServerStarted += HandleServerStarted;
            manager.OnClientConnectedCallback += HandleClientConnected;
            manager.OnClientDisconnectCallback += HandleClientDisconnected;
            _isSubscribed = true;
        }

        public void Detach()
        {
            if (!_isSubscribed)
            {
                return;
            }

            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null)
            {
                manager.OnServerStarted -= HandleServerStarted;
                manager.OnClientConnectedCallback -= HandleClientConnected;
                manager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            _isSubscribed = false;
        }

        public void StartHost()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || IsRunning)
            {
                return;
            }

            LogTopology(manager);

            if (manager.StartHost())
            {
                Mode = ConnectionMode.Host;
                LastMessage = "Host 시작";
            }
            else
            {
                LastMessage = "Host 시작 실패";
            }

            RaiseStateChanged();
        }

        public void StartClient()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || IsRunning)
            {
                return;
            }

            LogTopology(manager);

            if (manager.StartClient())
            {
                Mode = ConnectionMode.Client;
                LastMessage = "Client 접속 시도 중";
            }
            else
            {
                LastMessage = "Client 시작 실패";
            }

            RaiseStateChanged();
        }

        public void Shutdown()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !IsRunning)
            {
                return;
            }

            manager.Shutdown();

            Mode = ConnectionMode.None;
            ConnectedClientCount = 0;
            IsConnectedClient = false;
            LastMessage = "종료함";
            RaiseStateChanged();
        }

        // 인스펙터에 Network Topology 항목이 보이지 않으므로 값 자체를 로그로 남깁니다.
        // 이 프로젝트는 서버 권위 클라이언트-서버 모델이 전제입니다.
        // DistributedAuthority 로 돌고 있으면 3주차 예측 및 재조정의 전제가 무너집니다.
        private static void LogTopology(NetworkManager manager)
        {
            Debug.Log($"[Net] NetworkTopology = {manager.NetworkConfig.NetworkTopology}, " +
                      $"NGO TickRate = {manager.NetworkConfig.TickRate}");
        }

        private void HandleServerStarted()
        {
            LastMessage = "서버 시작됨";
            RefreshCounts();
        }

        private void HandleClientConnected(ulong clientId)
        {
            LastMessage = $"접속: clientId {clientId}";
            RefreshCounts();
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            LastMessage = $"끊김: clientId {clientId}";
            RefreshCounts();

            // 자기 자신이 끊긴 클라이언트면 모드를 되돌립니다.
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && Mode == ConnectionMode.Client && clientId == manager.LocalClientId)
            {
                Mode = ConnectionMode.None;
                LastMessage = "서버와 끊김";
                RaiseStateChanged();
            }
        }

        private void RefreshCounts()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null)
            {
                return;
            }

            ConnectedClientCount = manager.IsServer ? manager.ConnectedClientsIds.Count : 0;
            IsConnectedClient = manager.IsConnectedClient;
            RaiseStateChanged();
        }

        private void RaiseStateChanged()
        {
            RefreshFlags();
            StateChanged?.Invoke();
        }

        private void RefreshFlags()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null)
            {
                IsServer = false;
                IsClient = false;
                IsHost = false;
                LocalClientId = 0;
                return;
            }

            IsServer = manager.IsServer;
            IsClient = manager.IsClient;
            IsHost = manager.IsHost;
            LocalClientId = manager.LocalClientId;
        }
    }
}
