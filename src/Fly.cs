using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace PracticeTools;

// F1 / B + d-pad up fly cam (the game's own, moved with WASD / QE, shift and ctrl faster, scroll for speed, alt + scroll
// for zoom; on a gamepad left stick, RT / LT up / down, L3 faster, right stick to look).
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
        LoadSpots();
    }

    internal static void Update()
    {
        var pm = PlayerMovement.me;
        if (pm == null) { Flying = false; return; }
        if (Flying && !pm.inFlyCam) { Flying = false; if (Carrying) Drop(pm); }   // the game left fly cam on its own (death, cutscene, load)

        if (Binds.Pressed(Binds.Fly))
        {
            if (Carrying) Drop(pm);
            pm.ToggleFlyCam(); Flying = pm.inFlyCam;
            if (Flying && Core.CarryMode.Value) Pick(pm);
        }
        else if (Binds.Pressed(Binds.Save)) SaveSpot(pm);
        else if (Binds.Pressed(Binds.Load)) LoadSpot();

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

    static void Move(Transform t)
    {
        var dir = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) dir += Vector3.forward;
        if (Input.GetKey(KeyCode.S)) dir += Vector3.back;
        if (Input.GetKey(KeyCode.A)) dir += Vector3.left;
        if (Input.GetKey(KeyCode.D)) dir += Vector3.right;
        if (Input.GetKey(KeyCode.E)) dir += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) dir += Vector3.down;
        if (dir.sqrMagnitude > 1f) dir.Normalize();
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        float mult = shift && ctrl ? 7f : ctrl ? 5f : shift ? 2f : 1f;
        t.position += t.TransformDirection(dir) * (BaseSpeed * speedMult * mult * Time.unscaledDeltaTime);

        // gamepad: left stick moves, RT up / LT down, L3 held 5x faster, right stick looks (the game's fly cam only
        // reads the mouse; it takes its angles back from the transform each frame, so turning it here stays)
        var pad = new Vector3(Binds.Axis("Left Stick X"), Binds.Axis("Right Trigger") - Binds.Axis("Left Trigger"), Binds.Axis("Left Stick Y"));
        if (pad.sqrMagnitude > 1f) pad.Normalize();
        float padMult = Binds.Button("Left Stick Button") ? 5f : 1f;
        t.position += t.TransformDirection(pad) * (BaseSpeed * speedMult * padMult * Time.unscaledDeltaTime);
        float lx = Binds.Axis("Right Stick X"), ly = Binds.Axis("Right Stick Y");
        if (lx != 0f || ly != 0f)
        {
            var e = t.eulerAngles;
            float pitch = e.x > 180f ? e.x - 360f : e.x;
            pitch = Mathf.Clamp(pitch - ly * LookSpeed * Time.unscaledDeltaTime, -85f, 85f);
            t.eulerAngles = new Vector3(pitch, e.y + lx * LookSpeed * Time.unscaledDeltaTime, e.z);
        }
    }

    const float LookSpeed = 120f;   // degrees a second at full stick

    static void Scroll(PlayerMovement pm, bool flying)
    {
        float s = Input.GetAxis("Mouse ScrollWheel");
        if (s == 0f) return;
        if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) { if (s > 0f) pm.IncreaseFlyCamFOV(); else pm.DecreaseFlyCamFOV(); }
        else if (flying) speedMult = Mathf.Clamp(speedMult * (s > 0f ? 1.2f : 0.8f), 0.1f, 10f);
    }

    // ---- carry: nate goes where the camera goes ----
    // his whole rig (the animated body and the ragdoll under one parent) is moved by the camera's movement each frame,
    // the ragdoll kinematic with collisions off and his walking code paused. what he holds and his hat live outside
    // that rig: they get the same treatment and are moved along with him. on drop all of it comes back, velocities 0

    internal static bool Carrying;
    static Transform root;
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
        Core.Inst.LoggerInstance.Msg($"carry: moving {PathOf(root)}");
        if (cam.IsChildOf(root)) { Core.Inst.LoggerInstance.Warning("carry: the fly camera is inside nate's rig, can't carry"); root = null; return; }
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

    static Transform CommonParent(Transform a, Transform b)
    {
        for (var x = a; x != null; x = x.parent)
            for (var y = b; y != null; y = y.parent)
                if (x == y) return x;
        return null;
    }

    static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

    // ---- save / load spot ----

    static readonly Dictionary<int, Vector3> spots = new();
    static string SpotsFile => Path.Combine(Core.Dir, "spots.txt");
    static int Slot => SaveGod.me != null ? SaveGod.me.slot : 0;

    static void SaveSpot(PlayerMovement pm)
    {
        if (pm.hipPivot == null) return;
        spots[Slot] = pm.hipPivot.position;
        SaveSpots();
        Hud.Flash("SPOT SAVED");
    }

    internal static bool HasSpot => spots.ContainsKey(Slot);

    internal static void LoadSpot(bool fromMenu = false)
    {
        if (!spots.TryGetValue(Slot, out var p)) { Hud.Flash("NO SPOT SAVED (F9)"); return; }
        if (fromMenu) TeleportFromMenu(p); else Teleport(p);
    }

    internal static void Teleport(Vector3 p)
    {
        var m = Menu.me;
        if (m == null || m.teleporting) return;
        m.Teleport(p, false);
    }

    static void LoadSpots()
    {
        try
        {
            if (!File.Exists(SpotsFile)) return;
            foreach (var line in File.ReadAllLines(SpotsFile))
            {
                var f = line.Split(' ');
                if (f.Length != 4) continue;
                var c = CultureInfo.InvariantCulture;
                spots[int.Parse(f[0], c)] = new Vector3(float.Parse(f[1], c), float.Parse(f[2], c), float.Parse(f[3], c));
            }
        }
        catch { }
    }

    static void SaveSpots()
    {
        var c = CultureInfo.InvariantCulture;
        var lines = new List<string>();
        foreach (var kv in spots) lines.Add(string.Format(c, "{0} {1} {2} {3}", kv.Key, kv.Value.x, kv.Value.y, kv.Value.z));
        try { File.WriteAllLines(SpotsFile, lines); } catch { }
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

    // while flying, the game only sees the fly keys, the mouse and left click (teleport; not while carrying), and nate
    // doesn't get the gamepad's sticks and triggers (they fly the camera)
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
              or KeyCode.LeftShift or KeyCode.RightShift or KeyCode.LeftControl or KeyCode.RightControl
              or KeyCode.LeftAlt or KeyCode.RightAlt || Binds.IsBoundKey(k);

        static bool AllowedDown(KeyCode k) => Binds.IsBoundKey(k);

        [HarmonyPrefix, HarmonyPatch(typeof(Input), nameof(Input.GetKey), typeof(KeyCode))]
        static bool GetKey(KeyCode key, ref bool __result) { if (!Flying || Allowed(key)) return true; __result = false; return false; }

        [HarmonyPrefix, HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), typeof(KeyCode))]
        static bool GetKeyDown(KeyCode key, ref bool __result) { if (!Flying || AllowedDown(key)) return true; __result = false; return false; }

        [HarmonyPrefix, HarmonyPatch(typeof(Input), nameof(Input.GetAxis), typeof(string))]
        static bool GetAxis(string axisName, ref float __result)
        {
            if (!Flying || axisName is "Mouse ScrollWheel" or "Mouse X" or "Mouse Y") return true;
            __result = 0f; return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(Input), nameof(Input.GetMouseButton), typeof(int))]
        static bool GetMouseButton(int button, ref bool __result) { if (!Flying || button == 0 && !Carrying) return true; __result = false; return false; }

        [HarmonyPrefix, HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonDown), typeof(int))]
        static bool GetMouseButtonDown(int button, ref bool __result) { if (!Flying || button == 0 && !Carrying) return true; __result = false; return false; }

        [HarmonyPrefix, HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonUp), typeof(int))]
        static bool GetMouseButtonUp(int button, ref bool __result) { if (!Flying || button == 0 && !Carrying) return true; __result = false; return false; }
    }
}
