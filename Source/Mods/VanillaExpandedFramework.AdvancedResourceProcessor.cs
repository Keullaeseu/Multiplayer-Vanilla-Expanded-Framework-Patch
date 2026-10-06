using System.Collections;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Compat;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

internal partial class VanillaExpandedFramework
{
    #region Advanced Item Processor

    #region Fields

    // CompAdvancedResourceProcessor
    private static Type advancedResourceProcessorType;

    private static AccessTools.FieldRef<ThingComp, object> advancedResourceProcessorProcessStackField;

    // Process
    // Most of those have getters/setters, but given they're not virtual - not bothering to use them.
    private static AccessTools.FieldRef<object, ThingWithComps> processParentField;
    private static AccessTools.FieldRef<object, string> processIdField;
    private static AccessTools.FieldRef<object, Def> processDefField;
    private static AccessTools.FieldRef<object, BillRepeatModeDef> processRepeatModeField;
    private static AccessTools.FieldRef<object, QualityCategory> processQualityToOutputField;
    private static AccessTools.FieldRef<object, int> processCountField;
    private static AccessTools.FieldRef<object, int> processTargetCountField;
    private static AccessTools.FieldRef<object, float> processProgressField;

    private static AccessTools.FieldRef<object, bool> processSuspendedField;

    // ProcessStack
    private static FastInvokeHandler processStackAddProcessMethod;
    private static FastInvokeHandler processStackNotifyProcessChangeMethod;

    private static AccessTools.FieldRef<object, IList> processStackProcessesField;

    // ProcessUtility
    private static AccessTools.FieldRef<IDictionary> processUtilityClipboardField;

    // Overclock
    private static AccessTools.FieldRef<object, ThingComp> processorField;
    private static ISyncField overclockMultiplier;

    #endregion

    #region Main Patch

