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
                EditorGUILayout.LabelField("틱", driver.CurrentTick.ToString());
                EditorGUILayout.LabelField("이번 프레임 틱 수", driver.LastFrameTickCount.ToString());
                EditorGUILayout.LabelField("알파", driver.Alpha.ToString("F3"));
                EditorGUILayout.LabelField("누산기 잔여", driver.AccumulatorRemainder.ToString("F5"));

                EditorGUILayout.Space();
                EditorGUILayout.Vector2Field("위치", state.Position);
                EditorGUILayout.Vector2Field("속도", state.Velocity);
                EditorGUILayout.Toggle("접지", state.IsGrounded);
                EditorGUILayout.LabelField("코요테 잔여 틱", state.CoyoteTicksRemaining.ToString());
                EditorGUILayout.LabelField("바라보는 방향", state.FacingDirection.ToString());
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
