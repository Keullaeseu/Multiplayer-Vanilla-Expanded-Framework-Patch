using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Compat;
using Verse;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

internal partial class VanillaExpandedFramework
{
    #region Thing spawning on map generation (ObjectSpawnsDef)

    private static void PatchMapObjectGeneration()
    {
        // When calling "LongEventHandler.ExecuteWhenFinished" inside a
        // "MapGenerator:GenerateMap" call the seed will differ between
        // players. Is this something we should look into in MP itself?
        MpCompat.harmony.Patch(
            AccessTools.DeclaredMethod("VEF.Maps.VanillaExpandedFramework_MapGenerator_GenerateMap_Patch:DoMapSpawns"),
            new HarmonyMethod(PreDoSpawns),
            finalizer: new HarmonyMethod(PostDoSpawns));
    }

    private static void PreDoSpawns(Map map)
    {
        if (MP.IsInMultiplayer)
            Rand.PushState(Gen.HashCombineInt(Gen.HashCombineInt(map.uniqueID, map.Tile), Find.TickManager.TicksGame));
    }

    private static void PostDoSpawns()
    {
        if (MP.IsInMultiplayer)
            Rand.PopState();
    }

    #endregion
}