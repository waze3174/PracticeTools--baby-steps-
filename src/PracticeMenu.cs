using System;
using System.Collections.Generic;
using BabyStepsMenuLib;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PracticeTools;

// the PRACTICE page in the pause menu, built with BabyStepsMenuLib (optional: only touched when it's installed).
// tabs: Timer, Finder, Teleport, Display, Fly, Misc, Multiplayer, Controls (4 rows a page: more runs into the Close button).
// with FallTracker or Rulebook also installed, the pages open from one MODS button (MenuHub)
static class PracticeMenu
{
    static readonly List<Selectable> controls = new();   // every row, for the highlight
    static Slider itemSlider, playerSlider, campSlider, speedSlider, sizeSlider;
    static bool built;
    static readonly List<(Button button, Binds.Bind bind)> bindRows = new();

    internal const string PageName = "Practice";
    internal static void Announce() => MenuHub.Announce(PageName);

    internal static void Build(MelonLoader.MelonLogger.Instance log, HarmonyLib.Harmony harmony)
    {
        MenuHub.StyleWith(b => Style(b.GetComponentInChildren<TextMeshProUGUI>(true), Scale));
        MenuHub.Build(PageName, new()
        {
            ("Timer", TimerTab), ("Finder", FinderTab), ("Teleport", TeleportTab), ("Display", DisplayTab), ("Fly", FlyTab),
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
        sizeSlider = Slider(tab, "SIZE", 0.5f, 2f, Mathf.Clamp(Core.TimerScale.Value, 0.5f, 2f), v =>
        {
            Core.TimerScale.Value = Mathf.Round(v * 20f) / 20f;   // steps of 0.05
            Core.Changed();
            Labels();
        }, false);
        Button(tab, "Forget best times", () => { Splits.ResetBests(); Hud.Flash("BEST TIMES CLEARED"); });
    }

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
    }

    static void MultiplayerTab(MenuInjectionLibrary.TabBuilder tab)
    {
        built = true;
        playerSlider = Slider(tab, "PLAYER", 0f, 1f, 0f, v =>
        {
            int i = Mathf.RoundToInt(v);
            if (i >= 0 && i < PlayerFinder.Players.Count) { Core.ArrowPlayer.Value = PlayerFinder.Players[i].Name; Core.Changed(); }
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
        Button(tab, "Back to saved spot (F10)", () => Fly.LoadSpot(true));
    }

    static void DisplayTab(MenuInjectionLibrary.TabBuilder tab)
    {
        built = true;
        Toggle(tab, "Input display", Core.InputDisplay);
        Toggle(tab, "Speed", Core.ShowSpeed);
        Toggle(tab, "FPS", Core.ShowFps);
        speedSlider = Slider(tab, "GAME SPEED", 0.1f, 1f, Mathf.Clamp(Core.SlowMo.Value, 0.1f, 1f), v =>
        {
            Core.SlowMo.Value = Mathf.Round(v * 20f) / 20f;   // steps of 0.05
            Core.Changed();
            Labels();
        }, false);
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
        if (playerSlider != null)
        {
            int n = PlayerFinder.Players.Count;
            playerSlider.maxValue = Mathf.Max(1, n - 1);
            int sel = PlayerFinder.SelectedIndex();
            if (sel >= 0 && Mathf.RoundToInt(playerSlider.value) != sel) playerSlider.SetValueWithoutNotify(sel);
            string name = !PlayerFinder.Available ? "(no multiplayer mod)" : n == 0 ? "(nobody connected)" : sel >= 0 ? PlayerFinder.Players[sel].Name : "(pick one)";
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
        if (sizeSlider != null)
            SetLabel(sizeSlider, "SIZE ×" + Core.TimerScale.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
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
        var es = UnityEngine.EventSystems.EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        foreach (var c in controls)
            if (c != null) Highlight(c is Slider s ? LabelOf(s) : c.GetComponentInChildren<TextMeshProUGUI>(true), c, sel);
        foreach (var b in MenuHub.ChooserButtons)
            if (b != null) Highlight(b.GetComponentInChildren<TextMeshProUGUI>(true), b, sel);
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
