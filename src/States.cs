using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Il2Cpp;
using MelonLoader;
using Il2CppNWH.DWP2.WaterObjects;
using UnityEngine;

namespace PracticeTools;

// savestates: numbered slots, each with a short description, shared by every save slot. a state keeps nate's whole pose (every bone of his rig,
// the ragdoll's velocities), his walking state (PlayerMovement's and each foot's values), the camera, and a copy of
// the save file as it was.
// load (F10) puts nate and the camera back, teleporting first when he's far. full load also puts the state's copy of
// the save file in place of the save being played and restarts the game from the title (press continue): the world
// is as it was when the state was saved (items, cutscenes seen, the time of day...); then the pose goes back on top.
// the save it replaces is kept as states/before-full-load-<slot>.sav
static class States
{
    internal const int Count = 30;
    internal static MelonPreferences_Entry<int> Selected;   // 0-based

    internal class State
    {
        public string Desc = "";
        public int SaveSlot = -1;
        public Vector3 Hip;
        public bool HasPose;
        public string When = "";
    }

    static readonly State[] states = new State[Count];
    static string StateDir => Path.Combine(Core.Dir, "states");
    static string TextFile(int i) => Path.Combine(StateDir, $"{i + 1}.txt");
    static string SaveFile(int i) => Path.Combine(StateDir, $"{i + 1}.sav");
    // the game's journey files next to its save (path<slot>_<n>.sav, 50000 positions each): a state keeps copies
    static string[] StatePaths(int i) => Directory.Exists(StateDir) ? Directory.GetFiles(StateDir, $"{i + 1}.path*.sav") : new string[0];
    static int GameSlot => SaveGod.me != null ? SaveGod.me.slot : -1;
    static readonly CultureInfo C = CultureInfo.InvariantCulture;

    internal static State Get(int i) => i >= 0 && i < Count ? states[i] : null;
    internal static int Sel => Mathf.Clamp(Selected.Value, 0, Count - 1);

    internal static void Init()
    {
        Selected = Core.Cat.CreateEntry("StateSlot", 0, "Savestate slot", "The savestate slot F9 / F10 use (0 = slot 1).");
        Directory.CreateDirectory(StateDir);
        for (int i = 0; i < Count; i++) states[i] = ReadHeader(i);
        ImportSpots();
    }

    // ---- once a frame ----

    internal static void Update()
    {
        if (Binds.Pressed(Binds.Save)) Save(Sel);
        else if (Binds.Pressed(Binds.Load)) Load(Sel, false);
        CopyTick();
        LoadTick();
        Guard();
        UnstickUnload();
        LoadFix.Update();
        if (pauseSnap != null && (Menu.me == null || !Menu.me.paused)) pauseSnap = null;
    }

    // the game's unload (restart to the title, teleports) waits for every scenery LOD still marked "loading", but it has
    // already switched off the update that marks them loaded: one LOD queued at that moment and it waits forever. we run
    // that update for it while it unloads
    static float offSince = -1f;
    static bool unstickLogged;

    static void UnstickUnload()
    {
        var r = BestRegionLoader.me;
        var m = Menu.me;
        if (r == null || m == null || !r.off || !(m.restartingScene || m.teleporting)) { offSince = -1f; unstickLogged = false; return; }
        if (offSince < 0f) offSince = Time.unscaledTime;
        if (Time.unscaledTime - offSince < 2f) return;   // a normal unload is done by then
        if (!unstickLogged) { unstickLogged = true; DebugLog.Write("unload: still going after 2 s, marking the scenery that finished loading for it"); }
        r.UpdateLODLoadStatus();
        // the full load's restart (UnloadAll) waits for every prop prefab (0x1E0) and prop visual (0x1D0) still
        // "loading" (1). one whose load died (Player.log: "invalid operation handle") stays 1 for good: after 5 s,
        // they count as not loaded (0), the scene is being thrown away anyway
        if (m.restartingScene && Time.unscaledTime - offSince > 5f)
        {
            int props = Unload(r, 0x1E0), visuals = Unload(r, 0x1D0);
            if (props + visuals > 0) DebugLog.Write($"unload: {props} prop prefabs and {visuals} prop visuals never finished loading: let go so the restart goes on");
        }
    }

    static int Unload(BestRegionLoader r, int offset)
    {
        if (!Fly.Native(r, offset, out var st, out int len)) return 0;
        int n = Math.Min(len, r.propCount), k = 0;
        for (int i = 0; i < n; i++)
            if (System.Runtime.InteropServices.Marshal.ReadInt32(st, i * 4) == 1) { System.Runtime.InteropServices.Marshal.WriteInt32(st, i * 4, 0); k++; }
        return k;
    }

    // nate's ragdoll joints take their places from his animated skeleton, and the ragdoll is mapped back onto that
    // skeleton: a bone stretched once feeds itself (after some loads an arm grew to 5 m in 4 s, only a restart cleared
    // it). a bone twice its length (and 15 cm) too long goes back. only for 10 s after a state loads: the runaways
    // started after loads, and the water glitch's launch stretches him by itself (the guard would change the glitch)
    static float guardUntil = -1f;

    internal static void Guard()
    {
        if (Time.unscaledTime > guardUntil) return;
        var pm = PlayerMovement.me;
        var muscles = pm != null && pm.pm != null ? pm.pm.muscles : null;
        if (muscles == null || pm.hipPivot == null) return;
        // nate moved far in one go (the game dropping him back from the sky after he sank, a teleport, a load): for a
        // frame or two his skeleton and ragdoll are in different places and every bone looks metres long. not a runaway
        var hip = pm.hipPivot.position;
        bool jumped = Vector3.Distance(hip, guardHip) > 2f;
        guardHip = hip;
        if (jumped) guardQuietUntil = Time.frameCount + 3;
        if (Time.frameCount <= guardQuietUntil || (Menu.me != null && (Menu.me.teleporting || Menu.me.restartingScene))) return;
        // not in water: the water glitch's setup stretches his limbs too, and i load a state and fling within the
        // 10 s (bones were put back at the fling spot, and the flings came out far shorter than without the mod)
        if (FlingWatch.Parts > 0) return;
        int n = 0;
        string worst = null;
        float worstLen = 0f;
        foreach (var mu in muscles)
        {
            if (mu == null || mu.target == null || mu.joint == null || mu.joint.connectedBody == null) continue;   // the hips move freely
            var def = mu.defaultTargetLocalPosition;
            var now = mu.target.localPosition;
            // the length only, and only far too long: the game moves some bones a few cm itself (the shoulders, even
            // half again their length as he gets up), a runaway grows them by metres
            if (now.magnitude <= def.magnitude + Mathf.Max(0.15f, def.magnitude)) continue;
            mu.target.localPosition = def;
            n++;
            if (now.magnitude > worstLen) { worstLen = now.magnitude; worst = $"{mu.name} {now.magnitude:0.00} m, normal {def.magnitude:0.00}"; }
        }
        if (n > 0 && Time.unscaledTime >= guardLogAt)
        {
            guardLogAt = Time.unscaledTime + 1f;   // a line a second at most
            DebugLog.Write($"bone guard: {n} bones far too long put back (worst {worst}), hip {hip}");
        }
    }

