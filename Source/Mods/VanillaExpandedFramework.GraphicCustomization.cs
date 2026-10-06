using System.Collections;
using System.Runtime.Serialization;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Compat;
using RimWorld;
using Verse;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

internal partial class VanillaExpandedFramework
{
    #region Graphic Customization Dialog

    // Dialog_GraphicCustomization
    private static Type graphicCustomizationDialogType;
    internal static AccessTools.FieldRef<Window, ThingComp> graphicCustomizationCompField;
    private static AccessTools.FieldRef<Window, ThingComp> graphicCustomizationGeneratedNamesCompField;
    private static AccessTools.FieldRef<Window, Pawn> graphicCustomizationPawnField;
    private static AccessTools.FieldRef<Window, IList> graphicCustomizationCurrentVariantsField;
    private static AccessTools.FieldRef<Window, string> graphicCustomizationCurrentNameField;

    // TextureVariant
    private static SyncType variantsListType;
    private static AccessTools.FieldRef<object, string> textureVariantTexNameField;
    private static AccessTools.FieldRef<object, string> textureVariantTextureField;
    private static AccessTools.FieldRef<object, string> textureVariantOutlineField;
    private static AccessTools.FieldRef<object, object> textureVariantTextureVariantOverrideField;
    private static AccessTools.FieldRef<object, float> textureVariantChanceOverrideField;

    // TextureVariantOverride
    private static SyncType textureVariantOverrideType;
    private static AccessTools.FieldRef<object, float> textureVariantOverrideChanceField;
    private static AccessTools.FieldRef<object, string> textureVariantOverrideGroupNameField;
    private static AccessTools.FieldRef<object, string> textureVariantOverrideTexNameField;

