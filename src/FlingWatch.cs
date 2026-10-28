using System.Globalization;
using Il2Cpp;
using UnityEngine;

namespace PracticeTools;

// the water glitch's fling, measured each physics step, for the info corner. from my recordings: the water's push
// is sideways only, and lasts only while some of nate is still in water (it stops the step he's out). so a fling is
// as strong as the water ahead of it is long, and it goes where the stick points from the camera's facing.
// shown: how much of nate is in water and the water's push now, which way he moves, and the last fling (fastest
// speed, steps pushed, metres over water, height). each fling is also logged with where it started from
static class FlingWatch
{
    internal static int Parts;        // nate's parts in water now
    internal static bool Flinging => on;
    internal static float Push;       // the water's force on him now, all parts together
    internal static string Last;      // the last fling, one line
    internal static Vector2 Moving;   // nate's hips over the ground (x, z), m/s, smoothed

    const float Start = 40f, Slow = 20f;   // m/s: a fling starts above, ends after half a second below
    const float Jump = 100f;               // metres up or down in one step: the game putting him back up (YLoopCheck:
                                           // +300 over the ground or +2000), x / z kept. not sideways: on the ocean
                                           // past the map's edge he went up to 230 m a step. loads reset
    const float Pushing = 10000f;          // the water's force while it flings (a few hundred to 5000 when swimming)

    static Vector3 prev;
    static bool hasPrev, on;
    static Vector3 from, wetAt;
    static float peak, top, putUp, startYaw, stickAim;
    static int steps, pushed, slow;
    static string setup;

    internal static void Reset() { hasPrev = false; on = false; Moving = Vector2.zero; }

    internal static void Step()
    {
        var pm = PlayerMovement.me;
        var m = Menu.me;
        if (pm == null || pm.hipPivot == null) { Parts = 0; Push = 0f; Reset(); return; }
        Water(pm);
        if (Fly.Carrying || pm.inFlyCam || pm.inCutscene || (m != null && (m.teleporting || m.paused || m.inDream || m.inCutscene || m.inNonInteractiveCutscene))
            || Time.timeScale == 0f) { Reset(); return; }   // a cutscene puts nate places (the campfire's sit down read as a 298 m/s fling)

        var p = pm.hipPivot.position;
        if (!hasPrev) { prev = p; hasPrev = true; return; }
        var d = p - prev;
        prev = p;
        float dt = Time.fixedDeltaTime;
        bool jump = Mathf.Abs(d.y) > Jump;
        float v = jump ? 0f : d.magnitude / dt;
        if (!jump) Moving = Vector2.Lerp(Moving, new Vector2(d.x, d.z) / dt, 0.05f);   // ~1/3 s, so his sway doesn't swing it

        if (!on)
        {
            if (jump || v < Start) return;
            on = true;
            from = wetAt = p - d;
            peak = 0f; top = from.y; putUp = 0f; steps = pushed = slow = 0;
            CamAngle.Now(out startYaw, out _);
            float sx = Binds.Axis("Left Stick X"), sy = Binds.Axis("Left Stick Y");
            stickAim = sx * sx + sy * sy > 0.04f ? Mathf.Repeat(startYaw + Mathf.Atan2(sx, sy) * Mathf.Rad2Deg, 360f) : -1f;
            setup = string.Format(CultureInfo.InvariantCulture, "from {0:0.0} {1:0.0} {2:0.0}, camera {3:0.0}, stick {4:0.00} {5:0.00} ({6}), {7} shoulder",
                from.x, from.y, from.z, startYaw, sx, sy, stickAim < 0f ? "none" : Hud.Point16(stickAim) + " " + stickAim.ToString("0", CultureInfo.InvariantCulture),
                pm.camRight ? "left" : "right");
        }
        steps++;
        // the game putting him back up (nate flung into the ground): counted, the fling goes on from there
        if (jump) putUp += d.y;
        else
        {
            peak = Mathf.Max(peak, v);
            top = Mathf.Max(top, p.y - putUp);
            if (Push > Pushing) pushed++;
            if (Parts > 0) wetAt = p;
        }
        slow = v < Slow && !jump ? slow + 1 : 0;
        if (slow >= 30 || steps > 600) End(p);
    }

    static void End(Vector3 p)
    {
        on = false;
        var flat = new Vector2(wetAt.x - from.x, wetAt.z - from.z);
        var all = new Vector2(p.x - from.x, p.z - from.z);
        float heading = Mathf.Repeat(Mathf.Atan2(all.x, all.y) * Mathf.Rad2Deg, 360f);
        Last = string.Format(CultureInfo.InvariantCulture, "fling {0} {1:0}: top {2:0} m/s, pushed {3} steps over {4:0} m, {5:+0;-0} m up{6}",
            Hud.Point16(heading), heading, peak, pushed, flat.magnitude, top - from.y,
            Mathf.Abs(putUp) > 1f ? string.Format(CultureInfo.InvariantCulture, ", put {0:+0;-0} m", putUp) : "");
        Core.Inst.LoggerInstance.Msg($"{Last}, {all.magnitude:0} m in all. {setup}");
    }

    static void Water(PlayerMovement pm)
    {
        Parts = 0;
        var sum = Vector3.zero;
        var wos = pm.waterObjects;
        if (wos != null)
            foreach (var wo in wos)
            {
                if (wo == null) continue;
                if (wo.submergedVolume != 0f) Parts++;
                sum += wo.ResultForce;
            }
        Push = sum.magnitude;
    }
}
