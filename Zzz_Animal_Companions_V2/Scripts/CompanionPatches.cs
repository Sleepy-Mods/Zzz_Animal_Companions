using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;

// NOTE: This code targets common 7DTD types by name. If signatures change between versions,
// you may need to adjust the method names or add reflection.

namespace DireWolfMod
{
	[HarmonyPatch]
	public static class CompanionPatches
	{
		private const string CompanionClassName = "companionDireWolf";
		private const string StateCvar = "dwState"; // 0=follow, 1=stay (reserved)
		private const string OwnerCvar = "dwOwnerId"; // persistent per-wolf owner binding
		private const string SaddledVar = "dwSaddled";
		private const string InstallSaddleReq = "dwInstallSaddleReq";
		private const string InstallBagsReq = "dwInstallBagsReq";
		private const string StorageTierVar = "dwStorageTier"; // 0 none, 1 bags
		private const float AttackSearchRadiusOwner = 45f; // extended search around owner
		private const float AttackSearchRadiusSelf = 45f;  // extended search around self
		private const float AttackLeashFromOwner = 25f;    // max distance wolf may stray from owner when attacking
		private const string SaddleReapplyCvar = "dwSaddleReapply";

		private static readonly Dictionary<int, int> OwnerByCompanion = new Dictionary<int, int>();
		private static readonly Dictionary<int, int> CompanionByOwner = new Dictionary<int, int>();
		private static readonly Dictionary<int, Vector3> FollowTargetByCompanion = new Dictionary<int, Vector3>();
		private static readonly Dictionary<int, float> NextRepathTimeByCompanion = new Dictionary<int, float>();
		private static readonly Dictionary<int, float> LastProgressDistanceByCompanion = new Dictionary<int, float>();
		private static readonly Dictionary<int, float> LastProgressCheckTimeByCompanion = new Dictionary<int, float>();

		// When an entity is added to the world, if it's our companion, bind to nearest player
		[HarmonyPatch(typeof(EntityAlive), "OnAddedToWorld")]
		[HarmonyPostfix]
		public static void OnAddedToWorld_Post(EntityAlive __instance)
		{
			try
			{
				var self = __instance;
				if (self == null || self.EntityClass == null) return;
				if (!IsCompanion(self)) return;
				// Run only on the authoritative instance; avoid client-side interference
				if (self.isEntityRemote) { try { UnityEngine.Debug.Log($"[DireWolfMod] Skip OnAddedToWorld on client for wolf {self.entityId}"); } catch { } return; }

				var world = GameManager.Instance?.World;
				if (world == null) return;

				var owner = GetNearestPlayer(world, self.position, 12f);
				if (owner != null)
				{
					// Persist owner id on the wolf so clients/servers agree on ownership
					self.Buffs?.SetCustomVar(OwnerCvar, owner.entityId);
					RemoveExistingCompanion(world, owner.entityId, self.entityId);
					OwnerByCompanion[self.entityId] = owner.entityId;
					CompanionByOwner[owner.entityId] = self.entityId;
					try { UnityEngine.Debug.Log($"[DireWolfMod] Wolf {self.entityId} bound to owner {owner.entityId} at spawn"); } catch { }
				}

				// If already saddled, ensure saddle visual is attached on spawn
				try
				{
					int saddled = 0; try { saddled = (int)self.Buffs.GetCustomVar(SaddledVar); } catch { saddled = 0; }
					if (saddled == 1)
					{
						SaddleAssets.TryAttachSaddle(self);
					}
				}
				catch { }
			}
			catch { }
		}

		// Each live update, handle follow/assist
		[HarmonyPatch(typeof(EntityAlive), "OnUpdateLive")]
		[HarmonyPostfix]
		public static void OnUpdateLive_Post(EntityAlive __instance)
		{
			try { RunFollowAssist(__instance); } catch { }
			try { ProcessInstallRequests(__instance); } catch { }
			try
			{
				var self = __instance;
				if (self == null || self.EntityClass == null || self.isEntityRemote) return;
				int saddled = 0; try { saddled = (int)self.Buffs.GetCustomVar(SaddledVar); } catch { saddled = 0; }
				if (saddled == 1)
				{
					int needApply = 0; try { needApply = (int)self.Buffs.GetCustomVar(SaddleReapplyCvar); } catch { needApply = 0; }
					if (needApply == 1)
					{
						SaddleAssets.TryAttachSaddle(self);
						self.Buffs.SetCustomVar(SaddleReapplyCvar, 0);
					}
				}
			}
			catch { }
		}

