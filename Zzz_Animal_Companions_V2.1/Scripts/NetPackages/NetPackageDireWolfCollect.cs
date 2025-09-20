using UnityEngine;

namespace DireWolfMod
{
    public class NetPackageDireWolfCollect : NetPackage
    {
        private int _wolfId;
        private int _playerId;

        public NetPackageDireWolfCollect Setup(int wolfId, int playerId)
        {
            _wolfId = wolfId;
            _playerId = playerId;
            return this;
        }

        public override void read(PooledBinaryReader br)
        {
            _wolfId = br.ReadInt32();
            _playerId = br.ReadInt32();
        }

        public override void write(PooledBinaryWriter bw)
        {
            base.write(bw);
            bw.Write(_wolfId);
            bw.Write(_playerId);
        }

        public override int GetLength()
        {
            return 12;
        }

        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world == null) return;

            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            {
                SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(NetPackageManager.GetPackage<NetPackageDireWolfCollect>().Setup(_wolfId, _playerId), false);
                return;
            }

            var wolf = world.GetEntity(_wolfId) as EntityAlive;
            var player = world.GetEntity(_playerId) as EntityPlayer;
            if (wolf == null || player == null) return;

            int ownerId = 0;
            try { ownerId = (int)(wolf.Buffs?.GetCustomVar("dwOwnerId") ?? 0f); } catch { ownerId = 0; }
            if (ownerId != player.entityId) return;

            GameManager.Instance.World.RemoveEntity(_wolfId, EnumRemoveEntityReason.Despawned);
        }
    }
}


