using UnityEngine;

namespace DireWolfMod
{
    public class NetPackageDeployDireCompanion : NetPackage
    {
        private int entityType;
        private Vector3 pos;
        private Vector3 rot;
        private int ownerId;

        public NetPackageDeployDireCompanion Setup(int entityType, Vector3 pos, Vector3 rot, int ownerId)
        {
            this.entityType = entityType;
            this.pos = pos;
            this.rot = rot;
            this.ownerId = ownerId;
            return this;
        }

        public override void read(PooledBinaryReader br)
        {
            entityType = br.ReadInt32();
            pos = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            rot = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            ownerId = br.ReadInt32();
        }

        public override void write(PooledBinaryWriter bw)
        {
            base.write(bw);
            bw.Write(entityType);
            bw.Write(pos.x); bw.Write(pos.y); bw.Write(pos.z);
            bw.Write(rot.x); bw.Write(rot.y); bw.Write(rot.z);
            bw.Write(ownerId);
        }

        public override int GetLength()
        {
            return 64; // approximate
        }

        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world == null) return;

            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            {
                var pkg = NetPackageManager.GetPackage<NetPackageDeployDireCompanion>().Setup(entityType, pos, rot, ownerId);
                SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(pkg, true);
                return;
            }

            var entity = EntityFactory.CreateEntity(entityType, pos, rot) as EntityAlive;
            if (entity == null) return;
            GameManager.Instance.World.SpawnEntityInWorld(entity);
            entity.Buffs?.SetCustomVar("dwOwnerId", ownerId);
        }
    }
}


