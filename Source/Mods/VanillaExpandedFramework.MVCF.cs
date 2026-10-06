using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Compat;
using RimWorld.Planet;
using Verse;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

internal partial class VanillaExpandedFramework
{
    #region MVCF

    // MVCF //
    // Core
    private static IEnumerable<string> mvcfEnabledFeaturesSet;

    // VerbManager
    private static FastInvokeHandler mvcfVerbManagerPawnGetter;
    private static FastInvokeHandler mvcfVerbManagerTickMethod;
    private static AccessTools.FieldRef<object, IList> mvcfVerbManagerVerbsField;

    // PawnVerbUtility
    private delegate object GetManager(Pawn p, bool createIfMissing);

    private static GetManager mvcfPawnVerbUtilityGetManager;

    // ManagedVerb
    private static FastInvokeHandler mvcfManagedVerbManagerGetter;

    // VerbWithComps
    private static AccessTools.FieldRef<object, IList> mvcfVerbWithCompsField;

    // VerbComp
    private static AccessTools.FieldRef<object, object> mvcfVerbCompParentField;

    // WorldComponent_MVCF
    private static AccessTools.FieldRef<WorldComponent> mvcfWorldCompInstanceField;
    private static AccessTools.FieldRef<WorldComponent, IList> mvcfWorldCompTickManagersField;

    // WeakReference<VerbManager>
    private static FastInvokeHandler mvcfWeakReferenceTryGetVerbManagerMethod;