    static Vector3 guardHip;
    static int guardQuietUntil;
    static float guardLogAt;

    // ---- saving ----

    internal static void Save(int i)
    {
        var pm = PlayerMovement.me;
        if (pm == null || pm.hipPivot == null) { Hud.Flash("LOAD A SAVE FIRST"); return; }
        if (pm.inFlyCam || Fly.Carrying) { Hud.Flash("LEAVE FLY FIRST"); return; }
        if (Menu.me != null && Menu.me.teleporting) return;
        var root = Rig(pm);
        if (root == null) { Hud.Flash("CAN'T FIND NATE'S RIG"); return; }

        // paused, the world isn't as it was: the pause screen's camera stands far off, so the water counts nate as out of
        // its range (no water forces, every part above water). a state saved then came back stuck under water. use
        // nate as he was the moment the game paused (nothing moves while paused)
        bool fromPause = Menu.me != null && Menu.me.paused && pauseSnap != null;
        var s = states[i] ?? new State();
        s.SaveSlot = GameSlot; s.Hip = fromPause ? pauseHip : pm.hipPivot.position; s.HasPose = true; s.When = DateTime.Now.ToString("yyyy-MM-dd HH:mm", C);
        states[i] = s;
        var sb = new StringBuilder();
        Header(sb, s);
        if (fromPause) { sb.Append(pauseSnap); Core.Inst.LoggerInstance.Msg("savestate: paused, saved nate as he was when the game paused"); }
        else Pose(sb, pm, root);
        try { File.WriteAllText(TextFile(i), sb.ToString()); }
        catch (Exception e) { Core.Inst.LoggerInstance.Error("savestate: " + e.Message); Hud.Flash("COULDN'T WRITE THE STATE"); return; }

        // the save file: have the game write it now, then copy it once it's on disk
        try { File.Delete(SaveFile(i)); } catch { }
        copyFor = i; copyFrom = SavePath(GameSlot);
        if (copyFrom == null) { copyFor = -1; Core.Inst.LoggerInstance.Warning("savestate: can't find the save file"); }
        copyAfter = Time.unscaledTime + 0.3f; copyUntil = Time.unscaledTime + 3f;
        copyStamp = copyFrom != null && File.Exists(copyFrom) ? File.GetLastWriteTimeUtc(copyFrom) : DateTime.MinValue;
        try { SaveGod.me.WriteTheFile(); } catch (Exception e) { Core.Inst.LoggerInstance.Warning("savestate: write: " + e.Message); }
        // the journey: the game writes it every 10th save, so it's behind; have it written now too
        try { if (!SaveGod.me.currentlyWritingPositions) SaveGod.me.WritePositions(); } catch (Exception e) { Core.Inst.LoggerInstance.Warning("savestate: journey: " + e.Message); }
        Hud.Flash($"STATE {i + 1} SAVED");
    }

    static string pauseSnap;
    static Vector3 pauseHip;

    [HarmonyLib.HarmonyPatch(typeof(Menu), nameof(Menu.Pause))]
    static class SnapOnPause
    {
        static void Prefix(Menu __instance)
        {
            pauseSnap = null;
            if (__instance.paused || __instance.inDream) return;
            try
            {
                var pm = PlayerMovement.me;
                if (pm == null || pm.hipPivot == null || pm.inFlyCam || Fly.Carrying || __instance.teleporting) return;
                var root = Rig(pm);
                if (root == null) return;
                var sb = new StringBuilder();
                Pose(sb, pm, root, true);
                pauseSnap = sb.ToString();
                pauseHip = pm.hipPivot.position;
            }
            catch (Exception e) { pauseSnap = null; Core.Inst.LoggerInstance.Warning("savestate: pause snapshot: " + e.Message); }
        }
    }

    static int copyFor = -1;
    static string copyFrom;
    static float copyAfter, copyUntil;
    static DateTime copyStamp;

    static void CopyTick()
    {
        if (copyFor < 0 || Time.unscaledTime < copyAfter) return;
        bool fresh = File.Exists(copyFrom) && File.GetLastWriteTimeUtc(copyFrom) != copyStamp;
        if (!fresh && Time.unscaledTime < copyUntil) return;
        bool writing = SaveGod.me != null && SaveGod.me.currentlyWritingPositions;
        if (writing && Time.unscaledTime < copyUntil) return;
        try { File.Copy(copyFrom, SaveFile(copyFor), true); }
        catch (Exception e) { Core.Inst.LoggerInstance.Warning("savestate: copying the save: " + e.Message); }
        if (!fresh) Core.Inst.LoggerInstance.Warning("savestate: the game didn't write its save in time, kept its last one");
        CopyPaths(copyFor, copyFrom);
        copyFor = -1;
    }

    static string[] GamePaths(string savePath, int slot) =>
        Directory.GetFiles(Path.GetDirectoryName(savePath), $"path{slot}_*.sav");

    // path<slot>_<n>.sav into the state as <state>.path<n>.sav
    static void CopyPaths(int i, string savePath)
    {
        try
        {
            foreach (var f in StatePaths(i)) File.Delete(f);
            int n = 0;
            foreach (var f in GamePaths(savePath, GameSlot))
            {
                string num = Path.GetFileNameWithoutExtension(f).Substring(Path.GetFileNameWithoutExtension(f).IndexOf('_') + 1);
                File.Copy(f, Path.Combine(StateDir, $"{i + 1}.path{num}.sav"), true);
                n++;
            }
            if (n == 0) DebugLog.Write("savestate: no journey file yet");
        }
        catch (Exception e) { Core.Inst.LoggerInstance.Warning("savestate: copying the journey: " + e.Message); }
    }

