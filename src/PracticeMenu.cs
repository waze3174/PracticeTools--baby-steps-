using System;
using System.Collections.Generic;
using System.Reflection;
using BabyStepsMenuLib;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PracticeTools;

// the PRACTICE page in the pause menu, built with BabyStepsMenuLib (optional: only touched when it's installed).
// tabs: Timer, Finder, Teleport, Savestates, Routes, Display, Glitch, Fly, Misc, Multiplayer, Controls (6 rows a page at most: more runs into the Close button).
// with FallTracker or Rulebook also installed, the pages open from one MODS button (MenuHub)
static class PracticeMenu
{
    static readonly List<Selectable> controls = new();   // every row, for the highlight
    static Slider itemSlider, fruitSlider, playerSlider, campSlider, speedSlider, sizeSlider, stateSlider, routeSlider;
    static Button routeRecord, routePause, routeDescribe, routeDelete;
    static Button stateDelete, stateDescribe, forgetBests;
    static bool built;
    static readonly List<(Button button, Binds.Bind bind)> bindRows = new();

    internal const string PageName = "Practice";
    internal static void Announce() => MenuHub.Announce(PageName);

    internal static void Build(MelonLoader.MelonLogger.Instance log, HarmonyLib.Harmony harmony)
    {
        MenuHub.StyleWith(b => Style(b.GetComponentInChildren<TextMeshProUGUI>(true), Scale));
        MenuHub.Build(PageName, new()
        {
            ("Timer", TimerTab), ("Finder", FinderTab), ("Teleport", TeleportTab), ("Savestates", StatesTab), ("Routes", RoutesTab), ("Display", DisplayTab), ("Glitch", GlitchTab), ("Fly", FlyTab),
            ("Misc", MiscTab), ("Multiplayer", MultiplayerTab), ("Controls", ControlsTab),
        }, log, harmony);
    }

    static Toggle Toggle(MenuInjectionLibrary.TabBuilder tab, string label, MelonLoader.MelonPreferences_Entry<bool> e)
    {
        var t = tab.AddToggle(label, e.Value, (UnityAction<bool>)(v => { e.Value = v; Core.Changed(); }));
        if (t != null) { Style(t.GetComponentInChildren<TextMeshProUGUI>(true), Scale); controls.Add(t); }
        return t;
    }

    static Button Button(MenuInjectionLibrary.TabBuilder tab, string label, Action click)
    {
        var b = tab.AddButton(label, (UnityAction)(() => click()));
        if (b != null) { Style(b.GetComponentInChildren<TextMeshProUGUI>(true), Scale); controls.Add(b); }
        return b;
    }

    static Slider Slider(MenuInjectionLibrary.TabBuilder tab, string label, float min, float max, float value, Action<float> change, bool whole)
    {
        Slider s = null;
        s = tab.AddSlider(label, min, max, value, (UnityAction<float>)(v => change(v)));
        if (s == null) return null;
        s.wholeNumbers = whole;
        controls.Add(s);
        return s;
    }

    static void TimerTab(MenuInjectionLibrary.TabBuilder tab)
    {
        built = true;
        Toggle(tab, "Show timer", Core.ShowTimer);
        Toggle(tab, "Show splits", Core.ShowSplits);
        Toggle(tab, "+/-", Core.ShowDelta);
        Toggle(tab, "Time", Core.ShowTime);
        Toggle(tab, "Best run", Core.ShowBestRun);
        Toggle(tab, "Best segments", Core.ShowGold);
        Toggle(tab, "Any% splits (off: full run)", Core.AnyPercent);
        forgetBests = Button(tab, ForgetText(), () => { Splits.ResetBests(); Hud.Flash(Splits.AnyPct ? "ANY% BEST TIMES CLEARED" : "FULL RUN BEST TIMES CLEARED"); });
    }

    static string ForgetText() => Splits.AnyPct ? "Forget best times (any%)" : "Forget best times (full run)";

