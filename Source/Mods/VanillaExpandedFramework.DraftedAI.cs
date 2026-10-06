using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

internal partial class VanillaExpandedFramework
{
    #region Drafted AI

    private static FastInvokeHandler getDraftedActionDataMethod;
    private static FastInvokeHandler draftedActionDataGetPawnMethod;

    private static void PatchDraftedAi()
    {
        // Drafted AI is used by player controlled insectoids,
        // and allows the player to toggle hunt mode (search
        // and destroy) as well as toggling specific/all
        // abilities to be autocasted.

        getDraftedActionDataMethod = MethodInvoker.GetHandler(
            AccessTools.DeclaredMethod("VEF.AI.DraftedActionHolder:GetData"));
        draftedActionDataGetPawnMethod = MethodInvoker.GetHandler(
            AccessTools.DeclaredPropertyGetter("VEF.AI.DraftedActionData:Pawn"));

        var type = AccessTools.TypeByName("VEF.AI.DraftedActionData");
        MP.RegisterSyncMethod(type, "ToggleHuntMode");
        MP.RegisterSyncMethod(type, "ToggleAutoForAll");
        MP.RegisterSyncMethod(type, "ToggleAutoCastFor");
        MP.RegisterSyncWorker<object>(SyncDraftedActionData, type);
    }

    private static void SyncDraftedActionData(SyncWorker sync, ref object draftedActionData)
    {
        if (sync.isWriting)
        {
            if (draftedActionData == null)
                sync.Write((Pawn)null);
            else
                sync.Write((Pawn)draftedActionDataGetPawnMethod(draftedActionData));
        }
        else
        {
            var pawn = sync.Read<Pawn>();
            if (pawn != null)
                draftedActionData = getDraftedActionDataMethod(null, pawn);
        }
    }

    #endregion
}