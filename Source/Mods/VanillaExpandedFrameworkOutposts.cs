using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Compat;
using Outposts;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

/// <summary>
///     Vanilla Expanded Framework and other Vanilla Expanded mods by Oskar Potocki, XeoNovaDan, Orion, Kikohi, Taranchuk,
///     Sarg Bjornson, Erdelf,
///     Last Update: 5 Oct @ 7:10pm 2026
///     <see href="https://github.com/Vanilla-Expanded/VanillaExpandedFramework" />
///     <see href="https://steamcommunity.com/sharedfiles/filedetails/?id=2023507013" />
/// </summary>
[MpCompatFor("OskarPotocki.VanillaFactionsExpanded.Core")]
internal class VanillaExpandedFrameworkOutposts
{
    private const string LogPrefix = "[Multiplayer Vanilla Expanded Framework Outposts Patch]";

    // Dialog_CreateCamp fields (private readonly)
    private static readonly AccessTools.FieldRef<object, Caravan> createCampCreatorField =
        AccessTools.FieldRefAccess<Caravan>(typeof(Dialog_CreateCamp), "creator");

    private static readonly AccessTools.FieldRef<object, Dictionary<WorldObjectDef, Pair<string, string>>>
        createCampValidityField =
            AccessTools.FieldRefAccess<Dictionary<WorldObjectDef, Pair<string, string>>>(typeof(Dialog_CreateCamp),
                "validity");

    // Dialog_TakeItems fields/methods (private)
    private static readonly AccessTools.FieldRef<object, Vector2> takeItemsBottomButtonSizeField =
        AccessTools.FieldRefAccess<Vector2>(typeof(Dialog_TakeItems), "BottomButtonSize");

    private static readonly AccessTools.FieldRef<object, Caravan> takeItemsCaravanField =
        AccessTools.FieldRefAccess<Caravan>(typeof(Dialog_TakeItems), "caravan");

    private static readonly AccessTools.FieldRef<object, Outpost> takeItemsOutpostField =
        AccessTools.FieldRefAccess<Outpost>(typeof(Dialog_TakeItems), "outpost");

    private static readonly AccessTools.FieldRef<object, List<TransferableOneWay>> takeItemsTransferablesField =
        AccessTools.FieldRefAccess<List<TransferableOneWay>>(typeof(Dialog_TakeItems), "transferables");

    private static readonly MethodInfo takeItemsRecacheMethod =
        AccessTools.Method(typeof(Dialog_TakeItems), "CalculateAndRecacheTransferables");

    // Dialog_GiveItems fields/methods (private)
    private static readonly AccessTools.FieldRef<object, Vector2> giveItemsBottomButtonSizeField =
        AccessTools.FieldRefAccess<Vector2>(typeof(Dialog_GiveItems), "BottomButtonSize");

    private static readonly AccessTools.FieldRef<object, Caravan> giveItemsCaravanField =
        AccessTools.FieldRefAccess<Caravan>(typeof(Dialog_GiveItems), "caravan");

    private static readonly AccessTools.FieldRef<object, Outpost> giveItemsOutpostField =
        AccessTools.FieldRefAccess<Outpost>(typeof(Dialog_GiveItems), "outpost");

    private static readonly AccessTools.FieldRef<object, List<TransferableOneWay>> giveItemsTransferablesField =
        AccessTools.FieldRefAccess<List<TransferableOneWay>>(typeof(Dialog_GiveItems), "transferables");

    private static readonly MethodInfo giveItemsRecacheMethod =
        AccessTools.Method(typeof(Dialog_GiveItems), "CalculateAndRecacheTransferables");

    // WITab_Outpost_Gear (draggedItem private, SelOutpost public)
    private static readonly AccessTools.FieldRef<object, Thing> gearDraggedItemField =
        AccessTools.FieldRefAccess<Thing>(typeof(WITab_Outpost_Gear), "draggedItem");

    private static readonly MethodInfo gearTryEquipMethod =
        AccessTools.Method(typeof(WITab_Outpost_Gear), "TryEquipDraggedItem");

