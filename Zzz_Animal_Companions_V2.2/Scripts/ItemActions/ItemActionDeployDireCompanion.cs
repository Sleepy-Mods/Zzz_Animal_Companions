using UnityEngine;

namespace DireWolfMod
{
    public class ItemActionDeployDireCompanion : ItemActionSpawnVehicle
    {
        public override void ExecuteAction(ItemActionData _actionData, bool _bReleased)
        {
            var entityPlayerLocal = _actionData.invData.holdingEntity as EntityPlayerLocal;
            if (!entityPlayerLocal) return;
            if (!_bReleased) return;
            if (Time.time - _actionData.lastUseTime < this.Delay) return;
            if (Time.time - _actionData.lastUseTime < Constants.cBuildIntervall) return;

            var itemActionDataSpawnVehicle = (ItemActionSpawnVehicle.ItemActionDataSpawnVehicle)_actionData;
            if (!itemActionDataSpawnVehicle.ValidPosition) return;

            var iv = entityPlayerLocal.inventory.holdingItemItemValue;
            var entityClassID = -1;
            Vector3 rot = new Vector3(0f, entityPlayerLocal.rotation.y + 90f, 0f);

            if (iv.HasMetadata("EntityClassId"))
            {
                entityClassID = (int)iv.GetMetadata("EntityClassId");
            }
            else
            {
                if (iv.ItemClass.Properties.Values.ContainsKey("EntityClass"))
                {
                    var entityClass = iv.ItemClass.Properties.Values["EntityClass"];
                    if (!string.IsNullOrEmpty(entityClass))
                        entityClassID = EntityClass.FromString(entityClass);
                }
            }

            if (entityClassID == -1)
            {
                UnityEngine.Debug.Log("[DireWolfMod] No such entity class for deploy.");
                return;
            }

            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            {
                SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                    NetPackageManager.GetPackage<NetPackageDeployDireCompanion>().Setup(
                        entityClassID,
                        itemActionDataSpawnVehicle.Position,
                        rot,
                        entityPlayerLocal.entityId), true);
            }
            else
            {
                var entity = EntityFactory.CreateEntity(entityClassID,
                    itemActionDataSpawnVehicle.Position + Vector3.up * 0.25f,
                    rot) as EntityAlive;
                if (entity == null) return;
                GameManager.Instance.World.SpawnEntityInWorld(entity);
                entity.Buffs?.SetCustomVar("dwOwnerId", entityPlayerLocal.entityId);
            }

            if (itemActionDataSpawnVehicle.VehiclePreviewT)
            {
                UnityEngine.Object.Destroy(itemActionDataSpawnVehicle.VehiclePreviewT.gameObject);
            }

            entityPlayerLocal.RightArmAnimationUse = true;
            entityPlayerLocal.DropTimeDelay = 0.5f;
            entityPlayerLocal.inventory.DecHoldingItem(1);
            // Omit PlayOneShot to avoid requiring UnityEngine.AnimationModule at compile time
        }
    }
}