    private static void PatchGraphicCustomizationDialog()
    {
        var type = graphicCustomizationDialogType = AccessTools.TypeByName("VEF.Graphics.Dialog_GraphicCustomization");
        graphicCustomizationCompField = AccessTools.FieldRefAccess<ThingComp>(type, "comp");
        graphicCustomizationGeneratedNamesCompField = AccessTools.FieldRefAccess<ThingComp>(type, "compGeneratedName");
        graphicCustomizationPawnField = AccessTools.FieldRefAccess<Pawn>(type, "pawn");
        graphicCustomizationCurrentVariantsField = AccessTools.FieldRefAccess<IList>(type, "currentVariants");
        graphicCustomizationCurrentNameField = AccessTools.FieldRefAccess<string>(type, "currentName");
        // Accept customization
        MpCompat.RegisterLambdaMethod(type, nameof(Window.DoWindowContents), 0);
        MP.RegisterSyncWorker<Window>(SyncGraphicCustomizationDialog, type, true);
        MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "DrawConfirmButton"),
            new HarmonyMethod(CloseGraphicCustomizationDialogOnAccept));
        MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, nameof(Window.DoWindowContents)),
            new HarmonyMethod(CloseGraphicCustomizationWhenNoLongerValid));

        type = AccessTools.TypeByName("VEF.Graphics.TextureVariant");
        variantsListType = typeof(List<>).MakeGenericType(type);
        textureVariantTexNameField = AccessTools.FieldRefAccess<string>(type, "texName");
        textureVariantTextureField = AccessTools.FieldRefAccess<string>(type, "texture");
        textureVariantOutlineField = AccessTools.FieldRefAccess<string>(type, "outline");
        textureVariantChanceOverrideField = AccessTools.FieldRefAccess<float>(type, "chanceOverride");
        textureVariantTextureVariantOverrideField = AccessTools.FieldRefAccess<object>(type, "textureVariantOverride");
        MP.RegisterSyncWorker<object>(SyncTextureVariant, type, shouldConstruct: true);

        textureVariantOverrideType = type = AccessTools.TypeByName("VEF.Graphics.TextureVariantOverride");
        textureVariantOverrideChanceField = AccessTools.FieldRefAccess<float>(type, "chance");
        textureVariantOverrideGroupNameField = AccessTools.FieldRefAccess<string>(type, "groupName");
        textureVariantOverrideTexNameField = AccessTools.FieldRefAccess<string>(type, "texName");
        MP.RegisterSyncWorker<object>(SyncTextureVariantOverride, type, shouldConstruct: true);
    }

    private static void CloseGraphicCustomizationDialogOnAccept(Window __instance, ref Action action)
    {
        // The action is a synced method, and it handles closing the dialog. Since
        // the method ends up being synced the closing does not happen. This will
        // ensure that the dialog is closed when the accept button is pressed
        // and the proper method will be synced as well.
        var nonRefAction = action;
        action = (() => __instance.Close()) + nonRefAction;
    }

    private static void CloseGraphicCustomizationWhenNoLongerValid(Window __instance, ThingComp ___comp, Pawn ___pawn)
    {
        // Since the dialog doesn't pause, ensure we close it if the comp's parent
        // or the pawn ever get destroyed, despawned, or end up on different maps.
        if (MP.IsInMultiplayer && !IsGraphicCustomizationDialogValid(___comp, ___pawn))
            __instance.Close();
    }

    private static bool CancelGraphicCustomizationExecutionIfNoLongerValid(ThingComp ___comp, Pawn ___pawn)
    {
        return !MP.IsInMultiplayer || IsGraphicCustomizationDialogValid(___comp, ___pawn);
    }

    private static bool IsGraphicCustomizationDialogValid(ThingComp comp, Pawn pawn)
    {
        return comp.parent != null && pawn != null &&
               comp.parent.Spawned && pawn.Spawned &&
               !comp.parent.Destroyed && !pawn.Destroyed &&
               !pawn.Dead && comp.parent.Map == pawn.Map;
    }

    private static void SyncGraphicCustomizationDialog(SyncWorker sync, ref Window dialog)
    {
        ThingComp comp = null;

        if (sync.isWriting)
        {
            sync.Write(graphicCustomizationCompField(dialog));
        }
        else
        {
            // Skip constructor since it has a bunch of initialization we don't care about.
            dialog = (Window)FormatterServices.GetUninitializedObject(graphicCustomizationDialogType);

            comp = sync.Read<ThingComp>();
            graphicCustomizationCompField(dialog) = comp;
        }

        SyncGraphicCustomizationDialog(sync, ref dialog, comp);
    }

    internal static void SyncGraphicCustomizationDialog(SyncWorker sync, ref Window dialog,
        ThingComp graphicCustomizationComp)
    {
        if (sync.isWriting)
        {
            sync.Write(graphicCustomizationPawnField(dialog));
            sync.Write(graphicCustomizationCurrentVariantsField(dialog), variantsListType);
            sync.Write(graphicCustomizationCurrentNameField(dialog));
        }
        else
        {
            graphicCustomizationGeneratedNamesCompField(dialog) =
                graphicCustomizationComp?.parent.GetComp<CompGeneratedNames>();
            graphicCustomizationPawnField(dialog) = sync.Read<Pawn>();
            graphicCustomizationCurrentVariantsField(dialog) = sync.Read<IList>(variantsListType);
            graphicCustomizationCurrentNameField(dialog) = sync.Read<string>();
        }
    }

    private static void SyncTextureVariant(SyncWorker sync, ref object variant)
    {
        if (sync.isWriting)
        {
            if (variant == null)
            {
                sync.Write(false);
                return;
            }

            sync.Write(true);

            sync.Write(textureVariantTexNameField(variant));
            sync.Write(textureVariantTextureField(variant));
            sync.Write(textureVariantOutlineField(variant));
            sync.Write(textureVariantTextureVariantOverrideField(variant), textureVariantOverrideType);
            sync.Write(textureVariantChanceOverrideField(variant));
        }
        else if (sync.Read<bool>())
        {
            textureVariantTexNameField(variant) = sync.Read<string>();
            textureVariantTextureField(variant) = sync.Read<string>();
            textureVariantOutlineField(variant) = sync.Read<string>();
            textureVariantTextureVariantOverrideField(variant) = sync.Read<object>(textureVariantOverrideType);
            textureVariantChanceOverrideField(variant) = sync.Read<float>();
        }
    }

    private static void SyncTextureVariantOverride(SyncWorker sync, ref object variantOverride)
    {
        if (sync.isWriting)
        {
            if (variantOverride == null)
            {
                sync.Write(false);
                return;
            }

            sync.Write(true);
            sync.Write(textureVariantOverrideChanceField(variantOverride));
            sync.Write(textureVariantOverrideGroupNameField(variantOverride));
            sync.Write(textureVariantOverrideTexNameField(variantOverride));
        }
        else if (sync.Read<bool>())
        {
            textureVariantOverrideChanceField(variantOverride) = sync.Read<float>();
            textureVariantOverrideGroupNameField(variantOverride) = sync.Read<string>();
            textureVariantOverrideTexNameField(variantOverride) = sync.Read<string>();
        }
    }

    #endregion
}