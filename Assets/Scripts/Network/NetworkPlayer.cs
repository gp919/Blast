using Unity.Netcode;

namespace Blast.Network
{
    // 계층: Network. 스폰된 플레이어 프리팹에 붙는 컴포넌트입니다.
    //
    // 스폰 시점에 PlayerHandle 을 만들어 레지스트리에 넣습니다. 상위 계층은 핸들만
    // 보고 이 타입을 모릅니다. 이유는 PlayerHandle 주석 참조.
    //
    // 이 컴포넌트는 시뮬레이션을 돌리지 않습니다. 틱 드라이버는 씬에 하나뿐이고,
    // 여기서 각자 Update 를 돌리면 누산기와 틱 카운터가 엔티티마다 갈라집니다.
    // Game/TickDriver 주석 참조.
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        private PlayerHandle _handle;

        public override void OnNetworkSpawn()
        {
            // IsOwner 와 OwnerClientId 는 이 시점에 확정되어 있습니다.
            _handle = new PlayerHandle(gameObject, OwnerClientId, IsOwner, (int)OwnerClientId);
            PlayerRegistry.Register(_handle);
        }

        public override void OnNetworkDespawn()
        {
            PlayerRegistry.Unregister(_handle);
            _handle = null;
        }
    }
}