    static void FinderTab(MenuInjectionLibrary.TabBuilder tab)
    {
        built = true;
        itemSlider = Slider(tab, "ITEM", 0f, 1f, 0f, v =>
        {
            int i = Mathf.RoundToInt(v);
            if (i >= 0 && i < Finder.Items.Count) { Core.ArrowItem.Value = Finder.Items[i].Key; Core.Changed(); }
            Labels();
        }, true);
        Toggle(tab, "Item arrow", Core.Arrow);
        Button(tab, "Bring it here", () =>
        {
            var it = Finder.Selected();
            if (it == null) { Hud.Flash("PICK AN ITEM FIRST"); return; }
            Finder.Bring(it);
        });
        fruitSlider = Slider(tab, "FRUIT", 0f, 1f, 0f, v =>
        {
            int i = Mathf.RoundToInt(v);
            if (i >= 0 && i < Finder.FruitList.Count) { Core.ArrowFruit.Value = Finder.FruitList[i].Key; Core.Changed(); }
            Labels();
        }, true);
        Toggle(tab, "Fruit arrow", Core.FruitArrow);
        Button(tab, "Bring the fruit here", () =>
        {
            var it = Finder.Selected(true);
            if (it == null) { Hud.Flash("PICK A FRUIT FIRST"); return; }
            Finder.Bring(it);
        });
    }

    static void MultiplayerTab(MenuInjectionLibrary.TabBuilder tab)
    {
        built = true;
        // 0 is nobody, then each player
        playerSlider = Slider(tab, "PLAYER", 0f, 1f, 0f, v =>
        {
            int i = Mathf.RoundToInt(v) - 1;
            string want = i >= 0 && i < PlayerFinder.Players.Count ? PlayerFinder.Players[i].Name : "";
            if (Core.ArrowPlayer.Value != want) { Core.ArrowPlayer.Value = want; Core.Changed(); }
            Labels();
        }, true);
        Toggle(tab, "Player arrow", Core.PlayerArrow);
        Button(tab, "Teleport near this player", () =>
        {
            var p = PlayerFinder.Selected();
            if (p == null) { Hud.Flash("PICK A PLAYER FIRST"); return; }
            Fly.TeleportFromMenu(PlayerFinder.Near(p));
        });
    }

    static void TeleportTab(MenuInjectionLibrary.TabBuilder tab)
    {
        built = true;
        campSlider = Slider(tab, "CAMPFIRE", 0f, 1f, 0f, _ => Labels(), true);
        Button(tab, "Go to this campfire", () =>
        {
            var m = Il2Cpp.Menu.me;
            int i = campSlider != null ? Mathf.RoundToInt(campSlider.value) : 0;
            if (m == null || m.campfireDatas == null || i < 0 || i >= m.campfireDatas.Length || m.campfireDatas[i] == null) return;
            Fly.TeleportFromMenu(m.campfireDatas[i].pos);
        });
        Button(tab, "Load the picked state (F10)", () => States.Load(States.Sel, false));
    }

    // pick a slot on the slider; the buttons act on it. delete asks for a second press
    static int deleteArmed = -1;
    static float deleteUntil;

    static void StatesTab(MenuInjectionLibrary.TabBuilder tab)
    {
        built = true;
        stateSlider = Slider(tab, "STATE", 0f, States.Count - 1, States.Sel, v =>
        {
            States.Selected.Value = Mathf.RoundToInt(v);
            Core.Changed();
            deleteArmed = -1;
            Labels();
        }, true);
        Button(tab, "Save here (F9)", () => { States.Save(States.Sel); Labels(); });
        Button(tab, "Load (F10)", () => States.Load(States.Sel, false));
        Button(tab, "Full load (puts the save file back too)", () => States.Load(States.Sel, true));
        stateDescribe = Button(tab, "Describe", () => Describe(States.Sel));
        stateDelete = Button(tab, "Delete slot", () =>
        {
            int i = States.Sel;
            if (States.Get(i) == null) return;
            if (deleteArmed == i && Time.unscaledTime < deleteUntil) { States.Delete(i); deleteArmed = -1; Hud.Flash($"STATE {i + 1} DELETED"); }
            else { deleteArmed = i; deleteUntil = Time.unscaledTime + 3f; }
            Labels();
        });
    }

