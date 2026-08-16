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
        // Play 중에만 매 프레임 다시 그립니다. 편집 중에는 갱신할 것이 없습니다.
        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
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
                EditorGUILayout.LabelField("알파", driver.Alpha.ToString("F3"));
                EditorGUILayout.LabelField("누산기 잔여", driver.AccumulatorRemainder.ToString("F5"));

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("네트워크 (읽기 전용)", EditorStyles.boldLabel);

                // 서버만 시뮬레이션합니다. 이 토글이 꺼져 있는데 캐릭터가 움직이면
                // 클라이언트가 몰래 시뮬레이션하고 있다는 뜻이라 즉시 버그입니다.
                EditorGUILayout.Toggle("서버 권위 보유", driver.IsServerPeer);
                EditorGUILayout.LabelField("추적 중인 플레이어", driver.TrackedPlayerCount.ToString());
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

            // 클라이언트는 위치와 방향만 받습니다. 나머지 필드를 보고 "속도가 0 인데
            // 움직인다"고 오해하지 않도록 미리 알립니다.
            if (!driver.IsServerPeer && driver.TrackedPlayerCount > 0)
            {
                EditorGUILayout.HelpBox(
                    "클라이언트에서는 위치와 방향만 서버에서 받습니다. "
                    + "속도, 접지, 코요테 잔여 틱은 갱신되지 않습니다.",
                    MessageType.None);
            }

            // 이번 프레임 틱 수가 상한에 계속 붙어 있으면 시뮬레이션이 프레임을
            // 따라가지 못하고 시간을 버리는 중이라는 뜻입니다.
            if (driver.LastFrameTickCount >= SimulationConstants.MaxTicksPerFrame)
            {
                EditorGUILayout.HelpBox(
                    "프레임당 틱 수가 상한에 걸렸습니다. 누산 시간이 버려지고 있습니다.",
                    MessageType.Warning);
            }
        }
    }
}
