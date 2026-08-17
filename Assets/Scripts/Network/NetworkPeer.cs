using Unity.Netcode;

namespace Blast.Network
{
    // 계층: Network. 이 피어가 어떤 역할로 돌고 있는지를 상위 계층에 알려줍니다.
    //
    // 합성 루트가 "서버일 때만 시뮬레이션한다"를 판단하려면 이 값이 필요한데,
    // NetworkManager 를 직접 읽게 하면 Blast.Game 이 NGO 를 참조하게 됩니다.
    //
    // 값을 캐싱하지 않고 매번 조회하는 이유:
    //   캐싱하면 누가 언제 갱신하는가라는 문제가 생기고, 갱신 지점을 한 곳이라도
    //   빠뜨리면 역할이 틀린 채로 프레임이 돕니다. 그 형태의 버그는 "호스트에서만
    //   되는" 증상으로 나타나 원인 추적이 오래 걸립니다. NetworkManager 의 플래그
    //   조회는 필드 읽기 수준이라 프레임당 몇 번 부르는 비용은 무시할 수 있습니다.
    //
    // IsHost 를 노출하지 않는 것은 의도입니다. 서버 로직의 조건은 언제나 IsServer
    // 여야 합니다. IsHost 로 분기하는 순간 데디케이티드 서버로 돌릴 수 없게 됩니다.
    public static class NetworkPeer
    {
        public static bool IsServer
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                return manager != null && manager.IsServer;
            }
        }

        public static bool IsClient
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                return manager != null && manager.IsClient;
            }
        }
    }
}
