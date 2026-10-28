using System;
using System.Reflection;
using System.Runtime.InteropServices;
using BabyStepsMenuLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PracticeTools;

// typing a savestate's description: BabyStepsMenuLib's on-screen keyboard however it was opened, and a real keyboard
// types into it too (enter keeps, escape / B cancels). back only ends the typing: the game's menu gets no input while
// it's up and a few frames after, so it doesn't go back a page. without the library: typed straight into the row
// (steam's own keyboard on a gamepad)
static class Typing
{
    internal const int MaxLength = 40;
    internal static bool Active;      // typing into the row
    static bool onLib;                // the library's keyboard is up
    internal static string Text = "";
    static Action<string> done;
    static bool sendNav;
    static int startFrame;
    static MenuInjectionLibrary.InjectedMenu kb;

    internal static bool Busy => Active || onLib;
    // the game's menu stays out of it while typing and a few frames after (the back that ended it)
    internal static bool MenuBlocked => Busy || Time.frameCount <= blockUntil;
    static int blockUntil = -1;
    static MenuInjectionLibrary.InputFieldInfo field;
    internal static bool OnLib => onLib;
    internal static GameObject Row => backTo;   // the row being typed into

    // the page's gamepad list while the library's keyboard has shrunk it to one key, and the row to come back to
    static Il2Cpp.MenuItemList pageList;
    static GameObject[] pageItems;
    static GameObject backTo;
    static int giveBackFrames;

    internal static void Start(string value, bool pad, RectTransform row, Action<string> keep)
    {
        if (Busy) return;
        Text = value ?? ""; done = keep; startFrame = Time.frameCount;
        backTo = row != null ? row.gameObject : null;
        if (OpenLib(row)) { onLib = true; return; }
        Active = true;
        var es = EventSystem.current;
        if (es != null) { sendNav = es.sendNavigationEvents; es.sendNavigationEvents = false; }
        Binds.MuteFor(1);
        if (pad) SteamKeyboard(row);
    }

    static bool OpenLib(RectTransform row)
    {
        try
        {
            kb = MenuLibFix.OpenPage() ?? MenuInjectionLibrary.GetKeyboardMenu();
            if (kb == null) return false;
            pageList = MenuLibFix.ListOf(kb);
            pageItems = null;
            if (pageList != null && pageList.items != null)
            {
                pageItems = new GameObject[pageList.items.Length];
                for (int i = 0; i < pageItems.Length; i++) pageItems[i] = pageList.items[i];
            }
            var info = field = new MenuInjectionLibrary.InputFieldInfo
            {
                Value = Text,
                OnChanged = (UnityEngine.Events.UnityAction<string>)(v => Text = v.Length > MaxLength ? v.Substring(0, MaxLength) : v),
                Placeholder = "",
                // the library types into this text: the row itself
                DisplayText = row != null ? row.GetComponentInChildren<Il2CppTMPro.TextMeshProUGUI>(true) : null,
            };
            kb.OpenKeyboardForChat(info, () => { if (onLib) Finish(true); });
            return kb.IsKeyboardOpen;
        }
        catch (Exception e) { Core.Inst.LoggerInstance.Warning("menu keyboard: " + e.Message); return false; }
    }

    // once a frame
    internal static void Update()
    {
        if (onLib)
        {
            // closed without done (escape, B): cancelled
            if (kb == null || !kb.IsKeyboardOpen) { Finish(false); return; }
            if (Time.frameCount > startFrame) RealKeys();
            return;
        }
        if (giveBackFrames > 0) { giveBackFrames--; Select(); }
        if (!Active) return;
        var m = Il2Cpp.Menu.me;
        if (m == null || !m.paused) { Stop(); return; }
        if (Time.frameCount <= startFrame) return;   // the press that opened it
        if (Binds.RawKeyDown(KeyCode.Escape) || Binds.Button("B")) { Stop(); return; }
        foreach (char c in Input.inputString)
        {
            if (c == '\b') { if (Text.Length > 0) Text = Text.Substring(0, Text.Length - 1); }
            else if (c == '\n' || c == '\r') { var d = done; var t = Text; Stop(); d?.Invoke(t); return; }
            else if (!char.IsControl(c) && Text.Length < MaxLength) Text += c;
        }
    }

