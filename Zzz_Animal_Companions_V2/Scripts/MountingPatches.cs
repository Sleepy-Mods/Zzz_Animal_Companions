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
        private const string SaddledVar = "dwSaddled";
        private const string InstallSaddleReq = "dwInstallSaddleReq";
        private const string InstallBagsReq = "dwInstallBagsReq";
        private const string MoveVCvar = "dwMoveV";
        private const string MoveHCvar = "dwMoveH";
        private const string SprintCvar = "dwSprint";
        private const string ReqMountWolfId = "dwReqMountWolfId";
        private const string ReqMountAction = "dwReqMountAction"; // 1=mount,0=dismount
        private const string BiteReqCvar = "dwReqBite";
        private const string BiteWindowCvar = "dwBiteWindow";
        private const string SaddleAttachName = "dwSaddleGO";
        private static readonly Vector3 SeatLocalOffset = new Vector3(0f, 0.12f, -0.02f);

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
                    // (Reverted) no special bite key; stationary auto-bite handled on server
                    // Sync input axes to the player so server can drive
                    try
                    {
                        float axV = Input.GetAxisRaw("Vertical");
                        float axH = Input.GetAxisRaw("Horizontal");
                        __instance.Buffs?.SetCustomVar(MoveVCvar, axV);
                        __instance.Buffs?.SetCustomVar(MoveHCvar, axH);
                        // Sprint: keyboard LeftShift or controller RB (JoystickButton5)
                        bool sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || Input.GetKey(KeyCode.JoystickButton5);
                        __instance.Buffs?.SetCustomVar(SprintCvar, sprint ? 1 : 0);
                    }
                    catch { }
					// Debug: show axes briefly
					try {
						if (Time.frameCount % 20 == 0)
						{
							GameManager.ShowTooltip(__instance, $"Wolf Ctrl V:{Input.GetAxisRaw("Vertical"):0.00} H:{Input.GetAxisRaw("Horizontal"):0.00}");
						}
					} catch { }
                }
                else if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.JoystickButton0))
                {
                    var target = GetLookAtWolf(__instance) ?? FindNearestOwnedWolf(__instance, 2.5f);
                    if (target != null)
                    {
                        int saddled = 0; try { saddled = (int)target.Buffs.GetCustomVar(SaddledVar); } catch { saddled = 0; }
                        if (saddled == 1)
                        {
                            ToggleMount(__instance, target);
                        }
                        else
                        {
                            __instance.Buffs?.SetCustomVar(InstallSaddleReq, 1);
                            GameManager.ShowTooltip(__instance, "Installing saddle...");
                        }
                    }
                }

                // Show basic HUD hint when looking at the wolf
                var lookWolf = GetLookAtWolf(__instance);
                if (lookWolf != null)
                {
                    int saddledLook = 0; try { saddledLook = (int)lookWolf.Buffs.GetCustomVar(SaddledVar); } catch { saddledLook = 0; }
                    if (saddledLook == 1)
                    {
                        GameManager.ShowTooltip(__instance, "Press E to ride your Dire Wolf");
                    }
                    else
                    {
                        GameManager.ShowTooltip(__instance, "Press E to install the saddle");
                    }
                }
            }
            catch { }
        }

        [HarmonyPatch(typeof(EntityPlayer), "OnUpdateLive")]
        [HarmonyPostfix]
        public static void PlayerUpdate_CatchInstallRequests(EntityPlayer __instance)
        {
            // Client no-op; server reads player cvars and maps to the wolf
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
				// Prevent AI pathing from reclaiming control while mounted
				try { wolf.navigator?.clearPath(); } catch { }

                // Drive only on authority to avoid desync
                // Removed early return: allow client host to drive too; server will reconcile

                int riderId = (int)(wolf.Buffs?.GetCustomVar(RiderVar) ?? 0f);
                var riderEntity = wolf.world?.GetEntity(riderId) as EntityPlayer;
                if (riderEntity == null) return;

                // (Reverted) no bite request mapping; server will auto-bite when stationary

                Vector3 forward = riderEntity.transform.forward;
                Vector3 right = riderEntity.transform.right;
                float axV = 0f; float axH = 0f;
                try { axV = wolf.Buffs != null ? wolf.Buffs.GetCustomVar(MoveVCvar) : 0f; } catch { axV = 0f; }
                try { axH = wolf.Buffs != null ? wolf.Buffs.GetCustomVar(MoveHCvar) : 0f; } catch { axH = 0f; }
                bool sprinting = false; try { sprinting = wolf.Buffs != null && wolf.Buffs.GetCustomVar(SprintCvar) > 0f; } catch { sprinting = false; }
                // Fallback: if mapping failed, read directly from rider's CVars (client->server replicated)
                if (Mathf.Approximately(axV, 0f) && Mathf.Approximately(axH, 0f))
                {
                    try { axV = riderEntity.Buffs != null ? riderEntity.Buffs.GetCustomVar(MoveVCvar) : axV; } catch { }
                    try { axH = riderEntity.Buffs != null ? riderEntity.Buffs.GetCustomVar(MoveHCvar) : axH; } catch { }
                    try { sprinting = riderEntity.Buffs != null ? (riderEntity.Buffs.GetCustomVar(SprintCvar) > 0f) : sprinting; } catch { }
                }
				Vector3 move = Vector3.zero;
				move += forward * Mathf.Clamp(axV, -1f, 1f);
				move += right * Mathf.Clamp(axH, -1f, 1f);
				// Proactively prevent AI from steering while mounted
				try { wolf.SetRevengeTarget(null); } catch { }
				try { wolf.SetAttackTarget(null, 0); } catch { }
				if (move.sqrMagnitude > 0.0001f)
				{
					float baseSpeed = 4.8f; // +50% over previous
					float sprintMul = sprinting ? 1.6f : 1.0f;
					float inputMag = Mathf.Clamp01(new Vector2(axH, axV).magnitude);
					float speedFloor = 2.1f; // prevents stall at low input or on slopes
					float speed = Mathf.Max(baseSpeed * sprintMul * Mathf.Max(inputMag, 0.35f), speedFloor);
					Vector3 dir = move.normalized;
					// Longer horizon to keep AI occupied between ticks
					float horizon = sprinting ? 5.0f : 3.5f;
					Vector3 target = wolf.position + dir * horizon;
					wolf.moveHelper?.SetMoveTo(target, true);
					// Try to push velocity directly if the API exists
					try {
						var t = wolf.GetType();
						var mhField = t.GetField("moveHelper", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
						var mh = mhField != null ? mhField.GetValue(wolf) : null;
						if (mh != null)
						{
							var mSetVel = mh.GetType().GetMethod("SetVelocity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, new System.Type[] { typeof(Vector3) }, null);
							if (mSetVel != null) { mSetVel.Invoke(mh, new object[] { dir * speed }); }
						}
					} catch { }
					wolf.SetLookPosition(target);
				}
				else
				{
					// No input: keep AI from wandering; do not auto-attack (player triggers with F)
					wolf.moveHelper?.Stop();
					try { wolf.navigator?.clearPath(); } catch { }
				}

				// Stationary: small forward auto-bite with short cooldown
				if (move.sqrMagnitude <= 0.0001f)
				{
					try {
						float lastAtk = 0f; try { lastAtk = wolf.Buffs != null ? wolf.Buffs.GetCustomVar("dwLastMountAtk") : 0f; } catch { lastAtk = 0f; }
						if (Time.time - lastAtk > 0.8f)
						{
							var best = FindForwardHostile(wolf, 3.0f, 0.85f);
							if (best != null)
							{
								wolf.SetRevengeTarget(best);
								wolf.SetAttackTarget(best, 120);
								wolf.Buffs?.SetCustomVar("dwLastMountAtk", Time.time);
							}
						}
					} catch { }
				}
				// Keep rider seated on server
				{
					riderEntity.SetPosition(wolf.position + new Vector3(0, 0.5f, 0));
				}
				// Debug: show server/driver state
				try {
					if (Time.frameCount % 30 == 0)
					{
						var riderLocal = riderEntity as EntityPlayerLocal;
						if (riderLocal != null)
						{
							GameManager.ShowTooltip(riderLocal, $"Drive dbg remote:{wolf.isEntityRemote} mounted:{mounted} rider:{riderId} V:{axV:0.00} H:{axH:0.00}");
						}
					}
				} catch { }
            }
            catch { }
        }

        private static EntityAlive FindForwardHostile(EntityAlive wolf, float maxDist, float minDot)
        {
            try
            {
                var list = wolf.world?.Entities?.list; if (list == null) return null;
                Vector3 origin = wolf.position + new Vector3(0f, 0.8f, 0f);
                Vector3 fwd = wolf.transform.forward;
                EntityAlive best = null; float bestD2 = maxDist * maxDist;
                foreach (var e in list)
                {
                    var ea = e as EntityAlive; if (ea == null || ea == wolf) continue;
                    if (ea is EntityPlayer) continue;
                    var cls = ea.EntityClass; if (cls == null) continue;
                    string tags = cls.Properties?.Values != null && cls.Properties.Values.ContainsKey("Tags") ? cls.Properties.Values["Tags"] : string.Empty;
                    if (!tags.Contains("zombie") && !tags.Contains("enemy") && !tags.Contains("bandit")) continue;
                    Vector3 to = (ea.position + new Vector3(0, 0.6f, 0)) - origin;
                    float d2 = to.sqrMagnitude; if (d2 > bestD2) continue;
                    to.Normalize(); if (Vector3.Dot(fwd, to) < minDot) continue;
                    best = ea; bestD2 = d2;
                }
                return best;
            }
            catch { return null; }
        }

        // (Reverted) no seat transform helpers

        private static EntityAlive GetLookAtWolf(EntityPlayerLocal player)
        {
            var world = player.world;
            Vector3 origin = player.position + new Vector3(0, 1.6f, 0);
            Vector3 dir = player.GetLookVector();
            float maxDist = 3.25f;
            var list = world?.Entities?.list;
            if (list == null) return null;
            EntityAlive best = null;
            float bestD2 = maxDist * maxDist;
            foreach (var e in list)
            {
                var ea = e as EntityAlive;
                if (ea == null || ea.EntityClass == null) continue;
                if (!string.Equals(ea.EntityClass.entityClassName, CompanionClassName)) continue;
                // Aim check is generous; also measure to slight height offset
                Vector3 center = ea.position + new Vector3(0, 0.8f, 0);
                float d2 = (center - origin).sqrMagnitude;
                if (d2 < bestD2)
                {
                    Vector3 to = (center - origin).normalized;
                    if (Vector3.Dot(dir, to) > 0.6f)
                    {
                        bestD2 = d2;
                        best = ea;
                    }
                }
            }
            return best;
        }

        private static bool IsHoldingItem(EntityPlayerLocal player, string itemName)
        {
            try
            {
                var inv = player.inventory;
                if (inv == null) return false;
                var iv = inv.holdingItemItemValue;
                if (iv == null || iv.ItemClass == null) return false;
                string name = iv.ItemClass.Name;
                return string.Equals(name, itemName, System.StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static void ToggleMount(EntityPlayerLocal rider, EntityAlive wolf)
        {
            int mounted = (int)(wolf.Buffs?.GetCustomVar(MountCvar) ?? 0f);
            if (mounted == 0)
            {
                wolf.Buffs?.SetCustomVar(MountCvar, 1);
                wolf.Buffs?.SetCustomVar(RiderVar, rider.entityId);
                rider.SetPosition(wolf.position + new Vector3(0, 0.5f, 0));
                // Request server to apply mount state authoritatively
                rider.Buffs?.SetCustomVar(ReqMountWolfId, wolf.entityId);
                rider.Buffs?.SetCustomVar(ReqMountAction, 1);
            }
            else
            {
                wolf.Buffs?.SetCustomVar(MountCvar, 0);
                wolf.Buffs?.SetCustomVar(RiderVar, 0);
                rider.Buffs?.SetCustomVar(ReqMountWolfId, wolf.entityId);
                rider.Buffs?.SetCustomVar(ReqMountAction, 0);
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

        private static EntityAlive FindNearestOwnedWolf(EntityPlayerLocal player, float maxDist)
        {
            var list = player.world?.Entities?.list;
            if (list == null) return null;
            EntityAlive best = null;
            float bestD2 = maxDist * maxDist;
            int ownerId = player.entityId;
            foreach (var e in list)
            {
                var ea = e as EntityAlive;
                if (ea == null || ea.EntityClass == null) continue;
                if (!string.Equals(ea.EntityClass.entityClassName, CompanionClassName)) continue;
                int wolfOwner = (int)(ea.Buffs?.GetCustomVar("dwOwnerId") ?? 0f);
                if (wolfOwner != ownerId) continue;
                float d2 = (ea.position - player.position).sqrMagnitude;
                if (d2 < bestD2) { bestD2 = d2; best = ea; }
            }
            return best;
        }
    }
}


