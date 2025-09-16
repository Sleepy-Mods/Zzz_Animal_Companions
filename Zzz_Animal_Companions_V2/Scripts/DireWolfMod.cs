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
			try { SaddleAssets.Initialize(mod); } catch { }
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
		private const string InstallSaddleReq = "dwInstallSaddleReq";
		private const string InstallBagsReq = "dwInstallBagsReq";
		private const string MoveVCvar = "dwMoveV";
		private const string MoveHCvar = "dwMoveH";
		private const string SprintCvar = "dwSprint";
		private const string MountCvar = "dwMounted";
		private const string RiderVar = "dwRiderId";
		private const string ReqMountWolfId = "dwReqMountWolfId";
		private const string ReqMountAction = "dwReqMountAction";

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
				// Map client install requests and driving/mount state to the server-owned wolf for reliability
				HandleInstallRequests(player);
				HandleMountState(player);
				HandleDrivingInput(player);

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

		private static void HandleMountState(EntityPlayer player)
		{
			try
			{
				int wolfId = 0; int action = -1;
				try { wolfId = (int)player.Buffs.GetCustomVar(ReqMountWolfId); } catch { wolfId = 0; }
				try { action = (int)player.Buffs.GetCustomVar(ReqMountAction); } catch { action = -1; }
				if (wolfId == 0 || action < 0) return;
				player.Buffs.SetCustomVar(ReqMountWolfId, 0);
				player.Buffs.SetCustomVar(ReqMountAction, 0);

				var world = GameManager.Instance?.World; if (world == null) return;
				var wolf = world.GetEntity(wolfId) as EntityAlive; if (wolf == null) return;
				int ownerId = 0; try { ownerId = (int)wolf.Buffs.GetCustomVar(OwnerCvar); } catch { ownerId = 0; }
				if (ownerId != player.entityId) return;
				if (action == 1)
				{
					wolf.Buffs?.SetCustomVar(MountCvar, 1);
					wolf.Buffs?.SetCustomVar(RiderVar, player.entityId);
				}
				else
				{
					wolf.Buffs?.SetCustomVar(MountCvar, 0);
					wolf.Buffs?.SetCustomVar(RiderVar, 0);
				}
			}
			catch { }
		}

		private static void HandleDrivingInput(EntityPlayer player)
		{
			try
			{
				float axV = 0f, axH = 0f; bool sprint = false;
				try { axV = player.Buffs.GetCustomVar(MoveVCvar); } catch { }
				try { axH = player.Buffs.GetCustomVar(MoveHCvar); } catch { }
				try { sprint = player.Buffs.GetCustomVar(SprintCvar) > 0f; } catch { }
				if (axV == 0f && axH == 0f && !sprint) return;
				var world = GameManager.Instance?.World; if (world == null) return;
				// Find player’s mounted wolf
				EntityAlive mounted = null;
				var list = world.Entities?.list; if (list == null) return;
				foreach (var e in list)
				{
					var ea = e as EntityAlive; if (ea == null || ea.EntityClass == null) continue;
					if (!string.Equals(ea.EntityClass.entityClassName, CompanionClassName)) continue;
					int riderId = 0; try { riderId = (int)ea.Buffs.GetCustomVar(RiderVar); } catch { riderId = 0; }
					int mountedFlag = 0; try { mountedFlag = (int)ea.Buffs.GetCustomVar(MountCvar); } catch { mountedFlag = 0; }
					if (mountedFlag == 1 && riderId == player.entityId) { mounted = ea; break; }
				}
				if (mounted == null) return;
				mounted.Buffs?.SetCustomVar(MoveVCvar, axV);
				mounted.Buffs?.SetCustomVar(MoveHCvar, axH);
				mounted.Buffs?.SetCustomVar(SprintCvar, sprint ? 1 : 0);
			}
			catch { }
		}

		private static void HandleInstallRequests(EntityPlayer player)
		{
			try
			{
				bool reqSaddle = false;
				bool reqBags = false;
				try { reqSaddle = (player.Buffs.GetCustomVar(InstallSaddleReq) > 0f); } catch { }
				try { reqBags = (player.Buffs.GetCustomVar(InstallBagsReq) > 0f); } catch { }
				if (!reqSaddle && !reqBags) return;

				var world = GameManager.Instance?.World;
				if (world == null) return;
				// Prefer wolf under crosshair; else fallback to nearest
				var wolf = FindWolfUnderCrosshair(world, player) ?? FindNearestOwnedCompanionWolf(world, player.position, player.entityId, 4.0f);
				if (wolf != null)
				{
					if (reqSaddle) wolf.Buffs?.SetCustomVar(InstallSaddleReq, 1);
					if (reqBags) wolf.Buffs?.SetCustomVar(InstallBagsReq, 1);
				}
				// Clear the player requests to prevent re-triggering
				try { if (reqSaddle) player.Buffs.SetCustomVar(InstallSaddleReq, 0); } catch { }
				try { if (reqBags) player.Buffs.SetCustomVar(InstallBagsReq, 0); } catch { }
			}
			catch { }
		}

		private static EntityAlive FindWolfUnderCrosshair(World world, EntityPlayer player)
		{
			try
			{
				if (world == null || player == null) return null;
				Vector3 origin = player.position + new Vector3(0, 1.6f, 0);
				Vector3 dir = player.GetLookVector();
				float maxDist = 4.0f;
				EntityAlive best = null;
				float bestDot = 0.55f;
				var list = world.Entities?.list;
				if (list == null) return null;
				foreach (var e in list)
				{
					var ea = e as EntityAlive;
					if (ea == null || ea.EntityClass == null) continue;
					bool isWolf = string.Equals(ea.EntityClass.entityClassName, CompanionClassName, System.StringComparison.OrdinalIgnoreCase);
					if (!isWolf)
					{
						string tags = ea.EntityClass.Properties?.Values != null && ea.EntityClass.Properties.Values.ContainsKey("Tags") ? ea.EntityClass.Properties.Values["Tags"] : string.Empty;
						if (string.IsNullOrEmpty(tags) || !tags.Contains("companion") || !tags.Contains("wolf")) continue;
					}
					Vector3 center = ea.position + new Vector3(0, 0.8f, 0);
					float d2 = (center - origin).sqrMagnitude;
					if (d2 > (maxDist * maxDist)) continue;
					Vector3 to = (center - origin).normalized;
					float dot = Vector3.Dot(dir, to);
					if (dot > bestDot)
					{
						bestDot = dot;
						best = ea;
					}
				}
				return best;
			}
			catch { return null; }
		}

		private static EntityAlive FindNearestOwnedCompanionWolf(World world, Vector3 pos, int ownerId, float maxDist)
		{
			try
			{
				EntityAlive best = null;
				float bestD2 = maxDist * maxDist;
				var list = world?.Entities?.list;
				if (list == null) return null;
				foreach (var e in list)
				{
					var ea = e as EntityAlive;
					if (ea == null || ea.EntityClass == null) continue;
					bool isWolf = string.Equals(ea.EntityClass.entityClassName, CompanionClassName, System.StringComparison.OrdinalIgnoreCase);
					if (!isWolf)
					{
						string tags = ea.EntityClass.Properties?.Values != null && ea.EntityClass.Properties.Values.ContainsKey("Tags") ? ea.EntityClass.Properties.Values["Tags"] : string.Empty;
						if (string.IsNullOrEmpty(tags) || !tags.Contains("companion") || !tags.Contains("wolf")) continue;
					}
					int wolfOwner = 0; try { wolfOwner = (int)ea.Buffs.GetCustomVar(OwnerCvar); } catch { wolfOwner = 0; }
					if (wolfOwner != ownerId) continue;
					float d2 = (ea.position - pos).sqrMagnitude;
					if (d2 < bestD2) { bestD2 = d2; best = ea; }
				}
				return best;
			}
			catch { return null; }
		}

		// no explicit is-server check; gating via player.isEntityRemote covers host and dedicated
	}

	public static class SaddleAssets
	{
		private static AssetBundle _bundle;
		private static GameObject _saddlePrefab;

		private const string BundleRelPath = "Resources/direWolfSaddle.unity3d";
		private const string PrefabName = "direWolfSaddle_Prefab"; // provided by user
		private const string AttachName = "dwSaddleGO";

		public static void Initialize(Mod mod)
		{
			try
			{
				var modPath = mod.Path;
				var bundlePath = System.IO.Path.Combine(modPath, BundleRelPath);
				if (!System.IO.File.Exists(bundlePath))
				{
					UnityEngine.Debug.Log($"[DireWolfMod] Saddle bundle not found at {bundlePath}");
					return;
				}
				_bundle = AssetBundle.LoadFromFile(bundlePath);
				if (_bundle == null)
				{
					UnityEngine.Debug.Log("[DireWolfMod] Failed to load saddle AssetBundle");
					return;
				}
				_saddlePrefab = _bundle.LoadAsset<GameObject>(PrefabName);
				if (_saddlePrefab == null)
				{
					UnityEngine.Debug.Log($"[DireWolfMod] Failed to load prefab '{PrefabName}' from bundle");
				}
				else
				{
					UnityEngine.Debug.Log("[DireWolfMod] Saddle asset loaded");
				}
			}
			catch { }
		}

		public static void TryAttachSaddle(EntityAlive wolf)
		{
			try
			{
				if (wolf == null || _saddlePrefab == null) return;
				var existing = FindChildByName(wolf, AttachName);
				if (existing != null)
				{
					existing.transform.localPosition = new Vector3(0f, 0.475f, -0.31f);
					existing.transform.localEulerAngles = new Vector3(-30f, 0f, 0f);
					existing.transform.localScale = Vector3.one * 0.55f;
					return;
				}

				var go = UnityEngine.Object.Instantiate(_saddlePrefab);
				go.name = AttachName;
				var parent = wolf.transform;
				go.transform.SetParent(parent, false);
				go.transform.localPosition = new Vector3(0f, 0.475f, -0.31f);
				go.transform.localEulerAngles = new Vector3(-30f, 0f, 0f);
				go.transform.localScale = Vector3.one * 0.55f;
				TryClearTags(go);
				TryStripPhysics(go);
				// Mark for one more reapply on next tick to catch late-loaded bones or rig offsets
				try { wolf.Buffs?.SetCustomVar("dwSaddleReapply", 1); } catch { }
			}
			catch { }
		}

		public static void TryDetachSaddle(EntityAlive wolf)
		{
			try
			{
				var existing = FindChildByName(wolf, AttachName);
				if (existing != null)
				{
					UnityEngine.Object.Destroy(existing);
				}
			}
			catch { }
		}

		private static GameObject FindChildByName(EntityAlive wolf, string name)
		{
			try
			{
				var t = wolf.transform;
				for (int i = 0; i < t.childCount; i++)
				{
					var c = t.GetChild(i);
					if (c != null && c.gameObject != null && c.gameObject.name == name)
						return c.gameObject;
				}
			}
			catch { }
			return null;
		}

		private static void TryStripPhysics(GameObject root)
		{
			try
			{
				if (root == null) return;
				// Remove/disable physics-heavy components to avoid convex mesh warnings and spikes
				foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
				{
					try { mc.convex = false; mc.enabled = false; } catch { }
				}
				foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
				{
					try { UnityEngine.Object.Destroy(rb); } catch { }
				}
				// Optional: ensure there is at most one simple box collider if ever needed (currently none)
			}
			catch { }
		}

		private static void TryClearTags(GameObject root)
		{
			try
			{
				if (root == null) return;
				var all = root.GetComponentsInChildren<Transform>(true);
				foreach (var tr in all)
				{
					try { tr.gameObject.tag = "Untagged"; } catch { }
				}
			}
			catch { }
		}
	}
}


