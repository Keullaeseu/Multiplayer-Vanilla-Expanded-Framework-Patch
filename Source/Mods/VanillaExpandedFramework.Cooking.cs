using HarmonyLib;
using Multiplayer.Compat;
using RimWorld;
using Verse;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

internal partial class VanillaExpandedFramework
{
    #region Vanilla Cooking Expanded

    private static Type thoughtHediffType;

    private static void PatchCooking()
    {
        // Hediff is added the fist time MoodOffset is called, called during alert updates (not synced).
        thoughtHediffType = AccessTools.TypeByName("VEF.Cooking.Thought_Hediff");
        if (thoughtHediffType != null)
        {
            // Only apply the patch if there's actually any ThoughtDef that uses this specific hediff type.
            // No point applying a patch and having it run if it'll never actually do anything useful.
            // An example of a mod using it would be Vanilla Cooking Expanded (used for gourmet meals).
            // This also required us to run this patch late, as otherwise the DefDatabase wouldn't be initialized yet.
            if (DefDatabase<ThoughtDef>.AllDefsListForReading.Any(def =>
                    thoughtHediffType.IsAssignableFrom(def.thoughtClass)))
                PatchingUtilities.PatchTryGainMemory(TryGainThoughtHediff);
        }
        else
        {
            Log.Error(
                $"{LogPrefix} Trying to patch `VEF.Cooking.Thought_Hediff`, but the type is null. Did it get moved, renamed, or removed?");
        }
    }

    private static bool TryGainThoughtHediff(Thought_Memory thought)
    {
        if (!thoughtHediffType.IsInstanceOfType(thought))
            return false;

        // Call MoodOffset to cause the method to add hediffs, etc.
        thought.MoodOffset();
        return true;
    }

    #endregion
}