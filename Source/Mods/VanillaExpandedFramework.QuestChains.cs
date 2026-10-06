using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Compat;
using RimWorld;
using UnityEngine;
using Verse;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

internal partial class VanillaExpandedFramework
{
    #region Quest chain dev mode window

    // GameComponent_QuestChains
    private static AccessTools.FieldRef<GameComponent> questChainsGameCompInstanceField;
    private static AccessTools.FieldRef<GameComponent, IList> questChainsGameCompQuestsField;
    private static AccessTools.FieldRef<GameComponent, IList> questChainsGameCompFutureQuestsField;

    // QuestInfo
    private static FastInvokeHandler questInfoQuestGetter;

    // FutureQuestInfo
    private static AccessTools.FieldRef<object, QuestScriptDef> futureQuestInfoQuestDefField;

    private static void PatchQuestChainsDevMode()
    {
        // The window normally opens from debug actions menu, which
        // MP syncs by default. This patch basically makes sure the
        // dialog only opens for the player that attempted to open it.
        MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VEF.Storyteller.QuestChainsDevWindow:ViewQuestChains"),
            new HarmonyMethod(CancelDevModeWindowIfNotExecutingSelfCommand));

        // Prepare field access for our patches/synced methods
        var type = AccessTools.TypeByName("VEF.Storyteller.GameComponent_QuestChains");
        questChainsGameCompInstanceField = AccessTools.StaticFieldRefAccess<GameComponent>(
            AccessTools.DeclaredField(type, "Instance"));
        questChainsGameCompQuestsField = AccessTools.FieldRefAccess<IList>(type, "quests");
        questChainsGameCompFutureQuestsField = AccessTools.FieldRefAccess<IList>(type, "futureQuests");

        questInfoQuestGetter =
            MethodInvoker.GetHandler(AccessTools.DeclaredPropertyGetter("VEF.Storyteller.QuestInfo:Quest"));
        futureQuestInfoQuestDefField =
            AccessTools.FieldRefAccess<QuestScriptDef>("VEF.Storyteller.FutureQuestInfo:questDef");

        // Sync our button replacement methods
        MP.RegisterSyncMethod(MpMethodUtil.MethodOf(SyncedQuestChainDevForceEnd)).CancelIfAnyArgNull().SetDebugOnly();
        MP.RegisterSyncMethod(MpMethodUtil.MethodOf(SyncedQuestChainDevFireNow)).CancelIfAnyArgNull().SetDebugOnly();

