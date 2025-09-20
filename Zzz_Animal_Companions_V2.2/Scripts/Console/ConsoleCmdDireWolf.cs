using System;
using System.Collections.Generic;
using UnityEngine;

namespace DireWolfMod
{
    public class ConsoleCmdDireWolf : ConsoleCmdAbstract
    {
        public override string[] getCommands()
        {
            return new[] { "dw" };
        }

        public override string getDescription()
        {
            return "DireWolf multiplayer control: mount, drive, deploy, collect";
        }

        public override string getHelp()
        {
            return "dw mount <wolfId> <0|1> | dw drive <wolfId> <v> <h> <flags> | dw deploy <entityClassId> <x> <y> <z> <rotY> | dw collect <wolfId>";
        }

        public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
        {
            try
            {
                if (_params == null || _params.Count == 0) return;
                var world = GameManager.Instance?.World; if (world == null) return;
                var senderEntityId = -1;
                try { if (!object.ReferenceEquals(_senderInfo, null) && !object.ReferenceEquals(_senderInfo.RemoteClientInfo, null)) senderEntityId = _senderInfo.RemoteClientInfo.entityId; } catch { senderEntityId = -1; }
                var player = world.GetEntity(senderEntityId) as EntityPlayer;
                if (player == null) return;

                var sub = _params[0].ToLowerInvariant();
                if (sub == "mount" && _params.Count >= 3)
                {
                    int wolfId = StringParsers.ParseSInt32(_params[1]);
                    int action = StringParsers.ParseSInt32(_params[2]);
                    ApplyMount(world, player, wolfId, action == 1);
                    return;
                }
                if (sub == "drive" && _params.Count >= 5)
                {
                    int wolfId = StringParsers.ParseSInt32(_params[1]);
                    float v = StringParsers.ParseFloat(_params[2]);
                    float h = StringParsers.ParseFloat(_params[3]);
                    int flags = StringParsers.ParseSInt32(_params[4]);
                    ApplyDrive(world, player, wolfId, v, h, flags);
                    return;
                }
                if (sub == "deploy" && _params.Count >= 6)
                {
                    int ec = StringParsers.ParseSInt32(_params[1]);
                    float x = StringParsers.ParseFloat(_params[2]);
                    float y = StringParsers.ParseFloat(_params[3]);
                    float z = StringParsers.ParseFloat(_params[4]);
                    float rotY = StringParsers.ParseFloat(_params[5]);
                    ApplyDeploy(world, player, ec, new Vector3(x, y, z), new Vector3(0f, rotY, 0f));
                    return;
                }
                if (sub == "collect" && _params.Count >= 2)
                {
                    int wolfId = StringParsers.ParseSInt32(_params[1]);
                    ApplyCollect(world, player as EntityPlayerLocal, wolfId);
                    return;
                }
            }
            catch { }
        }

        private static void ApplyMount(World world, EntityPlayer rider, int wolfId, bool mount)
        {
            var wolf = world.GetEntity(wolfId) as EntityAlive; if (wolf == null) return;
            int ownerId = 0; try { ownerId = (int)(wolf.Buffs?.GetCustomVar("dwOwnerId") ?? 0f); } catch { }
            if (ownerId != rider.entityId) return;
            int currentMounted = 0; int currentRider = 0;
            try { currentMounted = (int)(wolf.Buffs?.GetCustomVar("dwMounted") ?? 0f); } catch { }
            try { currentRider = (int)(wolf.Buffs?.GetCustomVar("dwRiderId") ?? 0f); } catch { }
            if (mount)
            {
                if (currentMounted == 1 && currentRider != rider.entityId) return;
                wolf.Buffs?.SetCustomVar("dwMounted", 1);
                wolf.Buffs?.SetCustomVar("dwRiderId", rider.entityId);
                try { wolf.navigator?.clearPath(); } catch { }
                rider.SetPosition(wolf.position + new Vector3(0, 0.5f, 0));
            }
            else
            {
                if (currentMounted == 1 && currentRider != rider.entityId) return;
                wolf.Buffs?.SetCustomVar("dwMounted", 0);
                wolf.Buffs?.SetCustomVar("dwRiderId", 0);
            }
        }

        private static void ApplyDrive(World world, EntityPlayer rider, int wolfId, float v, float h, int flags)
        {
            var wolf = world.GetEntity(wolfId) as EntityAlive; if (wolf == null) return;
            int rid = 0; int mounted = 0;
            try { rid = (int)(wolf.Buffs?.GetCustomVar("dwRiderId") ?? 0f); } catch { }
            try { mounted = (int)(wolf.Buffs?.GetCustomVar("dwMounted") ?? 0f); } catch { }
            if (mounted != 1 || rid != rider.entityId) return;
            wolf.Buffs?.SetCustomVar("dwMoveV", Mathf.Clamp(v, -1f, 1f));
            wolf.Buffs?.SetCustomVar("dwMoveH", Mathf.Clamp(h, -1f, 1f));
            wolf.Buffs?.SetCustomVar("dwSprint", (flags & 1) != 0 ? 1 : 0);
        }

        private static void ApplyDeploy(World world, EntityPlayer owner, int entityClassId, Vector3 pos, Vector3 rot)
        {
            var entity = EntityFactory.CreateEntity(entityClassId, pos, rot) as EntityAlive;
            if (entity == null) return;
            world.SpawnEntityInWorld(entity);
            entity.Buffs?.SetCustomVar("dwOwnerId", owner.entityId);
        }

        private static void ApplyCollect(World world, EntityPlayerLocal player, int wolfId)
        {
            var wolf = world.GetEntity(wolfId) as EntityAlive; if (wolf == null || player == null) return;
            int ownerId = 0; try { ownerId = (int)(wolf.Buffs?.GetCustomVar("dwOwnerId") ?? 0f); } catch { }
            if (ownerId != player.entityId) return;
            var iv = ItemClass.GetItem("SummonDireWolfPickUpNPC", true);
            if (iv.IsEmpty()) iv = ItemClass.GetItem("toolWolfWhistle", true);
            var stack = new ItemStack(iv, 1);
            var uiforPlayer = LocalPlayerUI.GetUIForPlayer(player);
            if (!uiforPlayer.xui.PlayerInventory.AddItem(stack))
                GameManager.Instance.ItemDropServer(stack, player.GetPosition(), Vector3.zero, player.entityId, 60f, false);
            world.RemoveEntity(wolfId, EnumRemoveEntityReason.Despawned);
        }
    }
}


