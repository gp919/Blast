using System;
using Blast.Network;
using UnityEngine;

namespace Blast.Game
{
    // 계층: Game (합성 루트). 네트워크 지연 조건을 언제 어느 피어에 걸 것인가라는
    // 정책만 담당합니다. 조건을 실제로 거는 방법은 Network/NetworkConditionSimulator
    // 에 있고, 이 클래스는 그 수단이 어떻게 구현되어 있는지 알지 못합니다.
    //
    // 클라이언트 피어에만 거는 이유가 둘입니다.
    //   1. UTP 시뮬레이터 스테이지는 한 피어의 송신과 수신 양쪽에 걸립니다. 단방향
    //      지연 D 를 넣으면 그 피어가 겪는 왕복 지연은 2D 가 됩니다. 따라서 RTT
    //      150ms 를 만들려면 75 를 넣어야 합니다.
    //   2. MPPM 가상 플레이어는 메인 에디터와 같은 씬을 로드합니다. 컴포넌트를 그냥
    //      두면 Host 와 Client 양쪽에서 조건이 걸려 클라이언트가 겪는 RTT 가 다시
    //      두 배가 됩니다. 호스트 자신의 입력은 네트워크를 타지 않으므로 호스트에
    //      조건을 걸어서 얻는 것도 없습니다.
    //
    // 판정을 이벤트가 아니라 Update 의 폴링으로 하는 이유:
    //   조건이 바뀌어야 하는 시점이 접속, 종료, 끊김, F4 입력으로 여러 갈래인데,
    //   그때마다 통지 경로를 하나씩 이어 붙이면 한 군데만 빠뜨려도 조건이 걸린 채로
    //   또는 걸리지 않은 채로 남습니다. 그 상태로 영상을 찍으면 나중에 알아챌 방법이
    //   없습니다. 매 프레임 bool 두 개를 읽는 비용은 무시할 수 있고, 값이 바뀐
    //   프레임에만 실제 적용이 일어납니다.
    [DisallowMultipleComponent]
    public sealed class NetworkConditionController : MonoBehaviour
    {
        // 단방향 지연입니다. 왕복은 이 값의 두 배인 150ms 가 됩니다.
        public const int PacketDelayMs = 75;

        // 지터는 0 입니다. 분산을 넣으면 RTT 가 흔들려서 "150ms 조건에서 찍었다"는
        // 주장의 근거가 약해집니다. 지터가 입력 처리에 만드는 문제는 3주차 입력 버퍼
        // 이야기에서 따로 다룹니다.
        public const int PacketJitterMs = 0;

        // 확률 기반 손실입니다. 입력 RPC 와 NetworkVariable 은 둘 다 Reliable 이라
        // 손실은 입력 유실이 아니라 재전송 지연 스파이크로 나타납니다.
        public const int PacketLossPercent = 3;

        // 조건이 걸렸을 때 클라이언트가 겪게 되는 왕복 지연입니다. 표시 전용입니다.
        public const int TargetRoundTripMs = PacketDelayMs * 2;

        // 상태가 바뀔 때만 통지합니다. 표시 문자열을 매 프레임 다시 만들지 않기 위한
        // 것이며, ConnectionLauncher 와 같은 방식입니다.
        public event Action StateChanged;

        private readonly NetworkConditionSimulator _simulator = new NetworkConditionSimulator();

        // 작업자가 F4 로 요청한 상태입니다. 실제로 걸렸는지와는 다릅니다.
        private bool _isRequested = true;

        // 마지막으로 반영한 상태입니다. 이 값과 목표가 같으면 아무 일도 하지 않습니다.
        private bool _isActive;

        // 조건을 걸어 달라고 요청된 상태인지입니다.
        public bool IsRequested => _isRequested;

        // 이 피어에 실제로 조건이 걸려 있는지입니다. 서버 피어이거나 접속 전이면
        // 요청되어 있어도 false 입니다.
        public bool IsActive => _isActive;

        // 조건을 걸 수단이 확보되었는지입니다. false 면 F4 를 눌러도 아무 일도
        // 일어나지 않으므로 HUD 에 그대로 드러내야 합니다.
        public bool IsSimulatorAttached => _simulator.IsAttached;

        private void Awake()
        {
            _simulator.Attach(gameObject);
        }

        private void OnDestroy()
        {
            _simulator.Detach();
        }

        private void Update()
        {
            RefreshCondition();
        }

        // 같은 세션 안에서 정상 조건과 지연 조건을 번갈아 보여주기 위한 전환입니다.
        // 비교가 성립하려면 두 조건이 같은 영상에 담겨야 합니다.
        public void Toggle()
        {
            _isRequested = !_isRequested;

            // 다음 Update 를 기다리지 않고 즉시 반영합니다. 키를 누른 프레임과 표시가
            // 바뀌는 프레임이 어긋나면 영상에서 전환 시점을 짚기 어려워집니다.
            RefreshCondition();
        }

        private void RefreshCondition()
        {
            // IsServer 가 아닌 것만으로는 부족합니다. 접속 전에는 모든 피어에서
            // IsServer 가 false 라, 그대로 두면 아직 드라이버도 없는 상태에서 조건이
            // 걸렸다고 표시하게 됩니다.
            bool shouldApply = _isRequested && NetworkPeer.IsClient && !NetworkPeer.IsServer;
            if (shouldApply == _isActive)
            {
                return;
            }

            _isActive = shouldApply;

            if (shouldApply)
            {
                _simulator.Apply(PacketDelayMs, PacketJitterMs, PacketLossPercent);
                Debug.Log($"[Net] 네트워크 조건 적용: 단방향 {PacketDelayMs}ms, 지터 {PacketJitterMs}ms, " +
                          $"손실 {PacketLossPercent}% (RTT 목표 {TargetRoundTripMs}ms)");
            }
            else
            {
                _simulator.Clear();
                Debug.Log("[Net] 네트워크 조건 해제");
            }

            StateChanged?.Invoke();
        }
    }
}