        // Replace mod buttons with our own
        MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VEF.Storyteller.QuestChainsDevWindow:DrawQuestInfo"),
            transpiler: new HarmonyMethod(ReplaceQuestChainsWindowSuccessFailButtons));
        MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VEF.Storyteller.QuestChainsDevWindow:DrawFutureQuestInfo"),
            transpiler: new HarmonyMethod(ReplaceQuestChainsWindowFireNowButton));
    }

    private static bool CancelDevModeWindowIfNotExecutingSelfCommand()
    {
        // Only allow the method to run if it's SP, not a synced
        // command, or a synced command issues by self.
        return !MP.IsInMultiplayer || !MP.IsExecutingSyncCommand || MP.IsExecutingSyncCommandIssuedBySelf;
    }

    private static IEnumerable<CodeInstruction> ReplaceQuestChainsWindowSuccessFailButtons(
        IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
    {
        var target = AccessTools.DeclaredMethod(typeof(Widgets), nameof(Widgets.ButtonText),
            [typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(TextAnchor?)]);
        var forceSuccessReplacement = MpMethodUtil.MethodOf(ReplacedForceSuccessButton);
        var forceFailureReplacement = MpMethodUtil.MethodOf(ReplacedForceFailureButton);

        IEnumerable<CodeInstruction> ExtraInstructions(CodeInstruction _)
        {
            return
            [
                // Load the argument with index 2 (QuestInfo) to our method
                new CodeInstruction(OpCodes.Ldarg_2)
            ];
        }

        return instr
            .ReplaceMethod(target, forceSuccessReplacement, baseMethod, ExtraInstructions, expectedReplacements: 1,
                targetText: "Force Success")
            .ReplaceMethod(target, forceFailureReplacement, baseMethod, ExtraInstructions, expectedReplacements: 1,
                targetText: "Force Fail");
    }

    private static IEnumerable<CodeInstruction> ReplaceQuestChainsWindowFireNowButton(
        IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
    {
        var target = AccessTools.DeclaredMethod(typeof(Widgets), nameof(Widgets.ButtonText),
            [typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(TextAnchor?)]);
        var replacement = MpMethodUtil.MethodOf(ReplacedFireNowButton);

        IEnumerable<CodeInstruction> ExtraInstructions(CodeInstruction _)
        {
            return
            [
                // Load the argument with index 2 (FutureQuestInfo) to our method
                new CodeInstruction(OpCodes.Ldarg_2)
            ];
        }

        return instr.ReplaceMethod(target, replacement, baseMethod, ExtraInstructions, expectedReplacements: 1,
            targetText: "Fire Now");
    }

    private static bool ReplacedForceSuccessButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound,
        bool active, TextAnchor? overrideTextAnchor, object questInfo)
    {
        var result = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
        // Return result out of MP and return early if not pressed in MP.
        if (!MP.IsInMultiplayer || !result)
            return result;

        // If pressed and in MP, redirect the button
        SyncedQuestChainDevForceEnd((Quest)questInfoQuestGetter(questInfo), QuestEndOutcome.Success);

        return false;
    }

    private static bool ReplacedForceFailureButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound,
        bool active, TextAnchor? overrideTextAnchor, object questInfo)
    {
        var result = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
        // Return result out of MP and return early if not pressed in MP.
        if (!MP.IsInMultiplayer || !result)
            return result;

        // If pressed and in MP, redirect the button
        SyncedQuestChainDevForceEnd((Quest)questInfoQuestGetter(questInfo), QuestEndOutcome.Fail);

        return false;
    }

    private static bool ReplacedFireNowButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound,
        bool active, TextAnchor? overrideTextAnchor, object futureQuestInfo)
    {
        var result = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
        // Return result out of MP and return early if not pressed in MP.
        if (!MP.IsInMultiplayer || !result)
            return result;

        var def = futureQuestInfoQuestDefField(futureQuestInfo);
        var index = questChainsGameCompFutureQuestsField(questChainsGameCompInstanceField()).IndexOf(futureQuestInfo);
        // If pressed and in MP, redirect the button
        SyncedQuestChainDevFireNow(def, index);

        return false;
    }

    private static void SyncedQuestChainDevForceEnd(Quest quest, QuestEndOutcome outcome)
    {
        if (quest.State != QuestState.Ongoing)
            return;

        // Find the QuestInfo in GameComponent_QuestChains.
        // Assume there's only 1 match, as it wouldn't make much sense
        // for multiple QuestInfos to have the same quest assigned.

        // This check may be a bit pointless, but let's take it safe here
        // and check if there's a QuestInfo with our quest in the list.
        foreach (var questInfo in questChainsGameCompQuestsField(questChainsGameCompInstanceField()))
            if (questInfoQuestGetter(questInfo) == quest)
            {
                quest.End(outcome, false);
                return;
            }

        // Error log for person syncing the command.
        if (MP.IsExecutingSyncCommandIssuedBySelf)
            Log.Error(
                $"{LogPrefix} Trying to find a quest chain's QuestInfo for quest with ID {quest.id} and name {quest.name}, but there was no matching QuestInfo. This should not happen.");
    }

    private static void SyncedQuestChainDevFireNow(QuestScriptDef def, int index)
    {
        // Make sure the index is 0 or bigger
        if (index < 0)
        {
            if (MP.IsExecutingSyncCommandIssuedBySelf)
                Log.Error($"{LogPrefix} Failed syncing FutureQuestInfo, index too small: index={index}, def={def}");
            return;
        }

        // Make sure the index is within the list bounds
        var list = questChainsGameCompFutureQuestsField(questChainsGameCompInstanceField());
        if (index >= list.Count)
        {
            if (MP.IsExecutingSyncCommandIssuedBySelf)
                Log.Error(
                    $"{LogPrefix} Failed syncing FutureQuestInfo, index too high: index={index}, count={list.Count}, def={def}");
            return;
        }

        // Make sure the FutureQuestInfo at the target index has the same target QuestScriptDef.
        // It could technically be a different one with the same quest, but we have no real
        // way to be able to distinguish them otherwise in here without doing much bigger patches.
        var futureQuestInfo = list[index];
        var localDef = futureQuestInfoQuestDefField(futureQuestInfo);
        if (localDef != def)
        {
            if (MP.IsExecutingSyncCommandIssuedBySelf)
                Log.Error(
                    $"{LogPrefix} Failed syncing FutureQuestInfo, QuestScriptDef mismatch: index={index}, count={list.Count}, syncedDef={def}, localDef={localDef}");
            return;
        }

        // Create the quest and (if needed) send the letter
        var quest = QuestUtility.GenerateQuestAndMakeAvailable(def,
            StorytellerUtility.DefaultThreatPointsNow(Find.World));
        if (def.sendAvailableLetter)
            QuestUtility.SendLetterQuestAvailable(quest);
        // Remove the future quest from the list since it just became an active one
        list.Remove(futureQuestInfo);
    }

    #endregion
}