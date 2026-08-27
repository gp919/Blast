using Blast.Game;
using Blast.Presentation;
using Blast.Simulation;
using UnityEditor;
using UnityEngine;

namespace Blast.Editor
{
    // 계층: Editor 전용. 빌드에 포함되지 않습니다.
    //
    // 세 가지를 합니다.
    //   1. 애니메이션이 안 바뀔 때 원인을 가른다
    //   2. 충돌 박스를 스프라이트에서 파생시킨다
    //   3. 튜닝 값을 캐릭터 키로 나눠 보여준다
    //
    // 1 의 원인은 세 갈래인데 화면만 봐서는 구분이 되지 않습니다.
    //   a. Animator 가 프리팹에 안 붙었다
    //   b. Animator Controller 의 파라미터 이름이 코드와 다르다
    //   c. 값은 들어가는데 전이 조건이 안 맞는다
    // a 와 b 는 예외 없이 조용히 무시되므로 여기서 눈으로 확인합니다.
    //
    // Animator 파라미터를 이름으로 조회하지 않고 통째로 나열하는 것이 요점입니다.
    // 코드가 쓰는 이름을 그대로 조회하면 이름이 틀렸을 때도 0 이 나와서
    // "값이 안 들어간다"로만 보입니다. 실제 목록을 옆에 놓아야 오타가 드러납니다.
    [CustomEditor(typeof(PlayerPresenter))]
    public sealed class PlayerPresenterEditor : UnityEditor.Editor
    {
        // 코드가 세팅하는 파라미터입니다. Animator Controller 쪽 이름이 이것과
        // 정확히 같아야 합니다. PlayerPresenter 의 해시 필드와 짝입니다.
        private static readonly string[] _expectedParameters =
        {
            "Speed", "IsGrounded", "VerticalVelocity"
        };

        // 충돌 박스를 스프라이트 사각형의 몇 배로 잡을 것인가.
        //
        // 둘 다 1 이 아닌 이유가 있습니다. 스프라이트 바운드는 잘라낸 픽셀이 아니라
        // 슬라이스한 칸 전체라, 32x32 칸에 캐릭터가 24 픽셀만 차 있으면 투명 여백이
        // 그대로 포함됩니다. 게다가 팔이나 머리카락은 벽에 닿아도 상관없습니다.
        // 여백까지 충돌 폭에 넣으면 눈에는 여유가 있는데 벽 앞에서 멈추고,
        // 플레이어는 이유를 알 수 없습니다.
        private float _boxWidthRatio = 0.75f;
        private float _boxHeightRatio = 1.0f;

        // 캐릭터 키를 기준으로 본 튜닝의 권장 범위입니다. 절대 수치가 아니라
        // 비율이므로 축척을 어떻게 잡든 그대로 통합니다.
        private const float RecommendedApexMin = 1.5f;
        private const float RecommendedApexMax = 2.0f;
        private const float RecommendedSpeedMin = 3f;
        private const float RecommendedSpeedMax = 4f;

        // 정점까지 걸리는 시간입니다. 이것만은 비율이 아니라 절대 시간입니다.
        // 사람 손의 반응 속도에 묶인 값이라 캐릭터가 커지든 작아지든 같습니다.
        // 짧으면 점프가 톡 튀고 길면 둥실 뜹니다.
        private const float RecommendedApexTimeMin = 0.30f;
        private const float RecommendedApexTimeMax = 0.40f;

        // 매 프레임 다시 그리지 않고 초당 10회로 제한합니다. 근거는
        // InspectorRepaintThrottle 주석 참조.
        private InspectorRepaintThrottle _repaintThrottle;

        // AssetDatabase.FindAssets 는 프로젝트의 애셋 데이터베이스 전체를 타입
        // 조건으로 질의합니다. 결과가 항상 같은데도 인스펙터가 그려질 때마다
        // 반복하면 프레임 시간을 통째로 잡아먹습니다. 한 번 찾은 참조를 들고
        // 있으면 애셋의 값이 바뀌어도 그대로 최신 값이 읽히므로, 다시 찾아야 하는
        // 경우는 애셋이 새로 생성되거나 삭제되었을 때뿐입니다.
        private CharacterTuningAsset _cachedTuningAsset;

