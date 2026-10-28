using System;
using System.Text;
using Il2Cpp;
using Il2CppCinemachine;
using UnityEngine;

namespace PracticeTools;

// the game camera sometimes stops after a far load. PlayerMovement.gameCam is a Cinemachine virtual camera: the game
// moves it every frame (PlaceCamera), and Cinemachine's brain (Menu.brain) moves the real camera (PlayerMovement.camTrans)
// to whichever virtual camera is live. stuck, the virtual one keeps following nate and the real one doesn't move. when
// the real camera has stood still for a second while the virtual one moved, this logs once what Cinemachine is doing
// (to find the cause), then brings it back: the game camera and the brain switched on if they're off, the game
// camera made the live one if another took over, the real camera put on it if the brain still doesn't move it
static class CamWatch
{
    // ---- the cause (the brain was on, GameCam live, no blend, and still nothing moved) ----
    // Cinemachine's brain doesn't update itself here (its LateUpdate returns while playing): BestRegionLoader's
    // EarliestPossibleUpdate calls brain.ManualUpdate, once a frame, from PlayerMovement.EarlyUpdate or Menu.LateUpdate,
    // only while RegionLoaderOnPreCullHelper.firedThisFrame is false. that flag is set by the call and cleared only
    // near the end of the loader's pre-cull work (the helper's OnPreCull on the camera, after every chunk / prop / ground
    // cover load step and the prop drawing). if that work throws before it, or the helper's camera stops rendering, the
    // flag stays set: no brain update ever again (game camera and fly camera alike), and no reload clears it.
    // when the update hasn't run for 30 frames, this logs why (the helpers and their cameras, the loader's own pre-cull
    // called once to see if it throws) and from then clears the flag each frame the helper didn't, running the
    // loader's pre-cull itself too if that works, until the helper does it on its own again for a second.
    // the hat dream (Menu.CampFireCo: inDream, BBitsyBoss) switches the brain's camera off on purpose for its whole
    // length, so the flag stays set and the world's loading and camera wait: left alone then (helping in it got me
    // stuck on the dream's screen), and whenever else the game has that camera off
    static int lastEarliest, healthy;
    static bool helping, preCullRuns, toldLoader;

    internal static void Install(HarmonyLib.Harmony h)
    {
        h.Patch(HarmonyLib.AccessTools.Method(typeof(BestRegionLoader), nameof(BestRegionLoader.EarliestPossibleUpdate)),
            prefix: new HarmonyLib.HarmonyMethod(typeof(CamWatch), nameof(Ran)));
        h.Patch(HarmonyLib.AccessTools.Method(typeof(BestRegionLoader), nameof(BestRegionLoader.UpdateLODLoadStatus)),
            prefix: new HarmonyLib.HarmonyMethod(typeof(CamWatch), nameof(LodGuard)));
    }

    static void Ran() => lastEarliest = Time.frameCount;

    // ---- what throws: BestRegionLoader.UpdateLODLoadStatus, a NullReferenceException ----
    // in the loader's pre-cull, before the flag is cleared. for each prop whose visuals just finished loading
    // (lodNeedsUpdating, lodLoadState 1) it reads the loaded GabeLOD: bounds, levels (materials of each), screen
    // sizes, transforms. a load that died (far loads) leaves it null or missing some of those,
    // and it throws at that same prop every frame. such a prop goes back to unloaded (0), so the loader loads it again
    static int lodFixes;

    static void LodGuard(BestRegionLoader __instance)
    {
        try
        {
            var need = __instance.lodNeedsUpdating;
            var lods = __instance.loadedLODs;
            if (need == null || lods == null || !Fly.Native(__instance, 0x1D0, out var st, out int sn)) return;
            int n = Math.Min(__instance.propCount, Math.Min(need.Length, Math.Min(lods.Length, sn)));
            for (int i = 0; i < n; i++)
            {
                if (!need[i] || System.Runtime.InteropServices.Marshal.ReadInt32(st, i * 4) != 1) continue;
                string bad = Broken(lods[i]);
                if (bad == null) continue;
                lods[i] = null;
                need[i] = false;
                System.Runtime.InteropServices.Marshal.WriteInt32(st, i * 4, 0);
                if (++lodFixes <= 10 || lodFixes % 100 == 0)
                    DebugLog.Write($"camera: prop visual #{i} finished loading with {bad} (the area loader would throw on it and stop the camera): loads again ({lodFixes} so far)");
            }
        }
        catch (Exception e) { Core.Inst.LoggerInstance.Warning("camera: prop visuals check: " + e.Message); }
    }