    // a full load puts the state's journey back (the current one kept as before-full-load-path<slot>_<n>.sav). a state
    // from before journeys were kept leaves it as it is
    static void RestorePaths(int i, string savePath, int slot)
    {
        var mine = StatePaths(i);
        if (mine.Length == 0) { DebugLog.Write("full load: the state has no journey file: left as it is"); return; }
        foreach (var f in GamePaths(savePath, slot))
        {
            File.Copy(f, Path.Combine(StateDir, "before-full-load-" + Path.GetFileName(f)), true);
            File.Delete(f);
        }
        foreach (var f in mine)
        {
            string name = Path.GetFileNameWithoutExtension(f);
            string num = name.Substring(name.IndexOf(".path", StringComparison.Ordinal) + 5);
            File.Copy(f, Path.Combine(Path.GetDirectoryName(savePath), $"path{slot}_{num}.sav"), true);
        }
        DebugLog.Write($"full load: journey put back ({mine.Length} file{(mine.Length == 1 ? "" : "s")})");
    }

    // ---- loading ----

    static State pending;
    internal static bool Loading => pending != null;   // from the load (F10 / full load) until the state is applied
    static int pendingIndex;
    static bool pendingFull, teleported;
    static float applyAt;

    internal static void Load(int i, bool full)
    {
        var s = Get(i);
        if (s == null) { Hud.Flash($"STATE {i + 1} IS EMPTY (F9 SAVES)"); return; }
        var pm = PlayerMovement.me;
        var m = Menu.me;
        if (pm == null || m == null) { Hud.Flash("LOAD A SAVE FIRST"); return; }
        if (m.teleporting)
        {
            if (!full) { Hud.Flash("STILL LOADING: A FULL LOAD ENDS IT"); return; }
            CancelTeleport(m);
        }
        // a load still waiting (on a stuck teleport, say) gives way to a full load
        if (pending != null) { if (!full) return; pending = null; EndFarWait(); }
        if (pm.inFlyCam || Fly.Carrying) { Hud.Flash("LEAVE FLY FIRST"); return; }

        if (full)
        {
            if (!File.Exists(SaveFile(i))) { Hud.Flash($"STATE {i + 1} HAS NO SAVE FILE"); return; }
            MelonCoroutines.Start(FullLoad(i, s));
            return;
        }
        pending = s; pendingIndex = i; pendingFull = false; teleported = false; applyAt = -1f;
        if (m.paused) m.UnPause();
    }

    // the game writes its save every few seconds: stop that, put the state's save back, restart from the title
    static System.Collections.IEnumerator FullLoad(int i, State s)
    {
        var god = SaveGod.me;
        var m = Menu.me;
        god.stopSaving = true;
        float until = Time.unscaledTime + 0.6f;   // a write already started finishes first
        while (Time.unscaledTime < until) yield return null;
        until = Time.unscaledTime + 3f;   // and a journey write
        while (god.currentlyWritingPositions && Time.unscaledTime < until) yield return null;
        string path = SavePath(GameSlot);
        try
        {
            if (path == null) throw new Exception("can't find the save file");
            File.Copy(path, Path.Combine(StateDir, $"before-full-load-{GameSlot + 1}.sav"), true);
            File.Copy(SaveFile(i), path, true);
            RestorePaths(i, path, GameSlot);
            Splits.Restore(File.ReadAllLines(TextFile(i)), GameSlot);
        }
        catch (Exception e)
        {
            Core.Inst.LoggerInstance.Error("full load: " + e.Message);
            god.stopSaving = false;
            Hud.Flash("FULL LOAD FAILED");
            yield break;
        }
        Core.Inst.LoggerInstance.Msg($"full load: state {i + 1} put back as {path}, restarting");
        pending = s; pendingIndex = i; pendingFull = true; teleported = false; applyAt = -1f;
        m.StartCoroutine(m.RestartScene());
        Hud.Flash("SAVE PUT BACK: PRESS CONTINUE");
    }

    static void LoadTick()
    {
        if (pending == null) return;
        var pm = PlayerMovement.me;
        var m = Menu.me;
        if (m == null) return;
        // a full load waits for the title, continue, and the game running again
        if (pendingFull && (pm == null || !m.gameInProgress || m.restartingScene)) return;
        if (pm == null) { pending = null; EndFarWait(); return; }
        if (m.paused || m.teleporting) { applyAt = -1f; return; }

        if (!teleported)
        {
            teleported = true;
            bool far = Vector3.Distance(pm.hipPivot.position, pending.Hip) > 30f;
            if (far && !pendingFull && pending.HasPose) { StartFarWait(pm, m); return; }
            if (far || pendingFull) { m.Teleport(pending.Hip, false); return; }
        }
        if (farWait && !FarWaitDone()) return;
        if (applyAt < 0f) { applyAt = Time.unscaledTime + (pendingFull ? 1f : 0.25f); return; }   // let the teleport settle
        if (Time.unscaledTime < applyAt) return;

        var s = pending; pending = null;
        if (pendingFull && SaveGod.me != null) SaveGod.me.stopSaving = false;
        if (!s.HasPose) { Hud.Flash($"STATE {pendingIndex + 1} LOADED (SPOT ONLY)"); return; }
        try { Apply(pm, File.ReadAllLines(TextFile(pendingIndex))); Hud.Flash($"STATE {pendingIndex + 1} LOADED"); guardUntil = Time.unscaledTime + 10f; }
        catch (Exception e) { Core.Inst.LoggerInstance.Error("savestate: " + e); Hud.Flash("STATE LOAD FAILED"); }
    }

    // ---- a load far from nate ----
    // the game's teleport waits until every prop visual around the spot has loaded (a minute and more after a fling,
    // forever when one of those loads dies), and meanwhile nate falls through the ground unloading under him. so a
    // state far away doesn't teleport: nate is put in the state's pose right away (the loader follows him there) and the
    // game is held still (time scale 0, no physics) until the ground and the props you can touch are in. then, as after
    // a teleport, the ocean is rebuilt, a moment of physics runs, and the state is put back once more
    const float FarLoadCellsStuck = 5f, FarLoadPropWait = 20f, FarLoadGiveUp = 45f;
    static bool farWait;
    static float farSince, farReportAt, farCellsSince;
    static int farCells;