    private static void PatchMVCF()
    {
        PatchingUtilities.SetupAsyncTime();

        MpCompat.harmony.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.SpawnSetup)),
            postfix: new HarmonyMethod(typeof(VanillaExpandedFramework), nameof(EverybodyGetsVerbManager)));

        var type = AccessTools.TypeByName("MVCF.MVCF");
        // HashSet<string>, using it as IEnumerable<string> for a bit of extra safety in case it ever gets changed to a list or something.
        mvcfEnabledFeaturesSet = AccessTools.Field(type, "EnabledFeatures").GetValue(null) as IEnumerable<string>;
        if (mvcfEnabledFeaturesSet == null)
            Log.Warning($"{LogPrefix} Cannot access the list of enabled MVCF features, this may cause issues");

        type = AccessTools.TypeByName("MVCF.Utilities.PawnVerbUtility");
        mvcfPawnVerbUtilityGetManager = AccessTools.MethodDelegate<GetManager>(AccessTools.Method(type, "Manager"));

        type = AccessTools.TypeByName("MVCF.VerbManager");
        MP.RegisterSyncWorker<object>(SyncVerbManager, type, true);
        mvcfVerbManagerPawnGetter = MethodInvoker.GetHandler(AccessTools.PropertyGetter(type, "Pawn"));
        mvcfVerbManagerTickMethod = MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "Tick"));
        mvcfVerbManagerVerbsField = AccessTools.FieldRefAccess<IList>(type, "verbs");

        type = typeof(System.WeakReference<>).MakeGenericType(type);
        mvcfWeakReferenceTryGetVerbManagerMethod =
            MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "TryGetTarget"));

        type = AccessTools.TypeByName("MVCF.ManagedVerb");
        mvcfManagedVerbManagerGetter = MethodInvoker.GetHandler(AccessTools.PropertyGetter(type, "Manager"));
        MP.RegisterSyncWorker<object>(SyncManagedVerb, type, true);
        // Seems like selecting the Thing that holds the verb inits some stuff, so we need to set the context
        MP.RegisterSyncMethod(type, "Toggle");

        type = AccessTools.TypeByName("MVCF.VerbWithComps");
        mvcfVerbWithCompsField = AccessTools.FieldRefAccess<IList>(type, "comps");

        type = AccessTools.TypeByName("MVCF.VerbComps.VerbComp");
        mvcfVerbCompParentField = AccessTools.FieldRefAccess<object>(type, "parent");
        MP.RegisterSyncWorker<object>(SyncVerbComp, type, true);

        type = AccessTools.TypeByName("MVCF.VerbComps.VerbComp_Switch");
        // Switch used verb
        MP.RegisterSyncMethod(type, "Enable");

        type = AccessTools.TypeByName("MVCF.Reloading.Comps.VerbComp_Reloadable_ChangeableAmmo");
        var innerMethod = MpMethodUtil.GetLambda(type, "AmmoOptions", MethodType.Getter, null, 1);
        MP.RegisterSyncDelegate(type, innerMethod.DeclaringType.Name, innerMethod.Name);

        type = AccessTools.TypeByName("MVCF.PatchSets.PatchSet_HumanoidGizmos");
        MpCompat.RegisterLambdaDelegate(type, "GetGizmos_Postfix", 3); // Toggle fire at will
        MpCompat.RegisterLambdaDelegate(type, "GetAttackGizmos_Postfix", 4); // Interrupt Attack

        MpCompat.RegisterLambdaDelegate("MVCF.PatchSets.PatchSet_Animals", "Pawn_GetGizmos_Postfix",
            0); // Also interrupt Attack

        // Changes the verb, so when called before syncing (especially if the original method is canceled by another mod) - will cause issues.
        PatchingUtilities.PatchCancelInInterfaceSetResultToTrue(
            "MVCF.PatchSets.PatchSet_MultiVerb:Prefix_OrderForceTarget");

        // Verb ticking
        type = AccessTools.TypeByName("MVCF.WorldComponent_MVCF");
        mvcfWorldCompInstanceField =
            AccessTools.StaticFieldRefAccess<WorldComponent>(AccessTools.DeclaredField(type, "Instance"));
        mvcfWorldCompTickManagersField = AccessTools.FieldRefAccess<IList>(type, "TickManagers");

        MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, nameof(WorldComponent.WorldComponentTick)),
            transpiler: new HarmonyMethod(typeof(VanillaExpandedFramework), nameof(ReplaceTickWithConditionalTick)));
        MpCompat.harmony.Patch(
            AccessTools.DeclaredMethod(typeof(MapComponentUtility), nameof(MapComponentUtility.MapComponentTick)),
            postfix: new HarmonyMethod(typeof(VanillaExpandedFramework), nameof(TickVerbManagersForMap)));
    }

    // Initialize the VerbManager early, we expect it to exist on every player.
    private static void EverybodyGetsVerbManager(Pawn __instance)
    {
        // No point in doing this out of MP
        if (!MP.IsInMultiplayer)
            return;

        // In the unlikely case the feature set we got is null, we'll let it run anyway just in case.
        if (mvcfEnabledFeaturesSet == null)
            try
            {
                mvcfPawnVerbUtilityGetManager(__instance, true);
            }
            catch (NullReferenceException)
            {
                // Ignored
            }
        // If none of the features is enabled, there's not really any point in using the managers.
        else if (mvcfEnabledFeaturesSet.Any())
            mvcfPawnVerbUtilityGetManager(__instance, true);
    }

    private static void SyncVerbManager(SyncWorker sync, ref object obj)
    {
        if (sync.isWriting)
            // Sync the pawn that has the VerbManager
        {
            sync.Write((Pawn)mvcfVerbManagerPawnGetter(obj));
        }
        else
        {
            var pawn = sync.Read<Pawn>();

            // Either try getting the VerbManager from the comp, or create it if it's missing
            obj = mvcfPawnVerbUtilityGetManager(pawn, true);
            if (obj == null)
                throw new Exception($"MpCompat :: VerbManager of {pawn} isn't initialized! NO WAY!");
        }
    }

    private static void SyncManagedVerb(SyncWorker sync, ref object obj)
    {
        if (sync.isWriting)
        {
            // Get the VerbManager from inside of the ManagedVerb itself
            var verbManager = mvcfManagedVerbManagerGetter(obj);
            // Find the ManagedVerb inside of list of all verbs
            var managedVerbsList = mvcfVerbManagerVerbsField(verbManager);
            var index = managedVerbsList.IndexOf(obj);

            // Sync the index of the verb as well as the manager (if it's valid)
            sync.Write(index);
            if (index >= 0)
                SyncVerbManager(sync, ref verbManager);
        }
        else
        {
            // Read and check if the index is valid
            var index = sync.Read<int>();

            if (index >= 0)
            {
                // Read the verb manager
                object verbManager = null;
                SyncVerbManager(sync, ref verbManager);

                // Find the ManagedVerb with specific index inside of list of all verbs
                var managedVerbsList = mvcfVerbManagerVerbsField(verbManager);
                obj = managedVerbsList[index];
            }
        }
    }

    private static void SyncVerbComp(SyncWorker sync, ref object verbComp)
    {
        object verb = null;

        if (sync.isWriting)
        {
            verb = mvcfVerbCompParentField(verbComp);
            var index = mvcfVerbWithCompsField(verb).IndexOf(verbComp);

            SyncManagedVerb(sync, ref verb);
            sync.Write(index);
        }
        else
        {
            SyncManagedVerb(sync, ref verb);
            var index = sync.Read<int>();

            if (index >= 0) verbComp = mvcfVerbWithCompsField(verb)[index];
        }
    }

    private static void TickVerbManagersForMap(Map map)
    {
        // Map-specific ticking is only enabled in MP with async on.
        if (!MP.IsInMultiplayer || !PatchingUtilities.IsAsyncTime)
            return;

        var managers = mvcfWorldCompTickManagersField(mvcfWorldCompInstanceField());
        // Null or empty check
        if (managers is not { Count: > 0 })
            return;

        // out parameter
        var args = new object[1];
        foreach (var weakRef in managers)
            if ((bool)mvcfWeakReferenceTryGetVerbManagerMethod(weakRef, args))
            {
                var manager = args[0];
                if (mvcfVerbManagerPawnGetter(manager) is Pawn pawn && pawn.MapHeld == map)
                    mvcfVerbManagerTickMethod(manager);
            }
    }

    private static void TickOnlyNonMapManagers(object manager)
    {
        // Normal ticking (tied to world). Only do if not in MP, async is off,
        // the pawn is null (shouldn't happen?) or the pawn has no map.
        if (!MP.IsInMultiplayer ||
            !PatchingUtilities.IsAsyncTime ||
            mvcfVerbManagerPawnGetter(manager) is not Pawn pawn ||
            pawn.MapHeld == null)
            mvcfVerbManagerTickMethod(manager);
    }

    private static IEnumerable<CodeInstruction> ReplaceTickWithConditionalTick(IEnumerable<CodeInstruction> instr,
        MethodBase baseMethod)
    {
        var target = AccessTools.DeclaredMethod("MVCF.VerbManager:Tick", Type.EmptyTypes);
        var replacement = AccessTools.DeclaredMethod(typeof(VanillaExpandedFramework), nameof(TickOnlyNonMapManagers));
        var replacedCount = 0;

        foreach (var ci in instr)
        {
            if (ci.Calls(target))
            {
                ci.opcode = OpCodes.Call;
                ci.operand = replacement;

                replacedCount++;
            }

            yield return ci;
        }

        const int expected = 1;
        if (replacedCount != expected)
        {
            var name = (baseMethod.DeclaringType?.Namespace).NullOrEmpty()
                ? baseMethod.Name
                : $"{baseMethod.DeclaringType!.Name}:{baseMethod.Name}";
            Log.Warning(
                $"{LogPrefix} Patched incorrect number of VerbManager.Tick calls (patched {replacedCount}, expected {expected}) for method {name}");
        }
    }

    #endregion
}