    // what UpdateLODLoadStatus would find missing, or null when it's all there
    static string Broken(GabeLOD g)
    {
        if (g == null) return "nothing loaded";
        var lv = g.lods;
        var sp = g.screenPercents;
        var tr = g.lodTransforms;
        if (lv == null || lv.Length == 0) return "no levels";
        if (sp == null) return "no screen sizes";
        if (tr == null || tr.Length < sp.Length) return "too few transforms";
        if (lv.Length < sp.Length) return "too few levels";
        for (int k = 0; k < sp.Length; k++)
            if (lv[k].mats == null) return $"no materials on level {k}";
        return null;
    }

    internal static void Loader()
    {
        var m = Menu.me;
        var rl = BestRegionLoader.me;
        if (m == null || rl == null || !m.inited || m.paused || m.inDream || m.restartingScene || !CamOn(m))
        {
            if (helping) { helping = false; DebugLog.Write("camera: the game's camera is off or paused (menu, load, hat dream): PracticeTools leaves the area loader alone"); }
            lastEarliest = Time.frameCount;
            return;
        }
        bool ranAlready = lastEarliest == Time.frameCount;   // EarlyUpdate ran it: the helper cleared the flag last frame
        if (!helping)
        {
            if (Time.frameCount - lastEarliest < 30) return;
            helping = true; healthy = 0;
            var log = Core.Inst.LoggerInstance;
            DebugLog.Write($"camera: the area loader's frame update (which moves the camera) hasn't run for {Time.frameCount - lastEarliest} frames: its pre-cull never cleared RegionLoaderOnPreCullHelper.firedThisFrame. {Helpers(rl)}");
            try { rl.OnPreCull(); preCullRuns = true; DebugLog.Write("camera: the loader's pre-cull runs fine when PracticeTools calls it (so the helper's camera isn't rendering): PracticeTools calls it each frame until the helper does again"); }
            catch (Exception e) { preCullRuns = false; if (!toldLoader) { toldLoader = true; log.Warning("camera: the loader's pre-cull throws (the area loads after the error don't run either): " + e); } }
        }
        else if (ranAlready)
        {
            if (++healthy >= 60) { helping = false; DebugLog.Write("camera: the area loader's pre-cull runs on its own again"); }
            return;
        }
        else healthy = 0;
        if (preCullRuns) try { rl.OnPreCull(); } catch { preCullRuns = false; }
        RegionLoaderOnPreCullHelper.firedThisFrame = false;   // Menu.LateUpdate runs the frame update (and the brain) now
    }

    // the camera the brain drives (the one with the pre-cull helper) is on: off means the game turned it off itself
    static bool CamOn(Menu m)
    {
        try
        {
            var cam = m.brain != null ? m.brain.OutputCamera : null;
            return cam != null && cam.enabled && cam.gameObject.activeInHierarchy;
        }
        catch { return false; }
    }

    static string Helpers(BestRegionLoader rl)
    {
        var sb = new StringBuilder();
        try
        {
            sb.Append($"loader off {rl.off}, fully loaded {BestRegionLoader.fullyLoaded}, flag {RegionLoaderOnPreCullHelper.firedThisFrame}. pre-cull helpers:");
            int n = 0;
            foreach (var hp in Resources.FindObjectsOfTypeAll<RegionLoaderOnPreCullHelper>())
            {
                if (hp == null || !hp.gameObject.scene.IsValid()) continue;
                var cam = hp.GetComponent<Camera>();
                sb.Append($" {hp.name} (on {hp.enabled}, active {hp.gameObject.activeInHierarchy}, camera {(cam == null ? "none" : $"on {cam.enabled}, main {cam == Camera.main}")})");
                n++;
            }
            if (n == 0) sb.Append(" none");
        }
        catch (Exception e) { sb.Append(" (reading: " + e.Message + ")"); }
        return sb.ToString();
    }

    static Vector3 lastReal, lastVirtual;
    static float stillSince = -1f, moved;
    static bool told, forcing;