        // Animator.parameters 는 호출할 때마다 새 배열을 할당해 돌려주는
        // 프로퍼티입니다. 파라미터 목록은 런타임에 바뀌지 않으므로 Animator
        // 참조가 달라질 때만 다시 읽습니다.
        private Animator _cachedAnimator;
        private AnimatorControllerParameter[] _cachedParameters;

        // 문자열로 프로퍼티를 찾는 비용도 그려질 때마다 낼 이유가 없습니다.
        private SerializedProperty _animatorProperty;
        private SerializedProperty _spriteRendererProperty;

        private void OnEnable()
        {
            _animatorProperty = serializedObject.FindProperty("_animator");
            _spriteRendererProperty = serializedObject.FindProperty("_spriteRenderer");

            TryLoadTuningAsset(out _cachedTuningAsset);

            _repaintThrottle = new InspectorRepaintThrottle(this, 0.1);
            _repaintThrottle.Enable();
        }

        private void OnDisable()
        {
            _repaintThrottle.Disable();

            // 다음에 인스펙터가 열릴 때 다시 찾습니다. 그 사이에 애셋이 지워졌을
            // 수 있고, 도메인 리로드를 건너뛰는 설정에서는 참조가 남아 있어도
            // 무효해질 수 있습니다.
            _cachedTuningAsset = null;
            _cachedAnimator = null;
            _cachedParameters = null;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            PlayerPresenter presenter = (PlayerPresenter)target;

            // 편집 중에는 튜닝을 주입해줄 드라이버가 없어 프리젠터가 기본값으로
            // 기즈모를 그립니다. 인스펙터를 열어둔 동안만이라도 애셋의 실제 값을
            // 넣어주면 초록 상자가 진실을 그립니다. 런타임 코드는 여전히 애셋을
            // 모르고, 여기서 넣은 값은 직렬화되지 않습니다.
            CharacterTuning tuning = CharacterTuning.Default;
            bool hasTuningAsset = TryGetTuningAsset(out CharacterTuningAsset tuningAsset);
            if (hasTuningAsset)
            {
                tuning = tuningAsset.ToTuning();
                if (!Application.isPlaying)
                {
                    presenter.SetTuning(tuning);
                }
            }

            DrawAnimationSection(presenter);
            DrawCollisionBoxSection(presenter, tuning, tuningAsset);
            DrawTuningScaleSection(tuning);
        }

        private void DrawAnimationSection(PlayerPresenter presenter)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("애니메이션 (읽기 전용)", EditorStyles.boldLabel);

            if (!presenter.HasAnimator)
            {
                EditorGUILayout.HelpBox(
                    "Animator 가 지정되지 않았습니다. 이동과 기즈모는 그대로 동작하지만 "
                    + "애니메이션은 바뀌지 않습니다.", MessageType.Info);
            }

            if (!presenter.HasSpriteRenderer)
            {
                EditorGUILayout.HelpBox(
                    "Sprite Renderer 가 지정되지 않았습니다. 캐릭터가 좌우로 뒤집히지 않습니다.",
                    MessageType.Info);
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.LabelField(
                    "코드가 쓰는 파라미터", string.Join(", ", _expectedParameters));
                EditorGUILayout.HelpBox(
                    "Play 중에 파라미터 값과 애니메이션 소스가 여기 표시됩니다.",
                    MessageType.None);
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                // 이 캐릭터를 이 피어가 시뮬레이션하는지 여부입니다. 추정이면
                // 속도와 접지가 화면에서 되읽은 값이라 정확하지 않습니다.
                EditorGUILayout.LabelField(
                    "애니메이션 소스",
                    presenter.IsAnimationEstimated ? "위치 델타 추정 (원격)" : "시뮬레이션 상태");

                // 추정 경로에서만 의미가 있습니다. 캐릭터가 움직이는데 이 값이
                // 0 에 머물면 위치 갱신 자체가 도착하지 않는다는 뜻입니다.
                EditorGUILayout.Vector2Field("추정 속도", presenter.EstimatedVelocity);
            }

