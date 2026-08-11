using Blast.Simulation;
using UnityEngine;

namespace Blast.Game
{
    // 계층: Game (합성 루트). CharacterTuning 을 에디터에서 편집하기 위한 껍데기입니다.
    //
    // 로직은 없습니다. 직렬화된 필드를 읽어 Simulation 의 순수 구조체로 옮기는 것이
    // 전부입니다. 자체 엔진에서 초기화 시 ini 나 json 을 읽어 설정 구조체를 채우던
    // 것과 같은 역할이고, 차이는 에디터가 인스펙터 UI 와 애셋 파이프라인을
    // 대신 제공한다는 점뿐입니다.
    //
    // 씬이나 프리팹이 아니라 독립 애셋인 이유는 둘입니다.
    //   1. 씬 YAML 에 값이 섞이지 않습니다. 씬 파일은 배치만 담습니다
    //   2. Play 모드에서 바꾼 값이 Play 종료 후에도 남습니다. 씬 오브젝트의
    //      직렬화 값은 종료 시 되돌아가므로 튜닝 용도로 쓸 수 없습니다
    //
    // 주의: MPPM 가상 플레이어는 별도 프로세스라 각자 로드한 애셋 사본을 씁니다.
    // 멀티 세션이 붙어 있는 동안 값을 바꾸면 호스트에만 반영되어 클라이언트 예측이
    // 매 틱 어긋납니다. 튜닝은 단독 실행에서 하고, 저장한 뒤 재시작해서
    // 멀티 테스트를 시작하세요.
    [CreateAssetMenu(
        fileName = "CharacterTuning", menuName = "Blast/Character Tuning", order = 0)]
    public sealed class CharacterTuningAsset : ScriptableObject
    {
        // 기본값은 CharacterTuning.Default 와 같습니다. 새 애셋을 만들면
        // 리팩터링 이전 상수와 동일한 감각에서 출발합니다.

        [Header("이동")]
        [SerializeField] private float _moveSpeedPerSecond = 8f;

        [Header("중력. 아래 방향이므로 음수")]
        [SerializeField] private float _gravityPerSecond = -30f;
        [SerializeField] private float _terminalFallSpeed = -20f;

        [Header("점프")]
        [SerializeField] private float _jumpSpeedPerSecond = 13f;

        // 60Hz 기준 6틱이면 100ms. byte 는 인스펙터에서 다루기 불편해
        // int 로 받고 변환 시점에 좁힙니다.
        [Range(0, 30)]
        [SerializeField] private int _coyoteTicks = 6;

        [Header("충돌")]
        [SerializeField] private Vector2 _boxSize = new Vector2(0.4f, 0.5f);

        // 0 이나 음수가 되면 캐릭터가 지오메트리에 박혀 접지 판정이 깨집니다.
        // 조용히 망가지는 부류라 하한을 둡니다.
        [Min(MinSkinWidth)]
        [SerializeField] private float _skinWidth = 0.02f;

        // 1 에 가까울수록 평평한 바닥만 지면으로 인정합니다.
        [Range(0f, 1f)]
        [SerializeField] private float _minGroundNormalY = 0.5f;

        private const float MinSkinWidth = 0.001f;
        private const float MinBoxExtent = 0.01f;

        public CharacterTuning ToTuning()
        {
            return new CharacterTuning(
                _moveSpeedPerSecond,
                _gravityPerSecond,
                _terminalFallSpeed,
                _jumpSpeedPerSecond,
                _boxSize,
                _skinWidth,
                _minGroundNormalY,
                (byte)Mathf.Clamp(_coyoteTicks, 0, byte.MaxValue));
        }

        // Vector2 에는 Min 속성을 붙일 수 없어 여기서 걸러냅니다.
        // 박스가 0 이면 BoxCast 가 사실상 레이캐스트가 되어 모서리를 통과합니다.
        private void OnValidate()
        {
            _boxSize.x = Mathf.Max(MinBoxExtent, _boxSize.x);
            _boxSize.y = Mathf.Max(MinBoxExtent, _boxSize.y);
        }
    }
}