    // a real keyboard types into the library's keyboard: its text lives in our field, then it redraws
    static FieldInfo edited;
    static MethodInfo refresh;

    static void RealKeys()
    {
        string v = field.Value ?? "";
        bool changed = false;
        foreach (char c in Input.inputString)
        {
            if (c == '\b') { if (v.Length > 0) { v = v.Substring(0, v.Length - 1); changed = true; } }
            else if (c == '\n' || c == '\r') { field.Value = v; Text = v; kb.CloseKeyboardOverlay(); Finish(true); return; }
            else if (!char.IsControl(c) && v.Length < MaxLength) { v += c; changed = true; }
        }
        if (!changed) return;
        field.Value = v; Text = v;
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        edited ??= typeof(MenuInjectionLibrary.InjectedMenu).GetField("_kbHasEdited", any);
        refresh ??= typeof(MenuInjectionLibrary.InjectedMenu).GetMethod("KbRefreshDisplay", any, null, Type.EmptyTypes, null);
        try { edited?.SetValue(kb, true); refresh?.Invoke(kb, null); } catch { }
    }

    static void Finish(bool keep)
    {
        var d = done; onLib = false; done = null; field = null;
        blockUntil = Time.frameCount + 10;
        GiveBack();
        if (keep) d?.Invoke(Text);
    }

    // the keyboard leaves the page's list shrunk to its last key: put the rows back and the cursor on the row we
    // typed into, for a few frames (the library and the game move the cursor too)
    static void GiveBack()
    {
        if (pageList != null && pageItems != null) pageList.items = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject>(pageItems);
        giveBackFrames = 4;
        Select();
    }

    static void Select()
    {
        if (pageList == null || backTo == null) return;
        if (pageItems != null && (pageList.items == null || pageList.items.Length != pageItems.Length))
            pageList.items = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject>(pageItems);
        Il2Cpp.MenuItemList.active = pageList;
        var items = pageList.items;
        if (items != null)
            for (int i = 0; i < items.Length; i++)
                if (items[i] == backTo || (items[i] != null && backTo.transform.IsChildOf(items[i].transform))) { pageList.selected = i; break; }
        var es = EventSystem.current;
        if (es != null) es.SetSelectedGameObject(backTo);
    }

    static void Stop()
    {
        Active = false; done = null;
        blockUntil = Time.frameCount + 10;
        Binds.MuteFor(1);   // the enter / escape / B that ended it stays with us
        var es = EventSystem.current;
        if (es != null) es.sendNavigationEvents = sendNav;
    }

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr SteamAPI_SteamUtils_v010();

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    static extern bool SteamAPI_ISteamUtils_ShowFloatingGamepadTextInput(IntPtr self, int mode, int x, int y, int w, int h);

    static void SteamKeyboard(RectTransform row)
    {
        try
        {
            var utils = SteamAPI_SteamUtils_v010();
            if (utils == IntPtr.Zero) return;
            int x = 0, y = 0, w = Screen.width, h = 60;
            if (row != null)
            {
                // the row's screen rect (steam wants top-left origin) so the keyboard doesn't cover it
                var cs = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
                row.GetWorldCorners(cs);
                var canvas = row.GetComponentInParent<Canvas>();
                var cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                var a = RectTransformUtility.WorldToScreenPoint(cam, cs[0]);
                var b = RectTransformUtility.WorldToScreenPoint(cam, cs[2]);
                x = (int)a.x; y = Screen.height - (int)b.y; w = (int)(b.x - a.x); h = (int)(b.y - a.y);
            }
            if (!SteamAPI_ISteamUtils_ShowFloatingGamepadTextInput(utils, 0, x, y, w, h))
                Core.Inst.LoggerInstance.Msg("steam keyboard didn't open (type on a keyboard, or open steam's keyboard yourself)");
        }
        catch (Exception e) { Core.Inst.LoggerInstance.Warning("steam keyboard: " + e.Message); }
    }
}
