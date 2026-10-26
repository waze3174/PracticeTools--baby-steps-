using System.Globalization;
using System.Text;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PracticeTools;

// what's drawn on screen: timer + splits (top left, out of the minimap's way, scalable), speed and fps (bottom right), arrows to an item and a player, short messages
// (spot saved...). also the game's own input graph and the game speed (slow motion)
static class Hud
{
    static GameObject root;
    static Canvas canvas;
    static TextMeshProUGUI timer, splits, info, flash;
    static float flashUntil;

    internal static void Flash(string text)
    {
        if (flash == null) return;
        flash.text = text;
        flashUntil = Time.unscaledTime + 1.5f;
    }

    internal static void Update()
    {
        if (root == null && !Make()) return;
        bool inGame = PlayerMovement.me != null;
        // timer, splits, speed and fps stay off the pause menu
        bool playing = inGame && !(Menu.me != null && Menu.me.paused);
        // a finished run stays up over the ending for a while, whatever covers or hides the HUD
        bool linger = Splits.Finished && Time.unscaledTime - Splits.FinishedAt < LingerTime;
        canvas.sortingOrder = linger ? 32000 : 440;
        Timer(playing, linger);
        Info(playing);
        Arrows(inGame);
        flash.gameObject.SetActive(Time.unscaledTime < flashUntil);
        InputGraph(inGame);
        SlowMo(inGame);
    }

    // ---- timer and splits ----

    const float LingerTime = 15f;

    static void Timer(bool inGame, bool linger)
    {
        var end = Splits.List.Count > 0 ? Splits.List[Splits.List.Count - 1] : null;
        linger &= end != null && end.Time >= 0f;
        bool show = Core.ShowTimer.Value && (linger || inGame && Splits.Now >= 0f);
        // scaled from the top left corner; the splits sit right under the timer, or at its place without it
        float k = Mathf.Clamp(Core.TimerScale.Value, 0.5f, 2f);
        timer.rectTransform.localScale = splits.rectTransform.localScale = new Vector3(k, k, 1f);
        splits.rectTransform.anchoredPosition = new Vector2(30f, -20f - (Core.ShowTimer.Value ? 58f * k : 0f));
        timer.gameObject.SetActive(show);
        if (show) timer.text = Splits.Fmt(linger ? end.Time : Splits.Now);

        bool showSplits = Core.ShowSplits.Value && (linger || inGame && Splits.Now >= 0f);
        splits.gameObject.SetActive(showSplits);
        if (!showSplits) return;
        var sb = new StringBuilder();
        if (!Splits.Running && !Splits.Finished) sb.Append("<alpha=#80>splits start with a new game<alpha=#FF>\n");
        // columns: split | +/- against the best run | this run's time | the best run's time there | the best segment
        // in gold, each with a toggle; the shown ones sit 130 apart (room for 1:46:05.00 in bold)
        var list = Splits.List;
        bool showDelta = Core.ShowDelta.Value, showTime = Core.ShowTime.Value, bestRun = Core.ShowBestRun.Value, bestSeg = Core.ShowGold.Value;
        int x = 240;
        int deltaPos = showDelta ? x : -1; if (showDelta) x += 130;
        int timePos = showTime ? x : -1; if (showTime) x += 130;
        int runPos = bestRun ? x : -1; if (bestRun) x += 130;
        int segPos = bestSeg ? x : -1;
        const string Dim = "#9A9A9A", Gold = "#FFD84A", Ahead = "#5CE05C", Behind = "#E05C5C";
        if (showDelta || showTime || bestRun || bestSeg)
        {
            sb.Append($"<size=70%><color={Dim}>");
            if (showDelta) sb.Append($"<pos={deltaPos}>+/-");
            if (showTime) sb.Append($"<pos={timePos}>time");
            if (bestRun) sb.Append($"<pos={runPos}>best run");
            if (bestSeg) sb.Append($"<pos={segPos}>best seg");
            sb.Append("</color></size>\n");
        }
        for (int i = 0; i < list.Count; i++)
        {
            var s = list[i];
            bool cur = Splits.Running && !Splits.Finished && i == Splits.Current;
            string delta = "", time = "";
            if (s.Skipped && s.Time < 0f)
                time = $"<color={Dim}>skipped</color>";
            else if (s.Time >= 0f)
            {
                // the time and its difference share a colour: gold for a best segment, green ahead, red behind
                float seg = Splits.Segment(i);
                bool isGold = seg >= 0f && s.Gold >= 0f && seg <= s.Gold + 0.001f;
                string col = isGold ? Gold : s.Pb < 0f ? "#FFFFFF" : s.Time < s.Pb ? Ahead : Behind;
                time = $"<color={col}>{Splits.Fmt(s.Time)}</color>";
                if (s.Pb >= 0f) delta = $"<color={col}>{Splits.Delta(s.Time - s.Pb)}</color>";
            }
            // running behind the best run on the current split: show it live
            else if (cur && s.Pb >= 0f && Splits.Now > s.Pb) delta = $"<color={Behind}>{Splits.Delta(Splits.Now - s.Pb)}</color>";

            sb.Append(cur ? $"<b>{s.Name}</b>" : s.Time >= 0f || s.Skipped ? s.Name : $"<color=#B0B0B0>{s.Name}</color>");
            if (showDelta) sb.Append($"<pos={deltaPos}>{delta}");
            // a skipped split says so in the time column, or the +/- one without it
            if (showTime) sb.Append($"<pos={timePos}>{time}");
            else if (s.Skipped && s.Time < 0f && showDelta) sb.Append($"<pos={deltaPos}><color={Dim}>skipped</color>");
            if (bestRun) sb.Append($"<pos={runPos}><color={Dim}>{(s.Pb >= 0f ? Splits.Fmt(s.Pb) : "-")}</color>");
            if (bestSeg) sb.Append($"<pos={segPos}><color={Gold}>{(s.Gold >= 0f ? Splits.Fmt(s.Gold) : "-")}</color>");
            sb.Append('\n');
        }
        if (bestRun || bestSeg)
        {
            // best run, and the best possible run from the best segments
            float sob = 0f;
            foreach (var s in list) { if (s.Gold < 0f) { sob = -1f; break; } sob += s.Gold; }
            if (bestRun && end != null && end.Pb >= 0f) sb.Append($"<color={Dim}>best run<pos={runPos}>{Splits.Fmt(end.Pb)}</color>\n");
            if (bestSeg && sob >= 0f) sb.Append($"<color={Dim}>sum of best<pos={segPos}></color><color={Gold}>{Splits.Fmt(sob)}</color>\n");
        }
        splits.text = sb.ToString();
    }

