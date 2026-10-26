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
        Timer(playing);
        Info(playing);
        Arrows(inGame);
        flash.gameObject.SetActive(Time.unscaledTime < flashUntil);
        InputGraph(inGame);
        SlowMo(inGame);
    }

    // ---- timer and splits ----

    static void Timer(bool inGame)
    {
        bool show = inGame && Core.ShowTimer.Value && Splits.Now >= 0f;
        // scaled from the top left corner; the splits sit right under the timer, or at its place without it
        float k = Mathf.Clamp(Core.TimerScale.Value, 0.5f, 2f);
        timer.rectTransform.localScale = splits.rectTransform.localScale = new Vector3(k, k, 1f);
        splits.rectTransform.anchoredPosition = new Vector2(30f, -20f - (Core.ShowTimer.Value ? 58f * k : 0f));
        timer.gameObject.SetActive(show);
        if (show) timer.text = Splits.Fmt(Splits.Now);

        bool showSplits = inGame && Core.ShowSplits.Value && Splits.Now >= 0f;
        splits.gameObject.SetActive(showSplits);
        if (!showSplits) return;
        var sb = new StringBuilder();
        if (!Splits.Running && !Splits.Finished) sb.Append("<alpha=#80>splits start with a new game<alpha=#FF>\n");
        var list = Splits.List;
        for (int i = 0; i < list.Count; i++)
        {
            var s = list[i];
            bool cur = Splits.Running && !Splits.Finished && i == Splits.Current;
            string delta = "";
            string time;
            if (s.Time >= 0f)
            {
                time = Splits.Fmt(s.Time);
                if (s.Pb >= 0f)
                {
                    float d = s.Time - s.Pb;
                    float seg = s.Time - (i > 0 ? list[i - 1].Time : 0f);
                    // gold: best segment ever, green ahead, red behind
                    string col = s.Gold >= 0f && seg <= s.Gold + 0.001f ? "#FFD84A" : d < 0f ? "#5CE05C" : "#E05C5C";
                    delta = $"<color={col}>{Splits.Delta(d)}</color>  ";
                }
            }
            else
            {
                time = s.Pb >= 0f ? Splits.Fmt(s.Pb) : "-";
                // running behind the best run on the current split: show it live
                if (cur && s.Pb >= 0f && Splits.Now > s.Pb) delta = $"<color=#E05C5C>{Splits.Delta(Splits.Now - s.Pb)}</color>  ";
            }
            string line = $"{s.Name}   {delta}{time}";
            sb.Append(cur ? $"<color=#FFFFFF><b>{line}</b></color>" : s.Time >= 0f ? line : $"<alpha=#B0>{line}<alpha=#FF>").Append('\n');
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
        splits = Text("splits", src, 22, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(30f, -78f), new Vector2(700f, 400f));
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
