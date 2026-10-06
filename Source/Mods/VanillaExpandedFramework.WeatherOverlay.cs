using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Compat;
using Verse;

namespace MultiplayerVanillaExpandedFrameworkPatch.Source.Mods;

internal partial class VanillaExpandedFramework
{
    #region Patch Weather Overlay Effects

    private static Type weatherOverlayEffectsType;
    private static AccessTools.FieldRef<SkyOverlay, int> weatherOverlayEffectsNextDamageTickField;

    private static AccessTools.FieldRef<SkyOverlay, Dictionary<Map, int>>
        weatherOverlayEffectsNextDamageTickForMapField;

    private static void PatchWeatherOverlayEffects()
    {
        // It'll likely have issues with async time, as there's only 1 timer for all maps.
        weatherOverlayEffectsType = AccessTools.TypeByName("VEF.Weathers.WeatherOverlay_Effects");
        var nextDamageTickField = AccessTools.DeclaredField(weatherOverlayEffectsType, "nextDamageTick");
        var nextDamageTickForMapField = AccessTools.DeclaredField(weatherOverlayEffectsType, "nextDamageTickForMap");

        if (nextDamageTickForMapField != null)
        {
            weatherOverlayEffectsNextDamageTickForMapField =
                AccessTools.FieldRefAccess<SkyOverlay, Dictionary<Map, int>>(nextDamageTickForMapField);
        }
        else if (nextDamageTickField != null)
        {
            weatherOverlayEffectsNextDamageTickField = AccessTools.FieldRefAccess<SkyOverlay, int>(nextDamageTickField);
        }
        else
        {
            Log.Error($"{LogPrefix} VEF.Weathers.WeatherOverlay_Effects:nextDamageTick field, patch failed.");
            return;
        }

        MpCompat.harmony.Patch(
            AccessTools.DeclaredMethod(typeof(GameComponentUtility), nameof(GameComponentUtility.FinalizeInit)),
            postfix: new HarmonyMethod(typeof(VanillaExpandedFramework), nameof(RefreshWeatherOverlayEffectCache)));
    }

    private static void RefreshWeatherOverlayEffectCache()
    {
        if (!MP.IsInMultiplayer)
            return;

        foreach (var def in DefDatabase<WeatherDef>.AllDefsListForReading)
        {
            if (def.Worker.overlays == null)
                continue;

            foreach (var overlay in def.Worker.overlays)
                if (weatherOverlayEffectsType.IsInstanceOfType(overlay))
                {
                    if (weatherOverlayEffectsNextDamageTickForMapField != null)
                        weatherOverlayEffectsNextDamageTickForMapField(overlay).Clear();
                    else
                        weatherOverlayEffectsNextDamageTickField(overlay) = 0;
                }
        }
    }

    #endregion
}