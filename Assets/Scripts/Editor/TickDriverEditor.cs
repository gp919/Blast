using Blast.Core;
using Blast.Game;
using UnityEditor;
using UnityEngine;

namespace Blast.Editor
{
    // 계층: Editor 전용. 빌드에 포함되지 않습니다.
    //
    // 시뮬레이션 상태를 Inspector 에 읽기 전용으로 표시합니다.
    // TickDriver 에 [SerializeField] 를 붙여 노출하는 방법도 있지만 쓰지 않습니다.
    // 그렇게 하면 런타임 값이 씬 파일에 저장되어 YAML 이 오염되고, 에디터에서
    // 실수로 바꾼 값이 그대로 남습니다. 근거는 docs/project_context.md 3번 참조.
    [CustomEditor(typeof(TickDriver))]
    public sealed class TickDriverEditor : UnityEditor.Editor
    {
        // 매 프레임 다시 그리지 않고 초당 10회로 제한합니다. 근거는
        // InspectorRepaintThrottle 주석 참조.
        private InspectorRepaintThrottle _repaintThrottle;

        // 갱신 간격을 늘리면 순간적으로만 나타나는 값을 놓칩니다. 틱 수가 한
        // 프레임 동안만 상한에 붙었다가 돌아오면 화면에는 잡히지 않습니다.
        // 그래서 표시와 관측을 분리합니다. 그리는 것은 초당 10회로 줄이되,
        // 값을 읽는 것은 에디터 갱신 주기마다 계속합니다. 정수 비교 두 번이라
        // 비용이 사실상 없습니다.
        //
        // 에디터 갱신이 게임 프레임과 정확히 1대1로 대응한다는 보장은 없으므로
        // 이 값은 표본입니다. 다만 인스펙터가 그려지는 시점에만 읽는 것보다는
        // 훨씬 촘촘합니다.
        private int _peakTickCount;
        private bool _sawTickCap;

        private void OnEnable()
        {
            _repaintThrottle = new InspectorRepaintThrottle(this, 0.1);
            _repaintThrottle.Enable();
            EditorApplication.update += SampleTickCount;
        }

        private void OnDisable()
        {
            EditorApplication.update -= SampleTickCount;
            _repaintThrottle.Disable();
        }

        private void SampleTickCount()
        {
            // Play 를 벗어나면 지난 세션의 관측값을 들고 있을 이유가 없습니다.
            if (!Application.isPlaying)
            {
                _peakTickCount = 0;
                _sawTickCap = false;
                return;
            }

            TickDriver driver = target as TickDriver;
            if (driver == null)
            {
                return;
            }

            int ticks = driver.LastFrameTickCount;
            if (ticks > _peakTickCount)
            {
                _peakTickCount = ticks;
            }

            if (ticks >= SimulationConstants.MaxTicksPerFrame)
            {
                _sawTickCap = true;
            }
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (!Application.isPlaying)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "Play 중에 시뮬레이션 상태가 여기 표시됩니다.", MessageType.None);
                return;
            }

            TickDriver driver = (TickDriver)target;
            PlayerState state = driver.CurrentState;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("시뮬레이션 상태 (읽기 전용)", EditorStyles.boldLabel);