    // typed into the row: keys, or steam's keyboard when the row was pressed with the gamepad
    static void Describe(int i)
    {
        var s = States.Get(i);
        if (s == null) { Hud.Flash("SAVE A STATE THERE FIRST"); return; }
        bool pad = Binds.Button("A");
        Typing.Start(s.Desc, pad, stateDescribe != null ? stateDescribe.GetComponent<RectTransform>() : null, text => { States.SetDesc(i, text); Labels(); });
    }

    static string TypingText() => $"{Typing.Text}_  <alpha=#80><size=70%>ENTER KEEPS, ESC / B CANCELS</size><alpha=#FF>";

    // pick a slot; record lays a line behind nate, pause leaves a marker and resume only works back at it.
    // record over a filled slot and delete ask for a second press
    static int routeArmed = -1;
    static string routeArmedFor;
    static float routeArmedUntil;

    static bool Armed(string what, int i)
    {
        if (routeArmed == i && routeArmedFor == what && Time.unscaledTime < routeArmedUntil) { routeArmed = -1; return true; }
        routeArmed = i; routeArmedFor = what; routeArmedUntil = Time.unscaledTime + 3f;
        return false;
    }

    static void RoutesTab(MenuInjectionLibrary.TabBuilder tab)
    {
        built = true;
        routeSlider = Slider(tab, "ROUTE", 0f, Routes.Count - 1, Routes.Sel, v =>
        {
            Routes.Selected.Value = Mathf.RoundToInt(v);
            Core.Changed();
            routeArmed = -1;
            Labels();
        }, true);
        Toggle(tab, "Show route", Core.ShowRoute);
        routeRecord = Button(tab, "Start recording", () =>
        {
            int i = Routes.Sel;
            if (Routes.Recording) Routes.Stop();
            else if (Routes.Get(i) == null || Armed("record", i)) Routes.Start(i);
            Labels();
        });
        routePause = Button(tab, "Pause", () =>
        {
            if (Routes.Paused) Routes.Resume();
            else if (Routes.Recording) Routes.Pause();
            else if (Routes.Get(Routes.Sel) != null) Routes.Continue(Routes.Sel);
            else Hud.Flash("NOT RECORDING");
            Labels();
        });
        routeDescribe = Button(tab, "Describe", () =>
        {
            int i = Routes.Sel;
            var r = Routes.Get(i);
            if (r == null) { Hud.Flash("RECORD A ROUTE THERE FIRST"); return; }
            Typing.Start(r.Desc, Binds.Button("A"), routeDescribe != null ? routeDescribe.GetComponent<RectTransform>() : null, text => { Routes.SetDesc(i, text); Labels(); });
        });
        routeDelete = Button(tab, "Delete route", () =>
        {
            int i = Routes.Sel;
            if (Routes.Get(i) == null) return;
            if (Armed("delete", i)) { Routes.Delete(i); Hud.Flash($"ROUTE {i + 1} DELETED"); }
            Labels();
        });
    }