            // 프리젠터가 실제로 물고 있는 Animator 를 봅니다. 계층에서 찾아오면
            // 배선이 틀렸을 때도 옆에 있는 Animator 값이 보여서 정상으로 착각합니다.
            Animator animator = _animatorProperty != null
                ? _animatorProperty.objectReferenceValue as Animator
                : null;
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                EditorGUILayout.HelpBox(
                    "Animator Controller 가 없습니다. 파라미터를 세팅해도 아무 일도 "
                    + "일어나지 않습니다.", MessageType.Warning);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Animator 파라미터", EditorStyles.boldLabel);

            AnimatorControllerParameter[] parameters = GetParameters(animator);
            if (parameters.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "파라미터가 하나도 없습니다. Animator 창에서 "
                    + string.Join(", ", _expectedParameters) + " 를 추가하세요.",
                    MessageType.Warning);
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    AnimatorControllerParameter parameter = parameters[i];
                    switch (parameter.type)
                    {
                        case AnimatorControllerParameterType.Float:
                            EditorGUILayout.LabelField(
                                parameter.name, animator.GetFloat(parameter.nameHash).ToString("F2"));
                            break;
                        case AnimatorControllerParameterType.Bool:
                            EditorGUILayout.Toggle(parameter.name, animator.GetBool(parameter.nameHash));
                            break;
                        case AnimatorControllerParameterType.Int:
                            EditorGUILayout.LabelField(
                                parameter.name, animator.GetInteger(parameter.nameHash).ToString());
                            break;
                        default:
                            // 트리거입니다. 이 프로젝트에서는 쓰지 않습니다.
                            // 재조정으로 되감긴 구간에서 다시 발사되기 때문입니다.
                            EditorGUILayout.LabelField(parameter.name, "트리거 (사용 금지)");
                            break;
                    }
                }
            }

            for (int i = 0; i < _expectedParameters.Length; i++)
            {
                if (!HasParameter(parameters, _expectedParameters[i]))
                {
                    EditorGUILayout.HelpBox(
                        $"코드가 세팅하는 파라미터 {_expectedParameters[i]} 가 Animator 에 "
                        + "없습니다. 이름이 다르면 값은 조용히 버려집니다.",
                        MessageType.Warning);
                }
            }
        }

        // 충돌 박스를 스프라이트에서 파생시키는 도구입니다.
        //
        // 방향이 "그림 -> 박스"인 것이 요점입니다. 캐릭터 애셋은 확정됐고 맵과
        // 조작감은 아직 유동적이므로, 확정된 쪽이 기준이 되고 나머지가 따라옵니다.
        //
        // 두 상자를 같은 크기로 만드는 것이 목표가 아닙니다. 실제로 게임에 영향을
        // 주는 것은 초록 상자뿐이고 노란 상자는 스프라이트가 차지하는 자리일
        // 뿐입니다. 반드시 맞아야 하는 것은 발밑입니다. 그림의 아래끝이 박스
        // 아래끝과 어긋나면 캐릭터가 지면에 뜨거나 잠겨 보입니다.
        private void DrawCollisionBoxSection(
            PlayerPresenter presenter, in CharacterTuning tuning, CharacterTuningAsset tuningAsset)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("충돌 박스 (에디터 전용)", EditorStyles.boldLabel);

            if (tuningAsset == null)
            {
                EditorGUILayout.HelpBox(
                    "CharacterTuningAsset 을 찾지 못했습니다. 박스를 그림에 맞추려면 애셋이 필요합니다.",
                    MessageType.Warning);
                return;
            }

            SpriteRenderer spriteRenderer = _spriteRendererProperty != null
                ? _spriteRendererProperty.objectReferenceValue as SpriteRenderer
                : null;
            if (spriteRenderer == null || !presenter.TryGetVisualBounds(out Bounds bounds))
            {
                EditorGUILayout.HelpBox(
                    "스프라이트가 없습니다. Sprite Renderer 를 지정하고 스프라이트를 넣으세요.",
                    MessageType.None);
                return;
            }

            Vector3 rootPosition = presenter.transform.position;
            float boxBottom = rootPosition.y - tuning.BoxSize.y * 0.5f;
            float footError = bounds.min.y - boxBottom;
            float centerError = bounds.center.x - rootPosition.x;

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField(
                    "충돌 박스 (초록)", $"{tuning.BoxSize.x:F3} x {tuning.BoxSize.y:F3}");
                EditorGUILayout.LabelField(
                    "스프라이트 (노랑)", $"{bounds.size.x:F3} x {bounds.size.y:F3}");

                // 이 둘이 0 에 가까우면 정렬이 끝난 것입니다. 크기가 서로 달라도
                // 상관없습니다.
                EditorGUILayout.LabelField("발밑 오차", $"{footError:F3}");
                EditorGUILayout.LabelField("가로 중심 오차", $"{centerError:F3}");
            }

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                _boxWidthRatio = EditorGUILayout.Slider("박스 폭 비율", _boxWidthRatio, 0.3f, 1.2f);
                _boxHeightRatio = EditorGUILayout.Slider("박스 높이 비율", _boxHeightRatio, 0.4f, 1.2f);

                Vector2 preview = new Vector2(
                    bounds.size.x * _boxWidthRatio, bounds.size.y * _boxHeightRatio);
                EditorGUILayout.LabelField("적용될 박스", $"{preview.x:F3} x {preview.y:F3}");

                if (GUILayout.Button("충돌 박스를 스프라이트에 맞추기"))
                {
                    FitCollisionBoxToSprite(presenter, spriteRenderer, tuningAsset);
                }

                using (new EditorGUI.DisabledScope(!presenter.IsSpriteOnChild))
                {
                    if (GUILayout.Button("스프라이트 발밑 정렬"))
                    {
                        AlignFeet(presenter, spriteRenderer.transform, tuning);
                    }
                }
            }

            if (!presenter.IsSpriteOnChild)
            {
                EditorGUILayout.HelpBox(
                    "Sprite Renderer 가 루트에 있어 발밑 정렬을 할 수 없습니다. 루트 위치는 "
                    + "시뮬레이션이 매 프레임 덮어씁니다. 스프라이트를 자식 오브젝트로 옮기거나, "
                    + "Sprite Editor 에서 피벗을 조정해 여백을 없애세요.",
                    MessageType.Info);
            }

            EditorGUILayout.HelpBox(
                "이 버튼은 프리팹이 아니라 CharacterTuning 애셋을 고칩니다. 모든 캐릭터에 "
                + "동시에 적용되며, 값이 갈라지면 예측과 서버 결과가 어긋나므로 그것이 의도된 "
                + "동작입니다. MPPM 세션이 붙어 있는 동안에는 쓰지 마세요. 가상 플레이어는 "
                + "각자 로드한 애셋 사본을 씁니다.",
                MessageType.None);
        }

        // 튜닝 값을 캐릭터 키로 나눠서 봅니다.
        //
        // 절대 수치는 축척이 바뀌면 의미가 사라지지만 비율은 남습니다. "초당 5 유닛"은
        // 캐릭터가 커지면 느려지고 작아지면 빨라지는데, "초당 캐릭터 키 3.5배"는
        // 축척과 무관하게 같은 감각을 가리킵니다. 그래서 조작감은 이 비율로 잡고
        // 절대값은 거기서 역산합니다.
        private static void DrawTuningScaleSection(in CharacterTuning tuning)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("튜닝 진단 (캐릭터 키 기준)", EditorStyles.boldLabel);

            float height = tuning.BoxSize.y;
            if (height <= Mathf.Epsilon)
            {
                return;
            }

            // 정점 = v^2 / (2 * |g|). 등가속도 운동이므로 틱레이트와 무관합니다.
            float gravity = Mathf.Abs(tuning.GravityPerSecond);
            float apex = gravity > Mathf.Epsilon
                ? tuning.JumpSpeedPerSecond * tuning.JumpSpeedPerSecond / (2f * gravity)
                : 0f;

            float apexInHeights = apex / height;
            float speedInHeights = tuning.MoveSpeedPerSecond / height;

            // 정점까지 걸리는 시간입니다. v = g * t 이므로 t = v / g 입니다.
            float apexTime = gravity > Mathf.Epsilon ? tuning.JumpSpeedPerSecond / gravity : 0f;

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField("캐릭터 키 (박스 높이)", $"{height:F3}");
                EditorGUILayout.LabelField(
                    "점프 정점",
                    $"{apex:F3}  =  키의 {apexInHeights:F2} 배  (권장 {RecommendedApexMin} ~ {RecommendedApexMax})");
                EditorGUILayout.LabelField(
                    "정점 도달 시간",
                    $"{apexTime:F3} 초  (권장 {RecommendedApexTimeMin} ~ {RecommendedApexTimeMax})");
                EditorGUILayout.LabelField(
                    "이동 속도",
                    $"{tuning.MoveSpeedPerSecond:F2}/s  =  초당 키의 {speedInHeights:F2} 배  (권장 {RecommendedSpeedMin} ~ {RecommendedSpeedMax})");
            }

            bool apexOff = apexInHeights < RecommendedApexMin || apexInHeights > RecommendedApexMax;
            bool speedOff = speedInHeights < RecommendedSpeedMin || speedInHeights > RecommendedSpeedMax;
            bool timeOff = apexTime < RecommendedApexTimeMin || apexTime > RecommendedApexTimeMax;

            if (!apexOff && !speedOff && !timeOff)
            {
                return;
            }

            // 목표 두 개(정점 높이, 정점 도달 시간)에서 중력과 점프 속도를 함께
            // 역산합니다. 중력을 고정해두고 점프 속도만 맞추면 캐릭터 크기가 바뀔 때
            // 점프가 톡 튀거나 둥실 뜨게 됩니다. 등가속도 운동에서
            //   h = g * t^2 / 2  ->  g = 2h / t^2
            //   v = g * t        ->  v = 2h / t
            float targetApex = (RecommendedApexMin + RecommendedApexMax) * 0.5f * height;
            float targetTime = (RecommendedApexTimeMin + RecommendedApexTimeMax) * 0.5f;
            float suggestedGravity = -2f * targetApex / (targetTime * targetTime);
            float suggestedJump = 2f * targetApex / targetTime;
            float suggestedMove = (RecommendedSpeedMin + RecommendedSpeedMax) * 0.5f * height;

            // 종단 낙하 속도는 점프 속도의 약 2배로 둡니다. 이보다 느리면 긴 낙하가
            // 둥실거리고, 훨씬 빠르면 한 틱에 이동하는 거리가 커져 얇은 바닥을
            // 지나칠 위험이 생깁니다.
            float suggestedTerminal = -2f * suggestedJump;

            EditorGUILayout.HelpBox(
                "현재 박스 크기 기준으로는 아래가 출발점입니다. CharacterTuning 애셋에 "
                + "직접 넣고 플레이하면서 조정하세요.\n"
                + $"  이동 속도    {suggestedMove:F2}\n"
                + $"  중력         {suggestedGravity:F2}\n"
                + $"  점프 속도    {suggestedJump:F2}\n"
                + $"  종단 낙하    {suggestedTerminal:F2}",
                MessageType.Info);
        }

        // 충돌 박스를 스프라이트에서 파생시킵니다. 박스를 바꾸면 발밑 기준선도
        // 함께 움직이므로, 스프라이트가 자식에 있으면 정렬까지 이어서 합니다.
        private void FitCollisionBoxToSprite(
            PlayerPresenter presenter, SpriteRenderer spriteRenderer, CharacterTuningAsset tuningAsset)
        {
            if (!presenter.TryGetVisualBounds(out Bounds bounds))
            {
                return;
            }

            Vector2 boxSize = new Vector2(
                bounds.size.x * _boxWidthRatio, bounds.size.y * _boxHeightRatio);

            // SerializedObject 로 쓰면 Undo 등록과 애셋 더티 표시가 함께 처리됩니다.
            // 필드에 직접 대입하면 에디터가 변경을 모르고 저장되지 않습니다.
            SerializedObject serializedAsset = new SerializedObject(tuningAsset);
            SerializedProperty boxSizeProperty = serializedAsset.FindProperty("_boxSize");
            if (boxSizeProperty == null)
            {
                Debug.LogError(
                    "CharacterTuningAsset 에 _boxSize 필드가 없습니다. 필드 이름이 바뀌었는지 확인하세요.");
                return;
            }

            boxSizeProperty.vector2Value = boxSize;
            serializedAsset.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            // 갱신된 박스를 프리젠터에 즉시 반영해야 다음 줄의 정렬이 새 값으로 돕니다.
            CharacterTuning updated = tuningAsset.ToTuning();
            presenter.SetTuning(updated);

            if (presenter.IsSpriteOnChild)
            {
                AlignFeet(presenter, spriteRenderer.transform, updated);
            }

            SceneView.RepaintAll();
            Debug.Log($"[Tool] 충돌 박스를 스프라이트에 맞춤: {boxSize.x:F3} x {boxSize.y:F3}", tuningAsset);
        }

        // 그림의 아래끝을 충돌 박스의 아래끝에, 가로 중심을 박스 중심에 맞춥니다.
        private static void AlignFeet(
            PlayerPresenter presenter, Transform visual, in CharacterTuning tuning)
        {
            if (!presenter.TryGetVisualBounds(out Bounds bounds))
            {
                return;
            }

            Undo.RecordObject(visual, "스프라이트 발밑 정렬");

            Vector3 rootPosition = presenter.transform.position;
            float boxBottom = rootPosition.y - tuning.BoxSize.y * 0.5f;

            Vector3 position = visual.position;
            position.y += boxBottom - bounds.min.y;
            position.x += rootPosition.x - bounds.center.x;
            visual.position = position;

            // 프리팹 인스턴스라면 변경을 오버라이드로 기록해야 저장됩니다.
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
            EditorUtility.SetDirty(visual);
            SceneView.RepaintAll();
        }

        // 캐시된 튜닝 애셋을 돌려줍니다. 캐시가 비어 있을 때만 다시 찾습니다.
        private bool TryGetTuningAsset(out CharacterTuningAsset asset)
        {
            if (_cachedTuningAsset != null)
            {
                asset = _cachedTuningAsset;
                return true;
            }

            // Play 중에는 애셋이 새로 만들어지지 않습니다. 여기서 재조회를 허용하면
            // 애셋이 없는 프로젝트에서 매 프레임 전체 검색이 돌게 됩니다.
            if (Application.isPlaying)
            {
                asset = null;
                return false;
            }

            bool found = TryLoadTuningAsset(out _cachedTuningAsset);
            asset = _cachedTuningAsset;
            return found;
        }

        // Animator 참조가 그대로면 이전에 읽어둔 배열을 그대로 씁니다.
        // 이 프로젝트는 런타임에 Animator Controller 를 교체하지 않으므로
        // 참조 비교만으로 충분합니다.
        private AnimatorControllerParameter[] GetParameters(Animator animator)
        {
            if (_cachedAnimator != animator || _cachedParameters == null)
            {
                _cachedAnimator = animator;
                _cachedParameters = animator.parameters;
            }

            return _cachedParameters;
        }

        // 튜닝 애셋은 프로젝트에 하나뿐이어야 합니다. 여럿이면 어느 것이 빌드에
        // 들어가는지 코드만 봐서는 알 수 없으므로 알립니다.
        private static bool TryLoadTuningAsset(out CharacterTuningAsset asset)
        {
            string[] guids = AssetDatabase.FindAssets("t:CharacterTuningAsset");
            if (guids.Length == 0)
            {
                asset = null;
                return false;
            }

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            asset = AssetDatabase.LoadAssetAtPath<CharacterTuningAsset>(path);
            return asset != null;
        }

        private static bool HasParameter(AnimatorControllerParameter[] parameters, string name)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == name)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