    public VanillaExpandedFrameworkOutposts(ModContentPack mod)
    {
        Log.Message($"{LogPrefix} Initializing...");

        (Action patchMethod, string componentName)[] patches =
        [
            (PatchCreateCampDialog, "Create camp dialog"),
            (PatchTakeGiveItemsDialogs, "Take/give items dialogs"),
            (PatchOutpostGizmos, "Outpost gizmos"),
            (PatchOutpostGearTab, "Outpost gear tab")
        ];

        foreach (var (patchMethod, componentName) in patches)
            try
            {
                patchMethod();
            }
            catch (Exception exception)
            {
                Log.Error(
                    $"{LogPrefix} Encountered an error patching {componentName} part of Outposts - this part of the mod may not work properly!");
                Log.Error(exception.ToString());
            }

        Log.Message($"{LogPrefix} Initialized.");
    }

    private static void PatchCreateCampDialog()
    {
        MpCompat.harmony.Patch(AccessTools.Method(typeof(Dialog_CreateCamp), "DoOutpostDisplay"),
            new HarmonyMethod(typeof(VanillaExpandedFrameworkOutposts), nameof(PreDoOutpostDisplay)));
        MP.RegisterSyncMethod(typeof(VanillaExpandedFrameworkOutposts), nameof(SyncedCreateOutpost));
    }

    private static void PatchTakeGiveItemsDialogs()
    {
        MpCompat.harmony.Patch(AccessTools.Method(typeof(Dialog_TakeItems), "DoBottomButtons"),
            new HarmonyMethod(typeof(VanillaExpandedFrameworkOutposts), nameof(PreTakeItemsDoBottomButtons)));
        MP.RegisterSyncMethod(typeof(VanillaExpandedFrameworkOutposts), nameof(SyncedTakeItems));

        MpCompat.harmony.Patch(AccessTools.Method(typeof(Dialog_GiveItems), "DoBottomButtons"),
            new HarmonyMethod(typeof(VanillaExpandedFrameworkOutposts), nameof(PreGiveItemsDoBottomButtons)));
        MP.RegisterSyncMethod(typeof(VanillaExpandedFrameworkOutposts), nameof(SyncedGiveItems));
    }

    private static void PatchOutpostGizmos()
    {
        // Generic outpost
        // Stop packing (0), pack (1), pick colony to deliver to (8), and (dev) produce now (9), random pawn takes 10 damage (10), all pawns become hungry (11), pack instantly (12)
        MpCompat.RegisterLambdaMethod(typeof(Outpost), nameof(Outpost.GetGizmos), 0, 1, 8, 9, 10, 11, 12).Reverse()
            .Take(4).SetDebugOnly();
        // Pick delivery method (8)
        MpCompat.RegisterLambdaDelegate(typeof(Outpost), nameof(Outpost.GetGizmos), 8);
        // Remove pawn from outpost/create caravan (4), needs a slight workaround as pawn is inacessible
        var innerMethod = MpMethodUtil.GetLambda(typeof(Outpost), nameof(Outpost.GetGizmos), lambdaOrdinal: 4);
        var outpostsInnerClassThisField = AccessTools.FieldRefAccess<Outpost>(innerMethod.DeclaringType, "<>4__this");
        MP.RegisterSyncDelegate(typeof(Outpost), innerMethod.DeclaringType!.Name, innerMethod.Name)
            .TransformField("p", Serializer.New(
                (p, instance, _) => (outpostsInnerClassThisField(instance), p.thingIDNumber),
                ((Outpost o, int pawnId) tuple) =>
                    tuple.o.AllPawns.FirstOrDefault(p => p.thingIDNumber == tuple.pawnId)));

        MP.RegisterSyncWorker<ResultOption>(SyncResultOption);
        // Add pawn to outpost
        MpCompat.RegisterLambdaDelegate(typeof(Outpost), nameof(Outpost.GetCaravanGizmos), 2);

        // Outpost with results you can choose from
        MpCompat.RegisterLambdaDelegate("Outposts.Outpost_ChooseResult", "GetGizmos", 2);
    }