		// Fallback for versions that don't call OnUpdateLive
		[HarmonyPatch(typeof(EntityAlive), "Update")]
		[HarmonyPostfix]
		public static void Update_Post(EntityAlive __instance)
		{
			try { RunFollowAssist(__instance); } catch { }
		}

		private static void RunFollowAssist(EntityAlive self)
		{
			if (self == null || self.EntityClass == null) return;
			if (!IsCompanion(self)) return;
			// Run only on server/authority to avoid desync
			if (self.isEntityRemote) return;
			// If mounted, do not run follow/assist; ensure server has authority to move
			int mounted = (int)(self.Buffs?.GetCustomVar("dwMounted") ?? 0f);
			if (mounted == 1)
			{
				ClearFollowState(self.entityId);
				// Nudge up if clipping below ground
				try { if (self.position.y < 0.2f) self.SetPosition(self.position + new UnityEngine.Vector3(0f, 0.6f, 0f)); } catch { }
				// Let driving code control movement while mounted
				return;
			}

			// Only run for our companions; bind to explicit owner if present, else to nearest spawner
			if (!OwnerByCompanion.TryGetValue(self.entityId, out int ownerId))
			{
				// Prefer an explicit persisted binding if available
				int buffOwnerId = (int)(self.Buffs?.GetCustomVar(OwnerCvar) ?? 0f);
				if (buffOwnerId != 0)
				{
					OwnerByCompanion[self.entityId] = buffOwnerId;
					CompanionByOwner[buffOwnerId] = self.entityId;
					ownerId = buffOwnerId;
					try { UnityEngine.Debug.Log($"[DireWolfMod] Wolf {self.entityId} restored owner {ownerId} from buff"); } catch { }
				}
				else
				{
					var world0 = GameManager.Instance?.World;
					if (world0 != null)
					{
						// Tighter radius to capture the summoner even if multiple players are nearby
						var near = GetNearestPlayer(world0, self.position, 12f);
						if (near != null)
						{
							// Ensure one companion per owner
							RemoveExistingCompanion(world0, near.entityId, self.entityId);
							OwnerByCompanion[self.entityId] = near.entityId;
							CompanionByOwner[near.entityId] = self.entityId;
							self.Buffs?.SetCustomVar(OwnerCvar, near.entityId);
							ownerId = near.entityId;
							try { UnityEngine.Debug.Log($"[DireWolfMod] Wolf {self.entityId} auto-bound to nearby owner {ownerId}"); } catch { }
						}
					}
					if (ownerId == 0) return;
				}
			}

			var world = GameManager.Instance?.World;
			if (world == null) return;
			var owner = world.GetEntity(ownerId) as EntityPlayer;
			if (owner == null) return;

			float dist = self.GetDistance(owner);
			// Teleport back if too far (safety net)
			if (dist > 40f)
			{
				var p = owner.position + new Vector3(1.25f, 0.1f, 0f);
				self.SetPosition(p);
				ClearFollowState(self.entityId);
				return;
			}

			// Maintain follow while active; start if beyond threshold
			bool hasFollowTarget = FollowTargetByCompanion.TryGetValue(self.entityId, out var targetPos);
			bool followingActive = dist > 6.0f || hasFollowTarget;
			if (followingActive)
			{
				// Early assist even while following: attack within leash from owner
				var ownerAliveF = owner as EntityAlive;
				var ownerAttackerF = ownerAliveF != null ? ownerAliveF.GetRevengeTarget() as EntityAlive : null;
				var ownerTargetF = ownerAliveF != null ? ownerAliveF.GetAttackTarget() as EntityAlive : null;
				EntityAlive hostileF = ownerTargetF ?? ownerAttackerF;
				if (hostileF == null)
				{
					hostileF = GetNearestHostileNear(world, owner.position, AttackSearchRadiusOwner) ??
								GetNearestHostileNear(world, self.position, AttackSearchRadiusSelf);
				}
				if (hostileF != null)
				{
					float leash = (hostileF.position - owner.position).magnitude;
					if (leash <= AttackLeashFromOwner)
					{
						StopMove(self);
						self.SetRevengeTarget(hostileF);
						self.SetAttackTarget(hostileF, 120);
						return;
					}
				}

				// Prevent distractions like corpse eating while following
				try { self.SetRevengeTarget(null); } catch { }
				try { self.SetAttackTarget(null, 0); } catch { }

				float now = Time.time;
				bool needNewTarget = !hasFollowTarget;

				if (!needNewTarget)
				{
					float toTarget = (targetPos - self.position).magnitude;
					float targetFromOwner = (targetPos - owner.position).magnitude;
					bool reached = toTarget < 1.5f;
					bool drifted = targetFromOwner > 3.5f;
					bool timeToRepath = !NextRepathTimeByCompanion.TryGetValue(self.entityId, out var nextAt) || now >= nextAt;

					// progress check (stalled if not closing by >1m over 1.5s)
					if (!LastProgressCheckTimeByCompanion.TryGetValue(self.entityId, out var lastCheck)) lastCheck = 0f;
					if (!LastProgressDistanceByCompanion.TryGetValue(self.entityId, out var lastDist)) lastDist = toTarget + 999f;
					bool stalled = (now - lastCheck) >= 1.5f && (lastDist - toTarget) < 1.0f;

					if (reached || drifted || timeToRepath || stalled)
						needNewTarget = true;
				}

				if (needNewTarget)
				{
					// Bias waypoint behind the player for fewer collisions, tighter radius for responsiveness
					targetPos = PickWaypointAroundOwner(owner.position, Quaternion.Euler(0f, owner.rotation.y, 0f), 1.2f, 2.5f);
					FollowTargetByCompanion[self.entityId] = targetPos;
					NextRepathTimeByCompanion[self.entityId] = now + 0.15f; // faster repath
					LastProgressDistanceByCompanion[self.entityId] = (targetPos - self.position).magnitude;
					LastProgressCheckTimeByCompanion[self.entityId] = now;
				}
				else
				{
					// Update progress window periodically
					if (!LastProgressCheckTimeByCompanion.TryGetValue(self.entityId, out var lastCheck2)) lastCheck2 = 0f;
					if (now - lastCheck2 >= 1.5f)
					{
						LastProgressDistanceByCompanion[self.entityId] = (targetPos - self.position).magnitude;
						LastProgressCheckTimeByCompanion[self.entityId] = now;
					}
				}

				if (TrySetMoveTo(self, targetPos, true))
				{
					TryEnforceRun(self);
				}
			}

			// Stop following when comfortably close
			if (dist < 2.5f)
			{
				StopMove(self);
				ClearFollowState(self.entityId);
			}

			// Assist only when not actively following
			if (!followingActive)
			{
				var currentTarget = self.GetAttackTarget();
				var ownerAlive = owner as EntityAlive;
				var ownerAttacker = ownerAlive != null ? ownerAlive.GetRevengeTarget() as EntityAlive : null;
				var ownerTarget = ownerAlive != null ? ownerAlive.GetAttackTarget() as EntityAlive : null;
				if (ownerTarget != null)
				{
					self.SetRevengeTarget(ownerTarget);
					self.SetAttackTarget(ownerTarget, 120);
				}
				else if (ownerAttacker != null)
				{
					self.SetRevengeTarget(ownerAttacker);
					self.SetAttackTarget(ownerAttacker, 120);
				}
				else if (currentTarget == null)
				{
					// Prefer threats around the owner; fallback to nearest around self
					EntityAlive hostile = GetNearestHostileNear(world, owner.position, AttackSearchRadiusOwner) ??
													GetNearestHostileNear(world, self.position, AttackSearchRadiusSelf);
					if (hostile != null)
					{
						self.SetRevengeTarget(hostile);
						self.SetAttackTarget(hostile, 120);
					}
				}
			}
		}

