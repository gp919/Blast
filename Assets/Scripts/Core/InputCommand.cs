namespace Blast.Core
{
    // 계층: Core. 한 틱 분량의 플레이어 입력입니다.
    //
    // Input 계층이 생산하고 Simulation 이 소비하며 Network 가 서버로 전송합니다.
    // 참조 그래프가 Input -> Core 뿐이라 Input 계층은 Simulation 을 볼 수 없습니다.
    // 따라서 이 타입은 Core 에 있어야만 합니다. Simulation 에 두면 컴파일되지 않습니다.
    //
    // 모든 필드는 양자화된 정수입니다. 예측 결과와 서버 결과가 일치하려면
    // 클라이언트와 서버가 같은 입력 비트를 먹어야 하기 때문입니다.
    // 나중에 게임패드 아날로그 입력을 붙이더라도 시뮬레이션에 들어오기 전에
    // 고정 단계로 양자화해서 이 구조체에 담아야 합니다.
    //
    // 이름이 PlayerInput 이 아닌 이유: Input System 패키지에 같은 이름의
    // MonoBehaviour 가 있어 Input 계층에서 참조가 모호해집니다.
    // Quake 계열에서 틱당 입력을 usercmd 로 부르던 관례를 따릅니다.
    public struct InputCommand
    {
        // 이 입력이 적용될 틱입니다.
        // 서버는 이 번호로 입력을 정렬하고, 클라이언트는 재조정 시 이 번호부터 재생합니다.
        public uint Tick;

        // 좌우 이동. -1, 0, 1 만 허용합니다.
        public sbyte MoveX;

        // 이 틱에 점프가 새로 눌렸는지 여부입니다. 눌린 상태 유지가 아니라 에지입니다.
        //
        // 주의: Input System 의 wasPressedThisFrame 을 그대로 넣으면 안 됩니다.
        // 프레임과 틱은 1대1이 아닙니다. 고프레임에서는 틱이 돌지 않는 프레임의 누름이
        // 사라지고, 저프레임에서는 한 프레임에 틱이 여러 번 돌면서 같은 누름이
        // 여러 틱에 중복 소비됩니다.
        // Input 계층이 누름을 래치해두고 다음 틱이 소비할 때 지워야 합니다.
        public bool JumpPressed;
    }
}