    static void StartFarWait(PlayerMovement pm, Menu m)
    {
        try { Apply(pm, File.ReadAllLines(TextFile(pendingIndex))); }
        catch (Exception e)
        {
            Core.Inst.LoggerInstance.Warning("savestate: far load, putting nate there first: " + e.Message + ", teleporting instead");
            m.Teleport(pending.Hip, false);
            return;
        }
        farWait = true;
        farSince = Time.unscaledTime;
        farReportAt = farSince + 10f;
        farCells = -1;
        farCellsSince = farSince;
        Time.timeScale = 0f;
        Application.backgroundLoadingPriority = ThreadPriority.High;
        Hud.Flash("LOADING THE AREA");
    }

    // true once the area is in (or waited long enough)
    static bool FarWaitDone()
    {
        if (Time.timeScale != 0f) Time.timeScale = 0f;   // the pause menu sets it back to 1 when it closes
        float t = Time.unscaledTime - farSince;
        if (t < 0.5f) return false;   // the loader picks up the new spot over a few frames
        Fly.Pending(out int chunks, out int cells, out int visuals);
        if (Time.unscaledTime > farReportAt) { farReportAt += 10f; Fly.LoaderReport($"savestate load waiting {t:0} s"); }
        if (cells != farCells) { farCells = cells; farCellsSince = Time.unscaledTime; }
        bool ready = chunks == 0 && cells == 0;
        // prop cells that haven't moved in a while are stuck (some stay "loading" for good), not slow
        bool cellsStuck = Time.unscaledTime - farCellsSince > FarLoadCellsStuck;
        if (!ready && !(chunks == 0 && (cellsStuck || t > FarLoadPropWait)) && t < FarLoadGiveUp) return false;
        EndFarWait();
        DebugLog.Write($"savestate: far load, area in after {t:0.0} s" + (ready ? "" : $" (gave up waiting on {chunks} area chunks, {cells} prop cells)")
            + (visuals > 0 ? $", {visuals} prop visuals still loading" : ""));
        try { Il2CppCrest.OceanRenderer.Instance?.RebuildOcean(); } catch (Exception e) { Core.Inst.LoggerInstance.Warning("savestate: ocean: " + e.Message); }
        try { WeatherMan.me?.ResetFogHeight(); } catch (Exception e) { Core.Inst.LoggerInstance.Warning("savestate: fog: " + e.Message); }
        applyAt = Time.unscaledTime + 0.25f;
        return true;
    }

    static void EndFarWait()
    {
        if (!farWait) return;
        farWait = false;
        Application.backgroundLoadingPriority = ThreadPriority.Low;
        if (Time.timeScale == 0f && (Menu.me == null || !Menu.me.paused)) Time.timeScale = 1f;   // the speed setting puts its own back
    }

    // ---- a stuck teleport ----
    // the game's teleport (Menu.Teleport starts its TeleportCo coroutine) waits until the region loader says the area
    // around the target is loaded. after nate was flung far off the map that never came: it waited forever (BabyBlocks
    // only clears the flag after a while, the coroutine goes on waiting, and every teleport meanwhile queues behind it).
    // a full load ends it: the coroutine's next step says "done", and what its first step changed goes back

    static IntPtr teleportCo, cancelCo;

    [HarmonyLib.HarmonyPatch(typeof(Menu._TeleportCo_d__233), nameof(Menu._TeleportCo_d__233.MoveNext))]
    static class TeleportStep
    {
        static bool Prefix(Menu._TeleportCo_d__233 __instance, ref bool __result)
        {
            if (cancelCo != IntPtr.Zero && __instance.Pointer == cancelCo) { cancelCo = IntPtr.Zero; teleportCo = IntPtr.Zero; __result = false; return false; }
            teleportCo = __instance.Pointer;
            return true;
        }

        static void Postfix(Menu._TeleportCo_d__233 __instance, bool __result)
        {
            if (!__result && __instance.Pointer == teleportCo) teleportCo = IntPtr.Zero;
        }
    }

    static void CancelTeleport(Menu m)
    {
        Fly.LoaderReport("full load on a teleport that didn't end");
        cancelCo = teleportCo;
        m.teleporting = false;
        m.qedTeleport = false;
        var pm = m.player;
        if (pm != null)
        {
            if (pm.gameCam != null) pm.gameCam.enabled = true;
            if (!pm.gameObject.activeSelf) pm.gameObject.SetActive(true);
            var rl = BestRegionLoader.me;
            if (rl != null && pm.torsoRbs != null && pm.torsoRbs.Length > 0) rl.loadingTransform = pm.torsoRbs[0].transform;
        }
        Application.backgroundLoadingPriority = ThreadPriority.Low;
        Fly.ReleaseLoaderLock("a full load");   // the restart's unload waits for it
        Core.Inst.LoggerInstance.Msg(cancelCo != IntPtr.Zero ? "full load: ended a stuck teleport" : "full load: the game said it was teleporting, no teleport running: flag cleared");
    }

    // the game names its save "save<slot>.sav" and asks its platform layer for the folder (on steam
    // <its data folder>/<steam id>/save/, whoever is signed in, on windows or under proton). if that ever fails,
    // the newest file of that name under its data folder
    static string loggedPath;

    static string SavePath(int slot)
    {
        string name = SaveGod.GetSavePath(slot);
        if (Path.IsPathRooted(name)) return File.Exists(name) ? name : null;
        try
        {
            var dir = SaveGod.me?.platformService?.GetRootSaveDirectory();
            if (!string.IsNullOrEmpty(dir))
            {
                if (!Path.IsPathRooted(dir)) dir = Path.Combine(Application.persistentDataPath, dir);
                string p = Path.Combine(dir, name);
                if (File.Exists(p))
                {
                    if (loggedPath != p) { loggedPath = p; DebugLog.Write("savestate: the save file is " + p); }
                    return p;
                }
                Core.Inst.LoggerInstance.Warning($"savestate: no {name} in the game's save folder {dir}, looking around");
            }
        }
        catch (Exception e) { Core.Inst.LoggerInstance.Warning("savestate: save folder: " + e.Message); }
        string best = null; var at = DateTime.MinValue;
        try
        {
            foreach (var f in Directory.GetFiles(Application.persistentDataPath, name, SearchOption.AllDirectories))
            {
                var w = File.GetLastWriteTimeUtc(f);
                if (w > at) { at = w; best = f; }
            }
        }
        catch (Exception e) { Core.Inst.LoggerInstance.Warning("savestate: " + e.Message); }
        return best;
    }

    // ---- the slots ----

