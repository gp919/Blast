using Blast.Core;
using Unity.Netcode;

namespace Blast.Network
{
    // 계층: Network. InputCommand 의 전송용 표현입니다.
    //
    // 왜 Core/InputCommand 에 INetworkSerializable 을 직접 붙이지 않는가:
    //   그러면 참조 그래프 최하단인 Blast.Core 가 Unity.Netcode.Runtime 을 참조하게
    //   됩니다. Core 를 참조하는 Simulation, Input, 그리고 EditMode 테스트까지 전부
    //   NGO 에 묶입니다. 시뮬레이션을 네트워크 없이 돌려보는 경로가 막히는데,
    //   그 경로가 막히면 결정성 검증 자체를 할 수 없게 됩니다.
    //
    //   전송 표현과 시뮬레이션 표현을 분리해 두면 나중에 비트 패킹이나 델타 압축을
    //   넣을 때도 시뮬레이션 쪽 구조체를 건드리지 않습니다.
    //
    // 지금은 필드를 그대로 씁니다. 나이브 구현이 목적이라 압축하지 않습니다.
    // 3주차에 "최근 N 틱 입력 중복 전송"이 들어올 자리가 여기입니다.
    public struct NetworkInputCommand : INetworkSerializable
    {
        public uint Tick;
        public sbyte MoveX;
        public bool JumpPressed;

        public static NetworkInputCommand From(in InputCommand command)
        {
            return new NetworkInputCommand
            {
                Tick = command.Tick,
                MoveX = command.MoveX,
                JumpPressed = command.JumpPressed
            };
        }

        public InputCommand ToCommand()
        {
            return new InputCommand
            {
                Tick = Tick,
                MoveX = MoveX,
                JumpPressed = JumpPressed
            };
        }

        // 읽기와 쓰기가 같은 순서를 따라야 합니다. 한쪽만 필드를 추가하면 그 뒤의
        // 모든 값이 밀려서 읽히고, 컴파일은 통과하므로 실행 중에야 드러납니다.
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Tick);
            serializer.SerializeValue(ref MoveX);
            serializer.SerializeValue(ref JumpPressed);
        }
    }
}
