using System.Collections.Generic;
using HarmonyLib;
using Klei.AI;
using UnityEngine;

namespace PlanningTool.HarmonyPatches
{
    [HarmonyPatch(typeof(SaveGame), "OnPrefabInit")]
    public class SaveGame_OnPrefabInit_Patch
    {
        public static void Postfix(SaveGame __instance)
        {
            __instance.gameObject.AddOrGet<SaveLoadPlans>();
        }
    }

    [HarmonyPatch(typeof(Db), nameof(Db.Initialize))]
    public class Db_Initialize_Patch
    {
        public static void Postfix()
        {
            PlanningToolUserMod.OnDbInitialized();
        }
    }

    [HarmonyPatch(typeof(ToolMenu), "CreateBasicTools")]
    public class ToolMenu_CreateBasicTools_Patch
    {
        public static void Postfix(ToolMenu __instance)
        {
            __instance.basicTools.Insert(0,
                ToolMenu.CreateToolCollection(PTStrings.PLANNING_TOOL_NAME, PTAssets.IconToolPlanning.name,
                    ToolKeyBindings.PlanningToolAction.GetKAction(), nameof(PlanningToolInterface),
                    string.Format(PTStrings.PLANNING_TOOL_TOOLTIP, "{Hotkey}"), true));
        }
    }

    [HarmonyPatch(typeof(GridSettings), nameof(GridSettings.Reset))]
    public class GridSettings_Reset_Patch
    {
        public static void Postfix()
        {
            PlanGrid.Initialize();
        }
    }

    [HarmonyPatch(typeof(Game), "DestroyInstances")]
    public class Game_DestroyInstances_Patch
    {
        public static void Postfix()
        {
            PlanningToolInterface.DestroyInstance();
            PlanningToolBrushMenu.DestroyInstance();
            PlanningToolSettings.DestroyInstance();
            PlanGrid.Clear();
            SaveLoadPlans.DestroyInstance();
        }
    }

    [HarmonyPatch(typeof(PlayerController), "OnPrefabInit")]
    public class PlayerController_OnPrefabInit_Patch
    {
        public static void Postfix(PlayerController __instance)
        {
            var tools = new List<InterfaceTool>(__instance.tools);
            var createPlanTool = new GameObject(nameof(PlanningToolInterface), typeof(PlanningToolInterface));
            createPlanTool.transform.SetParent(__instance.gameObject.transform);
            createPlanTool.SetActive(true);
            createPlanTool.SetActive(false);
            tools.Add(createPlanTool.GetComponent<InterfaceTool>());
            __instance.tools = tools.ToArray();
        }
    }

    [HarmonyPatch(typeof(ToolMenu), "OnPrefabInit")]
    public class ToolMenu_OnPrefabInit_Patch
    {
        public static void Postfix()
        {
            // set up planning tool submenu
            var screen = ToolMenu.Instance.gameObject.AddComponent<PlanningToolBrushMenu>();
            screen.Activate();
        }
    }

    [HarmonyPatch(typeof(CancelTool))]
    public class CancelTool_Patch
    {
        public static string PLANNINGTOOL_PLAN = nameof(PLANNINGTOOL_PLAN);

        [HarmonyPatch("GetDefaultFilters")]
        [HarmonyPostfix]
        public static void GetDefaultFiltersPatch(Dictionary<string, ToolParameterMenu.ToggleState> filters)
        {
            filters.Add(PLANNINGTOOL_PLAN, ToolParameterMenu.ToggleState.Off);
        }

        [HarmonyPatch("OnDragTool")]
        [HarmonyPostfix]
        public static void OnDragToolPatch(int cell, CancelTool __instance)
        {
            var go = PlanGrid.Plans[cell];
            if (go != null && __instance.IsActiveLayer(PLANNINGTOOL_PLAN))
            {
                PlanGrid.Plans[cell] = null;
                SaveLoadPlans.Instance.PlanState.Remove(cell);
                Object.Destroy(go);
            }
        }