		private static void ProcessInstallRequests(EntityAlive self)
		{
			if (self == null || self.EntityClass == null) return;
			if (!IsCompanion(self)) return;
			if (self.isEntityRemote) return;

			var buffs = self.Buffs;
			if (buffs == null) return;

			bool reqSaddle = false;
			bool reqBags = false;
			try { reqSaddle = (buffs.GetCustomVar(InstallSaddleReq) > 0f) || (self.world?.GetPrimaryPlayer()?.Buffs?.GetCustomVar(InstallSaddleReq) > 0f); } catch { }
			try { reqBags = (buffs.GetCustomVar(InstallBagsReq) > 0f) || (self.world?.GetPrimaryPlayer()?.Buffs?.GetCustomVar(InstallBagsReq) > 0f); } catch { }
			if (!reqSaddle && !reqBags) return;

			// validate owner is nearby to authorize install
			int ownerId = 0;
			try { ownerId = (int)buffs.GetCustomVar(OwnerCvar); } catch { ownerId = 0; }
			if (ownerId == 0) { if (reqSaddle) buffs.SetCustomVar(InstallSaddleReq, 0); if (reqBags) buffs.SetCustomVar(InstallBagsReq, 0); return; }
			var world = GameManager.Instance?.World;
			var owner = world?.GetEntity(ownerId) as EntityPlayer;
			if (owner == null || (owner.position - self.position).sqrMagnitude > (4f * 4f)) { if (reqSaddle) buffs.SetCustomVar(InstallSaddleReq, 0); if (reqBags) buffs.SetCustomVar(InstallBagsReq, 0); return; }

			if (reqSaddle)
			{
				buffs.SetCustomVar(SaddledVar, 1);
				buffs.SetCustomVar(InstallSaddleReq, 0);
				// Attach saddle visual; request on client too
				SaddleAssets.TryAttachSaddle(self);
				// Mirror to client: briefly toggle a visual sync cvar
				try { buffs.SetCustomVar("dwSaddleSync", 1); } catch { }
				// Force reapply next tick to ensure transforms update across saves/loads
				try { buffs.SetCustomVar(SaddleReapplyCvar, 1); } catch { }
				// Also nudge the local player's UI with a hint
				try
				{
					var ownerIdLocal = (int)buffs.GetCustomVar(OwnerCvar);
					var ownerLocal = self.world?.GetEntity(ownerIdLocal) as EntityPlayerLocal;
					if (ownerLocal != null && ownerLocal == self.world?.GetPrimaryPlayer())
					{
						GameManager.ShowTooltip(ownerLocal, "Saddle installed. Press E near your wolf to ride.");
					}
				}
				catch { }
			}
			if (reqBags)
			{
				int saddled = 0; try { saddled = (int)buffs.GetCustomVar(SaddledVar); } catch { saddled = 0; }
				if (saddled == 1)
				{
					buffs.SetCustomVar(StorageTierVar, 1);
				}
				buffs.SetCustomVar(InstallBagsReq, 0);
			}
		}