    private static void PatchOutpostGearTab()
    {
        MP.RegisterSyncWorker<WITab_Outpost_Gear>(SyncWITabOutpostGear);

        // Equip item from outpost's WITab (private method, use string name)
        var method = AccessTools.DeclaredMethod(typeof(WITab_Outpost_Gear), "TryEquipDraggedItem");
        MP.RegisterSyncMethod(MpMethodUtil.MethodOf(SyncedTryEquipDraggedItem)).SetContext(SyncContext.WorldSelected);
        MpCompat.harmony.Patch(method,
            new HarmonyMethod(MpMethodUtil.MethodOf(PreTryEquipDraggedItem)),
            finalizer: new HarmonyMethod(MpMethodUtil.MethodOf(ClearDraggedItem)));

        // Equip weapon from outpost's WItab (private method)
        method = AccessTools.DeclaredMethod(typeof(WITab_Outpost_Gear), "TryEquipDraggedItem_Equipment");
        MP.RegisterSyncMethod(method).SetContext(SyncContext.WorldSelected)
            .TransformArgument(0, Serializer.New<Pawn, int>
            (
                p => p.thingIDNumber,
                id => ((Outpost)Find.WorldSelector.SingleSelectedObject).AllPawns.FirstOrDefault(p =>
                    p.thingIDNumber == id)
            ))
            .TransformArgument(1, Serializer.SimpleReader(() => (ThingWithComps)null));
        MpCompat.harmony.Patch(method,
            new HarmonyMethod(MpMethodUtil.MethodOf(SetupArgumentFromDraggedItem)),
            finalizer: new HarmonyMethod(MpMethodUtil.MethodOf(ClearDraggedItem)));

        method = AccessTools.DeclaredMethod(typeof(WITab_Outpost_Gear), "MoveDraggedItemToInventory");
        MP.RegisterSyncMethod(method).SetContext(SyncContext.WorldSelected);
        MpCompat.harmony.Patch(method,
            finalizer: new HarmonyMethod(MpMethodUtil.MethodOf(ClearDraggedItem)));
    }

    private static void SyncResultOption(SyncWorker sync, ref ResultOption option)
    {
        if (sync.isWriting) sync.Write(option.Thing);
        else
            option = new ResultOption
            {
                Thing = sync.Read<ThingDef>()
            };
    }

    private static bool PreDoOutpostDisplay(ref Rect inRect, WorldObjectDef outpostDef, Dialog_CreateCamp __instance)
    {
        if (!MP.IsInMultiplayer)
            return true;

        var creator = createCampCreatorField(__instance);
        var validity = createCampValidityField(__instance);

        var font = Text.Font;
        var anchor = Text.Anchor;
        Text.Font = GameFont.Tiny;
        inRect.height = Text.CalcHeight(outpostDef.description, inRect.width - 90f) + 60f;
        var outerRect = inRect.LeftPartPixels(50f);
        var rect = inRect.RightPartPixels(inRect.width - 60f);
        var expandingIconTexture = outpostDef.ExpandingIconTexture;
        GUI.color = creator.Faction.Color;
        Widgets.DrawTextureFitted(outerRect, expandingIconTexture, 1f,
            new Vector2(expandingIconTexture.width, expandingIconTexture.height), new Rect(0f, 0f, 1f, 1f));
        GUI.color = Color.white;
        Text.Font = GameFont.Medium;
        Widgets.Label(rect.TopPartPixels(30f), outpostDef.label.CapitalizeFirst(outpostDef));
        var rect2 = rect.BottomPartPixels(30f).LeftPartPixels(100f);
        var rect3 = rect.BottomPartPixels(30f).RightPartPixels(rect.width - 120f);
        Text.Font = GameFont.Tiny;
        Widgets.Label(new Rect(rect.x, rect.y + 30f, rect.width, rect.height - 60f), outpostDef.description);
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(rect3, validity[outpostDef].First);
        Text.Font = font;
        Text.Anchor = anchor;

        if (Widgets.ButtonText(rect2, "Outposts.Dialog.Create".Translate()))
        {
            if (validity[outpostDef].First.NullOrEmpty())
            {
                SyncedCreateOutpost(outpostDef, creator);
                __instance.Close();
                Find.WorldSelector.Deselect(creator);
            }
            else
            {
                Messages.Message(validity[outpostDef].First, MessageTypeDefOf.RejectInput, false);
            }
        }

        TooltipHandler.TipRegion(inRect, validity[outpostDef].Second);

        return false;
    }

