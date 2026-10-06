using System.Reflection;
using HarmonyLib;
using Multiplayer.Compat;
using RimWorld;
using Verse;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

internal partial class VanillaExpandedFramework
{
    #region Building stuff requiring non-construction skill

    // DefModExtension
    private static Type thingDefExtensionType;
    private static AccessTools.FieldRef<DefModExtension, object> thingDefExtensionConstructionSkillRequirementField;

    private static AccessTools.FieldRef<object, WorkTypeDef> constructionSkillRequirementWorkTypeField;

    // Temp value
    private static IConstructible lastThing;

    private static void PatchWorkGiverDeliverResources()
    {
        MethodInfo rwMethod;
        MethodInfo mpMethod;

        try
        {
            const string rwMethodPath =
                "RimWorld.WorkGiver_ConstructDeliverResourcesToBlueprints:NoCostFrameMakeJobFor";
            rwMethod = AccessTools.DeclaredMethod(rwMethodPath);
            if (rwMethod == null)
                throw new MissingMethodException($"Could not access method: {rwMethodPath}");

            const string mpMethodPath = "Multiplayer.Client.OnlyConstructorsPlaceNoCostFrames:IsConstruction";
            mpMethod = AccessTools.DeclaredMethod(mpMethodPath);
            if (mpMethod == null)
                throw new MissingMethodException($"Could not access method: {mpMethodPath}");

            thingDefExtensionType = AccessTools.TypeByName("VEF.Things.ThingDefExtension");
            thingDefExtensionConstructionSkillRequirementField = AccessTools.FieldRefAccess<object>(
                thingDefExtensionType, "constructionSkillRequirement");
            constructionSkillRequirementWorkTypeField = AccessTools.FieldRefAccess<WorkTypeDef>(
                "VEF.Pawns.ConstructionSkillRequirement:workType");
        }
        catch (Exception)
        {
            // Cleanup stuff that won't be ever used if exception occurs before patching.
            thingDefExtensionType = null;
            thingDefExtensionConstructionSkillRequirementField = null;
            constructionSkillRequirementWorkTypeField = null;

            throw;
        }

        MpCompat.harmony.Patch(rwMethod,
            new HarmonyMethod(typeof(VanillaExpandedFramework), nameof(PreNoCostFrameMakeJobFor)));
        MpCompat.harmony.Patch(mpMethod,
            postfix: new HarmonyMethod(typeof(VanillaExpandedFramework), nameof(PostIsConstruction)));
    }

    private static void PreNoCostFrameMakeJobFor(IConstructible c)
    {
        lastThing = c;
    }

    private static void PostIsConstruction(WorkGiver w, ref bool __result)
    {
        // If __result is true, the work type was construction, so MP allowed it.
        if (__result || lastThing is not Thing thing || thing.def?.entityDefToBuild?.modExtensions == null)
        {
            lastThing = null;
            return;
        }

        // Look for the VFE def mod extension
        foreach (var extension in thing.def.entityDefToBuild.modExtensions)
            if (extension != null && thingDefExtensionType.IsInstanceOfType(extension))
            {
                // Get the construction skill requirement object
                var constructionRequirement = thingDefExtensionConstructionSkillRequirementField(extension);
                if (constructionRequirement == null)
                    break;

                // Get the WorkTypeDef of the correct work requirement
                var constructionWorkType = constructionSkillRequirementWorkTypeField(constructionRequirement);
                // Set the result based on if the construction work type is the same as extension's work type.
                __result = w.def.workType == constructionWorkType;

                break;
            }

        lastThing = null;
    }

    #endregion
}