using HarmonyLib;
using Multiplayer.API;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

internal partial class VanillaExpandedFramework
{
    #region Moving Bases

    private static void PatchMovingBases()
    {
        var type = AccessTools.TypeByName("VEF.Planet.MovingBase");
        MP.RegisterSyncMethod(type, "Attack");

        // Upstream VEF now initializes AttackCommand as non-null static readonly,
        // so the temporary gizmo graphic fix is no longer needed.
    }

    #endregion
}