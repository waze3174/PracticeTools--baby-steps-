using System.Linq;
using MelonLoader;
using UnityEngine;

namespace PracticeTools;

// mods whose features are in here: with both installed they'd double up (two fly cams on F1, two timers, two skippers,
// two sunglasses toggles, two blinkers).
// once every mod has started, they're switched off inside MelonLoader for this session; their files stay as they are,
// so removing PracticeTools brings them back
static class Takeover
{
    static readonly string[] Names = { "FlyAndQuickSave", "BBSIGT", "CutsceneSkipperBBS", "SunglassessToggle", "SunglassesToggle", "BBSItemBlinker" };

    internal static void Run(MelonLogger.Instance log)
    {
        foreach (var m in MelonMod.RegisteredMelons.ToArray())
        {
            if (m == Core.Inst || !Names.Contains(m.Info.Name)) continue;
            try
            {
                m.Unregister("replaced by PracticeTools", true);
                log.Msg($"{m.Info.Name} {m.Info.Version} is switched off while PracticeTools is installed (its features are in here)");
            }
            catch (System.Exception e) { log.Warning($"couldn't switch off {m.Info.Name}: {e.Message}"); }
        }
        // FlyAndQuicksave patches the game's key reads with its own Harmony id (Unregister only undoes the melon's own
        // instance) and keeps its helpers on a GameObject
        HarmonyLib.Harmony.UnpatchID("BabyStepsQuickSave.InputOverride");
        var go = GameObject.Find("DevCheatChaperone_Mod");
        if (go != null) Object.Destroy(go);
    }
}