    internal static void SetDesc(int i, string desc)
    {
        var s = Get(i);
        if (s == null) return;
        s.Desc = (desc ?? "").Replace('\n', ' ').Replace('|', '/').Trim();
        if (s.Desc.Length > 40) s.Desc = s.Desc.Substring(0, 40);
        try
        {
            var lines = File.Exists(TextFile(i)) ? new List<string>(File.ReadAllLines(TextFile(i))) : new List<string>();
            lines.RemoveAll(l => l.StartsWith("desc|"));
            lines.Insert(0, "desc|" + s.Desc);
            File.WriteAllLines(TextFile(i), lines);
        }
        catch (Exception e) { Core.Inst.LoggerInstance.Warning("savestate: " + e.Message); }
    }

    internal static void Delete(int i)
    {
        if (i < 0 || i >= Count) return;
        states[i] = null;
        try { File.Delete(TextFile(i)); File.Delete(SaveFile(i)); foreach (var f in StatePaths(i)) File.Delete(f); } catch { }
    }

    // the old single spot per save (spots.txt) becomes the first free slots, once
    static void ImportSpots()
    {
        string old = Path.Combine(Core.Dir, "spots.txt"), done = Path.Combine(StateDir, ".spots-imported");
        if (!File.Exists(old) || File.Exists(done)) return;
        try
        {
            foreach (var line in File.ReadAllLines(old))
            {
                var f = line.Split(' ');
                if (f.Length != 4) continue;
                int free = Array.IndexOf(states, null);
                if (free < 0) break;
                var s = new State { Desc = "old spot", SaveSlot = int.Parse(f[0], C), Hip = new Vector3(float.Parse(f[1], C), float.Parse(f[2], C), float.Parse(f[3], C)) };
                states[free] = s;
                var sb = new StringBuilder(); Header(sb, s);
                File.WriteAllText(TextFile(free), sb.ToString());
            }
            File.WriteAllText(done, "");
        }
        catch (Exception e) { Core.Inst.LoggerInstance.Warning("savestate: importing spots: " + e.Message); }
    }

    // ---- the file ----
    // desc|text, save|slot, hip|x y z, when|date, then the pose: root|pos rot, t|index|local pos rot (every transform
    // of the rig, in order), rb|index|velocity angular velocity, pm|name|value, foot|n|name|value, cam|name|pos rot,
    // loops|the save's zLoops, glitch|the glitch markers' stage, route|the route being recorded (Routes.Snapshot)

    static void Header(StringBuilder sb, State s)
    {
        sb.Append("desc|").Append(s.Desc).Append('\n');
        sb.Append("save|").Append(s.SaveSlot.ToString(C)).Append('\n');
        sb.Append("hip|").Append(V(s.Hip)).Append('\n');
        sb.Append("when|").Append(s.When).Append('\n');
    }

    static State ReadHeader(int i)
    {
        try
        {
            if (!File.Exists(TextFile(i))) return null;
            var s = new State();
            foreach (var line in File.ReadLines(TextFile(i)))
            {
                int bar = line.IndexOf('|');
                if (bar < 0) continue;
                string k = line.Substring(0, bar), v = line.Substring(bar + 1);
                if (k == "desc") s.Desc = v;
                else if (k == "save") s.SaveSlot = int.Parse(v, C);
                else if (k == "hip") s.Hip = ParseV(v);
                else if (k == "when") s.When = v;
                else if (k == "root") s.HasPose = true;
            }
            return s;
        }
        catch { return null; }
    }

    static Transform Rig(PlayerMovement pm) => pm.pm != null ? Fly.CommonParent(pm.transform, pm.pm.transform) : null;

    static void Pose(StringBuilder sb, PlayerMovement pm, Transform root, bool quiet = false)
    {
        sb.Append("root|").Append(V(root.position)).Append(' ').Append(Q(root.rotation)).Append('\n');
        var ts = root.GetComponentsInChildren<Transform>(true);
        sb.Append("count|").Append(ts.Length.ToString(C)).Append('\n');
        for (int k = 0; k < ts.Length; k++)
        {
            var t = ts[k];
            sb.Append("t|").Append(k.ToString(C)).Append('|').Append(V(t.localPosition)).Append(' ').Append(Q(t.localRotation)).Append('\n');
            var rb = t.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
                sb.Append("rb|").Append(k.ToString(C)).Append('|').Append(V(rb.velocity)).Append(' ').Append(V(rb.angularVelocity)).Append('\n');
        }
        // nate's walking state and the ragdoll's own (down, getting up, how pinned each muscle is)
        int values = 0;
        foreach (var (key, o, type) in Parts(pm))
            foreach (var (name, value) in Values(o, type))
            {
                sb.Append("v|").Append(key).Append('|').Append(name).Append('|').Append(value).Append('\n');
                values++;
            }
        if (!quiet) DebugLog.Write($"savestate: {ts.Length} rig parts, {values} values");
        Cam(sb, "camTrans", pm.camTrans);
        if (pm.gameCam != null) Cam(sb, "gameCam", pm.gameCam.transform);
        var main = Camera.main;
        if (main != null) Cam(sb, "main", main.transform);
        // the animators: which animation each layer plays and their parameters. without them a floating nate comes
        // back in his standing animation (feet down, stiff legs) and the swim never picks up again
        var anims = root.GetComponentsInChildren<Animator>(true);
        for (int a = 0; a < anims.Length; a++)
        {
            var an = anims[a];
            if (an == null || !an.isActiveAndEnabled || an.runtimeAnimatorController == null) continue;
            for (int l = 0; l < an.layerCount; l++)
            {
                var st = an.IsInTransition(l) ? an.GetNextAnimatorStateInfo(l) : an.GetCurrentAnimatorStateInfo(l);
                sb.Append("anim|").Append(a.ToString(C)).Append('|').Append(l.ToString(C)).Append('|').Append(st.fullPathHash.ToString(C))
                  .Append('|').Append(F(st.normalizedTime)).Append('|').Append(F(an.GetLayerWeight(l))).Append('\n');
            }
            foreach (var p in an.parameters)
            {
                string v = p.type switch
                {
                    AnimatorControllerParameterType.Float => F(an.GetFloat(p.nameHash)),
                    AnimatorControllerParameterType.Int => an.GetInteger(p.nameHash).ToString(C),
                    AnimatorControllerParameterType.Bool => an.GetBool(p.nameHash) ? "1" : "0",
                    _ => null,   // triggers fire once, not kept
                };
                if (v != null) sb.Append("ap|").Append(a.ToString(C)).Append('|').Append(p.nameHash.ToString(C)).Append('|').Append(((int)p.type).ToString(C)).Append('|').Append(v).Append('\n');
            }
        }
        Splits.Snapshot(sb);
        // the save's map loop count (below 0 the campfires and the ending are off): a state from before a loop puts it back
        var save = SaveGod.theSave;
        if (save != null) sb.Append("loops|").Append(save.zLoops.ToString(C)).Append('\n');
        sb.Append("glitch|").Append(Hud.GlitchStage.ToString(C)).Append('\n');   // which glitch marker the setup is at
        Routes.Snapshot(sb);
        // what's in each hand and on his head, by name and where it was (two items can share a name)
        var hands = pm.handItems;
        if (hands != null)
            for (int k = 0; k < hands.Length; k++)
                sb.Append("hand|").Append(k.ToString(C)).Append('|').Append(hands[k] != null ? hands[k].gameObject.name + "|" + V(hands[k].transform.position) : "").Append('\n');
        sb.Append("hat|").Append(pm.currentHat != null ? pm.currentHat.gameObject.name + "|" + V(pm.currentHat.transform.position) : "").Append('\n');
    }

