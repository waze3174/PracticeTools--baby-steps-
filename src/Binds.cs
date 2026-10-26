using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace PracticeTools;

// the remappable controls: a keyboard key, and a gamepad button pressed while B is held (the game leaves the d-pad
// free; holding B keeps them apart from other mods' single presses). the gamepad goes through the game's own input
// (Rewired), by button name. also reads sticks and triggers for flying, and mutes the minimap mod's gamepad input
// while B is held, since it listens to the d-pad too
static class Binds
{
    internal class Bind
    {
        public string Name;
        public MelonPreferences_Entry<string> Key, Pad;
        public string DefKey, DefPad;
        public KeyCode KeyCode => Enum.TryParse<KeyCode>(Key.Value, true, out var k) ? k : KeyCode.None;
    }

    internal static Bind Fly, Save, Load;
    internal static readonly List<Bind> All = new();
    internal static MelonPreferences_Entry<string> Modifier;

    static Bind Add(string id, string name, string key, string pad)
    {
        var b = new Bind
        {
            Name = name, DefKey = key, DefPad = pad,
            Key = Core.Cat.CreateEntry(id + "Key", key, name + " key", "Keyboard key (Unity KeyCode name: F1, G, Alpha1...)."),
            Pad = Core.Cat.CreateEntry(id + "Pad", pad, name + " button", "Gamepad button, pressed while the modifier is held (Rewired name: D-Pad Up, Y, Right Stick Button...)."),
        };
        All.Add(b);
        return b;
    }

    internal static void Init()
    {
        Modifier = Core.Cat.CreateEntry("PadModifier", "B", "Gamepad modifier", "Hold this gamepad button, then press a control's button.");
        Fly = Add("Fly", "Fly", "F1", "D-Pad Up");
        Save = Add("SaveSpot", "Save spot", "F9", "D-Pad Left");
        Load = Add("LoadSpot", "Load spot", "F10", "D-Pad Right");
    }

    internal static void ResetAll()
    {
        foreach (var b in All) { b.Key.Value = b.DefKey; b.Pad.Value = b.DefPad; }
        Core.Changed();
    }

    internal static string Show(Bind b) => $"{b.Key.Value} / {Modifier.Value} + {b.Pad.Value}";

    // ---- this frame ----

    static readonly HashSet<string> down = new(StringComparer.OrdinalIgnoreCase), wasHeld = new(StringComparer.OrdinalIgnoreCase);
    internal static bool ModHeld;

    // once a frame, before anything asks: which bound buttons went down
    internal static void Update()
    {
        down.Clear();
        ModHeld = Button(Modifier.Value);
        foreach (var b in All)
        {
            bool held = ModHeld && Button(b.Pad.Value);
            if (held && !wasHeld.Contains(b.Pad.Value)) down.Add(b.Pad.Value);
            if (held) wasHeld.Add(b.Pad.Value); else wasHeld.Remove(b.Pad.Value);
        }
        CaptureTick();
    }

    internal static bool Pressed(Bind b) => Capturing == null && (KeyDown(b) || down.Contains(b.Pad.Value));
    static bool KeyDown(Bind b) { var k = b.KeyCode; return k != KeyCode.None && Input.GetKeyDown(k); }
    internal static bool IsBoundKey(KeyCode k) { foreach (var b in All) if (b.KeyCode == k) return true; return false; }

    // ---- the gamepad, by Rewired element name ----

    static readonly Dictionary<(int, string), int> ids = new();

    static IEnumerable<Il2CppRewired.Joystick> Pads()
    {
        var pm = Il2Cpp.PlayerMovement.me;
        if (pm == null || pm.rwPlayer == null) yield break;
        var c = pm.rwPlayer.controllers;
        for (int i = 0; i < c.joystickCount; i++) yield return c.Joysticks[i];
    }

    static int Id(Il2CppRewired.Joystick j, string name)
    {
        if (ids.TryGetValue((j.id, name), out int id)) return id;
        id = -1;
        var els = j.ElementIdentifiers;
        int n = els.Cast<Il2CppSystem.Collections.Generic.ICollection<Il2CppRewired.ControllerElementIdentifier>>().Count;
        for (int k = 0; k < n; k++)
            if (string.Equals(els[k].name, name, StringComparison.OrdinalIgnoreCase)) { id = els[k].id; break; }
        ids[(j.id, name)] = id;
        return id;
    }

