using HarmonyLib;
using System.Reflection;
using UnityEngine;

// Dire Wolf Companion - Harmony scaffolding
// Build as a DLL named DireWolfMod.dll and place in this mod's folder.

namespace DireWolfMod
{
    // 7DTD mod entrypoint implemented via IModApi
    public class Loader : IModApi
    {
        public void InitMod(Mod mod)
        {
            var harmony = new Harmony("com.sophia.direwolfmod");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            try { UnityEngine.Debug.Log("[DireWolfMod] Loaded and Harmony patches applied."); } catch { }
        }
    }

    [HarmonyPatch]
    public static class ServerSpawnPatch
    {
        private const string SummonBuff = "buffDireWolfSummon";
        private const string SpawnReqCvar = "dwSpawnReq";
        private const string OwnerCvar = "dwOwnerId";
        private const string CompanionClassName = "companionDireWolf";
        private const string CompanionBearClassName = "companionBear";

        [HarmonyPatch(typeof(EntityPlayer), "OnUpdateLive")]
        [HarmonyPostfix]
        public static void PlayerLive_Post(EntityPlayer __instance)
        {
            try
            {
                var player = __instance;
                if (player == null || player.world == null) return;
                // Run on authoritative instance (host/dedi). Player entities that are remote should not process here.
                if (player.isEntityRemote) return;

                if (player.Buffs == null) return;
                bool hasSummon = false;
                bool hasBearSummon = false;
                try { hasSummon = player.Buffs.HasBuff(SummonBuff); } catch { hasSummon = false; }
                try { hasBearSummon = player.Buffs.HasBuff("buffBearSummon"); } catch { hasBearSummon = false; }
                int spawnReq = 0;
                int spawnReqBear = 0;
                try { var tmp = player.Buffs.GetCustomVar(SpawnReqCvar); spawnReq = tmp > 0f ? 1 : 0; } catch { spawnReq = 0; }
                try { var tmp2 = player.Buffs.GetCustomVar("dwSpawnReqBear"); spawnReqBear = tmp2 > 0f ? 1 : 0; } catch { spawnReqBear = 0; }
                if (!hasSummon && spawnReq == 0 && !hasBearSummon && spawnReqBear == 0) return;

                try { if (hasSummon) player.Buffs.RemoveBuff(SummonBuff); } catch { }
                try { if (spawnReq != 0) player.Buffs.SetCustomVar(SpawnReqCvar, 0); } catch { }
                try { if (hasBearSummon) player.Buffs.RemoveBuff("buffBearSummon"); } catch { }
                try { if (spawnReqBear != 0) player.Buffs.SetCustomVar("dwSpawnReqBear", 0); } catch { }

                var world = GameManager.Instance?.World;
                if (world == null) return;

                // Despawn any existing companion for this player
                CompanionPatches.RemoveExistingCompanion(world, player.entityId, -1);

                // Spawn on server near player
                Vector3 pos = player.position + new Vector3(1.25f, 0.1f, 0f);
                string spawnClass = (hasBearSummon || spawnReqBear != 0) ? CompanionBearClassName : CompanionClassName;
                int ec = EntityClass.FromString(spawnClass);
                if (ec < 0) { UnityEngine.Debug.Log($"[DireWolfMod] Unable to find entity class {spawnClass}"); return; }
                var ent = EntityFactory.CreateEntity(ec, pos) as EntityAlive;
                if (ent == null) { UnityEngine.Debug.Log("[DireWolfMod] Failed to create companion entity"); return; }
                world.SpawnEntityInWorld(ent);
                ent.Buffs?.SetCustomVar(OwnerCvar, player.entityId);
                UnityEngine.Debug.Log($"[DireWolfMod] Server-spawned companion '{spawnClass}' id {ent.entityId} for owner {player.entityId}");
            }
            catch { }
        }

        // no explicit is-server check; gating via player.isEntityRemote covers host and dedicated
    }
}