    // after the pose: put back what he held and wore. an item already where it should be stays; a different one is
    // fetched (the nearest of that name to where the state had it) and grabbed the game's own way
    static void Holding(PlayerMovement pm, List<(int hand, string name, Vector3 at)> hands, (string name, Vector3 at)? hat)
    {
        var held = pm.handItems;
        foreach (var (k, name, at) in hands)
        {
            if (held == null || k >= held.Length) continue;
            var now = held[k];
            if (name == "") { if (now != null) pm.DropHandItem(k); continue; }
            if (now != null && now.gameObject.name == name) continue;
            var g = Nearest<Grabable>(name, at);
            if (g == null) { Core.Inst.LoggerInstance.Msg($"savestate: no {name} to put in hand {k}"); continue; }
            if (pm.handRbs != null && k < pm.handRbs.Length) g.transform.position = pm.handRbs[k].transform.position;
            pm.GrabGrabable(g, k);
        }
        if (hat == null) return;
        var (hatName, hatAt) = hat.Value;
        if (hatName == "") { if (pm.currentHat != null) pm.KnockOffHat(); return; }
        if (pm.currentHat != null && pm.currentHat.gameObject.name == hatName) return;
        var h = Nearest<Hat>(hatName, hatAt);
        if (h != null) pm.WearHat(h); else Core.Inst.LoggerInstance.Msg($"savestate: no {hatName} to wear");
    }

    static T Nearest<T>(string name, Vector3 at) where T : Component
    {
        T best = null; float d = float.MaxValue;
        foreach (var x in UnityEngine.Object.FindObjectsOfType<T>())
        {
            if (x == null || x.gameObject.name != name) continue;
            float e = (x.transform.position - at).sqrMagnitude;
            if (e < d) { d = e; best = x; }
        }
        return best;
    }

    static void Cam(StringBuilder sb, string name, Transform t)
    {
        if (t != null) sb.Append("cam|").Append(name).Append('|').Append(V(t.position)).Append(' ').Append(Q(t.rotation)).Append('\n');
    }

    static void Apply(PlayerMovement pm, string[] lines)
    {
        var root = Rig(pm);
        if (root == null) throw new Exception("no rig");
        FlingWatch.Reset();   // nate is put somewhere else: not a fling
        Hud.Moved();
        var ts = root.GetComponentsInChildren<Transform>(true);
        var hipBefore = pm.hipPivot.position;
        int count = -1;
        var vel = new List<(Rigidbody, Vector3, Vector3)>();
        Dictionary<string, (object o, Type type)> parts = null;
        var hands = new List<(int, string, Vector3)>();
        (string, Vector3)? hat = null;
        var anims = new List<(int a, int layer, int hash, float time, float weight)>();
        var aps = new List<(int a, int hash, int type, string value)>();
        bool floating = false;
        int skipped = 0, glitch = -1;   // a state from before the glitch markers: from the first again
        string[] route = null;
        foreach (var line in lines)
        {
            var f = line.Split('|');
            switch (f[0])
            {
                case "root":
                {
                    var n = Nums(f[1]);
                    root.SetPositionAndRotation(new Vector3(n[0], n[1], n[2]), new Quaternion(n[3], n[4], n[5], n[6]));
                    break;
                }
                case "count": count = int.Parse(f[1], C); break;
                case "t":
                {
                    if (count != ts.Length) break;   // a different rig: only the root and values go back
                    int k = int.Parse(f[1], C);
                    var n = Nums(f[2]);
                    ts[k].localPosition = new Vector3(n[0], n[1], n[2]);
                    ts[k].localRotation = new Quaternion(n[3], n[4], n[5], n[6]);
                    break;
                }
                case "rb":
                {
                    if (count != ts.Length) break;
                    var rb = ts[int.Parse(f[1], C)].GetComponent<Rigidbody>();
                    var n = Nums(f[2]);
                    if (rb != null) vel.Add((rb, new Vector3(n[0], n[1], n[2]), new Vector3(n[3], n[4], n[5])));
                    break;
                }
                case "v":
                {
                    if (f[1] == "pm" && f[2] == "floating") floating = f[3] == "1";
                    if (parts == null) { parts = new(); foreach (var (key, o, type) in Parts(pm)) parts[key] = (o, type); }
                    if (NotState.Contains(f[2])) break;   // in older states, left out now
                    if (!parts.TryGetValue(f[1], out var part) || !Set(part.o, Props(part.type), f[2], f[3])) skipped++;
                    break;
                }
                case "anim":
                    anims.Add((int.Parse(f[1], C), int.Parse(f[2], C), int.Parse(f[3], C), float.Parse(f[4], C), float.Parse(f[5], C)));
                    break;
                case "ap":
                    aps.Add((int.Parse(f[1], C), int.Parse(f[2], C), int.Parse(f[3], C), f[4]));
                    break;
                case "hand":
                    hands.Add((int.Parse(f[1], C), f[2], f.Length > 3 ? ParseV(f[3]) : Vector3.zero));
                    break;
                case "hat":
                    hat = (f[1], f.Length > 2 ? ParseV(f[2]) : Vector3.zero);
                    break;
                case "glitch":
                    glitch = int.Parse(f[1], C);
                    break;
                case "route":
                    route = f;
                    break;
                case "loops":
                {
                    var save = SaveGod.theSave;
                    int loops = int.Parse(f[1], C);
                    if (save != null && save.zLoops != loops)
                    {
                        DebugLog.Write($"savestate: map loops {save.zLoops} back to {loops}");
                        save.zLoops = loops;
                    }
                    break;
                }
                case "cam":
                {
                    var t = f[1] == "camTrans" ? pm.camTrans : f[1] == "gameCam" ? pm.gameCam?.transform : Camera.main?.transform;
                    var n = Nums(f[2]);
                    if (t != null) t.SetPositionAndRotation(new Vector3(n[0], n[1], n[2]), new Quaternion(n[3], n[4], n[5], n[6]));
                    break;
                }
            }
        }
        if (count != ts.Length) Core.Inst.LoggerInstance.Warning($"savestate: nate's rig has {ts.Length} parts, the state {count}: only his place went back");
        // the muscles' strengths went back: each joint gets its own on the next physics step (as Muscle.Reset does)
        var muscles = pm.pm != null ? pm.pm.muscles : null;
        if (muscles != null)
            foreach (var mu in muscles)
                if (mu != null) { mu.lastJointDriveRotationWeight = -1f; mu.lastRotationDamper = -1f; }
        Fly.Rezero(root);
        // only a floating nate needs his animation back (else he comes back standing in the water); on land the game
        // picks its own animation, as it always did
        if (floating) try { Animators(root, anims, aps); } catch (Exception e) { Core.Inst.LoggerInstance.Warning("savestate: animators: " + e.Message); }
        Physics.SyncTransforms();
        foreach (var t in ts)
        {
            var rb = t.GetComponent<Rigidbody>();
            if (rb != null) { rb.position = t.position; rb.rotation = t.rotation; }
        }
        foreach (var (rb, v, w) in vel) if (!rb.isKinematic) { rb.velocity = v; rb.angularVelocity = w; }
        // what he holds or wears outside the rig comes along
        var delta = pm.hipPivot.position - hipBefore;
        if (pm.handItems != null) foreach (var g in pm.handItems) Along(g, root, delta);
        Along(pm.currentHat, root, delta);
        try { Holding(pm, hands, hat); } catch (Exception e) { Core.Inst.LoggerInstance.Warning("savestate: hands: " + e.Message); }
        Hud.GlitchStage = glitch;
        Routes.Restore(route);
        if (skipped > 0) Core.Inst.LoggerInstance.Msg($"savestate: {skipped} values couldn't go back");
        // an animator left off by an older load (see animatorDisabled above) comes back on
        try
        {
            var puppet = pm.pm;
            var an = puppet != null ? puppet.targetAnimator : null;
            if (an != null && !an.enabled && !puppet.animatorDisabled) { an.enabled = true; DebugLog.Write("savestate: nate's animator was stuck off, switched back on"); }
        }
        catch (Exception e) { Core.Inst.LoggerInstance.Warning("savestate: animator: " + e.Message); }
    }

