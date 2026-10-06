using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Compat;
using UnityEngine;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

internal partial class VanillaExpandedFramework
{
    #region Small and generic patches

    // Generally, here's a place for patches that are a single method, without any stored fields, and aren't too long.

    private static void PatchOtherRng()
    {
        PatchingUtilities.PatchPushPopRand([
            // Uses GenView.ShouldSpawnMotesAt and uses RNG if it returns true,
            // and it's based on player camera position. Need to push/pop or it'll desync
            // unless all players looking when it's called
            "VEF.Hediffs.HediffComp_Spreadable:ThrowFleck",
            // GenView.ShouldSpawnMotesAt again
            "VEF.Maps.TerrainComp_MoteSpawner:ThrowMote"
        ]);
    }

    private static void PatchDebug()
    {
        MpCompat.RegisterLambdaMethod("VEF.Pawns.CompPawnDependsOn", "CompGetGizmosExtra", 0).SetDebugOnly();
    }

    private static void PatchVanillaApparelExpanded()
    {
        MpCompat.RegisterLambdaMethod("VEF.Apparels.CompSwitchApparel", "CompGetWornGizmosExtra", 0);
    }

    private static void PatchAnimalBehaviour()
    {
        // RNG
        PatchingUtilities.PatchSystemRand("VEF.AnimalBehaviours.DamageWorker_ExtraInfecter:ApplySpecialEffectsToPart",
            false);
        var rngFixConstructors = new[]
        {
            "VEF.AnimalBehaviours.CompInitialHediff",
            "VEF.AnimalBehaviours.DeathActionWorker_DropOnDeath"
        };
        PatchingUtilities.PatchSystemRandCtor(rngFixConstructors, false);

        // Gizmos
        var type = AccessTools.TypeByName("VEF.AnimalBehaviours.CompDestroyThisItem");
        MP.RegisterSyncMethod(type, "SetObjectForDestruction");
        MP.RegisterSyncMethod(type, "CancelObjectForDestruction");

        type = AccessTools.TypeByName("VEF.AnimalBehaviours.CompDieAndChangeIntoOtherDef");
        MP.RegisterSyncMethod(type, "ChangeDef");

        type = AccessTools.TypeByName("VEF.AnimalBehaviours.CompDiseasesAfterPeriod");
        MpCompat.RegisterLambdaMethod(type, "GetGizmos", 0).SetDebugOnly();

        type = AccessTools.TypeByName("VEF.AnimalBehaviours.Pawn_GetGizmos_Patch");
        // Draft/undraft toggle (0). Ordinal 1 is the pure isActive getter, needs no sync.
        MpCompat.RegisterLambdaDelegate(type, "Postfix", 0);
    }

    private static void PatchKCSG()
    {
        var type = AccessTools.TypeByName("KCSG.SettlementGenUtils");
        type = AccessTools.Inner(type, "Sampling");

        PatchingUtilities.PatchSystemRand(AccessTools.Method(type, "Sample"));

        // KCSG.SymbolResolver_ScatterStuffAround:Resolve uses seeder system RNG, should be fine
        // If not, will need patching

        PatchingUtilities.PatchPushPopRand([
            "KCSG.KCSG_Skyfaller:SaveImpact",
            "KCSG.KCSG_Skyfaller:Tick"
        ]);
    }

    private static void PatchGenes()
    {
        var type = AccessTools.TypeByName("VEF.Genes.CompHumanHatcher");
        PatchingUtilities.PatchSystemRand(AccessTools.Method(type, "Hatch"));
        MpCompat.RegisterLambdaMethod(type, "CompGetGizmosExtra", 0).SetDebugOnly();
    }

    private static void PatchExtraPregnancyApproaches()
    {
        MpCompat.RegisterLambdaDelegate(
            "VEF.Pawns.VanillaExpandedFramework_SocialCardUtility_DrawPregnancyApproach_Patch",
            "AddPregnancyApproachOptions",
            0, 2); // Disable extra approaches (0), set extra approach (2)
    }

    private static void PatchWeapons()
    {
        PatchingUtilities.PatchPushPopRand([
            // Musket guns, etc
            "VEF.Weapons.Verb_ShootWithSmoke:TryCastShot",
            "VEF.Weapons.SmokeMaker:ThrowMoteDef",
            "VEF.Weapons.SmokeMaker:ThrowFleckDef", // Possibly not needed? No ShouldSpawnMotesAt/mote saturation checks.
            "VEF.Weapons.SmokeMaker:ThrowSmokeTrail"
        ]);

        MpCompat.RegisterLambdaMethod("VEF.Weapons.CompLaserCapacitor", "CompGetGizmosExtra", 1);

        MpCompat.harmony.Patch(AccessTools.DeclaredPropertyGetter("VEF.Weapons.ExpandableProjectile:StartingPosition"),
            new HarmonyMethod(typeof(VanillaExpandedFramework), nameof(PreStartingPositionGetter)),
            new HarmonyMethod(typeof(VanillaExpandedFramework), nameof(PostStartingPositionGetter)));
    }

    private static void PreStartingPositionGetter(Vector3 ___startingPosition, bool ___pawnMoved,
        ref (Vector3, bool)? __state)
    {
        // If in interface, store the current values.
        if (MP.InInterface)
            __state = (___startingPosition, ___pawnMoved);
    }

    private static void PostStartingPositionGetter(ref Vector3 ___startingPosition, ref bool ___pawnMoved,
        (Vector3, bool)? __state)
    {
        // If state not null (in interface), restore previous values.
        // Alternatively, we could also have separate values for interface and simulation,
        // but seems a bit pointless to do it like this since it's just a minor thing.
        if (__state != null)
            (___startingPosition, ___pawnMoved) = __state.Value;
    }

    #endregion
}