    static void RouteLabels()
    {
        if (routeArmed >= 0 && Time.unscaledTime >= routeArmedUntil) routeArmed = -1;
        int sel = Routes.Sel;
        var r = Routes.Get(sel);
        if (routeSlider != null)
        {
            if (Mathf.RoundToInt(routeSlider.value) != sel) routeSlider.SetValueWithoutNotify(sel);
            string what = r == null ? "<alpha=#80>(EMPTY)<alpha=#FF>"
                : (string.IsNullOrEmpty(r.Desc) ? "(NO DESCRIPTION)" : r.Desc.ToUpperInvariant())
                  + string.Format(System.Globalization.CultureInfo.InvariantCulture, "  <alpha=#80><size=70%>{0:0.00} KM{1}</size><alpha=#FF>", r.Length / 1000f,
                      Routes.RecSlot == sel ? (Routes.Paused ? ", PAUSED" : ", RECORDING") : r.Marker != null ? ", PAUSED" : "");
            SetLabel(routeSlider, $"ROUTE {sel + 1}: {what}");
        }
        SetText(routeRecord, Routes.Recording ? $"Stop recording route {Routes.RecSlot + 1}"
            : r == null ? "Start recording"
            : routeArmed == sel && routeArmedFor == "record" ? $"Press again to record over route {sel + 1}" : "Record over this route");
        float d = Routes.ToMarker();
        SetText(routePause, Routes.Paused ? (d >= 0f && d <= Routes.ResumeRange ? "Resume here" : $"Resume: go back to the marker ({d:0} m)")
            : Routes.Recording ? "Pause (leaves a marker)"
            : r != null ? (r.Marker != null ? "Continue this route from its marker" : "Continue this route from its end")
            : "Pause");
        if (routeDescribe != null && !Typing.OnLib)
            SetText(routeDescribe, Typing.Active && Typing.Row == routeDescribe.gameObject ? TypingText() : "Describe");
        SetText(routeDelete, routeArmed == sel && routeArmedFor == "delete" ? $"Press again to delete route {sel + 1}" : "Delete route");
    }

    static void SetText(Button b, string text)
    {
        var t = b != null ? b.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        if (t != null && t.text != text) t.text = text;
    }

    static void DisplayTab(MenuInjectionLibrary.TabBuilder tab)
    {
        built = true;
        Toggle(tab, "Input display", Core.InputDisplay);
        Toggle(tab, "Speed", Core.ShowSpeed);
        Toggle(tab, "FPS", Core.ShowFps);
        Toggle(tab, "Coordinates", Core.ShowCoords);
        Toggle(tab, "Camera angle", Core.ShowAngle);
        Toggle(tab, "Compass", Core.ShowCompass);
        Toggle(tab, "Moving direction", Core.ShowMoving);
        Toggle(tab, "Steps taken", Core.ShowSteps);
    }

    static void GlitchTab(MenuInjectionLibrary.TabBuilder tab)
    {
        built = true;
        Toggle(tab, "Fling info", Core.ShowFling);
        Toggle(tab, "Map loops", Core.ShowLoops);
        Toggle(tab, "Kill height", Core.ShowKill);
        Toggle(tab, "Glitch markers", Core.ShowGlitchMarks);
    }

    static void FlyTab(MenuInjectionLibrary.TabBuilder tab)
    {
        built = true;
        Toggle(tab, "F1 carries nate (off: free camera)", Core.CarryMode);
    }

    static void MiscTab(MenuInjectionLibrary.TabBuilder tab)
    {
        built = true;
        Toggle(tab, "Auto skip cutscenes", Core.AutoSkip);
        Toggle(tab, "Item blinker", Core.ItemBlinker);
        sizeSlider = Slider(tab, "TIMER SIZE", 0.5f, 2f, Mathf.Clamp(Core.TimerScale.Value, 0.5f, 2f), v =>
        {
            Core.TimerScale.Value = Mathf.Round(v * 20f) / 20f;   // steps of 0.05
            Core.Changed();
            Labels();
        }, false);
        speedSlider = Slider(tab, "GAME SPEED", 0.1f, 1f, Mathf.Clamp(Core.SlowMo.Value, 0.1f, 1f), v =>
        {
            Core.SlowMo.Value = Mathf.Round(v * 20f) / 20f;   // steps of 0.05
            Core.Changed();
            Labels();
        }, false);
    }