        [HarmonyPatch("OnPrefabInit")]
        [HarmonyPostfix]
        public static void OnPrefabInitPatch(CancelTool __instance, Dictionary<string, ToolParameterMenu.ToggleState> ___filterTargets, Dictionary<string, ToolParameterMenu.ToggleState> ___overlayFilterTargets, ref Dictionary<string, ToolParameterMenu.ToggleState> ___currentFilterTargets)
        {
            var resetFilterMethod = AccessTools.Method(typeof(CancelTool), "ResetFilter",
                new[] { typeof(Dictionary<string, ToolParameterMenu.ToggleState>) });
            if (resetFilterMethod == null)
            {
                Debug.LogWarning("[PlanningTool] Unable to find CancelTool.ResetFilter");
                return;
            }

            if (___filterTargets == null || ___overlayFilterTargets == null || ___currentFilterTargets == null)
            {
                Debug.LogWarning("[PlanningTool] Fields in CancelTool do not match expected values, should all be initialized at this point.");
                return;
            }

            // PlanningToolInterface.SelectPlansAsToolFilter modifies overlayFilterTargets but it is not populated until the tool is selected for the first time,
            // so populate it earlier to allow changes to stick on first tool switch.
            // Note: ResetFilter assigns currentFilterTargets to the called filter dict, so reassign it back to filterTargets (shouldn't affect anything anyway)
            resetFilterMethod.Invoke(__instance, new object[]{___overlayFilterTargets});
            ___currentFilterTargets = ___filterTargets;
        }
    }

    /// <summary>
    /// Subscribe to GameplayEventManager.Instance.NewBuilding, and once a new building is constructed remove a plan
    /// on the building cells if the option is set.
    /// </summary>
    [HarmonyPatch(typeof(GameplayEventManager))]
    public class RemovePlanTileOnBuildingBuiltPatch
    {
        public static int SubscriptionId;

        [HarmonyPatch("OnPrefabInit")]
        [HarmonyPostfix]
        public static void OnPrefabInit_SetupSubscriber()
        {
            var instance = GameplayEventManager.Instance;
            if (instance == null)
            {
                Debug.LogWarning("[PlanningTool] GameplayEventManager.Instance was null after OnPrefabInit (?).");
                return;
            }

            // Triggered by Constructable.FinishConstruction
            if (SubscriptionId != 0)
            {
                Debug.LogWarning($"[PlanningTool] SubscriptionId {SubscriptionId} was already defined, this shouldn't happen. Were there two instances of GameplayEventManager created? Unsubscribing.");
                instance.Unsubscribe(SubscriptionId);
            }
            SubscriptionId = instance.Subscribe((int)GameHashes.NewBuilding, NewBuildingHandler);
        }

        [HarmonyPatch("OnCleanUp")]
        [HarmonyPrefix]
        public static void OnCleanUp_RemoveSubscriber()
        {
            if (SubscriptionId == 0) return;
            if (GameplayEventManager.Instance != null)
                GameplayEventManager.Instance.Unsubscribe(SubscriptionId);
            SubscriptionId = 0;
        }

        [HarmonyPatch("DestroyInstance")]
        [HarmonyPrefix]
        public static void DestroyInstance_RemoveSubscriber()
        {
            if (SubscriptionId == 0) return;
            if (GameplayEventManager.Instance != null)
                GameplayEventManager.Instance.Unsubscribe(SubscriptionId);
            SubscriptionId = 0;
        }

        public static void NewBuildingHandler(object o)
        {
            if (!(o is BonusEvent.GameplayEventData gameplayEventData))
            {
                Debug.LogWarning("[PlanningTool] Received GameHashes.NewBuilding event with malformed data.");
                return;
            }

            if (!ModOptions.Options.RemovePlansOnConstruction)
                return;

            // See: BuildingDef.UnmarkArea
            if (gameplayEventData.workable == null)
            {
                Debug.LogWarning("[PlanningTool] gameplayEventData.workable was null.");
                return;
            }

            if (gameplayEventData.building == null)
            {
                Debug.LogWarning("[PlanningTool] gameplayEventData.building was null.");
                return;
            }

            var cell = Grid.PosToCell(gameplayEventData.workable.transform.GetPosition());
            var building = gameplayEventData.building;
            var orientation = building.Orientation;
            if (cell == Grid.InvalidCell) return;
            for (var i = 0; i < building.Def.PlacementOffsets.Length; i++)
            {
                var rotatedCellOffset = Rotatable.GetRotatedCellOffset(building.Def.PlacementOffsets[i], orientation);
                var offsetCell = Grid.OffsetCell(cell, rotatedCellOffset);
                var go = PlanGrid.Plans[offsetCell];
                if (go != null)
                {
                    PlanGrid.Plans[offsetCell] = null;
                    SaveLoadPlans.Instance.PlanState.Remove(offsetCell);
                    Object.Destroy(go);
                }
            }
        }
    }

}