    private static void SyncedCreateOutpost(WorldObjectDef outpostDef, Caravan creator)
    {
        var outpost = (Outpost)WorldObjectMaker.MakeWorldObject(outpostDef);
        outpost.Name = NameGenerator.GenerateName(creator.Faction.def.settlementNameMaker,
            Find.WorldObjects.AllWorldObjects.OfType<Outpost>().Select(o => o.Name));
        outpost.Tile = creator.Tile;
        outpost.SetFaction(creator.Faction);
        Find.WorldObjects.Add(outpost);

        foreach (var pawn in creator.PawnsListForReading.ListFullCopy()) outpost.AddPawn(pawn);
    }

    private static bool PreTakeItemsDoBottomButtons(Rect rect, Dialog_TakeItems __instance)
    {
        if (!MP.IsInMultiplayer)
            return true;

        var bottomButtonSize = takeItemsBottomButtonSizeField(__instance);
        var transferables = takeItemsTransferablesField(__instance);
        var outpost = takeItemsOutpostField(__instance);
        var caravan = takeItemsCaravanField(__instance);

        var rect2 = new Rect(rect.width - bottomButtonSize.x, rect.height - 40f, bottomButtonSize.x,
            bottomButtonSize.y);

        if (Widgets.ButtonText(rect2, "Outposts.Take".Translate()))
        {
            var thingsToTransfer = transferables
                .Where(x => x.HasAnyThing && x.CountToTransfer > 0)
                .ToDictionary(x => x.ThingDef, x => x.CountToTransfer);

            SyncedTakeItems(outpost, caravan, thingsToTransfer);
            __instance.Close();
        }

        if (Widgets.ButtonText(new Rect(0f, rect2.y, bottomButtonSize.x, bottomButtonSize.y),
                "CancelButton".Translate()))
            __instance.Close();

        if (Widgets.ButtonText(
                new Rect(rect.width / 2f - bottomButtonSize.x, rect2.y, bottomButtonSize.x, bottomButtonSize.y),
                "ResetButton".Translate()))
        {
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            takeItemsRecacheMethod.Invoke(__instance, null);
        }

        return false;
    }

    private static void SyncedTakeItems(Outpost outpost, Caravan caravan, Dictionary<ThingDef, int> itemsToTransfer)
    {
        var dummyDialog = new Dialog_TakeItems(outpost, caravan);
        takeItemsRecacheMethod.Invoke(dummyDialog, null);
        var transferables = takeItemsTransferablesField(dummyDialog);

        foreach (var transferable in transferables)
        {
            if (transferable.HasAnyThing && itemsToTransfer.TryGetValue(transferable.ThingDef, out var count))
                transferable.ForceTo(count);

            while (transferable.HasAnyThing && transferable.CountToTransfer > 0)
            {
                var thing = transferable.things.Pop();

                if (thing.stackCount <= transferable.CountToTransfer)
                {
                    transferable.AdjustBy(-thing.stackCount);
                    thing.holdingOwner?.Remove(thing);
                    caravan.AddPawnOrItem(outpost.TakeItem(thing), true);
                }
                else
                {
                    caravan.AddPawnOrItem(thing.SplitOff(transferable.CountToTransfer), true);
                    transferable.AdjustTo(0);
                    transferable.things.Add(thing);
                }
            }
        }
    }