    // one row per control: press it, then the new key (or, on a gamepad, the new button; B stays the modifier)
    static void ControlsTab(MenuInjectionLibrary.TabBuilder tab)
    {
        built = true;
        foreach (var b in Binds.All)
        {
            var bind = b;
            var row = Button(tab, BindText(bind), () => { if (Binds.Capturing == null) Binds.Capture(bind, Labels); });
            if (row != null) bindRows.Add((row, bind));
        }
        Button(tab, "Reset controls", () => { Binds.ResetAll(); Labels(); Hud.Flash("CONTROLS RESET"); });
    }

    static string BindText(Binds.Bind b) => Binds.Capturing == b
        ? $"{b.Name.ToUpperInvariant()}: PRESS A KEY OR A BUTTON ({Mathf.CeilToInt(Binds.CaptureLeft)})"
        : $"{b.Name.ToUpperInvariant()}: {Binds.Show(b).ToUpperInvariant()}";

    // slider labels show what's picked; the item and campfire lists can change while the game runs
    static void Labels()
    {
        foreach (var (row, bind) in bindRows)
        {
            var t = row != null ? row.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            string text = BindText(bind);
            if (t != null && t.text != text) t.text = text;
        }
        if (itemSlider != null)
        {
            int n = Finder.Items.Count;
            itemSlider.maxValue = Mathf.Max(1, n - 1);
            int sel = Finder.SelectedIndex();
            if (sel >= 0 && Mathf.RoundToInt(itemSlider.value) != sel) itemSlider.SetValueWithoutNotify(sel);
            string name = n == 0 ? "(load a save)" : sel >= 0 ? Finder.Items[sel].Name : "(pick one)";
            SetLabel(itemSlider, $"ITEM: {name.ToUpperInvariant()}");
        }
        if (fruitSlider != null)
        {
            int n = Finder.FruitList.Count;
            fruitSlider.maxValue = Mathf.Max(1, n - 1);
            int sel = Finder.SelectedIndex(true);
            if (sel >= 0 && Mathf.RoundToInt(fruitSlider.value) != sel) fruitSlider.SetValueWithoutNotify(sel);
            string name = Il2Cpp.PlayerMovement.me == null ? "(load a save)" : n == 0 ? "(none found yet: go near one)" : sel >= 0 ? Finder.FruitList[sel].Name : "(pick one)";
            SetLabel(fruitSlider, $"FRUIT: {name.ToUpperInvariant()}");
        }
        if (playerSlider != null)
        {
            int n = PlayerFinder.Players.Count;
            playerSlider.maxValue = Mathf.Max(1, n);
            int sel = PlayerFinder.SelectedIndex();
            if (Mathf.RoundToInt(playerSlider.value) != sel + 1) playerSlider.SetValueWithoutNotify(sel + 1);
            string name = !PlayerFinder.Available ? "(no multiplayer mod)" : n == 0 ? "(nobody connected)" : sel >= 0 ? PlayerFinder.Players[sel].Name : "(nobody, slide right)";
            SetLabel(playerSlider, $"PLAYER: {name.ToUpperInvariant()}");
        }
        if (campSlider != null)
        {
            var m = Il2Cpp.Menu.me;
            int n = m != null && m.campfireDatas != null ? m.campfireDatas.Length : 0;
            campSlider.maxValue = Mathf.Max(1, n - 1);
            int i = Mathf.RoundToInt(campSlider.value);
            string title = i < n && m.campfireDatas[i] != null ? m.campfireDatas[i].title : "";
            SetLabel(campSlider, n == 0 ? "CAMPFIRE: (load a save)" : $"CAMPFIRE {i}: {(string.IsNullOrEmpty(title) ? "?" : title.ToUpperInvariant())}");
        }
        if (stateSlider != null)
        {
            int i = Mathf.RoundToInt(stateSlider.value);
            if (i != States.Sel) stateSlider.SetValueWithoutNotify(i = States.Sel);
            var s = States.Get(i);
            string what = s == null ? "<alpha=#80>(EMPTY)<alpha=#FF>"
                : (string.IsNullOrEmpty(s.Desc) ? "(NO DESCRIPTION)" : s.Desc.ToUpperInvariant())
                  + (s.HasPose ? "" : "  <alpha=#80><size=70%>SPOT ONLY</size><alpha=#FF>");
            SetLabel(stateSlider, $"STATE {i + 1}: {what}");
        }
        if (stateDescribe != null && !Typing.OnLib)   // on the library's keyboard, it types into the row itself
        {
            var t = stateDescribe.GetComponentInChildren<TextMeshProUGUI>(true);
            string text = Typing.Active && Typing.Row == stateDescribe.gameObject ? TypingText() : "Describe";
            if (t != null && t.text != text) t.text = text;
        }
        if (stateDelete != null)
        {
            var t = stateDelete.GetComponentInChildren<TextMeshProUGUI>(true);
            if (deleteArmed >= 0 && Time.unscaledTime >= deleteUntil) deleteArmed = -1;
            string text = deleteArmed >= 0 ? $"Press again to delete state {deleteArmed + 1}" : "Delete slot";
            if (t != null && t.text != text) t.text = text;
        }
        RouteLabels();
        if (forgetBests != null)
        {
            var t = forgetBests.GetComponentInChildren<TextMeshProUGUI>(true);
            string text = ForgetText();
            if (t != null && t.text != text) t.text = text;
        }
        if (sizeSlider != null)
            SetLabel(sizeSlider, "TIMER SIZE ×" + Core.TimerScale.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                + "  <alpha=#80><size=70%>NORMAL ×1.00</size><alpha=#FF>");
        if (speedSlider != null)
            SetLabel(speedSlider, "GAME SPEED ×" + Core.SlowMo.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                + "  <alpha=#80><size=70%>NORMAL ×1.00</size><alpha=#FF>");
    }

