using HarmonyLib;
using UnityEngine;

namespace DireWolfMod
{
    [HarmonyPatch]
    public static class MountingPatches
    {
        private const string CompanionWolfClassName = "companionDireWolf";
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
        private const string SaddleHintShownCvar = "dwSaddleHintShown";
        private const string InputDeviceCvar = "dwInputDevice"; // 0=kb/m, 1=controller
        private static readonly Vector3 SeatLocalOffset = new Vector3(0f, 0.12f, -0.02f);

        // Track E-hold pickup state per player
        private static readonly System.Collections.Generic.Dictionary<int, float> PickupHoldStartByPlayer = new System.Collections.Generic.Dictionary<int, float>();
        private static readonly System.Collections.Generic.Dictionary<int, int> PickupTargetWolfByPlayer = new System.Collections.Generic.Dictionary<int, int>();

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
                    if (Input.GetKeyDown(KeyCode.V) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1))
                    {
                        ToggleMount(__instance, mountedWolf);
                        return;
                    }
                    // Crouch behavior left unchanged per user request
                    // (Reverted) no special bite key; stationary auto-bite handled on server
                    // Sync input axes to the player so server can drive
                    try
                    {
                        float axV = Input.GetAxisRaw("Vertical");
                        float axH = Input.GetAxisRaw("Horizontal");
                        bool usedControllerAxis = false;
                        // Fallback to common controller joystick axes if keyboard axes are zero
                        if (Mathf.Approximately(axV, 0f) && Mathf.Approximately(axH, 0f))
                        {
                            float bestH = 0f, bestV = 0f, bestMag = 0f;
                            // Try Unity default joystick axes
                            try {
                                float jh = Input.GetAxisRaw("Joy X");
                                float jv = -Input.GetAxisRaw("Joy Y");
                                float mag = new Vector2(jh, jv).magnitude;
                                if (mag > bestMag) { bestMag = mag; bestH = jh; bestV = jv; }
                            } catch { }
                            try {
                                float jh = Input.GetAxisRaw("Joy 3rd Axis");
                                float jv = -Input.GetAxisRaw("Joy 4th Axis");
                                float mag = new Vector2(jh, jv).magnitude;
                                if (mag > bestMag) { bestMag = mag; bestH = jh; bestV = jv; }
                            } catch { }
                            try {
                                float jh = Input.GetAxisRaw("Joy 5th Axis");
                                float jv = -Input.GetAxisRaw("Joy 6th Axis");
                                float mag = new Vector2(jh, jv).magnitude;
                                if (mag > bestMag) { bestMag = mag; bestH = jh; bestV = jv; }
                            } catch { }
                            if (bestMag > 0.05f) { axH = bestH; axV = bestV; usedControllerAxis = true; }
                        }
                        // Mirror working mod: write inputs into player CVars; server tick will map onto wolf
                        bool sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || Input.GetKey(KeyCode.JoystickButton5);
                        __instance.Buffs?.SetCustomVar(MoveVCvar, axV);
                        __instance.Buffs?.SetCustomVar(MoveHCvar, axH);
                        __instance.Buffs?.SetCustomVar(SprintCvar, sprint ? 1 : 0);

                        // Track last input device for device-specific prompts
                        bool controllerPressed = Input.GetKey(KeyCode.JoystickButton0) || Input.GetKey(KeyCode.JoystickButton1) || Input.GetKey(KeyCode.JoystickButton2) || Input.GetKey(KeyCode.JoystickButton3) || Input.GetKey(KeyCode.JoystickButton4) || Input.GetKey(KeyCode.JoystickButton5) || Input.GetKey(KeyCode.JoystickButton6) || Input.GetKey(KeyCode.JoystickButton7) || Input.GetKey(KeyCode.JoystickButton8) || Input.GetKey(KeyCode.JoystickButton9) || Input.GetKey(KeyCode.JoystickButton10) || Input.GetKey(KeyCode.JoystickButton11);
                        if (controllerPressed || usedControllerAxis)
                        {
                            try { __instance.Buffs.SetCustomVar(InputDeviceCvar, 1); } catch { }
                        }
                        else if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.V) || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                        {
                            try { __instance.Buffs.SetCustomVar(InputDeviceCvar, 0); } catch { }
                        }
                    }
                    catch { }

                    // Client-side seat clamp to eliminate interpolation jitter: match server offset precisely
                    try { __instance.SetPosition(mountedWolf.position + new Vector3(0f, 0.5f, 0f)); } catch { }
                }
                else if (Input.GetKeyDown(KeyCode.V) || Input.GetKeyDown(KeyCode.JoystickButton0))
                {
                    var target = GetLookAtCompanion(__instance) ?? FindNearestOwnedCompanion(__instance, 2.5f);
                    if (target != null)
                    {
                        // Mount if this species is rideable; no saddle requirement
                        if (IsRideable(target))
                        {
                            ToggleMount(__instance, target);
                        }
                    }
                }

                // Hold E (independent of V) on a nearby/aimed companion to dismiss/pick up
                if (mountedWolf == null)
                {
                    var lookOrNear = GetLookAtCompanion(__instance) ?? FindNearestOwnedCompanion(__instance, 2.5f);
                    bool holdingE = Input.GetKey(KeyCode.E);
                    int pid = __instance.entityId;
                    if (holdingE && lookOrNear != null)
                    {
                        int wid = lookOrNear.entityId;
                        if (!PickupHoldStartByPlayer.ContainsKey(pid) || !PickupTargetWolfByPlayer.ContainsKey(pid) || PickupTargetWolfByPlayer[pid] != wid)
                        {
                            PickupHoldStartByPlayer[pid] = Time.time;
                            PickupTargetWolfByPlayer[pid] = wid;
                            GameManager.ShowTooltip(__instance, "Hold E to dismiss companion");
                        }
                        else
                        {
                            float held = Time.time - PickupHoldStartByPlayer[pid];
                            if (held >= 0.6f)
                            {
                                var pkg = NetPackageManager.GetPackage<NetPackageDireWolfCollect>().Setup(wid, pid);
                                if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
                                {
                                    SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(pkg, false);
                                }
                                else
                                {
                                    pkg.ProcessPackage(GameManager.Instance.World, GameManager.Instance);
                                }
                                GameManager.ShowTooltip(__instance, "Companion dismissed");
                                PickupHoldStartByPlayer.Remove(pid);
                                PickupTargetWolfByPlayer.Remove(pid);
                            }
                        }
                    }
                    else
                    {
                        PickupHoldStartByPlayer.Remove(pid);
                        PickupTargetWolfByPlayer.Remove(pid);
                    }
                }

                // Disable all saddle tooltips/hints
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
                if (!IsCompanionEntity(wolf)) return;
                int mounted = (int)(wolf.Buffs?.GetCustomVar(MountCvar) ?? 0f);
                if (mounted == 0) return;
				// Prevent AI pathing from reclaiming control while mounted
				try { wolf.navigator?.clearPath(); } catch { }

                // Drive only on authority to avoid desync
                // Removed early return: allow client host to drive too; server will reconcile
                // Authority-only driving: server/host applies movement; clients do not drive
                if (wolf.isEntityRemote) return;
                
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
				// Debug HUD tooltips removed
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

        private static EntityAlive GetLookAtCompanion(EntityPlayerLocal player)
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
                if (!IsCompanionEntity(ea)) continue;
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
                // Request server to apply mount state authoritatively via CVars
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
                if (!IsCompanionEntity(ea)) continue;
                int mounted = (int)(ea.Buffs?.GetCustomVar(MountCvar) ?? 0f);
                int riderId = (int)(ea.Buffs?.GetCustomVar(RiderVar) ?? 0f);
                if (mounted == 1 && riderId == rider.entityId) return ea;
            }
            return null;
        }

        private static EntityAlive FindNearestOwnedCompanion(EntityPlayerLocal player, float maxDist)
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
                if (!IsCompanionEntity(ea)) continue;
                int wolfOwner = (int)(ea.Buffs?.GetCustomVar("dwOwnerId") ?? 0f);
                if (wolfOwner != ownerId) continue;
                float d2 = (ea.position - player.position).sqrMagnitude;
                if (d2 < bestD2) { bestD2 = d2; best = ea; }
            }
            return best;
        }

        private static bool IsCompanionEntity(EntityAlive ea)
        {
            try
            {
                var name = ea.EntityClass.entityClassName ?? string.Empty;
                if (name.StartsWith("companion", System.StringComparison.OrdinalIgnoreCase)) return true;
                var tags = ea.EntityClass.Properties?.Values != null && ea.EntityClass.Properties.Values.ContainsKey("Tags") ? ea.EntityClass.Properties.Values["Tags"] : string.Empty;
                return !string.IsNullOrEmpty(tags) && tags.Contains("companion");
            }
            catch { return false; }
        }

        private static bool IsRideable(EntityAlive ea)
        {
            try
            {
                if (ea == null || ea.EntityClass == null) return false;
                string ecn = ea.EntityClass.entityClassName ?? string.Empty;
                // Rideable list per user request (exclude coyote)
                if (string.Equals(ecn, "companionDireWolf", System.StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(ecn, "companionDireWolfFire", System.StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(ecn, "companionBear", System.StringComparison.OrdinalIgnoreCase)) return true;
                // Mountain lion: now non-rideable
                if (string.Equals(ecn, "companionBuck", System.StringComparison.OrdinalIgnoreCase)) return true;
                // Non-rideable: coyote, vulture, snake, rabbit, doe
                return false;
            }
            catch { return false; }
        }
    }
}


