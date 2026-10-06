using System.Collections;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Compat;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

internal partial class VanillaExpandedFramework
{
    #region Abilities

    // CompAbility
    private static Type compAbilitiesType;
    private static AccessTools.FieldRef<object, IEnumerable> learnedAbilitiesField;

    // CompAbilityApparel
    private static Type compAbilitiesApparelType;
    private static AccessTools.FieldRef<object, IEnumerable> givenAbilitiesField;
    private static FastInvokeHandler abilityApparelPawnGetter;

    // Ability
    private static FastInvokeHandler abilityInitMethod;
    private static AccessTools.FieldRef<object, Thing> abilityHolderField;
    private static AccessTools.FieldRef<object, Pawn> abilityPawnField;
    private static AccessTools.FieldRef<IExposable, int> abilityCooldownField;
    private static ISyncField abilityAutoCastField;

    private static void PatchAbilities()
    {
        PatchingUtilities.SetupAsyncTime();

        // Comp holding ability
        // CompAbility
        compAbilitiesType = AccessTools.TypeByName("VEF.Abilities.CompAbilities");
        learnedAbilitiesField = AccessTools.FieldRefAccess<IEnumerable>(compAbilitiesType, "learnedAbilities");
        // Unlock ability, user-input use by Vanilla Psycasts Expanded
        MP.RegisterSyncMethod(compAbilitiesType, "GiveAbility");
        // CompAbilityApparel
        compAbilitiesApparelType = AccessTools.TypeByName("VEF.Abilities.CompAbilitiesApparel");
        givenAbilitiesField = AccessTools.FieldRefAccess<IEnumerable>(compAbilitiesApparelType, "givenAbilities");
        abilityApparelPawnGetter =
            MethodInvoker.GetHandler(AccessTools.PropertyGetter(compAbilitiesApparelType, "Pawn"));
        //MP.RegisterSyncMethod(compAbilitiesApparelType, "Initialize");

        // Ability itself
        var type = AccessTools.TypeByName("VEF.Abilities.Ability");

        abilityInitMethod = MethodInvoker.GetHandler(AccessTools.Method(type, "Init"));
        abilityHolderField = AccessTools.FieldRefAccess<Thing>(type, "holder");
        abilityPawnField = AccessTools.FieldRefAccess<Pawn>(type, "pawn");
        abilityCooldownField = AccessTools.FieldRefAccess<int>(type, "cooldown");
        // There's another method taking LocalTargetInfo. Harmony grabs the one we need, but just in case specify the types to avoid ambiguity.
        MP.RegisterSyncMethod(type, "StartAbilityJob", [typeof(GlobalTargetInfo[])])
            // Need to transform arguments to properly handle multiple maps
            .TransformArgument(0, Serializer.New(
                (GlobalTargetInfo[] targets)
                    => targets.Select(x => (
                        thingId: x.thingInt?.thingIDNumber ?? -1,
                        tileId: x.tileInt,
                        worldObjectId: x.worldObjectInt?.ID ?? -1,
                        cell: x.cellInt,
                        mapId: x.mapInt?.uniqueID ?? -1)).ToList(),
                result => result.Select(x =>
                {
                    var (thingId, tileId, worldObjectId, cell, mapId) = x;
                    if (thingId != -1)
                    {
                        if (MP.TryGetThingById(thingId, out var thing))
                            return new GlobalTargetInfo(thing);
                    }
                    else if (tileId != -1)
                    {
                        return new GlobalTargetInfo(tileId);
                    }
                    else if (worldObjectId != -1)
                    {
                        var worldObject = Find.World.worldObjects.AllWorldObjects.Find(w => w.ID == worldObjectId) ??
                                          Find.World.pocketMaps.Find(p => p.ID == worldObjectId);
                        if (worldObject != null)
                            return new GlobalTargetInfo(worldObject);
                    }
                    else if (cell.IsValid)
                    {
                        return new GlobalTargetInfo(cell, Find.Maps.FirstOrDefault(x => x.uniqueID == mapId), true);
                    }

                    return GlobalTargetInfo.Invalid;
                }).ToArray()));
        MP.RegisterSyncWorker<ITargetingSource>(SyncVEFAbility, type, true);
        abilityAutoCastField = MP.RegisterSyncField(type, "autoCast");
        MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "DoAction"),
            new HarmonyMethod(typeof(VanillaExpandedFramework), nameof(PreAbilityDoAction)),
            new HarmonyMethod(typeof(VanillaExpandedFramework), nameof(PostAbilityDoAction)));

        foreach (var target in type.AllSubclasses().Concat(type))
        {
            // Fix timestamp.
            // We really could use implicit fixers, so we don't have to register one fixer per ability type.
            if (!target.IsAbstract)
                PatchingUtilities.RegisterTimestampFixer(target, MpMethodUtil.MethodOf(FixAbilityTimestamp));

            // We need to set this up in all subtypes that override GetGizmo, as we need to make sure
            // that the call to Command_Ability constructor has the proper time. We could patch the
            // constructor of all subtypes of Command_Ability, but the issue with that is that the
            // gizmo are not guaranteed to be of that specific type. On top of that, their arguments
            // (which we'd need to use to setup the time snapshot) may have different names, and may
            // be in a different order. It's just simpler to patch this specific method for simplicity sake,
            // instead of trying to patch every relevant constructor.
            var method = AccessTools.DeclaredMethod(target, "GetGizmo");
            if (method != null)
                MpCompat.harmony.Patch(method,
                    new HarmonyMethod(MpMethodUtil.MethodOf(PreGetGizmo)),
                    finalizer: new HarmonyMethod(MpMethodUtil.MethodOf(RestoreProperTimeSnapshot)));
        }

        type = AccessTools.TypeByName("VEF.Apparels.CompShieldField");
        MpCompat.RegisterLambdaMethod(type, nameof(ThingComp.CompGetWornGizmosExtra), 0);
        MpCompat.RegisterLambdaMethod(type, "GetGizmos", 0, 2);

        // Time snapshot fix for gizmo itself
        type = AccessTools.TypeByName("VEF.Abilities.Command_Ability");
        foreach (var targetType in type.AllSubclasses().Concat(type))
        {
            var method = AccessTools.DeclaredMethod(targetType, nameof(Command.GizmoOnGUIInt));
            if (method != null)
                MpCompat.harmony.Patch(method,
                    new HarmonyMethod(MpMethodUtil.MethodOf(PreAbilityGizmoGui)),
                    finalizer: new HarmonyMethod(MpMethodUtil.MethodOf(RestoreProperTimeSnapshot)));
        }
    }

    private static void SyncVEFAbility(SyncWorker sync, ref ITargetingSource source)
    {
        if (sync.isWriting)
        {
            sync.Write(abilityHolderField(source));
            sync.Write(source.GetVerb.GetUniqueLoadID());
        }
        else
        {
            var holder = sync.Read<Thing>();
            var uid = sync.Read<string>();
            if (holder is ThingWithComps thing)
            {
                IEnumerable list = null;

                var compAbilities = thing.AllComps.FirstOrDefault(c => compAbilitiesType.IsInstanceOfType(c));
                ThingComp compAbilitiesApparel = null;
                if (compAbilities != null)
                    list = learnedAbilitiesField(compAbilities);

                if (list == null)
                {
                    compAbilitiesApparel =
                        thing.AllComps.FirstOrDefault(c => compAbilitiesApparelType.IsInstanceOfType(c));
                    if (compAbilitiesApparel != null)
                        list = givenAbilitiesField(compAbilitiesApparel);
                }

                if (list != null)
                {
                    foreach (var o in list)
                    {
                        var its = o as ITargetingSource;
                        if (its?.GetVerb.GetUniqueLoadID() == uid)
                        {
                            source = its;
                            break;
                        }
                    }

                    if (source != null && compAbilitiesApparel != null)
                    {
                        // Set the pawn and initialize the Ability, as it might have been skipped
                        var pawn = abilityApparelPawnGetter(compAbilitiesApparel) as Pawn;
                        abilityPawnField(source) = pawn;
                        abilityInitMethod(source);
                    }
                }
                else
                {
                    Log.Error($"{LogPrefix} SyncVEFAbility : Holder is missing or of unsupported type");
                }
            }
            else
            {
                Log.Error($"{LogPrefix} SyncVEFAbility : Holder isn't a ThingWithComps");
            }
        }
    }

    private static void PreAbilityDoAction(object __instance)
    {
        if (!MP.IsInMultiplayer)
            return;

        MP.WatchBegin();
        abilityAutoCastField.Watch(__instance);
    }

    private static void PostAbilityDoAction()
    {
        if (!MP.IsInMultiplayer)
            return;

        MP.WatchEnd();
    }

    // We need to set the time snapshot when constructing the gizmo since it's disabled in
    // the constructor, meaning that it will be incorrectly disabled if we don't do it.
    private static void PreGetGizmo(object __instance, out PatchingUtilities.TimeSnapshot? __state)
    {
        __state = SetTemporaryTimeSnapshot(__instance);
    }

    // We need to set the time snapshot when drawing the gizmo GUI, as otherwise it'll
    // display completely incorrect values for cooldown or will allow for the ability
    // usage while it should still be on cooldown.
    public static void PreAbilityGizmoGui(object ___ability, out PatchingUtilities.TimeSnapshot? __state)
    {
        __state = SetTemporaryTimeSnapshot(___ability);
    }

    private static PatchingUtilities.TimeSnapshot? SetTemporaryTimeSnapshot(object ability)
    {
        if (!MP.IsInMultiplayer)
            return null;

        var target = abilityPawnField(ability) ?? abilityHolderField(ability);
        if (target?.Map == null)
            return null;

        return PatchingUtilities.TimeSnapshot.GetAndSetFromMap(target.Map);
    }

    public static void RestoreProperTimeSnapshot(PatchingUtilities.TimeSnapshot? __state)
    {
        __state?.Set();
    }

    private static ref int FixAbilityTimestamp(IExposable ability)
    {
        return ref abilityCooldownField(ability);
    }

    #endregion
}