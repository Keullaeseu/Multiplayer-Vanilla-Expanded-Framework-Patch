using System.Collections;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Compat;
using Verse;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

internal partial class VanillaExpandedFramework
{
    #region Hireable Factions

    // Dialog_Hire
    private static Type hireDialogType;
    private static AccessTools.FieldRef<object, Dictionary<PawnKindDef, Pair<int, string>>> hireDataField;
    private static ISyncField daysAmountField;

    private static ISyncField currentFactionDefField;

    // Dialog_ContractInfo
    private static Type contractInfoDialogType;

    // HireableSystemStaticInitialization
    private static AccessTools.FieldRef<IList> hireablesList;

    private static void PatchHireableFactions()
    {
        hireDialogType = AccessTools.TypeByName("VEF.Planet.Dialog_Hire");

        DialogUtilities.RegisterDialogCloseSync(hireDialogType, true);
        MP.RegisterSyncMethod(hireDialogType, nameof(Window.OnAcceptKeyPressed));
        MP.RegisterSyncWorker<Window>(SyncHireDialog, hireDialogType);
        MP.RegisterSyncMethod(typeof(VanillaExpandedFramework), nameof(SyncedSetHireData));
        hireDataField =
            AccessTools.FieldRefAccess<Dictionary<PawnKindDef, Pair<int, string>>>(hireDialogType, "hireData");

        // I don't think daysAmountBuffer needs to be synced, just daysAmount only
        daysAmountField = MP.RegisterSyncField(hireDialogType, "daysAmount");
        currentFactionDefField = MP.RegisterSyncField(hireDialogType, "curFaction");
        MpCompat.harmony.Patch(AccessTools.Method(hireDialogType, nameof(Window.DoWindowContents)),
            new HarmonyMethod(typeof(VanillaExpandedFramework), nameof(PreHireDialogDoWindowContents)),
            new HarmonyMethod(typeof(VanillaExpandedFramework), nameof(PostHireDialogDoWindowContents)));

        // There seems to be a 50/50 chance trying to open hiring window will fail and cause an error
        // this is here to fix that issue
        var type = AccessTools.TypeByName("VEF.Planet.Hireable");
        MP.RegisterSyncWorker<object>(SyncHireable, type);
        MpCompat.RegisterLambdaDelegate(type, "CommFloatMenuOption", 0);

        hireablesList = AccessTools.StaticFieldRefAccess<IList>(
            AccessTools.Field(AccessTools.TypeByName("VEF.Planet.HireableSystemStaticInitialization"), "Hireables"));

        contractInfoDialogType = AccessTools.TypeByName("VEF.Planet.Dialog_ContractInfo");

        DialogUtilities.RegisterDialogCloseSync(contractInfoDialogType, true);
        MP.RegisterSyncWorker<Window>(SyncContractInfoDialog, contractInfoDialogType);
        // Cancel contract confirmation (1). Ordinal 0 is a pure pawn list filter, needs no sync.
        MpCompat.RegisterLambdaMethod(contractInfoDialogType, "DoWindowContents", 1);

        MpCompat.RegisterLambdaDelegate("VEF.Planet.HiringContractTracker", "CommFloatMenuOption", 0);
    }

    private static void SyncHireDialog(SyncWorker sync, ref Window dialog)
    {
        // The dialog should just be open
        if (!sync.isWriting)
            dialog = Find.WindowStack.Windows.FirstOrDefault(x => x.GetType() == hireDialogType);
    }

    private static void PreHireDialogDoWindowContents(Window __instance,
        Dictionary<PawnKindDef, Pair<int, string>> ___hireData, ref Dictionary<PawnKindDef, Pair<int, string>> __state)
    {
        if (!MP.IsInMultiplayer)
            return;

        MP.WatchBegin();
        daysAmountField.Watch(__instance);
        currentFactionDefField.Watch(__instance);

        __state = ___hireData.ToDictionary(x => x.Key, x => x.Value);
    }

    private static void PostHireDialogDoWindowContents(Window __instance,
        Dictionary<PawnKindDef, Pair<int, string>> ___hireData, Dictionary<PawnKindDef, Pair<int, string>> __state)
    {
        if (!MP.IsInMultiplayer)
            return;

        MP.WatchEnd();

        foreach (var (pawn, value) in __state)
            if (value.First != ___hireData[pawn].First)
            {
                hireDataField(__instance) = __state;
                SyncedSetHireData(___hireData);
                break;
            }
    }

    private static void SyncedSetHireData(Dictionary<PawnKindDef, Pair<int, string>> hireData)
    {
        var dialog = Find.WindowStack.Windows.FirstOrDefault(x => x.GetType() == hireDialogType);

        if (dialog != null)
            hireDataField(dialog) = hireData;
    }

    private static void SyncContractInfoDialog(SyncWorker sync, ref Window window)
    {
        if (!sync.isWriting)
            window = Find.WindowStack.windows.FirstOrDefault(x => x.GetType() == contractInfoDialogType);
    }

    private static void SyncHireable(SyncWorker sync, ref object obj)
    {
        if (sync.isWriting)
            sync.Write(hireablesList().IndexOf(obj));
        else
            obj = hireablesList()[sync.Read<int>()];
    }

    #endregion
}