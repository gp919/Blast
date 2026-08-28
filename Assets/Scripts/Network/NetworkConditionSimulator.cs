using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
using UnityEngine;

namespace Blast.Network
{
    // 계층: Network. Multiplayer Tools 의 Network Simulator 를 감싸는 얇은 래퍼입니다.
    //
    // ConnectionLauncher 가 NGO 를 이 계층 안에 가두는 것과 같은 이유입니다. 상위 계층은
    // 지연과 로스라는 숫자만 다루고, 어느 패키지의 어떤 컴포넌트가 그 숫자를 거는지는
    // 알지 못합니다. 나중에 트랜스포트를 직접 만들게 되면 이 파일만 바뀝니다.
    //
    // 동작 원리:
    //   UnityTransport 는 드라이버를 만들 때 UTP 파이프라인에 SimulatorPipelineStage 를
    //   끼워 넣습니다. 시뮬레이터가 하는 일은 그 스테이지의 파라미터를
    //   ModifySimulatorStageParameters 로 바꾸는 것이 전부이고, 접속 중에도 즉시
    //   반영됩니다. 그래서 조건을 바꾸려고 재접속할 필요가 없습니다.
    //
    // 스테이지는 UNITY_MP_TOOLS_NETSIM_IMPLEMENTATION_ENABLED 가 정의된 경우에만
    // 파이프라인에 들어갑니다. 에디터에서는 자동으로 정의되지만 일반 빌드에서는
    // 정의되지 않습니다. 정의되지 않은 채로 돌면 프리셋을 넣어도 아무 일이 일어나지
    // 않고 오류도 나지 않습니다. 따라서 이 클래스가 보고하는 값은 "요청한 조건"이며,
    // 실제로 걸렸는지는 RNSM 의 RTT 로 확인해야 합니다.
    //
    // MonoBehaviour 가 아닙니다. 수명은 소유자가 Attach 와 Detach 로 관리합니다.
    public sealed class NetworkConditionSimulator
    {
        // 프리셋 이름을 고정합니다. 이름이 바뀔 때마다 툴 패키지가 프리셋 변경을
        // 에디터 분석 이벤트로 보고하므로, 전환할 때마다 이름이 달라지면 안 됩니다.
        private const string PresetName = "Blast Dev Condition";

        // 프리셋 인스턴스를 재사용합니다. 전환할 때마다 새로 만들면 그때마다 힙 할당이
        // 생깁니다. 매 프레임 경로는 아니지만 습관을 맞춰 둡니다.
        private readonly NetworkSimulatorPreset _preset = NetworkSimulatorPreset.Create(PresetName);

        private NetworkSimulator _simulator;

        // 조건을 걸 수단이 실제로 확보되었는지입니다. false 면 아래 값들은 의미가 없습니다.
        public bool IsAttached => _simulator != null;

        // 이 피어에 조건이 걸려 있는지입니다.
        public bool IsApplied { get; private set; }

        // 현재 걸려 있는 값입니다. 걸려 있지 않으면 전부 0 입니다.
        public int PacketDelayMs { get; private set; }
        public int PacketJitterMs { get; private set; }
        public int PacketLossPercent { get; private set; }

        // 씬에 NetworkSimulator 가 이미 있으면 그것을 쓰고, 없으면 host 오브젝트에
        // 붙입니다. 두 개가 존재하면 나중에 파라미터를 쓴 쪽이 이기는데, 어느 쪽이
        // 이겼는지 알 방법이 없어 조건을 잘못 주장하게 됩니다.
        //
        // 런타임에 붙이는 것을 택한 이유는 씬 배선을 늘리지 않기 위해서입니다. 이
        // 컴포넌트는 직렬화할 값이 없고, Play 중에는 인스펙터에 그대로 보이므로
        // 필요하면 손으로 값을 바꿔 볼 수도 있습니다.
        public void Attach(GameObject host)
        {
            if (_simulator != null)
            {
                return;
            }

            // FindFirstObjectByType 이 아니라 FindAnyObjectByType 입니다. 앞의 것은
            // 인스턴스 ID 순서에 의존해서 Unity 6 에서 폐기되었고, 애초에 우리에게
            // 필요한 것도 "정렬해서 첫 번째"가 아니라 "이미 하나 있으면 그것"입니다.
            _simulator = UnityEngine.Object.FindAnyObjectByType<NetworkSimulator>();
            if (_simulator != null)
            {
                Debug.Log($"[Net] 네트워크 조건: 씬의 NetworkSimulator 를 사용합니다 ({_simulator.gameObject.name})");
                return;
            }

            if (host == null)
            {
                Debug.LogWarning("[Net] 네트워크 조건: 붙일 오브젝트가 없어 시뮬레이터를 준비하지 못했습니다.");
                return;
            }

            _simulator = host.AddComponent<NetworkSimulator>();
            Debug.Log($"[Net] 네트워크 조건: {host.name} 에 NetworkSimulator 를 추가했습니다");
        }

        public void Detach()
        {
            if (_simulator == null)
            {
                return;
            }

            Clear();
            _simulator = null;
        }

        // packetDelayMs 는 단방향 값입니다. 시뮬레이터 스테이지는 이 피어의 송신과 수신
        // 양쪽에 걸리므로, 이 피어가 겪는 왕복 지연은 이 값의 두 배가 됩니다.
        public void Apply(int packetDelayMs, int packetJitterMs, int packetLossPercent)
        {
            if (_simulator == null)
            {
                return;
            }

            _preset.PacketDelayMs = packetDelayMs;
            _preset.PacketJitterMs = packetJitterMs;
            _preset.PacketLossPercent = packetLossPercent;

            // 주기적 드롭은 쓰지 않습니다. n 번째 패킷마다 버리는 방식이라 재현성은
            // 좋지만, 실제 회선의 손실은 그런 규칙성을 갖지 않습니다.
            _preset.PacketLossInterval = 0;

            // 대입하는 순간 setter 가 파라미터를 드라이버에 밀어 넣습니다. 같은
            // 인스턴스를 다시 대입해도 값은 반영됩니다.
            _simulator.ConnectionPreset = _preset;

            PacketDelayMs = packetDelayMs;
            PacketJitterMs = packetJitterMs;
            PacketLossPercent = packetLossPercent;
            IsApplied = true;
        }

        public void Clear()
        {
            PacketDelayMs = 0;
            PacketJitterMs = 0;
            PacketLossPercent = 0;
            IsApplied = false;

            if (_simulator == null)
            {
                return;
            }

            _simulator.ConnectionPreset = NetworkSimulatorPresets.None;
        }
    }
}
