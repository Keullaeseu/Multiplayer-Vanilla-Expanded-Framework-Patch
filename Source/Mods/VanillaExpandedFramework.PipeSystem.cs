using System.Collections;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Compat;
using RimWorld;
using Verse;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

internal partial class VanillaExpandedFramework
{
    #region Pipe System

    // Deconstruct
    private static Type deconstructPipeDesignatorType;

    private static AccessTools.FieldRef<Designator_Deconstruct, Def> deconstructPipeDesignatorNetDefField;

    // Pipe net manager
    private static Type pipeNetManagerType;

    private static AccessTools.FieldRef<MapComponent, IList> pipeNetManagerPipeNetsListField;

    // Pipe net
    private static AccessTools.FieldRef<object, Map> pipeNetMapField;
    private static AccessTools.FieldRef<object, Def> pipeNetDefField;

    private static void PatchPipeSystem()
    {
        // Increase/decrease by 1/10
        MpCompat.RegisterLambdaMethod("PipeSystem.CompConvertToThing", "PostSpawnSetup", 0, 1, 2, 3);
        // (Dev) trigger countdown
        MpCompat.RegisterLambdaMethod("PipeSystem.CompExplosiveContent", "CompGetGizmosExtra", 0).SetDebugOnly();
        // Choose output
        MpCompat.RegisterLambdaMethod("PipeSystem.CompResourceProcessor", "PostSpawnSetup", 1);
        // Extract resource (0), toggle allow manual refill (2), transfer to other containers (3)
        MpCompat.RegisterLambdaMethod("PipeSystem.CompResourceStorage", "PostSpawnSetup", 0, 2, 3);
        // (Dev) fill/add 5/empty
        MpCompat.RegisterLambdaMethod("PipeSystem.CompResourceStorage", "CompGetGizmosExtra", 0, 1, 2).SetDebugOnly();
        // Spawn resource now
        MpCompat.RegisterLambdaMethod("PipeSystem.CompSpawnerOrNet", "CompGetGizmosExtra", 0).SetDebugOnly();

        // Designator
        var type = deconstructPipeDesignatorType = AccessTools.TypeByName("PipeSystem.Designator_DeconstructPipe");
        deconstructPipeDesignatorNetDefField = AccessTools.FieldRefAccess<Def>(type, "pipeNetDef");
        MP.RegisterSyncWorker<Designator_Deconstruct>(SyncDeconstructPipeDesignator, type);

        // Pipe net
        type = pipeNetManagerType = AccessTools.TypeByName("PipeSystem.PipeNetManager");
        pipeNetManagerPipeNetsListField = AccessTools.FieldRefAccess<IList>(type, "pipeNets");

        type = AccessTools.TypeByName("PipeSystem.PipeNet");
        pipeNetMapField = AccessTools.FieldRefAccess<Map>(type, "map");
        pipeNetDefField = AccessTools.FieldRefAccess<Def>(type, "def");
        MP.RegisterSyncWorker<object>(SyncPipeNet, type, true);
    }

    private static void SyncDeconstructPipeDesignator(SyncWorker sync, ref Designator_Deconstruct designator)
    {
        if (sync.isWriting)
            sync.Write(deconstructPipeDesignatorNetDefField(designator));
        else
            designator =
                (Designator_Deconstruct)Activator.CreateInstance(deconstructPipeDesignatorType, sync.Read<Def>());
    }

    private static void SyncPipeNet(SyncWorker sync, ref object pipeNet)
    {
        if (sync.isWriting)
        {
            // Sync null net as -1
            if (pipeNet == null)
            {
                sync.Write(-1);
                return;
            }

            // No map, can't get manager - log error and treat as null
            var map = pipeNetMapField(pipeNet);
            if (map == null)
            {
                Log.Error($"{LogPrefix} Trying to sync a PipeNet with a null map. PipeNet={pipeNet}");
                sync.Write(-1);
                return;
            }

            // No manager for map, shouldn't happen - log error and treat as null
            var manager = map.GetComponent(pipeNetManagerType);
            if (manager == null)
            {
                Log.Error(
                    $"{LogPrefix} Trying to sync a PipeNet with a map that doesn't have PipeNetManager. PipeNet={pipeNet}, Map={map}");
                sync.Write(-1);
                return;
            }

            var def = pipeNetDefField(pipeNet);
            var list = pipeNetManagerPipeNetsListField(manager);
            var index = -1;
            var found = false;

            foreach (var currentPipeNet in list)
                if (def == pipeNetDefField(currentPipeNet))
                {
                    index++;
                    if (pipeNet == currentPipeNet)
                    {
                        found = true;
                        break;
                    }
                }

            if (!found)
            {
                // We did not find the pipe net - log error and treat as null
                Log.Error(
                    $"{LogPrefix} Trying to sync a PipeNet, but it's not held by the manager. PipeNet={pipeNet}, map={map}, manager={manager}");
                sync.Write(-1);
            }
            else
            {
                sync.Write(index);
                sync.Write(def);
                sync.Write(manager);
            }
        }
        else
        {
            var index = sync.Read<int>();
            // If negative - it's null
            if (index < 0)
                return;

            var def = sync.Read<Def>();
            var manager = sync.Read<MapComponent>();
            var list = pipeNetManagerPipeNetsListField(manager);
            var currentIndex = 0;

            foreach (var currentPipeNet in list)
                if (def == pipeNetDefField(currentPipeNet))
                {
                    if (currentIndex == index)
                    {
                        pipeNet = currentPipeNet;
                        break;
                    }

                    currentIndex++;
                }
        }
    }

    #endregion
}