using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace PracticeTools;

// F1 / B + d-pad up fly cam (the game's own, moved with WASD, space / ctrl (or E / Q) up / down, shift faster, right
// mouse button much faster, scroll for speed, alt + scroll for zoom; on a gamepad left stick, RT / LT up / down, L3
// faster, right stick to look).
// two modes: free cam (left click teleports nate to it) or carry (nate hangs in front of the camera, no physics or
// collisions, dropped where he is on F1 again). F9 saves nate's spot, F10 teleports him back to it, per save slot.
// the keys and buttons are remappable (Binds)
static class Fly
{
    internal static bool Flying;
    static float speedMult = 1f;
    const float BaseSpeed = 10f;

    internal static void Install(HarmonyLib.Harmony h)
    {
        // BlockKeys is patched with the rest of the [HarmonyPatch] classes by MelonLoader
        h.Patch(AccessTools.Method(typeof(Menu), nameof(Menu.Teleport)), prefix: new HarmonyMethod(typeof(Fly), nameof(TeleportPrefix)));
    }

    internal static void Update()
    {
        var pm = PlayerMovement.me;
        if (pm == null) { Flying = false; return; }
        if (Flying && !pm.inFlyCam) { Flying = false; if (Carrying) Drop(pm); }   // the game left fly cam on its own (death, cutscene, load)

        if (Binds.Pressed(Binds.Fly))
        {
            bool aim = Carrying && pm.flyCam != null;
            var flyRot = aim ? pm.flyCam.transform.rotation : Quaternion.identity;
            if (Carrying) Drop(pm);
            pm.ToggleFlyCam(); Flying = pm.inFlyCam;
            if (Flying && Core.CarryMode.Value) Pick(pm);
            if (aim && !Flying) AimLike(pm, flyRot);
        }
        AimTick(pm);

        if (Flying && pm.flyCam != null) { Move(pm.flyCam.transform); Scroll(pm, true); if (Carrying) Carry(pm); }
        else Scroll(pm, false);
        UnfreezeTick();
        if (queued.HasValue && Menu.me != null && !Menu.me.paused) { var q = queued.Value; queued = null; Teleport(q); }
    }

    // from the pause menu: unpause first, teleport once the game runs again
    static Vector3? queued;
    internal static void TeleportFromMenu(Vector3 p)
    {
        queued = p;
        if (Menu.me != null && Menu.me.paused) Menu.me.UnPause();
    }

    // our own key reads while flying get past BlockKeys (the game's don't)
    static bool own;

    static void Move(Transform t)
    {
        own = true;
        try { MoveCam(t); } finally { own = false; }
    }