    internal static void Update()
    {
        var pm = PlayerMovement.me;
        var m = Menu.me;
        if (pm == null || m == null || pm.gameCam == null || pm.camTrans == null) { Reset(); return; }
        if (m.paused || m.teleporting || m.inNonInteractiveCutscene || pm.inCutscene || pm.inFirstPerson || pm.inFlyCam
            || Fly.Carrying || (pm.dead && pm.dontFollowDead) || Time.timeScale == 0f)
        { Reset(); return; }

        var real = pm.camTrans.position;
        var vcam = pm.gameCam.transform;
        if (forcing) return;   // LateUpdate

        if ((real - lastReal).sqrMagnitude > 1e-8f) { stillSince = -1f; moved = 0f; }
        else
        {
            if (stillSince < 0f) stillSince = Time.unscaledTime;
            moved += (vcam.position - lastVirtual).magnitude;
            if (Time.unscaledTime - stillSince > 1f && moved > 0.3f && (vcam.position - real).sqrMagnitude > 0.01f)
            {
                if (!told) { told = true; DebugLog.Write(Describe(pm, m, Time.unscaledTime - stillSince)); }
                Fix(pm, m);
                stillSince = -1f; moved = 0f;
            }
        }
        lastReal = pm.camTrans.position;
        lastVirtual = vcam.position;
    }

    // after the game's own camera work (Cinemachine's brain runs in LateUpdate too, ours may come before or after it)
    internal static void LateUpdate()
    {
        if (!forcing) return;
        var pm = PlayerMovement.me;
        if (pm == null || pm.gameCam == null || pm.camTrans == null) return;
        var vcam = pm.gameCam.transform;
        pm.camTrans.SetPositionAndRotation(vcam.position, vcam.rotation);
    }

    static string Describe(PlayerMovement pm, Menu m, float still)
    {
        var sb = new StringBuilder();
        sb.Append($"camera: stuck {still:0.0} s, the game's virtual camera moved on. real camera {pm.camTrans.name} {pm.camTrans.position}, game camera {pm.gameCam.transform.position}");
        try
        {
            sb.Append($"\n  game camera: on {pm.gameCam.enabled}, active {pm.gameCam.gameObject.activeInHierarchy}, priority {pm.gameCam.Priority}");
            var b = m.brain;
            if (b == null) sb.Append("\n  no brain");
            else
            {
                sb.Append($"\n  brain: on {b.enabled}, active {b.gameObject.activeInHierarchy}, on camera {b.name}, live {Name(b.ActiveVirtualCamera)}, blending {b.IsBlending}");
                if (b.IsBlending && b.ActiveBlend != null) sb.Append($" ({b.ActiveBlend.Description})");
                sb.Append($", ignores time scale {b.m_IgnoreTimeScale}");
            }
            var core = CinemachineCore.Instance;
            if (core != null)
            {
                sb.Append($"\n  virtual cameras: {core.VirtualCameraCount}, solo {Name(CinemachineBrain.SoloCamera)}");
                for (int i = 0; i < core.VirtualCameraCount && i < 12; i++)
                {
                    var v = core.GetVirtualCamera(i);
                    if (v != null) sb.Append($"\n    {v.name}: priority {v.Priority}, on {v.enabled}, active {v.gameObject.activeInHierarchy}");
                }
            }
            sb.Append($"\n  Camera.main {(Camera.main != null ? Camera.main.name : "none")}, time scale {Time.timeScale}");
        }
        catch (Exception e) { sb.Append("\n  (reading Cinemachine: " + e.Message + ")"); }
        return sb.ToString();
    }

    static string Name(ICinemachineCamera c)
    {
        try { return c == null ? "none" : c.Name; } catch { return "?"; }
    }

    static void Fix(PlayerMovement pm, Menu m)
    {
        var log = Core.Inst.LoggerInstance;
        try
        {
            if (!pm.gameCam.enabled) { pm.gameCam.enabled = true; log.Msg("camera: the game camera was off: on"); return; }
            var b = m.brain;
            if (b != null && !b.enabled) { b.enabled = true; log.Msg("camera: Cinemachine's brain was off: on"); return; }
            var solo = CinemachineBrain.SoloCamera;
            if (solo != null) { CinemachineBrain.SoloCamera = null; log.Msg($"camera: {Name(solo)} was soloed: cleared"); return; }
            if (b != null && b.ActiveVirtualCamera != null && b.ActiveVirtualCamera.Pointer != pm.gameCam.Pointer)
            {
                pm.gameCam.MoveToTopOfPrioritySubqueue();
                log.Msg($"camera: {Name(b.ActiveVirtualCamera)} was live instead of the game camera: game camera put first");
                return;
            }
        }
        catch (Exception e) { log.Warning("camera: " + e.Message); }
        // nothing switched off and the game camera is the live one: the brain just isn't moving the real camera
        forcing = true;
        log.Msg("camera: the brain doesn't move the real camera: PracticeTools moves it onto the game camera until the next pause / fly / load");
    }

    static void Reset()
    {
        stillSince = -1f;
        moved = 0f;
        lastReal = Vector3.zero;
        forcing = false;
    }
}
