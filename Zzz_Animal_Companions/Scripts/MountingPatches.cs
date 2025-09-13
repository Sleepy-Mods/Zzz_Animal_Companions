using HarmonyLib;
using UnityEngine;

namespace DireWolfMod
{
    [HarmonyPatch]
    public static class MountingPatches
    {
        private const string CompanionClassName = "companionDireWolf";
        private const string MountCvar = "dwMounted";
        private const string RiderVar = "dwRiderId";

        [HarmonyPatch(typeof(EntityPlayerLocal), "Update")]
        [HarmonyPostfix]
        public static void PlayerUpdate_Post(EntityPlayerLocal __instance)
        {
            try
            {
                if (__instance == null || __instance.world == null) return;
                if (GameManager.Instance.IsPaused()) return;

                var mountedWolf = FindMountedWolf(__instance);
                if (mountedWolf != null)
                {
                    if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1))
                    {
                        ToggleMount(__instance, mountedWolf);
                        return;
                    }
                }
                else if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.JoystickButton0))
                {
                    var target = GetLookAtWolf(__instance);
                    if (target != null)
                    {
                        ToggleMount(__instance, target);
                    }
                }
            }
            catch { }
        }

        [HarmonyPatch(typeof(EntityAlive), "OnUpdateLive")]
        [HarmonyPostfix]
        public static void WolfDrive_Post(EntityAlive __instance)
        {
            try
            {
                var wolf = __instance;
                if (wolf == null || wolf.EntityClass == null) return;
                if (!string.Equals(wolf.EntityClass.entityClassName, CompanionClassName)) return;
                int mounted = (int)(wolf.Buffs?.GetCustomVar(MountCvar) ?? 0f);
                if (mounted == 0) return;

                var rider = wolf.world?.GetPrimaryPlayer() as EntityPlayerLocal;
                if (rider == null) return;
                int riderId = (int)(wolf.Buffs?.GetCustomVar(RiderVar) ?? 0f);
                if (rider.entityId != riderId) return;

                Vector3 forward = rider.transform.forward;
                Vector3 right = rider.transform.right;
                Vector3 move = Vector3.zero;
                float axV = Input.GetAxisRaw("Vertical");
                float axH = Input.GetAxisRaw("Horizontal");
                move += forward * Mathf.Clamp(axV, -1f, 1f);
                move += right * Mathf.Clamp(axH, -1f, 1f);
                if (move.sqrMagnitude > 0.01f)
                {
                    move = move.normalized * 2.0f;
                    Vector3 target = wolf.position + move;
                    wolf.moveHelper?.SetMoveTo(target, false);
                    wolf.SetLookPosition(target);
                }

                rider.SetPosition(wolf.position + new Vector3(0, 0.5f, 0));
            }
            catch { }
        }

        private static EntityAlive GetLookAtWolf(EntityPlayerLocal player)
        {
            var world = player.world;
            Vector3 origin = player.position + new Vector3(0, 1.6f, 0);
            Vector3 dir = player.GetLookVector();
            float maxDist = 2.5f;
            var list = world?.Entities?.list;
            if (list == null) return null;
            EntityAlive best = null;
            float bestD2 = maxDist * maxDist;
            foreach (var e in list)
            {
                var ea = e as EntityAlive;
                if (ea == null || ea.EntityClass == null) continue;
                if (!string.Equals(ea.EntityClass.entityClassName, CompanionClassName)) continue;
                float d2 = (ea.position - origin).sqrMagnitude;
                if (d2 < bestD2)
                {
                    Vector3 to = (ea.position - origin).normalized;
                    if (Vector3.Dot(dir, to) > 0.85f)
                    {
                        bestD2 = d2;
                        best = ea;
                    }
                }
            }
            return best;
        }

        private static void ToggleMount(EntityPlayerLocal rider, EntityAlive wolf)
        {
            int mounted = (int)(wolf.Buffs?.GetCustomVar(MountCvar) ?? 0f);
            if (mounted == 0)
            {
                wolf.Buffs?.SetCustomVar(MountCvar, 1);
                wolf.Buffs?.SetCustomVar(RiderVar, rider.entityId);
                rider.SetPosition(wolf.position + new Vector3(0, 0.5f, 0));
            }
            else
            {
                wolf.Buffs?.SetCustomVar(MountCvar, 0);
                wolf.Buffs?.SetCustomVar(RiderVar, 0);
            }
        }

        private static EntityAlive FindMountedWolf(EntityPlayerLocal rider)
        {
            var list = rider.world?.Entities?.list;
            if (list == null) return null;
            foreach (var e in list)
            {
                var ea = e as EntityAlive;
                if (ea == null || ea.EntityClass == null) continue;
                if (!string.Equals(ea.EntityClass.entityClassName, CompanionClassName)) continue;
                int mounted = (int)(ea.Buffs?.GetCustomVar(MountCvar) ?? 0f);
                int riderId = (int)(ea.Buffs?.GetCustomVar(RiderVar) ?? 0f);
                if (mounted == 1 && riderId == rider.entityId) return ea;
            }
            return null;
        }
    }
}