    // ---- speed and fps ----

    static Vector3 lastPos;
    static bool hasLast;
    static float speed, fps;

    static void Info(bool inGame)
    {
        float dt = Time.unscaledDeltaTime;
        if (dt > 0f) fps = Mathf.Lerp(fps, 1f / dt, Mathf.Clamp01(dt * 4f));
        if (inGame)
        {
            var p = PlayerMovement.pos;
            float gdt = Time.deltaTime;
            if (hasLast && gdt > 0f)
            {
                var d = p - lastPos; d.y = 0f;
                float v = d.magnitude / gdt;
                if (v < 50f) speed = Mathf.Lerp(speed, v, Mathf.Clamp01(gdt * 5f));   // teleports don't count
            }
            lastPos = p; hasLast = true;
        }
        else hasLast = false;

        var sb = new StringBuilder();
        if (Core.ShowSpeed.Value) sb.Append(speed.ToString("0.00", CultureInfo.InvariantCulture)).Append(" m/s\n");
        if (Core.ShowFps.Value) sb.Append(Mathf.RoundToInt(fps)).Append(" fps\n");
        if (!Mathf.Approximately(Core.SlowMo.Value, 1f)) sb.Append("speed ×").Append(Core.SlowMo.Value.ToString("0.00", CultureInfo.InvariantCulture)).Append('\n');
        info.gameObject.SetActive(inGame && sb.Length > 0);
        info.text = sb.ToString();
    }

    // ---- arrows: to the picked item (gold) and the picked player (their colour) ----

    class Pointer { public Image Arrow; public TextMeshProUGUI Label; }
    static Pointer itemPointer, playerPointer;

    static void Arrows(bool inGame)
    {
        var it = inGame && Core.Arrow.Value ? Finder.Selected() : null;
        Point(itemPointer, it != null ? Finder.Pos(it) + Vector3.up * 0.3f : null, it?.Name, new Color(1f, 0.85f, 0.4f, 0.95f));
        var pl = inGame && Core.PlayerArrow.Value ? PlayerFinder.Selected() : null;
        var c = pl != null ? pl.Color : Color.white; c.a = 0.95f;
        Point(playerPointer, pl != null ? pl.Root.position + Vector3.up * 0.8f : null, pl?.Name, c);
    }

