using UnityEngine;

namespace Blast.Simulation
{
    // 계층: Simulation. 캐릭터 컨트롤러의 튜닝 파라미터 묶음입니다.
    //
    // PlayerState 와 구분해야 합니다. 상태는 매 틱 시뮬레이션이 쓰는 값이고,
    // 튜닝은 사람이 정해 세션 내내 바뀌지 않는 설계 데이터입니다.
    // 상태를 직렬화하면 씬 파일에 런타임 값이 저장되어 오염되지만, 튜닝은
    // 직렬화해야 게임을 멈추지 않고 조정할 수 있습니다. 둘에 같은 규칙을
    // 적용하면 안 됩니다.
    //
    // ScriptableObject 가 아니라 순수 struct 인 이유는 셋입니다.
    //   1. Simulation 이 UnityEngine.Object 에 묶이지 않습니다
    //   2. EditMode 테스트에서 애셋 없이 임의 튜닝을 주입할 수 있습니다
    //   3. 값 복사라 Step 이 도는 도중에 외부에서 바뀔 수 없습니다
    // 에디터에서 편집 가능한 애셋 껍데기는 Game/CharacterTuningAsset 에 있습니다.
    //
    // 값은 전부 초당 단위입니다. 틱당 단위로 박으면 틱레이트를 바꿀 때
    // 전부 다시 튜닝해야 합니다. docs/project_context.md 3번 참조.
    //
    // readonly struct 이므로 in 파라미터로 넘겨도 방어적 복사본이 생기지 않습니다.
    // 일반 struct 였다면 in 으로 받은 값에 프로퍼티를 읽을 때마다 복사가 일어납니다.
    public readonly struct CharacterTuning
    {
        public readonly float MoveSpeedPerSecond;
        public readonly float GravityPerSecond;
        public readonly float TerminalFallSpeed;
        public readonly float JumpSpeedPerSecond;

        // 충돌 박스 크기입니다. 클라이언트와 서버가 반드시 같은 값을 써야 합니다.
        // 애셋 하나가 빌드에 포함되므로 프리팹이나 씬에 따라 갈라지지 않습니다.
        // 프리팹마다 다른 값을 넣을 수 있게 되는 순간 예측과 서버 결과가 갈라집니다.
        public readonly Vector2 BoxSize;

        // 표면에서 띄워둘 여유입니다. 히트 지점에 정확히 붙이면 부동소수점 오차로
        // 지오메트리 안쪽에 들어가고, 다음 틱 캐스트가 콜라이더 내부에서 시작해
        // 거리 0 에 쓰레기 법선을 받거나 아예 놓칩니다. 끼거나 뚫거나 둘 중 하나입니다.
        public readonly float SkinWidth;

        // 벽을 바닥으로 오인하지 않기 위한 최소 법선 y 입니다.
        public readonly float MinGroundNormalY;

        // 지면에서 벗어난 뒤 점프를 허용하는 틱 수입니다. 60Hz 기준 6틱이면 100ms.
        public readonly byte CoyoteTicks;

        // 파생값입니다. 캐릭터는 항상 스킨만큼 떠 있으므로 접지 프로브는
        // 그보다 멀리 봐야 합니다. 독립 필드로 두면 스킨과 어긋났을 때
        // 접지가 조용히 깨지므로 계산으로만 얻습니다.
        public float GroundProbeDistance => SkinWidth * 2f;

        public CharacterTuning(
            float moveSpeedPerSecond,
            float gravityPerSecond,
            float terminalFallSpeed,
            float jumpSpeedPerSecond,
            Vector2 boxSize,
            float skinWidth,
            float minGroundNormalY,
            byte coyoteTicks)
        {
            MoveSpeedPerSecond = moveSpeedPerSecond;
            GravityPerSecond = gravityPerSecond;
            TerminalFallSpeed = terminalFallSpeed;
            JumpSpeedPerSecond = jumpSpeedPerSecond;
            BoxSize = boxSize;
            SkinWidth = skinWidth;
            MinGroundNormalY = minGroundNormalY;
            CoyoteTicks = coyoteTicks;
        }

        // 애셋이 할당되지 않았을 때의 대체값이자 테스트 기본값입니다.
        // 점프 속도 13 은 sqrt(2 * 30 * 2.8) 을 반올림한 값이고, 중력 30 에서
        // 대략 2.8 유닛 높이까지 오릅니다.
        public static CharacterTuning Default => new CharacterTuning(
            moveSpeedPerSecond: 8f,
            gravityPerSecond: -30f,
            terminalFallSpeed: -20f,
            jumpSpeedPerSecond: 13f,
            boxSize: new Vector2(0.4f, 0.5f),
            skinWidth: 0.02f,
            minGroundNormalY: 0.5f,
            coyoteTicks: 6);
    }
}
