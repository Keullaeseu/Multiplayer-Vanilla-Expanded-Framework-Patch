using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Compat;
using UnityEngine;
using Verse;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

internal partial class VanillaExpandedFramework
{
    #region Vanilla Furniture Expanded

    // Vanilla Furniture Expanded
    private static Type randomBuildingGraphicCompType;
    private static FastInvokeHandler randomBuildingGraphicCompChangeGraphicMethod;
    private static FastInvokeHandler randomBuildingGraphicCompResetGraphicsMethod;

    // Glowers
    private static Type dummyGlowerType;
    private static AccessTools.FieldRef<ThingComp, CompGlower> compGlowerExtendedGlowerField;
    private static AccessTools.FieldRef<object, object> dummyGlowerParentCompField;

    private static void PatchVanillaFurnitureExpanded()
    {
        MpCompat.RegisterLambdaMethod("VEF.Buildings.CompConfigurableSpawner", "CompGetGizmosExtra", 0).SetDebugOnly();

        var type = AccessTools.TypeByName("VEF.Buildings.Command_SetItemsToSpawn");
        MpCompat.RegisterLambdaDelegate(type, "ProcessInput", 1);
        MP.RegisterSyncWorker<Command>(SyncCommandWithCompBuilding, type, shouldConstruct: true);

        MpCompat.RegisterLambdaMethod("VEF.Buildings.CompRockSpawner", "CompGetGizmosExtra", 0);

        type = AccessTools.TypeByName("VEF.Buildings.Command_SetStoneType");
        MP.RegisterSyncWorker<Command>(SyncCommandWithCompBuilding, type, shouldConstruct: true);
        // Random stone type (0, captures only this -> instance method, synced with the command),
        // specific stone type (1, captures loop local -> display class delegate)
        MpCompat.RegisterLambdaMethod(type, "ProcessInput", 0);
        MpCompat.RegisterLambdaDelegate(type, "ProcessInput", 1);

        type = randomBuildingGraphicCompType = AccessTools.TypeByName("VEF.Buildings.CompRandomBuildingGraphic");
        randomBuildingGraphicCompChangeGraphicMethod =
            MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "ChangeGraphic"));
        randomBuildingGraphicCompResetGraphicsMethod =
            MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "ResetGraphics"));
        MpCompat.RegisterLambdaMethod(type, "CompGetGizmosExtra", 0);

        // Preferably leave it at the end in case it fails - if it fails all the other stuff here will still get patched
        type = AccessTools.TypeByName("VEF.Buildings.Dialog_ChooseGraphic");
        MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "DoWindowContents"),
            transpiler: new HarmonyMethod(typeof(VanillaExpandedFramework),
                nameof(Dialog_ChooseGraphic_ReplaceSelectionButton)));
        MP.RegisterSyncMethod(typeof(VanillaExpandedFramework), nameof(Dialog_ChooseGraphic_SyncChange));

        // Glowers
        type = AccessTools.TypeByName("VEF.Buildings.CompGlowerExtended");
        MP.RegisterSyncMethod(type, "SwitchColor");
        compGlowerExtendedGlowerField = AccessTools.FieldRefAccess<CompGlower>(type, "compGlower");
        // Inner method of CompGlowerExtended
        type = AccessTools.Inner(type, "CompGlower_SetGlowColorInternal_Patch");
        PatchingUtilities.PatchCancelInInterface(AccessTools.DeclaredMethod(type, "Postfix"));

        type = dummyGlowerType = AccessTools.TypeByName("VEF.Buildings.DummyGlower");
        dummyGlowerParentCompField = AccessTools.FieldRefAccess<object>(type, "parentComp");

        // Syncing of wall-light type of glower doesn't work with MP, as what they do
        // is spawning a dummy thing, attaching the glower to it, followed by despawning
        // the dummy thing. If something is not spawned and doesn't have a holder MP won't
        // be able to sync it properly due to it missing parent/map it's attached to,
        // and will report it as inaccessible. This works as a workaround to all of this.
        // 
        // We normally sync CompGlower as ThingComp. If we add an explicit sync worker
        // for it, then we'll (basically) replace the vanilla worker for it. It'll be
        // used in situations where we're trying to sync CompGlower directly instead of ThingComp.
        // Can't be synced with `isImplicit: true`, as it'll cause it to sync it with ThingComp
        // sync worker first before syncing it using this specific sync worker.
        MP.RegisterSyncWorker<CompGlower>(SyncCompGlower);

        // Customizable graphic
        type = AccessTools.TypeByName("VEF.Buildings.CompCustomizableGraphic");
        // Target static method with (List<CompCustomizableGraphic>, int) arguments rather than
        // instance type with (int?, bool, bool) types. Technically, the second one should also
        // work, but all arguments (besides instance) would be repeated. On top of that,
        // it would also sync the temporary changes to the graphic, which would cause us to sync
        // unnecessarily frequently, rather than only when changing the graphic itself
        // (and cause some issues on top of that).
        MP.RegisterSyncMethod(type, "SelectGraphic", [typeof(List<>).MakeGenericType(type), typeof(int)]);
    }

    private static bool Dialog_ChooseGraphic_ReplacementButton(Rect butRect, bool doMouseoverSound, Thing thingToChange,
        int index, Window window)
    {
        var result = Widgets.ButtonInvisible(butRect, doMouseoverSound);
        if (!MP.IsInMultiplayer || !result)
            return result;

        window.Close();

        Dialog_ChooseGraphic_SyncChange(index, thingToChange,
            // Filter the comps before syncing them
            Find.Selector.SelectedObjects
                .OfType<ThingWithComps>()
                .Where(thing => thing.def == thingToChange.def)
                .Select(thing => thing.AllComps.FirstOrDefault(x => x.GetType() == randomBuildingGraphicCompType))
                .Where(comp => comp != null));

        return false;
    }

    private static void Dialog_ChooseGraphic_SyncChange(int index, Thing thingToChange,
        IEnumerable<ThingComp> compsToChange)
    {
        // StyleDef things reset to the style graphic instead of applying graphic 0, same as vanilla
        var reset = index == 0 && thingToChange.StyleDef != null;
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            foreach (var comp in compsToChange)
                if (reset)
                    randomBuildingGraphicCompResetGraphicsMethod(comp);
                else
                    randomBuildingGraphicCompChangeGraphicMethod(comp, false, index, false);
        });

        foreach (var comp in compsToChange)
            comp.parent.DirtyMapMesh(comp.parent.Map);
    }

    private static IEnumerable<CodeInstruction> Dialog_ChooseGraphic_ReplaceSelectionButton(
        IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
    {
        // Technically no need to replace specify the argument types, but including them just in case another method with same name gets added in the future
        var targetMethod = AccessTools.DeclaredMethod(typeof(Widgets), nameof(Widgets.ButtonInvisible),
            [typeof(Rect), typeof(bool)]);
        var replacementMethod = AccessTools.DeclaredMethod(typeof(VanillaExpandedFramework),
            nameof(Dialog_ChooseGraphic_ReplacementButton));

        var type = AccessTools.TypeByName("VEF.Buildings.Dialog_ChooseGraphic");
        var thingToChangeField = AccessTools.DeclaredField(type, "thingToChange");

        var instructions = instr.ToList();
        FieldInfo indexField = null;

        foreach (var codeInstruction in instructions)
            if (indexField == null && (codeInstruction.opcode == OpCodes.Ldfld ||
                                       codeInstruction.opcode == OpCodes.Stfld)
                                   && codeInstruction.operand is FieldInfo { Name: "i" } field)
                indexField = field;

        if (indexField == null)
        {
            Log.Error($"{LogPrefix} Dialog_ChooseGraphic: could not find index field 'i', patch not applied.");
            foreach (var codeInstruction in instructions)
                yield return codeInstruction;
            yield break;
        }

        // Find the index of the local holding the compiler-generated display class
        // instance that contains 'i'. Looked up via the method body locals (their
        // operands surface as LocalVariableInfo, not LocalBuilder), and avoid a
        // hardcoded local index (previously Ldloc_S 5) as compiler versions may
        // change it (e.g. DisplayClass9_0 vs DisplayClass10_0).
        var displayClassLocalIndex = -1;
        var methodLocals = baseMethod.GetMethodBody()?.LocalVariables;
        if (methodLocals != null)
            foreach (var local in methodLocals)
                if (local.LocalType == indexField.DeclaringType)
                {
                    displayClassLocalIndex = local.LocalIndex;
                    break;
                }

        if (displayClassLocalIndex < 0)
        {
            Log.Error(
                $"{LogPrefix} Dialog_ChooseGraphic: could not find display class local of type {indexField.DeclaringType}, patch not applied.");
            foreach (var codeInstruction in instructions)
                yield return codeInstruction;
            yield break;
        }

        // Ldloc with an int index, same as upstream (short form where possible).
        var loadDisplayClass = displayClassLocalIndex switch
        {
            0 => new CodeInstruction(OpCodes.Ldloc_0),
            1 => new CodeInstruction(OpCodes.Ldloc_1),
            2 => new CodeInstruction(OpCodes.Ldloc_2),
            3 => new CodeInstruction(OpCodes.Ldloc_3),
            _ => new CodeInstruction(OpCodes.Ldloc_S, displayClassLocalIndex)
        };

        foreach (var codeInstruction in instructions)
        {
            if (codeInstruction.opcode == OpCodes.Call && codeInstruction.operand is MethodInfo method &&
                method == targetMethod)
            {
                codeInstruction.operand = replacementMethod;

                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return
                    new CodeInstruction(OpCodes.Ldfld, thingToChangeField); // Load in the "thingToChange" (Thing) field
                yield return loadDisplayClass; // Load the display class instance holding 'i'
                yield return
                    new CodeInstruction(OpCodes.Ldfld, indexField); // Load in the "i" (int) from the nested type
                yield return new CodeInstruction(OpCodes.Ldarg_0); // Load in the instance (Dialog_ChooseGraphic)
            }

            yield return codeInstruction;
        }
    }

    private static void SyncCompGlower(SyncWorker sync, ref CompGlower glower)
    {
        if (sync.isWriting)
        {
            // Check if the glower's parent thing is of DummyGlower type
            if (dummyGlowerType.IsInstanceOfType(glower.parent))
            {
                sync.Write(true);
                // Sync the CompGlowerExtended (field is typed as CompGlowerExtended in current VEF, cast to ThingComp for syncing)
                sync.Write((ThingComp)dummyGlowerParentCompField(glower.parent));
            }
            else
            {
                // Handle this as normal ThingComp, letting MP sync it
                sync.Write(false);
                sync.Write<ThingComp>(glower);
            }
        }
        else
        {
            // Check if we're reading a normal glower or a glower
            // with a VFE dummy parent and handle appropriately.
            if (sync.Read<bool>())
                glower = compGlowerExtendedGlowerField(sync.Read<ThingComp>());
            else
                glower = sync.Read<ThingComp>() as CompGlower;
        }
    }

    #endregion
}