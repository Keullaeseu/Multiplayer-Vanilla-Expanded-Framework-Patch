using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Compat;
using Verse;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

internal partial class VanillaExpandedFramework
{
    #region Special Terrain

    private static Type terrainCompType;
    private static AccessTools.FieldRef<object, object> terrainCompParentField;
    private static AccessTools.FieldRef<object, IntVec3> terrainInstancePositionField;

    private static void PatchSpecialTerrain()
    {
        MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VEF.Maps.SpecialTerrainList:TerrainUpdate"),
            new HarmonyMethod(typeof(VanillaExpandedFramework), nameof(RemoveTerrainUpdateTimeBudget)));

        // Fix unsafe GetHashCode call
        terrainCompType = AccessTools.TypeByName("VEF.Maps.TerrainComp");
        terrainCompParentField = AccessTools.FieldRefAccess<object>(terrainCompType, "parent");
        terrainInstancePositionField = AccessTools.FieldRefAccess<IntVec3>("VEF.Maps.TerrainInstance:positionInt");

        MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VEF.Maps.ActiveTerrainUtility:HashCodeToMod"),
            new HarmonyMethod(MpMethodUtil.MethodOf(SaferGetHashCode)));
    }

    private static void RemoveTerrainUpdateTimeBudget(ref long timeBudget)
    {
        if (MP.IsInMultiplayer)
            timeBudget = long.MaxValue; // Basically limitless time

        // The method is limited in updating a max of 1/3 of all active special terrains.
        // If we'd want to work on having a performance option of some sort, we'd have to
        // base it around amount of terrain updates per tick, instead of basing it on actual time.
    }

    private static void SaferGetHashCode(ref object obj)
    {
        if (!MP.IsInMultiplayer || obj is IntVec3)
            return;
        if (terrainCompType.IsInstanceOfType(obj))
        {
            // Use parent IntVec3 position, since it'll be safe to call GetHashCode on
            obj = terrainInstancePositionField(terrainCompParentField(obj));
            return;
        }

        Log.ErrorOnce(
            $"{LogPrefix} Potentially unsupported type for HashCodeToMod call in Multiplayer, desyncs likely to happen. Object type: {obj.GetType()}",
            obj.GetHashCode());
    }

    #endregion
}