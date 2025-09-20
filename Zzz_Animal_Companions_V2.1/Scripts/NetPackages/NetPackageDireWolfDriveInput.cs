using UnityEngine;

namespace DireWolfMod
{
    public class NetPackageDireWolfDriveInput : NetPackage
    {
        private int _wolfId;
        private float _v;
        private float _h;
        private byte _flags; // bit 0 = sprint

        public NetPackageDireWolfDriveInput Setup(int wolfId, float v, float h, byte flags)
        {
            _wolfId = wolfId;
            _v = Mathf.Clamp(v, -1f, 1f);
            _h = Mathf.Clamp(h, -1f, 1f);
            _flags = (byte)(flags & 0x1);
            return this;
        }

        public override void read(PooledBinaryReader br)
        {
            _wolfId = br.ReadInt32();
            _v = br.ReadSingle();
            _h = br.ReadSingle();
            _flags = br.ReadByte();
        }

        public override void write(PooledBinaryWriter bw)
        {
            base.write(bw);
            bw.Write(_wolfId);
            bw.Write(_v);
            bw.Write(_h);
            bw.Write(_flags);
        }

        public override int GetLength()
        {
            return 4 + 4 + 4 + 1 + 4; // approximate
        }

        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world == null) return;

            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            {
                SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                    NetPackageManager.GetPackage<NetPackageDireWolfDriveInput>().Setup(_wolfId, _v, _h, _flags), false);
                return;
            }

            var wolf = world.GetEntity(_wolfId) as EntityAlive;
            var senderId = -1;
            try { senderId = Sender != null ? Sender.entityId : -1; } catch { senderId = -1; }
            var rider = world.GetEntity(senderId) as EntityPlayer;
            if (wolf == null || rider == null) return;

            int rid = 0; int mounted = 0;
            try { rid = (int)(wolf.Buffs?.GetCustomVar("dwRiderId") ?? 0f); } catch { }
            try { mounted = (int)(wolf.Buffs?.GetCustomVar("dwMounted") ?? 0f); } catch { }
            if (mounted != 1 || rid != rider.entityId) return;

            // Apply input server-side by updating CVars consumed by driving logic
            wolf.Buffs?.SetCustomVar("dwMoveV", _v);
            wolf.Buffs?.SetCustomVar("dwMoveH", _h);
            wolf.Buffs?.SetCustomVar("dwSprint", (_flags & 1) != 0 ? 1 : 0);
        }
    }
}