    static void Animators(Transform root, List<(int a, int layer, int hash, float time, float weight)> anims, List<(int a, int hash, int type, string value)> aps)
    {
        if (anims.Count == 0) return;
        var all = root.GetComponentsInChildren<Animator>(true);
        foreach (var (a, hash, type, value) in aps)
        {
            if (a >= all.Length || all[a] == null) continue;
            var an = all[a];
            switch ((AnimatorControllerParameterType)type)
            {
                case AnimatorControllerParameterType.Float: an.SetFloat(hash, float.Parse(value, C)); break;
                case AnimatorControllerParameterType.Int: an.SetInteger(hash, int.Parse(value, C)); break;
                case AnimatorControllerParameterType.Bool: an.SetBool(hash, value == "1"); break;
            }
        }
        var played = new HashSet<int>();
        foreach (var (a, layer, hash, time, weight) in anims)
        {
            if (a >= all.Length || all[a] == null || layer >= all[a].layerCount) continue;
            var an = all[a];
            if (layer > 0) an.SetLayerWeight(layer, weight);
            an.Play(hash, layer, time);
            played.Add(a);
        }
        // posed now, not next frame (the ragdoll maps onto it)
        foreach (int a in played) all[a].Update(0f);
    }

    static void Along(Grabable g, Transform root, Vector3 delta)
    {
        if (g == null || g.transform.IsChildOf(root)) return;
        g.transform.position += delta;
        foreach (var rb in g.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.position = rb.transform.position;
            if (!rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }
    }

    // ---- PlayerMovement's and FootData's own values, by reflection over the interop properties ----

    static readonly Dictionary<Type, Dictionary<string, PropertyInfo>> propCache = new();

    // bookkeeping and settings that aren't nate's state: left as they are
    static readonly HashSet<string> NotState = new()
    {
        "jobScheduled", "freshFrame", "updates", "fDelt", "playingBack", "debuggingFeet", "drawBalanceLines", "_showFPS", "showFPS",
        "qedToggle", "invertX", "invertY", "mouseSpeed", "cameraSensitivity", "currentlyUsingKeyboard", "beaconShadowsOn",
        "inFlyCam", "inCutscene", "exitingCutscene", "inFirstPerson", "lookingThroughScope", "prevLookingThroughScope", "eyesScopable",
        "holdingPedometer", "cameraButtonPrompting", "handCamIndex", "stopGetUpRecording", "sampledGetUp", "internalCollisions",
        "timeSinceHatPutOn", "timeSinceFloating",
        "timeOfFirstUpdate", "instanceID", "triangleCount", "vertexCount",   // water objects' own bookkeeping
        // the game swaps nate's materials when these flip (see-through while the camera is inside him): setting the flag
        // alone left him see-through for good. and a running job's handle
        "seeThroughNate", "footJob",
        // PuppetMaster's frame flags: each physics step it switches nate's animator off (animatorDisabled on) and the
        // frame's Update switches it back on. saved in a frame without a physics step (paused, slow-mo) they read off;
        // put back while the animator was off, nothing ever switched it on again: a frozen animation the ragdoll kept
        // following (the disjoint)
        "animatorDisabled", "fixedFrame", "readInFixedUpdate", "sampleTargetMappedState",
        "WaterFlows", "WaterHeights", "WaterNormals", "WorldVertices", "splashParams", "hit", "curSlosh",   // their buffers and effects
        // what the ragdoll last handed to its joints and colliders, kept so it only hands them over again on a change
        // (Muscle.MuscleRotation: the joint's strength; PuppetMaster: angular limits, internal collisions, solver
        // iterations). put back, they said the state's values while the joints kept the ones from before the load,
        // and with nothing changing after, never got them: limbs as stiff as before the load (×3 if he was floating)
        "lastJointDriveRotationWeight", "lastRotationDamper", "angularLimitsEnabled", "internalCollisionsEnabled", "lastSolverIterationCount",
        // Time.time of the muscle's last read / write: from another session they can lie ahead of the clock, and the
        // mapped velocity isn't worked out again till the clock passes them
        "lastReadTime", "lastWriteTime",
    };

    // what's saved, by name: PlayerMovement, each foot, the PuppetMaster, its puppet behaviour, each muscle and its props
    static IEnumerable<(string, object, Type)> Parts(PlayerMovement pm)
    {
        yield return ("pm", pm, typeof(PlayerMovement));
        var feet = pm.feet;
        if (feet != null)
            for (int k = 0; k < feet.Length; k++)
                if (feet[k] != null) yield return ("foot" + k.ToString(C), feet[k], typeof(FootData));
        var puppet = pm.pm;
        if (puppet == null) yield break;
        yield return ("puppet", puppet, typeof(Il2CppRootMotion.Dynamics.PuppetMaster));
        var bs = puppet.behaviours;
        if (bs != null)
            for (int k = 0; k < bs.Length; k++)
            {
                var bp = bs[k] != null ? bs[k].TryCast<Il2CppRootMotion.Dynamics.BehaviourPuppet>() : null;
                if (bp != null) yield return ("behaviour" + k.ToString(C), bp, typeof(Il2CppRootMotion.Dynamics.BehaviourPuppet));
            }
        var ms = puppet.muscles;
        if (ms != null)
            for (int k = 0; k < ms.Length; k++)
            {
                if (ms[k] == null) continue;
                yield return ("muscle" + k.ToString(C), ms[k], typeof(Il2CppRootMotion.Dynamics.Muscle));
                if (ms[k].props != null) yield return ("mprops" + k.ToString(C), ms[k].props, typeof(Il2CppRootMotion.Dynamics.Muscle.Props));
            }
        var wos = pm.waterObjects;
        if (wos != null)
            for (int k = 0; k < wos.Length; k++)
                if (wos[k] != null) yield return ("water" + k.ToString(C), wos[k], typeof(WaterObject));
    }

    // the interop's field properties (not the game's own get/set methods, which can do more than store a value) of
    // plain types, enums, and structs of those (saved field by field as "name.field")
    static Dictionary<string, PropertyInfo> Props(Type type)
    {
        if (propCache.TryGetValue(type, out var d)) return d;
        d = new Dictionary<string, PropertyInfo>();
        foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0 || NotState.Contains(p.Name)) continue;
            if (Array.Exists(p.GetGetMethod().GetCustomAttributes(false), a => a.GetType().Name == "CallerCountAttribute")) continue;
            var t = p.PropertyType;
            if (Plain(t) || StructFields(t).Length > 0) d[p.Name] = p;
        }
        propCache[type] = d;
        return d;
    }

    static bool Plain(Type t) => t == typeof(float) || t == typeof(int) || t == typeof(uint) || t == typeof(bool) || t == typeof(Vector2)
        || t == typeof(Vector3) || t == typeof(Quaternion) || t.IsEnum;

    static readonly Dictionary<Type, FieldInfo[]> structCache = new();

    static FieldInfo[] StructFields(Type t)
    {
        if (structCache.TryGetValue(t, out var f)) return f;
        f = !t.IsValueType || t.IsPrimitive || Plain(t) ? Array.Empty<FieldInfo>()
            : Array.FindAll(t.GetFields(BindingFlags.Instance | BindingFlags.Public), x => Plain(x.FieldType));
        structCache[t] = f;
        return f;
    }

    static IEnumerable<(string, string)> Values(object o, Type type)
    {
        foreach (var p in Props(type).Values)
        {
            object v;
            try { v = p.GetValue(o); } catch { continue; }
            if (Plain(p.PropertyType)) { var s = Fmt(v); if (s != null) yield return (p.Name, s); continue; }
            foreach (var f in StructFields(p.PropertyType))
            {
                var s = Fmt(f.GetValue(v));
                if (s != null) yield return (p.Name + "." + f.Name, s);
            }
        }
    }

    static string Fmt(object v) => v switch
    {
        float x => x.ToString("R", C),
        int x => x.ToString(C),
        uint x => x.ToString(C),
        bool x => x ? "1" : "0",
        Vector2 x => x.x.ToString("R", C) + " " + x.y.ToString("R", C),
        Vector3 x => V(x),
        Quaternion x => Q(x),
        Enum x => Convert.ToInt64(x, C).ToString(C),
        _ => null,
    };

    static object Parse(Type t, string value)
    {
        if (t == typeof(float)) return float.Parse(value, C);
        if (t == typeof(int)) return int.Parse(value, C);
        if (t == typeof(uint)) return uint.Parse(value, C);
        if (t == typeof(bool)) return value == "1";
        if (t.IsEnum) return Enum.ToObject(t, long.Parse(value, C));
        var n = Nums(value);
        return t == typeof(Vector2) ? new Vector2(n[0], n[1]) : t == typeof(Vector3) ? new Vector3(n[0], n[1], n[2]) : new Quaternion(n[0], n[1], n[2], n[3]);
    }

    static bool Set(object o, Dictionary<string, PropertyInfo> props, string name, string value)
    {
        int dot = name.IndexOf('.');
        if (!props.TryGetValue(dot < 0 ? name : name.Substring(0, dot), out var p)) return false;
        try
        {
            if (dot < 0) { p.SetValue(o, Parse(p.PropertyType, value)); return true; }
            var f = Array.Find(StructFields(p.PropertyType), x => x.Name == name.Substring(dot + 1));
            if (f == null) return false;
            object box = p.GetValue(o);
            f.SetValue(box, Parse(f.FieldType, value));
            p.SetValue(o, box);
            return true;
        }
        catch { return false; }
    }

    static string V(Vector3 v) => v.x.ToString("R", C) + " " + v.y.ToString("R", C) + " " + v.z.ToString("R", C);
    static string F(float f) => f.ToString("R", C);
    static string Q(Quaternion q) => q.x.ToString("R", C) + " " + q.y.ToString("R", C) + " " + q.z.ToString("R", C) + " " + q.w.ToString("R", C);
    static Vector3 ParseV(string s) { var n = Nums(s); return new Vector3(n[0], n[1], n[2]); }

    static float[] Nums(string s)
    {
        var f = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var n = new float[f.Length];
        for (int k = 0; k < f.Length; k++) n[k] = float.Parse(f[k], C);
        return n;
    }
}