    static void MoveCam(Transform t)
    {
        var dir = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) dir += Vector3.forward;
        if (Input.GetKey(KeyCode.S)) dir += Vector3.back;
        if (Input.GetKey(KeyCode.A)) dir += Vector3.left;
        if (Input.GetKey(KeyCode.D)) dir += Vector3.right;
        if (Input.GetKey(KeyCode.E)) dir += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) dir += Vector3.down;
        // space and ctrl only move ours (the game's fly cam doesn't read space, and reads ctrl as a speed change)
        if (Input.GetKey(KeyCode.Space)) dir += Vector3.up;
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) dir += Vector3.down;
        if (dir.sqrMagnitude > 1f) dir.Normalize();
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool fast = Input.GetMouseButton(1);
        float mult = shift && fast ? 7f : fast ? 5f : shift ? 2f : 1f;
        t.position += Along(t, dir) * (BaseSpeed * speedMult * mult * Time.unscaledDeltaTime);

        // gamepad: left stick moves, RT up / LT down, L3 held 5x faster, right stick looks (the game's fly cam only
        // reads the mouse; it takes its angles back from the transform each frame, so turning it here stays)
        var pad = new Vector3(Binds.Axis("Left Stick X"), Binds.Axis("Right Trigger") - Binds.Axis("Left Trigger"), Binds.Axis("Left Stick Y"));
        if (pad.sqrMagnitude > 1f) pad.Normalize();
        float padMult = Binds.Button("Left Stick Button") ? 5f : 1f;
        t.position += Along(t, pad) * (BaseSpeed * speedMult * padMult * Time.unscaledDeltaTime);

        // the game's camera invert settings: the fly cam turns with the mouse as is, so an inverted axis gets turned
        // back twice as far here; the stick is read here, so it's just flipped
        var pm = PlayerMovement.me;
        bool invX = pm != null && pm.invertX, invY = pm != null && pm.invertY;
        if ((invX || invY) && Menu.me != null && !Menu.me.paused)
        {
            float mx = invX ? Input.GetAxis("Mouse X") : 0f, my = invY ? Input.GetAxis("Mouse Y") : 0f;
            if (mx != 0f || my != 0f)
            {
                var e = t.eulerAngles;
                float pitch = e.x > 180f ? e.x - 360f : e.x;
                t.eulerAngles = new Vector3(Mathf.Clamp(pitch + 2f * my, -89f, 89f), e.y - 2f * mx, e.z);
            }
        }
        float lx = Binds.Axis("Right Stick X") * (invX ? -1f : 1f), ly = Binds.Axis("Right Stick Y") * (invY ? -1f : 1f);
        if (lx != 0f || ly != 0f)
        {
            var e = t.eulerAngles;
            float pitch = e.x > 180f ? e.x - 360f : e.x;
            pitch = Mathf.Clamp(pitch - ly * LookSpeed * Time.unscaledDeltaTime, -85f, 85f);
            t.eulerAngles = new Vector3(pitch, e.y + lx * LookSpeed * Time.unscaledDeltaTime, e.z);
        }
    }

    const float LookSpeed = 120f;   // degrees a second at full stick

    // forward / sideways go where the camera looks; up / down are the world's up and down
    static Vector3 Along(Transform t, Vector3 dir) => t.TransformDirection(new Vector3(dir.x, 0f, dir.z)) + Vector3.up * dir.y;

    static void Scroll(PlayerMovement pm, bool flying)
    {
        float s = Input.GetAxis("Mouse ScrollWheel");
        if (s == 0f) return;
        if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) { if (s > 0f) pm.IncreaseFlyCamFOV(); else pm.DecreaseFlyCamFOV(); }
        else if (flying) speedMult = Mathf.Clamp(speedMult * (s > 0f ? 1.2f : 0.8f), 0.1f, 10f);
    }

    // ---- after a carry the game camera looks where the fly camera looked ----
    // the game camera orbits nate by PlayerMovement.camAng (yaw) and camOrbit (up / down); measured in recordings, it
    // looks at yaw camAng + 20 (camAng - 20 over the left shoulder, camRightAmount 1) and pitch 40 - 118 * camOrbit
    // degrees up (camOrbit 0 is the highest it goes). set again for a few frames in case leaving fly cam resets it
    static float aimYaw, aimOrbit;
    static int aimFrames;

    static void AimLike(PlayerMovement pm, Quaternion rot)
    {
        var f = rot * Vector3.forward;
        float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        float up = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
        aimYaw = Mathf.Repeat(yaw - 20f + 40f * pm.camRightAmount, 360f);
        aimOrbit = Mathf.Clamp((40f - up) / 118f, 0f, 1f);
        aimFrames = 3;
        AimTick(pm);
    }

    static void AimTick(PlayerMovement pm)
    {
        if (aimFrames <= 0) return;
        aimFrames--;
        pm.camAng = aimYaw;
        pm.camOrbit = aimOrbit;
    }

    // ---- carry: nate goes where the camera goes ----
    // his whole rig (the animated body and the ragdoll under one parent) is moved by the camera's movement each frame,
    // the ragdoll kinematic with collisions off and his walking code paused. what he holds and his hat live outside
    // that rig: they get the same treatment and are moved along with him. on drop all of it comes back, velocities 0

    internal static bool Carrying;
    static Transform root;
    static Quaternion rootRot;   // the rig's rotation when picked up: the game expects it back
    static Vector3 lastCam;
    static Quaternion lastCamRot;
    static readonly List<(Rigidbody rb, bool kinematic, bool collide)> bodies = new();
    static readonly List<(Collider c, bool on)> colliders = new();
    static readonly List<Transform> along = new();   // held items and hat that aren't inside the rig
    static Il2CppRootMotion.Dynamics.PuppetMaster.Mode puppetMode;

    static void Pick(PlayerMovement pm)
    {
        var cam = pm.flyCam != null ? pm.flyCam.transform : null;
        var puppet = pm.pm;
        if (cam == null || puppet == null) return;
        root = CommonParent(pm.transform, puppet.transform);
        if (root == null) { Core.Inst.LoggerInstance.Warning("carry: no common parent for nate's rig"); return; }
        DebugLog.Write($"carry: moving {PathOf(root)}");
        if (cam.IsChildOf(root)) { Core.Inst.LoggerInstance.Warning("carry: the fly camera is inside nate's rig, can't carry"); root = null; return; }
        rootRot = root.rotation;
        puppetMode = puppet.mode;
        puppet.mode = Il2CppRootMotion.Dynamics.PuppetMaster.Mode.Kinematic;
        bodies.Clear(); colliders.Clear();
        Freeze(root, false);
        along.Clear();
        if (pm.handItems != null)
            foreach (var it in pm.handItems) Carried(it);
        Carried(pm.currentHat);
        pm.enabled = false;
        lastCam = cam.position; lastCamRot = cam.rotation;
        Carrying = true;
    }

    static void Carry(PlayerMovement pm)
    {
        var cam = pm.flyCam.transform;
        if (root == null) { Carrying = false; return; }
        // nate keeps his place in front of the camera: turn him around it with its yaw, move him with it
        float yaw = Mathf.DeltaAngle(lastCamRot.eulerAngles.y, cam.rotation.eulerAngles.y);
        root.RotateAround(lastCam, Vector3.up, yaw);
        root.position += cam.position - lastCam;
        foreach (var t in along)
        {
            if (t == null) continue;
            t.RotateAround(lastCam, Vector3.up, yaw);
            t.position += cam.position - lastCam;
        }
        lastCam = cam.position; lastCamRot = cam.rotation;
        Physics.SyncTransforms();
    }

    static void Drop(PlayerMovement pm)
    {
        Carrying = false;
        // nate is dropped as he hangs, turned with the camera. the rig's root was turned too; left turned, he stands
        // slanted (the game takes its up from it) until a teleport. so only the root turns back, his parts keep their
        // place, and the game's heading for his hips turns with him
        if (root != null)
        {
            float turned = Mathf.DeltaAngle(rootRot.eulerAngles.y, root.rotation.eulerAngles.y);
            var kids = new List<(Transform t, Vector3 p, Quaternion r)>();
            for (int k = 0; k < root.childCount; k++) { var c = root.GetChild(k); kids.Add((c, c.position, c.rotation)); }
            root.rotation = rootRot;
            foreach (var (t, p, r) in kids) t.SetPositionAndRotation(p, r);
            pm.hipAng = Mathf.Repeat(pm.hipAng + turned, 360f);
            pm.fpsEulY = Mathf.Repeat(pm.fpsEulY + turned, 360f);
        }
        Rezero(root);
        foreach (var (rb, kinematic, collide) in bodies)
        {
            if (rb == null) continue;
            rb.isKinematic = kinematic; rb.detectCollisions = collide;
            if (!kinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }
        bodies.Clear();
        foreach (var (c, on) in colliders) if (c != null) c.enabled = on;
        colliders.Clear();
        along.Clear();
        if (pm.pm != null) pm.pm.mode = puppetMode;
        pm.enabled = true;
        if (pm.anim != null && pm.anim.speed == 0f) pm.anim.speed = 1f;
        Physics.SyncTransforms();
        try { pm.ResetGroundables(); } catch (System.Exception e) { Core.Inst.LoggerInstance.Warning("carry: " + e.Message); }
    }

    // an item nate holds or wears: no physics, no collisions while carried; moved along unless it's inside the rig
    static void Carried(Grabable g)
    {
        if (g == null) return;
        var t = g.transform;
        if (!t.IsChildOf(root)) { Freeze(t, true); along.Add(t); }
        else foreach (var c in t.GetComponentsInChildren<Collider>(true)) { colliders.Add((c, c.enabled)); c.enabled = false; }
    }

    // every rigidbody under t kinematic with collisions off (and, for items, every collider off), remembering how they were
    static void Freeze(Transform t, bool collidersToo)
    {
        foreach (var rb in t.GetComponentsInChildren<Rigidbody>(true))
        {
            if (!rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            bodies.Add((rb, rb.isKinematic, rb.detectCollisions));
            rb.isKinematic = true; rb.detectCollisions = false;
        }
        if (collidersToo)
            foreach (var c in t.GetComponentsInChildren<Collider>(true)) { colliders.Add((c, c.enabled)); c.enabled = false; }
    }

    // the game keeps nate's rig root (Dudest) at the world origin and some of its code counts on that: moved, his
    // balance and swim targets end up off by the offset and the ragdoll gets pulled apart (worst while he floats).
    // whatever moved it (carry, a state saved after one), the root goes back and its children keep their place
    internal static void Rezero(Transform root)
    {
        if (root == null || root.parent != null) return;
        var off = root.position;
        if (off.sqrMagnitude < 1e-6f) return;
        var kids = new List<(Transform t, Vector3 p, Quaternion r)>();
        for (int k = 0; k < root.childCount; k++) { var c = root.GetChild(k); kids.Add((c, c.position, c.rotation)); }
        root.position = Vector3.zero;
        foreach (var (t, p, r) in kids) t.SetPositionAndRotation(p, r);
        Physics.SyncTransforms();
        DebugLog.Write($"nate's rig root was {off} off the origin: put back");
    }

    internal static Transform CommonParent(Transform a, Transform b)
    {
        for (var x = a; x != null; x = x.parent)
            for (var y = b; y != null; y = y.parent)
                if (x == y) return x;
        return null;
    }

    static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

    internal static void Teleport(Vector3 p)
    {
        var m = Menu.me;
        if (m == null || m.teleporting) return;
        m.Teleport(p, false);
    }

    // ---- BabyBlocks cuts long teleports short ----
    // BabyBlocks clears Menu.teleporting when it stays on for 8 s, its fix for its own stuck loads. loading a save or a
    // long teleport can take longer than that, and cut off it hangs on the loading screen or leaves the camera stuck.
    // so its check waits until the flag has been on for 90 s. its other fix (the area loader's lock, after 6 s) is
    // left alone: never force that lock open, the loader's jobs then run over each other and load things twice
    // (nate falls through the world). stuck loads are mended by LoadFix instead

    const float RealLoadTime = 90f;
    static float teleportingSince = -1f;

    // once a frame
    internal static void TrackLoads()
    {
        Since(Menu.me != null && Menu.me.teleporting, ref teleportingSince);
        if (teleportingSince >= 0f && Time.unscaledTime - teleportingSince > 10f && Time.unscaledTime - lastLoaderReport > 10f)
        {
            lastLoaderReport = Time.unscaledTime;
            LoaderReport($"teleport running {Time.unscaledTime - teleportingSince:0} s");
        }
        else if (teleportingSince < 0f) lastLoaderReport = -100f;
    }

    // what's still loading around the loader's spot: area chunks (the ground), prop cells (props you can touch), prop
    // visuals (looks only)
    internal static void Pending(out int chunks, out int cells, out int visuals)
    {
        chunks = cells = visuals = 0;
        var rl = BestRegionLoader.me;
        if (rl == null) return;
        var cm = rl.chunkMap;
        if (cm != null)
            for (int i = 0; i < cm.Length; i++)
            {
                var c = cm[i];
                if (c != null && c.shouldLoad && (int)c.loadState != 2) chunks++;
            }
        var pc = rl.propLoaderMap;
        if (pc != null && Native(rl, 0x208, out var want, out int wn))
            for (int i = 0; i < pc.Length && i < wn; i++)
                if (System.Runtime.InteropServices.Marshal.ReadByte(want, i) != 0 && pc[i] != null && (int)pc[i].loadState != 2) cells++;
        if (Native(rl, 0x218, out var rend, out int rn) && Native(rl, 0x1D0, out var lod, out int ln))
            for (int i = 0; i < rn && i < ln; i++)
                if (System.Runtime.InteropServices.Marshal.ReadByte(rend, i) != 0 && System.Runtime.InteropServices.Marshal.ReadInt32(lod, i * 4) != 2) visuals++;
    }

    // ---- what a hung teleport waits for ----
    // a teleport waits for BestRegionLoader.fullyLoaded, which the loader clears every frame while any of these isn't
    // done: an area chunk it wants (or wants gone), a prop cell near nate, a prop's render data. the report names them,
    // in the debug log
    static float lastLoaderReport = -100f;
    static readonly string[] StateName = { "unloaded", "loading", "loaded", "unloading" };

    internal static void LoaderReport(string why)
    {
        var rl = BestRegionLoader.me;
        var sb = new System.Text.StringBuilder();
        sb.Append($"loader ({why}): fullyLoaded {BestRegionLoader.fullyLoaded}, lock {BestRegionLoader.somebodyLoading}");
        sb.Append($" (last taken by {BestRegionLoader.whosLoading ?? "-"}, free {BestRegionLoader.framesSinceSomebodyLoading} frames)");
        if (rl == null) { sb.Append(", no loader"); Report(sb.ToString()); return; }
        var lt = rl.loadingTransform;
        sb.Append(lt != null ? $", loads around {lt.name} {lt.position}" : ", loads around nothing");
        if (rl.off) sb.Append(", loader off");

        var chunks = rl.chunkMap;
        int nc = 0;
        var cs = new System.Text.StringBuilder();
        if (chunks != null)
            for (int i = 0; i < chunks.Length; i++)
            {
                var c = chunks[i];
                if (c == null) continue;
                int st = (int)c.loadState;
                if (c.shouldLoad ? st == 2 : st == 0) continue;
                if (nc++ < 8) cs.Append($" #{c.index} {Name(st)}{(c.shouldLoad ? "" : " (wanted gone)")}");
            }
        sb.Append($"\n  chunks not done: {nc}{cs}");

        // the loader's NativeArrays aren't usable through the interop: read them raw (offsets from the game's 2022.3 build)
        var cells = rl.propLoaderMap;
        int np = 0;
        var ps = new System.Text.StringBuilder();
        if (cells != null && Native(rl, 0x208, out var want, out int wn))
            for (int i = 0; i < cells.Length && i < wn; i++)
            {
                if (System.Runtime.InteropServices.Marshal.ReadByte(want, i) == 0) continue;
                var c = cells[i];
                if (c == null || (int)c.loadState == 2) continue;
                if (np++ < 8) ps.Append($" #{c.cellIndex} {Name((int)c.loadState)}");
            }
        sb.Append($"\n  prop cells not done: {np}{ps}");

        int nl = 0;
        var ls = new System.Text.StringBuilder();
        if (Native(rl, 0x218, out var rend, out int rn) && Native(rl, 0x1D0, out var lod, out int ln))
            for (int i = 0; i < rn && i < ln; i++)
            {
                if (System.Runtime.InteropServices.Marshal.ReadByte(rend, i) == 0) continue;
                int st = System.Runtime.InteropServices.Marshal.ReadInt32(lod, i * 4);
                if (st == 2) continue;
                if (nl++ < 8) ls.Append($" #{i} {Name(st)}");
            }
        sb.Append($"\n  prop render data not done: {nl}{ls}");
        Report(sb.ToString());
    }

    static string Name(int st) => st >= 0 && st < StateName.Length ? StateName[st] : st.ToString();

    // NativeArray<T> in a field: buffer pointer, then int length
    internal static bool Native(BestRegionLoader rl, int offset, out System.IntPtr buf, out int len)
    {
        buf = System.Runtime.InteropServices.Marshal.ReadIntPtr(rl.Pointer + offset);
        len = System.Runtime.InteropServices.Marshal.ReadInt32(rl.Pointer + offset + 8);
        return buf != System.IntPtr.Zero && len > 0;
    }

    static void Report(string s) => DebugLog.Write(s);

    // BestRegionLoader.somebodyLoading is the area loader's one lock: a prop load takes it (only outside teleports) and
    // gives it back when the load ends cleanly. a load that errors keeps it. only let go when everything is reloaded
    // anyway (a full load)
    internal static void ReleaseLoaderLock(string why)
    {
        if (!BestRegionLoader.somebodyLoading) return;
        BestRegionLoader.somebodyLoading = false;
        DebugLog.Write($"the area loader's lock was stuck ({why}, a prop load that never finished): let go");
    }

    static void Since(bool on, ref float since)
    {
        if (!on) since = -1f;
        else if (since < 0f) since = Time.unscaledTime;
    }

    // BabyBlocks checks the flag every frame (Core.WatchMenuTeleporting, from its OnUpdate) and clears it after 8 s.
    // the game's flags are plain fields (their setters can't be patched), so that check is skipped while the flag has
    // been on for less than 90 s; it runs as usual otherwise
    internal static void GuardBabyBlocks(HarmonyLib.Harmony h, MelonLoader.MelonLogger.Instance log)
    {
        System.Type core = null;
        foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
            if (a.GetName().Name == "BabyBlocks")
                foreach (var t in AccessTools.GetTypesFromAssembly(a))
                    if (AccessTools.DeclaredMethod(t, "WatchMenuTeleporting") != null) { core = t; break; }
        if (core == null) return;
        var tele = AccessTools.DeclaredMethod(core, "WatchMenuTeleporting");
        h.Patch(tele, prefix: new HarmonyMethod(typeof(Fly), nameof(WatchTeleporting)));
        log.Msg($"BabyBlocks can only clear a teleport after {RealLoadTime:0} s");
    }

    // the flag as it is now (BabyBlocks' update can come before ours in a frame): off, its check runs and resets its
    // own timer; on, it waits for 90 s from when we first saw it on (or from now)
    static bool WatchTeleporting() => Allowed(Menu.me != null && Menu.me.teleporting, ref teleportingSince, "Menu.teleporting");

    static float lastBlockLog = -100f;

    static bool Allowed(bool on, ref float since, string what, float limit = RealLoadTime)
    {
        if (!on) return true;
        if (since < 0f) since = Time.unscaledTime;
        if (Time.unscaledTime - since >= limit) return true;
        // it would clear it at 8 s: say so once in a while
        if (Time.unscaledTime - since > 6f && Time.unscaledTime - lastBlockLog > 10f)
        {
            lastBlockLog = Time.unscaledTime;
            DebugLog.Write($"kept {what} on past BabyBlocks' limit ({Time.unscaledTime - since:0} s so far)");
        }
        return false;
    }

    // ---- teleporting out of fly cam leaves nate frozen ----
    // after a teleport that starts in fly cam the game leaves PlayerMovement switched off and the animator at speed 0,
    // so nate never reads input again. once the teleport is done, switch both back on. other teleports are left alone

    static bool armed;
    static float fixAt = -1f;

    static void TeleportPrefix()
    {
        var me = PlayerMovement.me;
        if (me != null && me.inFlyCam) { armed = true; fixAt = -1f; }
    }

    static void UnfreezeTick()
    {
        if (!armed) return;
        if (Menu.me != null && Menu.me.teleporting) return;
        if (fixAt < 0f) { fixAt = Time.realtimeSinceStartup + 0.25f; return; }   // let the teleport's last frame settle
        if (Time.realtimeSinceStartup < fixAt) return;
        armed = false;
        var me = PlayerMovement.me;
        if (me == null || me.dead || me.inCutscene) return;
        if (!me.enabled) me.enabled = true;
        if (me.anim != null && me.anim.speed == 0f) me.anim.speed = 1f;
    }

    // while flying, the game only sees the fly keys (not ctrl: that's down here), the mouse and left click (teleport;
    // not while carrying), and nate doesn't get the gamepad's sticks and triggers (they fly the camera)
    static bool inGetInput;

    [HarmonyPatch(typeof(PlayerMovement), "GetInput")]
    static class NateInput
    {
        static void Prefix() => inGetInput = true;
        static void Finalizer() => inGetInput = false;
    }

    [HarmonyPatch(typeof(Il2CppRewired.Player), nameof(Il2CppRewired.Player.GetAxis), typeof(int))]
    static class NoPadWhileFlying
    {
        static void Postfix(ref float __result) { if (inGetInput && Flying) __result = 0f; }
    }

    [HarmonyPatch]
    static class BlockKeys
    {
        static bool Allowed(KeyCode k) =>
            k is KeyCode.W or KeyCode.A or KeyCode.S or KeyCode.D or KeyCode.Q or KeyCode.E
              or KeyCode.LeftShift or KeyCode.RightShift
              or KeyCode.LeftAlt or KeyCode.RightAlt || Binds.IsBoundKey(k);

        static bool AllowedDown(KeyCode k) => Binds.IsBoundKey(k);

        [HarmonyPrefix, HarmonyPatch(typeof(Input), nameof(Input.GetKey), typeof(KeyCode))]
        static bool GetKey(KeyCode key, ref bool __result) { if (!Flying || own || Allowed(key)) return true; __result = false; return false; }

        [HarmonyPrefix, HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), typeof(KeyCode))]
        static bool GetKeyDown(KeyCode key, ref bool __result) { if (!Flying || own || AllowedDown(key)) return true; __result = false; return false; }

        [HarmonyPrefix, HarmonyPatch(typeof(Input), nameof(Input.GetAxis), typeof(string))]
        static bool GetAxis(string axisName, ref float __result)
        {
            if (!Flying || own || axisName is "Mouse ScrollWheel" or "Mouse X" or "Mouse Y") return true;
            __result = 0f; return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(Input), nameof(Input.GetMouseButton), typeof(int))]
        static bool GetMouseButton(int button, ref bool __result) { if (!Flying || own || button == 0 && !Carrying) return true; __result = false; return false; }

        [HarmonyPrefix, HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonDown), typeof(int))]
        static bool GetMouseButtonDown(int button, ref bool __result) { if (!Flying || own || button == 0 && !Carrying) return true; __result = false; return false; }

        [HarmonyPrefix, HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonUp), typeof(int))]
        static bool GetMouseButtonUp(int button, ref bool __result) { if (!Flying || own || button == 0 && !Carrying) return true; __result = false; return false; }
    }
}
