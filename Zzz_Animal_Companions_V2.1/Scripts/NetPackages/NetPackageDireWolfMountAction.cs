using UnityEngine;

namespace DireWolfMod
{
    public class NetPackageDireWolfMountAction : NetPackage
    {
        private int _wolfId;
        private byte _action; // 1=mount, 0=dismount

        public NetPackageDireWolfMountAction Setup(int wolfId, byte action)
        {
            _wolfId = wolfId;
            _action = action;
            return this;
        }

        public override void read(PooledBinaryReader br)
        {
            _wolfId = br.ReadInt32();
            _action = br.ReadByte();
        }

        public override void write(PooledBinaryWriter bw)
        {
            base.write(bw);
            bw.Write(_wolfId);
            bw.Write(_action);
        }

        public override int GetLength()
        {
            return 8;
        }

        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world == null) return;

            // Server-authoritative only
            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            {
                // Relay to server if somehow received on client
                SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(NetPackageManager.GetPackage<NetPackageDireWolfMountAction>().Setup(_wolfId, _action), false);
                return;
            }

            var wolf = world.GetEntity(_wolfId) as EntityAlive;
            var senderId = -1;
            try { senderId = Sender != null ? Sender.entityId : -1; } catch { senderId = -1; }
            var rider = world.GetEntity(senderId) as EntityPlayer;
            if (wolf == null || rider == null) return;

            // Ownership validation
            int ownerId = 0;
            try { ownerId = (int)(wolf.Buffs?.GetCustomVar("dwOwnerId") ?? 0f); } catch { ownerId = 0; }
            if (ownerId != rider.entityId) return;

            // Single rider rule
            int currentMounted = 0; int currentRider = 0;
            try { currentMounted = (int)(wolf.Buffs?.GetCustomVar("dwMounted") ?? 0f); } catch { }
            try { currentRider = (int)(wolf.Buffs?.GetCustomVar("dwRiderId") ?? 0f); } catch { }

            bool wantMount = _action == 1;
            if (wantMount)
            {
                if (currentMounted == 1 && currentRider != rider.entityId) return;
                wolf.Buffs?.SetCustomVar("dwMounted", 1);
                wolf.Buffs?.SetCustomVar("dwRiderId", rider.entityId);
                try { wolf.navigator?.clearPath(); } catch { }
                // Seat rider on server to minimize jitter
                rider.SetPosition(wolf.position + new Vector3(0, 0.5f, 0));
            }
            else
            {
                if (currentMounted == 1 && currentRider != rider.entityId) return;
                wolf.Buffs?.SetCustomVar("dwMounted", 0);
                wolf.Buffs?.SetCustomVar("dwRiderId", 0);
            }
        }
    }
}


