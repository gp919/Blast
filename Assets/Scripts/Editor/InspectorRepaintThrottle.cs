using UnityEditor;
using UnityEngine;

namespace Blast.Editor
{
    // 계층: Editor 전용. 빌드에 포함되지 않습니다.
    //
    // Play 중에 인스펙터를 일정 간격으로만 다시 그리게 만듭니다.
    //
    // RequiresConstantRepaint 가 true 를 반환하면 인스펙터가 게임 프레임마다
    // 통째로 다시 그려집니다. IMGUI 는 리테인드 방식이 아니라 즉시 모드이므로,
    // 레이아웃 계산과 문자열 생성이 매 프레임 처음부터 반복됩니다. 그 비용이
    // 프로파일러에서 EditorLoop 로 잡히고, 프레임 시간이 길어지면 누산기에 밀린
    // 틱이 늘어 MaxTicksPerFrame 상한에 걸립니다. 상한에 걸리면 시뮬레이션
    // 시간이 실제로 버려지므로, 관측 수단이 관측 대상을 바꾸게 됩니다.
    //
    // 사람이 눈으로 읽는 값에 초당 60회는 필요하지 않습니다. 초당 10회면 충분하고,
    // 그만큼 에디터가 게임 루프에서 가져가는 시간이 줄어듭니다.
    public sealed class InspectorRepaintThrottle
    {
        private readonly UnityEditor.Editor _owner;
        private readonly double _intervalSeconds;
        private double _nextRepaintTime;
        private bool _isEnabled;

        public InspectorRepaintThrottle(UnityEditor.Editor owner, double intervalSeconds)
        {
            _owner = owner;
            _intervalSeconds = intervalSeconds;
        }

        // 에디터의 OnEnable 에서 호출합니다.
        public void Enable()
        {
            if (_isEnabled)
            {
                return;
            }

            _isEnabled = true;
            EditorApplication.update += Tick;
        }

        // 에디터의 OnDisable 에서 반드시 호출해야 합니다. 해제하지 않으면 파괴된
        // 에디터 인스턴스를 가리키는 델리게이트가 남아 매번 예외가 납니다.
        public void Disable()
        {
            if (!_isEnabled)
            {
                return;
            }

            _isEnabled = false;
            EditorApplication.update -= Tick;
        }

        private void Tick()
        {
            // 편집 중에는 갱신할 런타임 값이 없습니다. 인스펙터는 사용자가
            // 조작할 때 알아서 다시 그려집니다.
            if (!Application.isPlaying)
            {
                return;
            }

            // 대상 오브젝트가 파괴되면 Repaint 가 예외를 던집니다. 플레이 종료나
            // 디스폰 시점에 실제로 발생하므로 막아둡니다.
            if (_owner == null || _owner.target == null)
            {
                return;
            }

            // EditorApplication.timeSinceStartup 은 에디터의 벽시계입니다.
            // Time.time 과 달리 타임스케일과 무관하므로, 게임을 일시정지해도
            // 인스펙터 갱신은 계속됩니다.
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextRepaintTime)
            {
                return;
            }

            _nextRepaintTime = now + _intervalSeconds;
            _owner.Repaint();
        }
    }
}