    static void Point(Pointer p, Vector3? where, string name, Color color)
    {
        var cam = Camera.main;
        bool show = where.HasValue && cam != null;
        p.Arrow.gameObject.SetActive(show);
        p.Label.gameObject.SetActive(show);
        if (!show) return;

        var target = where.Value;
        p.Arrow.color = color;
        float dist = Vector3.Distance(PlayerMovement.pos, target);
        p.Label.text = $"{name}  {dist.ToString("0", CultureInfo.InvariantCulture)} m";

        float w = Screen.width, h = Screen.height, k = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        float margin = 70f * k;
        var sp = cam.WorldToScreenPoint(target);
        var center = new Vector2(w / 2f, h / 2f);
        Vector2 at; float angle; Vector2 labelOff;
        if (sp.z > 0f && sp.x > margin && sp.x < w - margin && sp.y > margin && sp.y < h - margin)
        {
            // on screen: point down at it
            at = new Vector2(sp.x, sp.y + 40f * k); angle = 180f; labelOff = new Vector2(0f, 45f * k);
        }
        else
        {
            // off screen (or behind): on the screen's edge, pointing the way to turn
            var dir = new Vector2(sp.x, sp.y) - center;
            if (sp.z < 0f) dir = -dir;
            if (dir.sqrMagnitude < 1f) dir = Vector2.down;
            float t = Mathf.Min((w / 2f - margin) / Mathf.Max(Mathf.Abs(dir.x), 0.001f), (h / 2f - margin) / Mathf.Max(Mathf.Abs(dir.y), 0.001f));
            at = center + dir * t;
            angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            labelOff = -dir.normalized * 60f * k;
        }
        p.Arrow.rectTransform.anchoredPosition = at / k;
        p.Arrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, angle);
        p.Label.rectTransform.anchoredPosition = (at + labelOff) / k;
    }

    // ---- the game's input graph (sticks and triggers), on the main camera ----

    static float nextGraphCheck;

    static void InputGraph(bool inGame)
    {
        if (Time.unscaledTime < nextGraphCheck) return;
        nextGraphCheck = Time.unscaledTime + 0.5f;
        var cam = Camera.main;
        if (cam == null) return;
        var g = cam.GetComponent<Il2Cpp.InputGraph>();
        bool want = inGame && Core.InputDisplay.Value;
        if (want && g == null) g = cam.gameObject.AddComponent<Il2Cpp.InputGraph>();
        if (g != null && g.enabled != want) g.enabled = want;
    }

    // ---- game speed ----

    static float setTo = -1f;

    static void SlowMo(bool inGame)
    {
        float want = Mathf.Clamp(Core.SlowMo.Value, 0.1f, 1f);
        var m = Menu.me;
        bool paused = m != null && m.paused;
        // the game sets 0 while paused and its own values in places: only touch a running game, and only put back 1
        // when it's still what we set
        if (!inGame || paused || Time.timeScale == 0f) return;
        if (!Mathf.Approximately(want, 1f)) { if (!Mathf.Approximately(Time.timeScale, want)) { Time.timeScale = want; setTo = want; } }
        else if (setTo > 0f) { if (Mathf.Approximately(Time.timeScale, setTo)) Time.timeScale = 1f; setTo = -1f; }
    }

    // ---- building it ----

    static bool Make()
    {
        var m = Menu.me;
        if (m == null) return false;
        TMP_Text src = m.ContinueGameButtonText;
        root = new GameObject("PracticeTools hud");
        Object.DontDestroyOnLoad(root);
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 440;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;

        timer = Text("timer", src, 44, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(30f, -20f), new Vector2(600f, 60f));
        splits = Text("splits", src, 22, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(30f, -78f), new Vector2(800f, 400f));
        info = Text("info", src, 24, TextAlignmentOptions.BottomRight, new Vector2(1f, 0f), new Vector2(-30f, 30f), new Vector2(400f, 120f));
        flash = Text("flash", src, 30, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(800f, 50f));
        flash.color = new Color(1f, 0.85f, 0.4f, 0.9f);
        timer.gameObject.SetActive(false); splits.gameObject.SetActive(false); info.gameObject.SetActive(false);
        flash.gameObject.SetActive(false);
        var sprite = ArrowSprite();
        itemPointer = MakePointer("item", src, sprite);
        playerPointer = MakePointer("player", src, sprite);
        return true;
    }

    static Pointer MakePointer(string name, TMP_Text src, Sprite sprite)
    {
        var label = Text(name + " arrow label", src, 22, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, new Vector2(500f, 40f));
        label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        var a = new GameObject(name + " arrow");
        a.transform.SetParent(root.transform, false);
        var img = a.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(44f, 44f);
        label.gameObject.SetActive(false); a.SetActive(false);
        return new Pointer { Arrow = img, Label = label };
    }

    static TextMeshProUGUI Text(string name, TMP_Text src, float size, TextAlignmentOptions align, Vector2 anchor, Vector2 pos, Vector2 box)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (src != null) { t.font = src.font; t.fontSharedMaterial = src.fontSharedMaterial; }
        t.fontSize = size;
        t.color = new Color(1f, 1f, 1f, 0.9f);
        t.alignment = align;
        t.enableWordWrapping = false;
        t.richText = true;
        t.raycastTarget = false;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = box;
        return t;
    }

    // a filled triangle pointing up
    static Sprite ArrowSprite()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var clear = new Color(1f, 1f, 1f, 0f);
        for (int y = 0; y < n; y++)
        {
            float half = (n - 1 - y) / 2f;   // wide at the bottom, a point at the top
            for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Abs(x - (n - 1) / 2f);
                float edge = Mathf.Clamp01(half - dx + 0.5f);   // soft edge
                tex.SetPixel(x, y, edge > 0f ? new Color(1f, 1f, 1f, edge) : clear);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }
}