    internal static bool Button(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var j in Pads()) { int id = Id(j, name); if (id >= 0 && j.GetButtonById(id)) return true; }
        return false;
    }

    internal static float Axis(string name)
    {
        float v = 0f;
        foreach (var j in Pads()) { int id = Id(j, name); if (id >= 0) { float a = j.GetAxisById(id); if (Mathf.Abs(a) > Mathf.Abs(v)) v = a; } }
        return Mathf.Abs(v) < 0.15f ? 0f : v;   // dead zone
    }

    // ---- remapping: the next key or button pressed becomes the bind ----

    internal static Bind Capturing;
    internal static float CaptureLeft;
    static float captureStart;
    static Action onCaptured;
    static readonly HashSet<string> heldAtStart = new(StringComparer.OrdinalIgnoreCase);
    static KeyCode[] keys;
    // buttons that run the menu itself (select, back, pause) can't be bound
    static readonly HashSet<string> menuButtons = new(StringComparer.OrdinalIgnoreCase) { "A", "B", "Start", "Guide" };

    internal static void Capture(Bind b, Action done)
    {
        Capturing = b; onCaptured = done; captureStart = Time.unscaledTime;
        heldAtStart.Clear();
        foreach (var n in PadButtonNames()) if (Button(n)) heldAtStart.Add(n);
    }

    static void CaptureTick()
    {
        if (Capturing == null) return;
        ownRead = true;
        try { CaptureStep(); } finally { ownRead = false; }
    }

    static void CaptureStep()
    {
        float t = Time.unscaledTime - captureStart;
        CaptureLeft = 5f - t;
        if (CaptureLeft <= 0f) { Finish(); return; }
        if (t < 0.3f) return;   // the press that started it
        keys ??= Array.FindAll((KeyCode[])Enum.GetValues(typeof(KeyCode)), k =>
            k != KeyCode.None && !k.ToString().StartsWith("Mouse") && !k.ToString().StartsWith("Joystick"));
        foreach (var k in keys)
        {
            if (!Input.GetKeyDown(k)) continue;
            if (k != KeyCode.Escape && k != KeyCode.Return && k != KeyCode.KeypadEnter) { Capturing.Key.Value = k.ToString(); Core.Changed(); }
            Finish();
            return;
        }
        foreach (var n in PadButtonNames())
        {
            bool held = Button(n);
            if (!held) { heldAtStart.Remove(n); continue; }
            if (heldAtStart.Contains(n) || menuButtons.Contains(n) || string.Equals(n, Modifier.Value, StringComparison.OrdinalIgnoreCase)) continue;
            Capturing.Pad.Value = n; Core.Changed();
            Finish();
            return;
        }
    }

    static void Finish()
    {
        Capturing = null;
        muteUntil = Time.frameCount + 1;   // the press that ended it mustn't reach the menu either
        var a = onCaptured; onCaptured = null;
        a?.Invoke();
    }

    // while a remap waits, the menu (and the game) see no keys and no gamepad, so back / escape don't close the page
    static bool ownRead;
    static int muteUntil = -1;
    static bool Muted => !ownRead && (Capturing != null || Time.frameCount <= muteUntil);

    [HarmonyPatch]
    static class MuteKeys
    {
        [HarmonyPrefix, HarmonyPatch(typeof(Input), nameof(Input.GetKey), typeof(KeyCode))]
        static bool GetKey(ref bool __result) { if (!Muted) return true; __result = false; return false; }

        [HarmonyPrefix, HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), typeof(KeyCode))]
        static bool GetKeyDown(ref bool __result) { if (!Muted) return true; __result = false; return false; }
    }

    [HarmonyPatch]
    static class MutePadButtons
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var n in new[] { "GetButton", "GetButtonDown", "GetButtonUp", "GetButtonRepeating", "GetNegativeButton", "GetNegativeButtonDown", "GetNegativeButtonRepeating" })
            {
                var m = AccessTools.Method(typeof(Il2CppRewired.Player), n, new[] { typeof(int) });
                if (m != null) yield return m;
            }
        }
        static bool Prefix(ref bool __result) { if (!Muted) return true; __result = false; return false; }
    }

    [HarmonyPatch(typeof(Il2CppRewired.Player), nameof(Il2CppRewired.Player.GetAxis), typeof(int))]
    static class MutePadAxis
    {
        static bool Prefix(ref float __result) { if (!Muted) return true; __result = 0f; return false; }
    }

    static List<string> padNames;
    static IEnumerable<string> PadButtonNames()
    {
        if (padNames == null)
        {
            foreach (var j in Pads())
            {
                padNames = new List<string>();
                var els = j.ElementIdentifiers;
                int n = els.Cast<Il2CppSystem.Collections.Generic.ICollection<Il2CppRewired.ControllerElementIdentifier>>().Count;
                for (int k = 0; k < n; k++)
                    if (els[k].elementType == Il2CppRewired.ControllerElementType.Button) padNames.Add(els[k].name);
                break;
            }
        }
        return padNames ?? (IEnumerable<string>)Array.Empty<string>();
    }

    // ---- the minimap mod reads the d-pad straight from the pad every frame; while B is held it's ours. it also
    // doesn't know about the title screen or cutscenes (hat ones too), so it's muted there as well ----

    internal static void MuteMinimap(HarmonyLib.Harmony h, MelonLogger.Instance log)
    {
        var t = AccessTools.TypeByName("BabyStepsMinimap.Core");
        if (t == null) return;
        int n = 0;
        foreach (var name in new[] { "HandleGameplayInput", "HandleHelpInput" })
        {
            var m = AccessTools.Method(t, name);
            if (m == null) continue;
            h.Patch(m, prefix: new HarmonyMethod(typeof(Binds), nameof(MinimapPrefix)));
            n++;
        }
        if (n > 0) log.Msg($"minimap's gamepad input is muted while {Modifier.Value} is held, on the title screen and in cutscenes");
    }

    static bool MinimapPrefix() => !ModHeld && !MinimapOff();

    static bool MinimapOff()
    {
        var m = Il2Cpp.Menu.me;
        if (Il2Cpp.PlayerMovement.me == null || m == null) return true;
        return m.titleAreaActive || m.inCutscene || m.inNonInteractiveCutscene || m.playingCutscene != null;
    }
}