		private static void ClearFollowState(int entityId)
		{
			FollowTargetByCompanion.Remove(entityId);
			NextRepathTimeByCompanion.Remove(entityId);
			LastProgressDistanceByCompanion.Remove(entityId);
			LastProgressCheckTimeByCompanion.Remove(entityId);
		}

		private static Vector3 PickWaypointAroundOwner(Vector3 ownerPos, Quaternion ownerRot, float minRadius, float maxRadius)
		{
			// Prefer behind the player (180 deg) within +/-45 deg, with some randomness
			float baseAngle = 180f * Mathf.Deg2Rad;
			float jitter = UnityEngine.Random.Range(-45f, 45f) * Mathf.Deg2Rad;
			float angle = baseAngle + jitter;
			float radius = UnityEngine.Random.Range(minRadius, maxRadius);
			Vector3 local = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
			Vector3 worldDir = ownerRot * local;
			Vector3 target = ownerPos + worldDir;
			target.y = ownerPos.y + 0.1f;
			return target;
		}

		private static bool TrySetMoveTo(EntityAlive self, Vector3 target, bool run)
		{
			try
			{
				var t = self.GetType();
				var moveHelperField = t.GetField("moveHelper", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (moveHelperField == null) return false;
				var moveHelper = moveHelperField.GetValue(self);
				if (moveHelper == null) return false;

				var mhType = moveHelper.GetType();
				var m = mhType.GetMethod("SetMoveTo", new Type[] { typeof(Vector3), typeof(bool) });
				if (m != null)
				{
					m.Invoke(moveHelper, new object[] { target, run });
					return true;
				}
				m = mhType.GetMethod("SetMoveTo", new Type[] { typeof(Vector3) });
				if (m != null)
				{
					m.Invoke(moveHelper, new object[] { target });
					return true;
				}
			}
			catch { }
			return false;
		}

		private static void TryEnforceRun(EntityAlive self)
		{
			try
			{
				var t = self.GetType();
				var moveHelperField = t.GetField("moveHelper", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (moveHelperField == null) return;
				var moveHelper = moveHelperField.GetValue(self);
				if (moveHelper == null) return;
				var mhType = moveHelper.GetType();
				var runField = mhType.GetField("running", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (runField != null)
				{
					runField.SetValue(moveHelper, true);
					return;
				}
				var setRun = mhType.GetMethod("SetRunning", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(bool) }, null);
				if (setRun != null)
				{
					setRun.Invoke(moveHelper, new object[] { true });
				}
			}
			catch { }
		}

		private static void StopMove(EntityAlive self)
		{
			try
			{
				var t = self.GetType();
				var moveHelperField = t.GetField("moveHelper", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (moveHelperField == null) return;
				var moveHelper = moveHelperField.GetValue(self);
				if (moveHelper == null) return;
				var mhType = moveHelper.GetType();
				var stop = mhType.GetMethod("Stop", Type.EmptyTypes);
				if (stop != null) { stop.Invoke(moveHelper, null); return; }
				var clear = mhType.GetMethod("Clear", Type.EmptyTypes);
				if (clear != null) { clear.Invoke(moveHelper, null); return; }
			}
			catch { }
		}

		private static bool IsCompanion(EntityAlive self)
		{
			try
			{
				if (self == null || self.EntityClass == null) return false;
				var cls = self.EntityClass;
				string ecn = cls.entityClassName;
				if (string.Equals(ecn, CompanionClassName, StringComparison.OrdinalIgnoreCase)) return true;
				string tags = cls.Properties?.Values != null && cls.Properties.Values.ContainsKey("Tags") ? cls.Properties.Values["Tags"] : string.Empty;
				return !string.IsNullOrEmpty(tags) && tags.Contains("companion");
			}
			catch { return false; }
		}

		private static EntityPlayer GetNearestPlayer(World world, UnityEngine.Vector3 pos, float maxDist)
		{
			EntityPlayer nearest = null;
			float best = maxDist * maxDist;
			var list = world?.Players?.list as List<EntityPlayer>;
			if (list == null) return null;
			foreach (var p in list)
			{
				if (p == null) continue;
				float d2 = (p.position - pos).sqrMagnitude;
				if (d2 < best)
				{
					best = d2;
					nearest = p;
				}
			}
			return nearest;
		}

		private static EntityAlive GetNearestHostileNear(World world, Vector3 pos, float radius)
		{
			try
			{
				var list = world?.Entities?.list;
				if (list == null) return null;
				EntityAlive best = null;
				float bestD2 = radius * radius;
				foreach (var e in list)
				{
					if (e == null) continue;
					var ea = e as EntityAlive;
					if (ea == null) continue;
					if (ea is EntityPlayer) continue;
					// Treat undead and enemy animals as hostiles by tag
					var cls = ea.EntityClass;
					if (cls == null) continue;
					string tags = cls.Properties?.Values != null && cls.Properties.Values.ContainsKey("Tags") ? cls.Properties.Values["Tags"] : string.Empty;
					if (!tags.Contains("zombie") && !tags.Contains("enemy") && !tags.Contains("bandit")) continue;
					float d2 = (ea.position - pos).sqrMagnitude;
					if (d2 < bestD2)
					{
						bestD2 = d2;
						best = ea;
					}
				}
				return best;
			}
			catch { return null; }
		}

		internal static void RemoveExistingCompanion(World world, int ownerId, int newEntityId)
		{
			try
			{
				if (CompanionByOwner.TryGetValue(ownerId, out var oldId) && oldId != newEntityId)
				{
					OwnerByCompanion.Remove(oldId);
					CompanionByOwner.Remove(ownerId);
					// Only the authority should despawn
					var old = world.GetEntity(oldId) as EntityAlive;
					if (old != null && !old.isEntityRemote)
					{
						world.RemoveEntity(oldId, EnumRemoveEntityReason.Despawned);
						try { UnityEngine.Debug.Log($"[DireWolfMod] Despawned previous wolf {oldId} for owner {ownerId}"); } catch { }
					}
				}
			}
			catch { }
		}
	}
}


