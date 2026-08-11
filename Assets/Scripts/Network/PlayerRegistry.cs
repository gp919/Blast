using System;
using System.Collections.Generic;

namespace Blast.Network
{
    // 계층: Network. 스폰된 플레이어 목록과 로컬 소유자를 들고 있는 창구입니다.
    //
    // 왜 이것이 필요한가:
    //   플레이어는 씬에 미리 있지 않고 접속 후에 스폰됩니다. 합성 루트가
    //   [SerializeField] 로 물어둘 대상이 없다는 뜻입니다. 그렇다고 TickDriver 가
    //   NetworkManager.SpawnManager 를 뒤지게 하면 Blast.Game 이 NGO 를 참조하게
    //   되고, 매 프레임 탐색 비용도 생깁니다. 스폰된 쪽이 등록하고 상위가 구독하는
    //   방향이면 NGO 타입이 이 어셈블리 밖으로 나가지 않습니다.
    //
    // static 인 것에 대해:
    //   Domain Reload 가 켜져 있어(project_context.md 저장소 특이사항) Play 진입마다
    //   초기화됩니다. 그래도 Despawn 에서 확실히 해제하고, 씬 전환 등으로 해제가
    //   누락될 경우를 대비해 Clear 경로를 열어둡니다.
    public static class PlayerRegistry
    {
        private static readonly List<PlayerHandle> _players = new List<PlayerHandle>(4);

        // 스폰과 디스폰 시점에만 발생합니다. 매 프레임 폴링하지 않기 위한 것입니다.
        public static event Action<PlayerHandle> PlayerSpawned;
        public static event Action<PlayerHandle> PlayerDespawned;

        public static IReadOnlyList<PlayerHandle> Players => _players;

        // 이 피어가 입력으로 조종하는 플레이어입니다. 접속 전에는 null 입니다.
        public static PlayerHandle LocalPlayer { get; private set; }

        internal static void Register(PlayerHandle player)
        {
            if (player == null || _players.Contains(player))
            {
                return;
            }

            _players.Add(player);

            if (player.IsLocalOwner)
            {
                LocalPlayer = player;
            }

            PlayerSpawned?.Invoke(player);
        }

        internal static void Unregister(PlayerHandle player)
        {
            if (player == null || !_players.Remove(player))
            {
                return;
            }

            if (LocalPlayer == player)
            {
                LocalPlayer = null;
            }

            PlayerDespawned?.Invoke(player);
        }

        // 접속을 끊었다가 다시 붙는 경로에서 잔여 항목이 남지 않도록 하는 안전장치입니다.
        public static void Clear()
        {
            _players.Clear();
            LocalPlayer = null;
        }
    }
}