    // the library puts the slider's label next to it in the same row
    static void SetLabel(Slider s, string text)
    {
        var t = LabelOf(s);
        if (t == null) return;
        if (t.text != text) t.text = text;
        Style(t, Scale);
        // the label keeps room for its whole text so it never runs under the slider; the slider gets what's left
        var le = t.GetComponent<LayoutElement>() ?? t.gameObject.AddComponent<LayoutElement>();
        float w = Mathf.Max(le.minWidth, t.preferredWidth + 12f);
        le.minWidth = le.preferredWidth = w;
    }

    static TextMeshProUGUI LabelOf(Slider s) =>
        s == null || s.transform.parent == null ? null : s.transform.parent.GetComponentInChildren<TextMeshProUGUI>(true);

    // the game shows which menu item is selected through the item's own highlight; the library's copies
    // don't have one, so the selected row's label turns white and underlined like a selected menu button
    internal static void Update()
    {
        if (!built && MenuHub.ChooserButtons.Count == 0) return;
        var m = Il2Cpp.Menu.me;
        if (m == null || !m.paused) return;
        Labels();
        try { WideTabs(); } catch (Exception e) { tabsBroken = true; Core.Inst.LoggerInstance.Warning("tab bar: " + e.Message); }
        var es = UnityEngine.EventSystems.EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        foreach (var c in controls)
            if (c != null) Highlight(c is Slider s ? LabelOf(s) : c.GetComponentInChildren<TextMeshProUGUI>(true), c, sel);
        foreach (var b in MenuHub.ChooserButtons)
            if (b != null) Highlight(b.GetComponentInChildren<TextMeshProUGUI>(true), b, sel);
    }

    // the library builds each page's tab bar itself (BBSMP_TabHeader, tabs BBSMP_Tab_0.., copies of a game button,
    // all one width, placed by hand from the left) and shrinks each name to fit (SAVESTATES, MULTIPLAYER come out
    // tiny and run into each other). each tab gets the width of its name instead, all at one size; the bar grows to
    // the right, there's room
    static readonly string[] TabNames = { "Timer", "Finder", "Teleport", "Savestates", "Routes", "Display", "Glitch", "Fly", "Misc", "Multiplayer", "Controls" };
    static RectTransform tabBar;
    static float nextTabCheck;
    static bool tabsBroken, tabsLogged, otherLogged;