    private static bool PreGiveItemsDoBottomButtons(Rect rect, Dialog_GiveItems __instance)
    {
        if (!MP.IsInMultiplayer)
            return true;

        var bottomButtonSize = giveItemsBottomButtonSizeField(__instance);
        var transferables = giveItemsTransferablesField(__instance);
        var outpost = giveItemsOutpostField(__instance);
        var caravan = giveItemsCaravanField(__instance);

        var rect2 = new Rect(rect.width - bottomButtonSize.x, rect.height - 40f, bottomButtonSize.x,
            bottomButtonSize.y);

        if (Widgets.ButtonText(rect2, "Outposts.Give".Translate()))
        {
            var thingsToTransfer = transferables
                .Where(x => x.HasAnyThing && x.CountToTransfer > 0)
                .ToDictionary(x => x.ThingDef, x => x.CountToTransfer);

            SyncedGiveItems(outpost, caravan, thingsToTransfer);
            __instance.Close();
        }

        if (Widgets.ButtonText(new Rect(0f, rect2.y, bottomButtonSize.x, bottomButtonSize.y),
                "CancelButton".Translate()))
            __instance.Close();

        if (Widgets.ButtonText(
                new Rect(rect.width / 2f - bottomButtonSize.x, rect2.y, bottomButtonSize.x, bottomButtonSize.y),
                "ResetButton".Translate()))
        {
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            giveItemsRecacheMethod.Invoke(__instance, null);
        }

        return false;
    }

    private static void SyncedGiveItems(Outpost outpost, Caravan caravan, Dictionary<ThingDef, int> itemsToTransfer)
    {
        var dummyDialog = new Dialog_GiveItems(outpost, caravan);
        giveItemsRecacheMethod.Invoke(dummyDialog, null);
        var transferables = giveItemsTransferablesField(dummyDialog);

        foreach (var transferable in transferables)
        {
            if (transferable.HasAnyThing && itemsToTransfer.TryGetValue(transferable.ThingDef, out var count))
                transferable.ForceTo(count);

            while (transferable.HasAnyThing && transferable.CountToTransfer > 0)
            {
                var thing = transferable.things.Pop();

                if (thing.stackCount <= transferable.CountToTransfer)
                {
                    transferable.AdjustBy(-thing.stackCount);
                    thing.holdingOwner?.Remove(thing);
                    outpost.AddItem(thing);
                }
                else
                {
                    outpost.AddItem(thing.SplitOff(transferable.CountToTransfer));
                    transferable.AdjustTo(0);
                    transferable.things.Add(thing);
                }
            }
        }
    }

    private static void SyncWITabOutpostGear(SyncWorker sync, ref WITab_Outpost_Gear tab)
    {
        if (sync.isWriting)
        {
            sync.Write(gearDraggedItemField(tab).thingIDNumber);
        }
        else
        {
            var id = sync.Read<int>();

            tab = new WITab_Outpost_Gear();
            gearDraggedItemField(tab) = tab.SelOutpost.Things.FirstOrDefault(t => t.thingIDNumber == id);
        }
    }

    private static void SyncedTryEquipDraggedItem(WITab_Outpost_Gear tab, int pawnId)
    {
        var pawn = tab.SelOutpost.AllPawns.FirstOrDefault(p => p.thingIDNumber == pawnId);
        if (pawn != null)
            gearTryEquipMethod.Invoke(tab, [pawn]);
    }

    private static bool PreTryEquipDraggedItem(WITab_Outpost_Gear __instance, Pawn p)
    {
        if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
            return true;

        var draggedItem = gearDraggedItemField(__instance);

        if (p.apparel == null || draggedItem is not Apparel)
            return true;

        SyncedTryEquipDraggedItem(__instance, p.thingIDNumber);
        return false;
    }

    private static bool SetupArgumentFromDraggedItem(ref ThingWithComps eq, Thing ___draggedItem)
    {
        if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand)
            return true;

        if (___draggedItem is not ThingWithComps thing)
            return false;

        eq = thing;
        return true;
    }

    private static void ClearDraggedItem(ref Thing ___draggedItem)
    {
        ___draggedItem = null;
    }
}