    private static void PatchAdvancedResourceProcessor()
    {
        var type = advancedResourceProcessorType = AccessTools.TypeByName("PipeSystem.CompAdvancedResourceProcessor");
        advancedResourceProcessorProcessStackField = AccessTools.FieldRefAccess<object>(type, "processStack");
        // Add to process stack (5). Fish filters/ordering shifted it from 2.
        MpCompat.RegisterLambdaDelegate(type, "ProcessesOptions", MethodType.Getter, 5);
        // Toggle: output to ground
        MpCompat.RegisterLambdaMethod(type, "Settings", MethodType.Getter, 0);
        // Extract at current quality (0), and debug: finish in 10 ticks (3), advance progress 1 day (4), empty wastepacks (5).
        // 2 is newly added for overclock 
        MpCompat.RegisterLambdaMethod(type, nameof(ThingComp.CompGetGizmosExtra), 0);
        MpCompat.RegisterLambdaDelegate(type, nameof(ThingComp.CompGetGizmosExtra), 3, 4, 5).SetDebugOnly();
        // Handle paste (1) gizmo differently, since we need to also sync the clipboard.
        MpCompat.harmony.Patch(MpMethodUtil.GetLambda(type, nameof(ThingComp.CompGetGizmosExtra), lambdaOrdinal: 1),
            new HarmonyMethod(PrePasteProcessesGizmo));


        // The process, we'll need to sync it
        type = AccessTools.TypeByName("PipeSystem.Process");
        processParentField = AccessTools.FieldRefAccess<ThingWithComps>(type, "parent");
        processIdField = AccessTools.FieldRefAccess<string>(type, "id");
        processDefField = AccessTools.FieldRefAccess<Def>(type, "def");
        processRepeatModeField = AccessTools.FieldRefAccess<BillRepeatModeDef>(type, "repeatMode");
        processQualityToOutputField = AccessTools.FieldRefAccess<QualityCategory>(type, "qualityToOutput");
        processCountField = AccessTools.FieldRefAccess<int>(type, "processCount");
        processTargetCountField = AccessTools.FieldRefAccess<int>(type, "targetCount");
        processProgressField = AccessTools.FieldRefAccess<float>(type, "progress");
        processSuspendedField = AccessTools.FieldRefAccess<bool>(type, "suspended");
        MP.RegisterSyncWorker<object>(SyncProcess, type, true);
        // Called (together with delete on process stack) when deleting. Other places this is called from don't matter.
        MP.RegisterSyncMethod(type, "ResetProcess");
        // Repeat count (0), target count (1), forever (2)
        MpCompat.RegisterLambdaMethod(type, "Options", MethodType.Getter, 0, 1, 2);
        MP.RegisterSyncMethod(MpMethodUtil.MethodOf(SyncedProcessModifyTargetCountButton));
        MP.RegisterSyncMethod(MpMethodUtil.MethodOf(SyncedProcessSuspendButton));
        MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "DoInterface"),
            transpiler: new HarmonyMethod(ReplaceProcessImageButtons));

        // Sync adding/removing from/reorganizing the process stack
        type = AccessTools.TypeByName("PipeSystem.ProcessStack");
        processStackAddProcessMethod = MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "AddProcess"));
        processStackNotifyProcessChangeMethod =
            MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "Notify_ProcessChange"));
        processStackProcessesField = AccessTools.FieldRefAccess<IList>(type, "processes");
        MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "Reorder"),
            new HarmonyMethod(PreProcessStackReorder));
        // It's not easily possible to sync ProcessStack since it has no reference to its parent.
        // Its children do have reference to its parent, but it may have no children at the time,
        // so we need to transform the target to properly handle it.
        MP.RegisterSyncMethod(type, "AddProcess")
            .CancelIfAnyArgNull()
            .TransformTarget(
                Serializer.New((object _, object _, object[] args) => (ThingWithComps)args[1],
                    GetProcessStackFromThingWithComps), true);
        MP.RegisterSyncMethod(type, "Delete")
            .CancelIfAnyArgNull()
            .TransformTarget(
                Serializer.New((object _, object _, object[] args) => processParentField(args[0]),
                    GetProcessStackFromThingWithComps), true);
        MP.RegisterSyncMethod(type, "Reorder")
            .CancelIfAnyArgNull()
            .TransformTarget(
                Serializer.New((object _, object _, object[] args) => processParentField(args[0]),
                    GetProcessStackFromThingWithComps), true);

        // Sync ITab_Processor
        type = AccessTools.TypeByName("PipeSystem.ProcessUtility");
        processUtilityClipboardField =
            AccessTools.StaticFieldRefAccess<IDictionary>(AccessTools.DeclaredField(type, "Clipboard"));

        type = AccessTools.TypeByName("PipeSystem.ITab_Processor");
        MP.RegisterSyncMethod(MpMethodUtil.MethodOf(SyncedPasteProcesses)).CancelIfAnyArgNull();
        MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, nameof(ITab.FillTab)),
            transpiler: new HarmonyMethod(ReplaceProcessorITabButtons));

        // Sync OverclockMultiplier window
        overclockMultiplier =
            MP.RegisterSyncField(AccessTools.Field(advancedResourceProcessorType, "overclockMultiplier"));
        type = AccessTools.TypeByName("PipeSystem.Window_Overclock");
        processorField = AccessTools.FieldRefAccess<ThingComp>(type, "building");
        MpCompat.harmony.Patch(AccessTools.Method(type, "DoWindowContents"),
            new HarmonyMethod(PreOverclockWindow),
            new HarmonyMethod(PostOverclockWindow)
        );
    }

    #endregion

    #region Utility methods

    private static object GetProcessStackFromComp(ThingComp comp)
    {
        return advancedResourceProcessorProcessStackField(comp);
    }

    private static object GetProcessStackFromThingWithComps(ThingWithComps thing)
    {
        return GetProcessStackFromComp(GetProcessorCompFromThingWithComps(thing));
    }

    // We could technically use CachedCompAdvancedProcessor, but if the Thing is not spawned it would not be there.
    // It probably should never happen, but you can never be too safe.
    private static ThingComp GetProcessorCompFromThingWithComps(ThingWithComps thing)
    {
        return thing.AllComps.Find(x => advancedResourceProcessorType.IsInstanceOfType(x));
    }

    private static object GetProcessById(IList processes, string id)
    {
        return processes?.Cast<object>().FirstOrDefault(process => processIdField(process) == id);
    }

    #endregion

    #region Minor patches

    private static void PreProcessStackReorder(object process, ref int offset, IList ___processes)
    {
        if (!MP.IsExecutingSyncCommand)
            return;

        // Ensure that index + offset will always fit in range of the list.
        var index = ___processes.IndexOf(process);
        var num = Mathf.Clamp(offset + index, 0, ___processes.Count - 1);
        offset = num - index;
    }

    private static bool PrePasteProcessesGizmo(ThingComp __instance)
    {
        if (!MP.IsInMultiplayer)
            return true;

        var clipboard = processUtilityClipboardField();
        if (!clipboard.Contains(__instance.parent.def))
            return false;

        if (clipboard[__instance.parent.def] is not IList processes || processes.Count == 0)
            return false;

        // Rather than using the processes themselves, grab a list of their defs, repeat modes, target counts and qualities.
        // The gizmo paste uses default quality, same as vanilla.
        var list = processes.Cast<object>()
            .Select(process => (processDefField(process), processRepeatModeField(process),
                processTargetCountField(process), QualityCategory.Normal)).ToList();
        SyncedPasteProcesses(__instance, list);
        return false;
    }

    private static void PreOverclockWindow(Window __instance)
    {
        var processor = processorField(__instance);
        MP.WatchBegin();
        overclockMultiplier.Watch(processor);
    }

    private static void PostOverclockWindow(Window __instance)
    {
        MP.WatchEnd();
    }

    #endregion

    #region ITab Patches

    private static void SyncedPasteProcesses(ThingComp comp,
        List<(Def def, BillRepeatModeDef repeatMode, int targetCount, QualityCategory quality)> clipboard)
    {
        // Just in case
        if (clipboard.Empty())
            return;

        var processStack = GetProcessStackFromComp(comp);
        var processes = processStackProcessesField(processStack);
        // Clear before pasting
        processes.Clear();

        // Add all the pasted processes, same as vanilla.
        foreach (var (def, repeatMode, targetCount, quality) in clipboard)
            processStackAddProcessMethod(processStack, def, comp.parent, repeatMode, targetCount, quality);

        // Probably not needed since the list is cleared? But
        // do the same as the mod and reset the progress to 0.
        foreach (var process in processes)
            processProgressField(process) = 0f;
    }

    private static bool ReplacedPasteProcesses(Rect butRect, Texture2D tex, bool doMouseoverSound, string tooltip)
    {
        var result = Widgets.ButtonImage(butRect, tex, doMouseoverSound, tooltip);
        if (!result || !MP.IsInMultiplayer)
            return result;

        if (Find.Selector.SingleSelectedThing is not ThingWithComps thing)
            return false;

        // Grab the comp. Null shouldn't happen, but let's be safe anyway.
        var comp = GetProcessorCompFromThingWithComps(thing);
        if (comp == null)
            return false;

        var clipboard = processUtilityClipboardField();
        if (!clipboard.Contains(thing.def))
            return false;

        if (clipboard[thing.def] is not IList processes || processes.Count == 0)
            return false;

        // Rather than using the processes themselves, grab a list of their defs, repeat modes, target counts and qualities.
        // The ITab paste keeps each process quality, same as vanilla.
        var list = processes.Cast<object>()
            .Select(process => (processDefField(process), processRepeatModeField(process),
                processTargetCountField(process), processQualityToOutputField(process))).ToList();
        SyncedPasteProcesses(comp, list);

        return false;
    }

    private static void SyncedProcessModifyTargetCountButton(ThingComp comp, string processId, int offset)
    {
        var stack = GetProcessStackFromComp(comp);
        var process = GetProcessById(processStackProcessesField(stack), processId);
        if (process == null)
            return;

        ref var targetCount = ref processTargetCountField(process);

        if (targetCount == -1)
        {
            // Leaving forever mode, same as vanilla: back to repeat count mode
            processRepeatModeField(process) = BillRepeatModeDefOf.RepeatCount;
            processCountField(process) = 0;
            targetCount = 1;
        }
        else if (targetCount > -1)
        {
            // Ensure that if we're reducing the count that it will be at least 0
            targetCount = Mathf.Max(0, targetCount + offset);
        }
        // No handling for < -1, not intended to ever happen. No handling as a precaution for issues?

        processStackNotifyProcessChangeMethod(stack);
    }

    private static bool ReplacedProcessModifyTargetCountButton(WidgetRow row, Texture2D texture, string tooltip,
        Color? mouseoverColor, Color? backgroundColor, Color? mouseoverBackgroundColor, bool doMouseoverSound,
        float overrideSize, object process)
    {
        var result = row.ButtonIcon(texture, tooltip, mouseoverColor, backgroundColor, mouseoverBackgroundColor,
            doMouseoverSound, overrideSize);
        if (!result || !MP.IsInMultiplayer)
            return result;

        var parent = processParentField(process);
        if (parent == null)
            return false;
        var comp = GetProcessorCompFromThingWithComps(parent);
        if (comp == null)
            return false;

        var id = processIdField(process);
        var offset = GenUI.CurrentAdjustmentMultiplier();
        if (texture == TexButton.Minus)
            offset = -offset;
        SyncedProcessModifyTargetCountButton(comp, id, offset);

        SoundDefOf.DragSlider.PlayOneShotOnCamera();
        return false;
    }

    private static void SyncedProcessSuspendButton(ThingComp comp, string processId)
    {
        var stack = GetProcessStackFromComp(comp);
        var process = GetProcessById(processStackProcessesField(stack), processId);
        if (process == null)
            return;

        ref var suspended = ref processSuspendedField(process);
        suspended = !suspended;
        processStackNotifyProcessChangeMethod(stack);
    }

    private static bool ReplacedProcessSuspendButton(Rect butRect, Texture2D tex, Color baseColor,
        bool doMouseoverSound, string tooltip, object process)
    {
        var result = Widgets.ButtonImage(butRect, tex, baseColor, doMouseoverSound, tooltip);
        if (!result || !MP.IsInMultiplayer)
            return result;

        var parent = processParentField(process);
        if (parent == null)
            return false;
        var comp = GetProcessorCompFromThingWithComps(parent);
        if (comp == null)
            return false;

        var id = processIdField(process);
        SyncedProcessSuspendButton(comp, id);

        SoundDefOf.DragSlider.PlayOneShotOnCamera();
        return false;
    }

    private static IEnumerable<CodeInstruction> ReplaceProcessorITabButtons(IEnumerable<CodeInstruction> instr,
        MethodBase baseMethod)
    {
        var target = AccessTools.DeclaredMethod(typeof(Widgets), nameof(Widgets.ButtonImage),
            [typeof(Rect), typeof(Texture2D), typeof(bool), typeof(string)]);
        var replacement = MpMethodUtil.MethodOf(ReplacedPasteProcesses);

        return instr.ReplaceMethod(target, replacement, baseMethod, expectedReplacements: 1,
            targetText: "PipeSystem_PasteProcesses");
    }

    private static IEnumerable<CodeInstruction> ReplaceProcessImageButtons(IEnumerable<CodeInstruction> instr,
        MethodBase baseMethod)
    {
        var widgetRowTarget = AccessTools.DeclaredMethod(typeof(WidgetRow), nameof(WidgetRow.ButtonIcon));
        var widgetRowReplacement = MpMethodUtil.MethodOf(ReplacedProcessModifyTargetCountButton);
        var firstTargetField = AccessTools.DeclaredField(typeof(TexButton), nameof(TexButton.Plus));
        var secondTargetField = AccessTools.DeclaredField(typeof(TexButton), nameof(TexButton.Minus));

        var widgetButtonTarget = AccessTools.DeclaredMethod(typeof(Widgets), nameof(Widgets.ButtonImage),
            [typeof(Rect), typeof(Texture2D), typeof(Color), typeof(bool), typeof(string)]);
        var widgetButtonReplacement = MpMethodUtil.MethodOf(ReplacedProcessSuspendButton);
        var widgetButtonField = AccessTools.DeclaredField(typeof(TexButton), nameof(TexButton.Suspend));

        IEnumerable<CodeInstruction> ExtraInstructions(CodeInstruction _)
        {
            // Load this
            yield return CodeInstruction.LoadArgument(0);
        }

        return instr
            .ReplaceMethod(widgetRowTarget, widgetRowReplacement, baseMethod, ExtraInstructions,
                expectedReplacements: 2,
                targetInstruction: ci => ci.LoadsField(firstTargetField) || ci.LoadsField(secondTargetField))
            .ReplaceMethod(widgetButtonTarget, widgetButtonReplacement, baseMethod, ExtraInstructions,
                expectedReplacements: 1, targetInstruction: ci => ci.LoadsField(widgetButtonField));
    }

    #endregion

    #region Sync Workers

    private static void SyncProcess(SyncWorker sync, ref object process)
    {
        if (sync.isWriting)
        {
            if (process == null)
            {
                sync.Write<ThingComp>(null);
                Log.Error($"{LogPrefix} Trying to sync a null process.");
                return;
            }

            var parent = GetProcessorCompFromThingWithComps(processParentField(process));
            sync.Write(parent);

            // Probably not needed unless some weird bugs going on
            if (parent == null)
            {
                Log.Error($"{LogPrefix} Trying to sync a process with no parent. Process={process}");
                return;
            }

            sync.Write(processIdField(process));
        }
        else
        {
            var comp = sync.Read<ThingComp>();
            if (comp == null)
                return;

            var stack = advancedResourceProcessorProcessStackField(comp);
            if (stack == null)
            {
                Log.Error($"{LogPrefix} The process has no stack. Comp={comp}");
                return;
            }

            var list = processStackProcessesField(stack);
            // Shouldn't happen
            if (list == null)
            {
                Log.Error($"{LogPrefix} The process has ProcessStack with no process list. Comp={comp}");
                return;
            }

            var id = sync.Read<string>();
            process = GetProcessById(list, id);
            if (process == null)
                Log.Error($"{LogPrefix} Could not find the correct process. Comp={comp}, id={id}");
        }
    }

    #endregion

    #endregion
}