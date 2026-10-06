using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Compat;
using RimWorld;
using Verse;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

internal partial class VanillaExpandedFramework
{
    #region Faction Discovery

    // Dialog_NewFactionSpawning
    private static Type newFactionSpawningDialogType;
    private static AccessTools.FieldRef<object, FactionDef> factionDefField;

    private static void PatchFactionDiscovery()
    {
        newFactionSpawningDialogType = AccessTools.TypeByName("VEF.Factions.Dialog_NewFactionSpawning");
        factionDefField = AccessTools.FieldRefAccess<FactionDef>(newFactionSpawningDialogType, "factionDef");

        MP.RegisterSyncMethod(MpMethodUtil.GetLocalFunc(newFactionSpawningDialogType, "SpawnWithBases",
            localFunc: "SpawnCallback"));
        MP.RegisterSyncMethod(newFactionSpawningDialogType, "SpawnWithoutBases");
        MP.RegisterSyncMethod(newFactionSpawningDialogType, "Ignore");
        MP.RegisterSyncWorker<Window>(SyncFactionDiscoveryDialog, newFactionSpawningDialogType);

        // This will only open the dialog for host only on game load, but will
        // allow other players to access it from the mod settings.
        var type = AccessTools.TypeByName(
            "VEF.Factions.VanillaExpandedFramework_GameComponentUtility_LoadedGame_Patch");
        type = AccessTools.Inner(type, "LoadedGame");
        MpCompat.harmony.Patch(AccessTools.Method(type, "OnGameLoaded"),
            new HarmonyMethod(typeof(VanillaExpandedFramework), nameof(HostOnlyNewFactionDialog)));
    }

    private static void SyncFactionDiscoveryDialog(SyncWorker sync, ref Window window)
    {
        if (sync.isWriting)
        {
            sync.Write(factionDefField(window));
        }
        else
        {
            // For the person using the dialog, grab the existing one as we'll need to call the method on that instance
            // to open the next dialog with new faction.
            window = Find.WindowStack.Windows.FirstOrDefault(x => x.GetType() == newFactionSpawningDialogType);
            // We need to load the def, even if we don't use it - otherwise the synced method parameters will end up messed up
            var factionDef = sync.Read<FactionDef>();

            if (window == null)
            {
                window ??= (Window)Activator.CreateInstance(
                    newFactionSpawningDialogType,
                    AccessTools.allDeclared,
                    null,
                    [new List<FactionDef>().GetEnumerator()],
                    null);
                factionDefField(window) = factionDef;
            }
        }
    }

    private static bool HostOnlyNewFactionDialog()
    {
        return !MP.IsInMultiplayer || MP.IsHosting;
    }

    #endregion
}