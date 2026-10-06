using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Compat;
using Verse;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

internal partial class VanillaExpandedFramework
{
    #region Door Teleporter

    // Dialog_RenameDoorTeleporter
    private static Type renameDoorTeleporterDialogType;
    private static ConstructorInfo renameDoorTeleporterDialogConstructor;
    private static AccessTools.FieldRef<object, ThingWithComps> renameDoorTeleporterDialogThingField;

    private static void PatchDoorTeleporter()
    {
        var type = AccessTools.TypeByName("VEF.Buildings.DoorTeleporter");
        // Destroy
        MpCompat.RegisterLambdaMethod(type, "GetDoorTeleporterGismoz", 0).SetContext(SyncContext.None);
        // Teleport to x
        MpCompat.RegisterLambdaDelegate(type, nameof(ThingWithComps.GetFloatMenuOptions), 0)[0]
            .TransformField("doorTeleporter", Serializer.New<Thing, int>(
                    t => t.thingIDNumber,
                    id =>
                    {
                        MP.TryGetThingById(id, out var thing);
                        return thing;
                    }),
                true);

        renameDoorTeleporterDialogType = AccessTools.TypeByName("VEF.Buildings.Dialog_RenameDoorTeleporter");
        renameDoorTeleporterDialogConstructor = AccessTools.DeclaredConstructor(renameDoorTeleporterDialogType, [type]);
        renameDoorTeleporterDialogThingField =
            AccessTools.FieldRefAccess<ThingWithComps>(renameDoorTeleporterDialogType, "DoorTeleporter");

        PatchingUtilities.PatchPushPopRand(renameDoorTeleporterDialogConstructor);
    }

    #endregion
}