    static void WideTabs()
    {
        if (tabsBroken || Time.unscaledTime < nextTabCheck) return;
        nextTabCheck = Time.unscaledTime + 0.25f;
        if (tabBar == null || tabList.Count == 0) tabBar = FindTabBar();
        if (tabBar == null || !tabBar.gameObject.activeInHierarchy) return;

        var tabs = new List<(RectTransform rt, TMP_Text t)>();
        float size = 0f;
        foreach (var rt in tabList)
        {
            var t = rt != null ? rt.GetComponentInChildren<TMP_Text>(true) : null;
            if (t == null) continue;
            tabs.Add((rt, t));
            size = Mathf.Max(size, t.enableAutoSizing ? t.fontSizeMax : t.fontSize);
        }
        if (tabs.Count == 0) { if (!otherLogged) { otherLogged = true; DebugLog.Write($"tabs: {tabList.Count} tabs, none with a TMP text: {Describe(tabList)}"); } return; }
        size = Mathf.Clamp(size, 16f, 30f);
        if (!tabsLogged) LogTabs(tabBar, tabs);
        const float gap = 4f;
        // 9 tabs at size 30 came to 1660 wide and fit; more tabs shrink the text so the bar stays about that wide
        // (widths go with the size)
        const float maxWide = 1700f;
        float all = -gap;
        foreach (var (_, t) in tabs) all += t.GetPreferredValues(t.text, 9999f, 9999f).x / Mathf.Max(t.fontSize, 1f) * size + size * 1.2f + gap;
        if (all > maxWide) size = Mathf.Max(16f, Mathf.Floor(size * maxWide / all));
        float x = 0f;
        foreach (var (rt, t) in tabs)
        {
            t.enableAutoSizing = false;
            t.fontSize = size;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            float w = Mathf.Ceil(t.GetPreferredValues(t.text).x + size * 1.2f);
            // anchored on the bar's left edge (the library's own placing): width, then side by side
            rt.anchorMin = new Vector2(0f, rt.anchorMin.y); rt.anchorMax = new Vector2(0f, rt.anchorMax.y);
            rt.pivot = new Vector2(0f, rt.pivot.y);
            rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
            rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
            var le = rt.GetComponent<LayoutElement>();
            if (le != null) { le.minWidth = le.preferredWidth = w; le.flexibleWidth = 0f; }
            // the text fills its tab
            var tr = t.rectTransform;
            if (tr != rt) { tr.anchorMin = new Vector2(0f, tr.anchorMin.y); tr.anchorMax = new Vector2(1f, tr.anchorMax.y); tr.offsetMin = new Vector2(0f, tr.offsetMin.y); tr.offsetMax = new Vector2(0f, tr.offsetMax.y); }
            x += w + gap;
        }
        var hlg = tabBar.GetComponent<HorizontalLayoutGroup>();
        if (hlg != null) hlg.enabled = false;   // placed by hand above
        if (tabBar.rect.width < x) tabBar.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, x);
        if (!tabsLogged)
        {
            tabsLogged = true;
            DebugLog.Write($"tabs: {tabs.Count} at size {size:0}, {x - gap:0} wide{(hlg != null ? " (its row layout switched off)" : "")}");
        }
    }

    // our page's tabs: the library's own list of them (RuntimeTabMenu.Tabs of the open page's tab menu)
    static readonly List<RectTransform> tabList = new();
    static FieldInfo[] tabMenuFields;

    static RectTransform FindTabBar()
    {
        tabList.Clear();
        var page = MenuLibFix.OpenPage();
        if (page == null) return null;
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        tabMenuFields ??= new[] { typeof(MenuInjectionLibrary.InjectedMenu).GetField("_tabMenu", any), typeof(MenuInjectionLibrary.InjectedMenu).GetField("_currentTabMenu", any) };
        foreach (var f in tabMenuFields)
        {
            var tm = f?.GetValue(page);
            if (tm == null) continue;
            if (tm.GetType().GetProperty("Tabs", any)?.GetValue(tm) is not System.Collections.IEnumerable list) continue;
            foreach (var o in list)
            {
                var rt = o is GameObject go ? go.GetComponent<RectTransform>() : o is Component c ? c.GetComponent<RectTransform>() : null;
                if (rt != null) tabList.Add(rt);
            }
            if (tabList.Count >= 5) return tabList[0].parent as RectTransform;
            if (!otherLogged) { otherLogged = true; DebugLog.Write($"tabs: the open page's tab menu has {tabList.Count} tabs ({tm.GetType().Name})"); }
            tabList.Clear();
        }
        return null;
    }

    static string Describe(List<RectTransform> l)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var rt in l)
        {
            sb.Append(rt.name).Append(" [");
            foreach (var c in rt.GetComponentsInChildren<Component>(true)) if (c != null) sb.Append(c.GetIl2CppType().Name).Append(' ');
            sb.Append("] ");
        }
        return sb.ToString();
    }

    static void LogTabs(RectTransform bar, List<(RectTransform rt, TMP_Text t)> tabs)
    {
        var sb = new System.Text.StringBuilder($"tab bar {bar.name} ({Comps(bar)}) {bar.rect.width:0}x{bar.rect.height:0}, parent {bar.parent?.name}:");
        foreach (var (rt, t) in tabs)
            sb.Append($"\n  {rt.name} ({Comps(rt)}) {rt.rect.width:0}x{rt.rect.height:0} at {rt.localPosition.x:0}; text {t.name} '{t.text}' ({Comps(t.transform)}) auto {t.enableAutoSizing} {t.fontSize:0}");
        DebugLog.Write(sb.ToString());
    }

    static string Comps(Transform t)
    {
        var names = new List<string>();
        foreach (var c in t.GetComponents<Component>()) if (c != null && c.GetIl2CppType().Name != "RectTransform") names.Add(c.GetIl2CppType().Name);
        return string.Join(", ", names);
    }

    static readonly Dictionary<int, FontStyles> baseStyle = new();

    static void Highlight(TextMeshProUGUI t, Selectable s, GameObject sel)
    {
        if (t == null || s == null || !s.gameObject.activeInHierarchy) return;
        bool on = sel != null && sel == s.gameObject;
        t.color = MenuColor(on);
        // keep the text's own style (the game's buttons are bold caps), only add the underline
        int id = t.GetInstanceID();
        if (!baseStyle.TryGetValue(id, out var st)) { st = t.fontStyle & ~FontStyles.Underline; baseStyle[id] = st; }
        t.fontStyle = on ? st | FontStyles.Underline : st;
    }

    // the game's menu text colour: its buttons' normal (pale) or selected tint
    static Color MenuColor(bool selected)
    {
        var m = Il2Cpp.Menu.me;
        TMP_Text src = m != null ? m.ContinueGameButtonText : null;
        var c = src != null ? src.color : Color.white;
        var btn = m != null ? m.ContinueGameButton : null;
        if (btn != null) { var cb = btn.colors; c *= (selected ? cb.selectedColor : cb.normalColor) * cb.colorMultiplier; c.a = Mathf.Min(c.a, 1f); }
        return c;
    }

    // the library makes text as big as the menu's buttons, in white: smaller, in the menu's colour
    const float Scale = 0.5f;
    static readonly HashSet<int> styled = new();

    static void Style(TMP_Text t, float scale)
    {
        if (t == null || !styled.Add(t.GetInstanceID())) return;
        var m = Il2Cpp.Menu.me;
        TMP_Text src = m != null ? m.ContinueGameButtonText : null;
        if (src != null) t.fontSize = src.fontSize * scale; else t.fontSize *= scale;
        t.enableAutoSizing = false;
        t.richText = true;
        t.color = MenuColor(false);
    }
}
