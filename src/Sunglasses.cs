using HarmonyLib;
using Il2Cpp;

namespace PracticeTools;

// the sunglasses hat shows things that are only there through sunglasses (the game's SunglassesChecker turns their
// renderers on while nate wears it). F6 / B + d-pad down shows them without the hat, and again goes back to the game's
// own rule. replaces SunglassessToggle
static class Sunglasses
{
    internal static bool Forced;

    internal static void Update()
    {
        if (!Binds.Pressed(Binds.Sunglasses)) return;
        Forced = !Forced;
        Hud.Flash(Forced ? "SUNGLASSES ON" : "SUNGLASSES OFF");
    }

    [HarmonyPatch(typeof(SunglassesChecker), nameof(SunglassesChecker.Update))]
    static class CheckerPatch
    {
        static void Postfix(SunglassesChecker __instance)
        {
            if (Forced && !__instance.on) __instance.SetOn(true);
        }
    }
}
