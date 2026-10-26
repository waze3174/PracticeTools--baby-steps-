using Il2Cpp;
using UnityEngine;

namespace PracticeTools;

// auto skip: as a cutscene starts, jump it to its end the way the game does when you win the skip minigame
// (Menu.SkipCutscene), so the minigame never shows. the hat scenes (BBitsyBoss) and the ending (the map with your
// path and the credits) play as normal
static class Skipper
{
    static GameObject bitsy;
    static float started = -1f, tried;

    internal static void Update()
    {
        var m = Menu.me;
        if (!Core.AutoSkip.Value || m == null || !m.inNonInteractiveCutscene || m.playingCutscene == null) { started = -1f; return; }
        if (bitsy == null) bitsy = GameObject.Find("BigManagerPrefab/Menu/BBitsyBoss");
        if (bitsy != null && bitsy.activeInHierarchy) return;
        var c = Splits.Credits;
        if (c != null && c.started && !c.done) return;
        // give the cutscene a moment to start, then skip; again every half second if it's still going
        if (started < 0f) started = Time.unscaledTime;
        if (Time.unscaledTime - started < 0.2f || Time.unscaledTime - tried < 0.5f) return;
        tried = Time.unscaledTime;
        m.SkipCutscene();
    }
}