            // DisabledScope 로 감싸 편집을 막습니다.
            // 시뮬레이션 상태를 에디터에서 바꿀 수 있으면 관측이 아니라 개입이 됩니다.
            using (new EditorGUI.DisabledScope(true))
            {
                // 접속 전에는 구동 대상이 없어 틱이 진행하지 않습니다.
                // 캐릭터가 안 움직일 때 원인을 여기서 먼저 가릅니다.
                EditorGUILayout.Toggle("로컬 플레이어 스폰됨", driver.HasLocalPlayer);
                EditorGUILayout.LabelField("틱", driver.CurrentTick.ToString());
                EditorGUILayout.LabelField("이번 프레임 틱 수", driver.LastFrameTickCount.ToString());

                // 표시 간격 사이에 지나간 최대치입니다. 프레임이 튄 흔적은
                // 여기에만 남습니다.
                EditorGUILayout.LabelField("관측 최대 틱 수", _peakTickCount.ToString());
                EditorGUILayout.LabelField("알파", driver.Alpha.ToString("F3"));
                EditorGUILayout.LabelField("누산기 잔여", driver.AccumulatorRemainder.ToString("F5"));

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("네트워크 (읽기 전용)", EditorStyles.boldLabel);

                // 서버만 시뮬레이션합니다. 이 토글이 꺼져 있는데 캐릭터가 움직이면
                // 클라이언트가 몰래 시뮬레이션하고 있다는 뜻이라 즉시 버그입니다.
                EditorGUILayout.Toggle("서버 권위 보유", driver.IsServerPeer);
                EditorGUILayout.LabelField(
                    "권한 모드", driver.IsClientAuthorityMode ? "클라 권위" : "서버 권위");
                EditorGUILayout.LabelField("추적 중인 플레이어", driver.TrackedPlayerCount.ToString());

                // 서버 권위면 서버에서 전원, 클라 권위면 각 피어에서 1 이 나옵니다.
                // 이 값이 두 피어에서 동시에 0 이 아닌 같은 캐릭터를 가리키면
                // 둘이 서로 위치를 덮어쓰는 중입니다.
                EditorGUILayout.LabelField(
                    "여기서 시뮬레이션 중", driver.SimulatedHereCount.ToString());
                EditorGUILayout.LabelField("마지막 송신 입력 틱", driver.LastSentInputTick.ToString());

                // 서버에서만 올라갑니다. 서버인데 이 값이 멈춰 있으면 입력 RPC 가
                // 도달하지 않고 있다는 뜻입니다.
                EditorGUILayout.LabelField("마지막 수신 입력 틱", driver.LocalReceivedInputTick.ToString());

                EditorGUILayout.Space();
                EditorGUILayout.Vector2Field("위치", state.Position);
                EditorGUILayout.Vector2Field("속도", state.Velocity);
                EditorGUILayout.Toggle("접지", state.IsGrounded);
                EditorGUILayout.LabelField("코요테 잔여 틱", state.CoyoteTicksRemaining.ToString());
                EditorGUILayout.LabelField("바라보는 방향", state.FacingDirection.ToString());
            }

            // 시뮬레이션하지 않는 캐릭터는 위치와 방향만 받습니다. 나머지 필드를 보고
            // "속도가 0 인데 움직인다"고 오해하지 않도록 미리 알립니다.
            if (!driver.IsServerPeer && !driver.IsClientAuthorityMode && driver.TrackedPlayerCount > 0)
            {
                EditorGUILayout.HelpBox(
                    "클라이언트에서는 위치와 방향만 서버에서 받습니다. "
                    + "속도, 접지, 코요테 잔여 틱은 갱신되지 않습니다.",
                    MessageType.None);
            }

            // 틱 수가 상한에 닿으면 시뮬레이션이 프레임을 따라가지 못하고 누산
            // 시간을 버립니다. 한 번이라도 닿았으면 계속 알립니다. 순간적으로
            // 지나간 사건을 놓치면 원인을 되짚을 단서가 사라지기 때문입니다.
            if (_sawTickCap)
            {
                EditorGUILayout.HelpBox(
                    "프레임당 틱 수가 상한에 걸린 적이 있습니다. 그 프레임에서는 누산 시간이 "
                    + "버려졌습니다. 인스펙터나 프로파일러 같은 에디터 창을 열어둔 것만으로도 "
                    + "발생할 수 있으므로, 성능을 판단할 때는 창을 닫고 다시 보세요.",
                    MessageType.Warning);
            }

            // 조건을 바꾼 뒤 다시 관측하려면 기준점을 새로 잡아야 합니다.
            if (GUILayout.Button("관측값 초기화"))
            {
                _peakTickCount = 0;
                _sawTickCap = false;
            }
        }
    }
}
