using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Compat;
using Verse;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

/// <summary>
///     Vanilla Expanded Framework and other Vanilla Expanded mods by Oskar Potocki, XeoNovaDan, Orion, Kikohi, Taranchuk,
///     Sarg Bjornson, Erdelf,
///     Last Update: 5 Oct @ 7:10pm 2026
///     <see href="https://github.com/Vanilla-Expanded/VanillaExpandedFramework" />
///     <see href="https://steamcommunity.com/sharedfiles/filedetails/?id=2023507013" />
/// </summary>
[MpCompatFor("OskarPotocki.VanillaFactionsExpanded.Core")]
internal partial class VanillaExpandedFramework
{
    private const string LogPrefix = "[Multiplayer Vanilla Expanded Framework Patch]";

    public VanillaExpandedFramework(ModContentPack mod)
    {
        Log.Message($"{LogPrefix} Initializing...");

        (Action patchMethod, string componentName, bool latePatch)[] patches =
        [
            (PatchAdvancedResourceProcessor, "Advanced Resource Processor", true),
            (PatchOtherRng, "Other RNG", false),
            (PatchWeapons, "Weapons", false),
            (PatchDebug, "Debug Gizmos", false),
            (PatchAbilities, "Abilities", true),
            (PatchHireableFactions, "Hireable Factions", false),
            (PatchVanillaFurnitureExpanded, "Vanilla Furniture Expanded", true),
            (PatchAnimalBehaviour, "Animal Behaviour", false),
            (PatchMVCF, "Multi-Verb Combat Framework", false),
            (PatchVanillaApparelExpanded, "Vanilla Apparel Expanded", false),
            (PatchPipeSystem, "Pipe System", true),
            (PatchKCSG, "KCSG (custom structure generation)", false),
            (PatchFactionDiscovery, "Faction Discovery", false),
            (PatchGenes, "Genes", false),
            (PatchCooking, "Cooking", true),
            (PatchDoorTeleporter, "Teleporter Doors", true),
            (PatchSpecialTerrain, "Special Terrain", true),
            (PatchWeatherOverlayEffects, "Weather Overlay Effects", false),
            (PatchExtraPregnancyApproaches, "Extra Pregnancy Approaches", false),
            (PatchWorkGiverDeliverResources, "Building stuff requiring non-construction skill", false),
            (PatchGraphicCustomizationDialog, "Graphic Customization Dialog", true),
            (PatchDraftedAi, "Drafted AI", true),
            (PatchMapObjectGeneration, "Thing spawning on map generation (ObjectSpawnsDef)", false),
            (PatchQuestChainsDevMode, "Quest chain dev mode window", false),
            (PatchMovingBases, "Moving bases", true)
        ];

        foreach (var (patchMethod, componentName, latePatch) in patches)
        {
            if (latePatch)
                LongEventHandler.ExecuteWhenFinished(ApplyPatch);
            else
                ApplyPatch();

            void ApplyPatch()
            {
                try
                {
#if DEBUG
                    Log.Message($"{LogPrefix} Patching Vanilla Expanded Framework - {componentName}");
#endif

                    patchMethod();
                }
                catch (Exception exception)
                {
                    Log.Error(
                        $"{LogPrefix} Encountered an error patching {componentName} part of Vanilla Expanded Framework - this part of the mod may not work properly!");
                    Log.Error(exception.ToString());
                }
            }
        }

        Log.Message($"{LogPrefix} Initialized.");
    }

    #region Shared sync workers and patches

    private static void SyncCommandWithCompBuilding(SyncWorker sync, ref Command command)
    {
        var traverse = Traverse.Create(command);
        var building = traverse.Field("building");

        if (sync.isWriting)
            sync.Write(building.GetValue() as ThingComp);
        else
            building.SetValue(sync.Read<ThingComp>());
    }

    #endregion
}