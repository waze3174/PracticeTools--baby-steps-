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
    static TextMeshProUGUI timer, steps, splits, info, flash, compass;
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
        CompassStrip(playing);
        Arrows(inGame);
        AngleMark(playing);
        GlitchMarkers(playing);
        CenterDot();
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
        bool showClock = Core.ShowTimer.Value && (linger || inGame && Splits.Now >= 0f);
        var save = SaveGod.theSave;
        bool showSteps = Core.ShowSteps.Value && (linger || inGame) && save != null;
        // scaled from the top left corner; the splits sit right under the timer, or at its place without it
        float k = Mathf.Clamp(Core.TimerScale.Value, 0.5f, 2f);
        timer.rectTransform.localScale = splits.rectTransform.localScale = new Vector3(k, k, 1f);
        splits.rectTransform.anchoredPosition = new Vector2(30f, -20f - (Core.ShowTimer.Value || Core.ShowSteps.Value ? 58f * k : 0f));
        timer.gameObject.SetActive(showClock);
        if (showClock) timer.text = Splits.Fmt(linger ? end.Time : Splits.Now);
        // its own text at a fixed place right of the timer (room for 1:46:05.00), so the ticking time doesn't move it
        steps.gameObject.SetActive(showSteps);
        if (showSteps)
        {
            steps.rectTransform.localScale = new Vector3(k, k, 1f);
            steps.rectTransform.anchoredPosition = new Vector2(30f + (showClock ? 290f * k : 0f), -20f - 18f * k);
            steps.text = $"steps taken: {save.steps}";
        }

        bool showSplits = Core.ShowSplits.Value && (linger || inGame && Splits.Now >= 0f);
        splits.gameObject.SetActive(showSplits);
        if (!showSplits) return;
        var sb = new StringBuilder();
        if (!Splits.Running && !Splits.Finished) sb.Append("<alpha=#80>splits start with a new game<alpha=#FF>\n");
        // columns: split | +/- against the best run | this run's time | the best run's time there | the best segment
        // in gold, each with a toggle. each column is as wide as its widest entry (at least 240 for the split names and
        // 130 for the times, so a normal run doesn't move), so long times push the next columns along
        var list = Splits.List;
        bool showDelta = Core.ShowDelta.Value, showTime = Core.ShowTime.Value, bestRun = Core.ShowBestRun.Value, bestSeg = Core.ShowGold.Value;
        const string Dim = "#9A9A9A", Gold = "#FFD84A", Ahead = "#5CE05C", Behind = "#E05C5C";
        bool any = showDelta || showTime || bestRun || bestSeg;
        var rows = new System.Collections.Generic.List<string[]>();   // split, +/-, time, best run, best seg
        if (any) rows.Add(new[] { Splits.AnyPct ? "split (any%)" : "split (full run)", "+/-", "time", "best run", "best seg" });
        int first = rows.Count;
        int current = Splits.ShownCurrent;
        for (int i = 0; i < list.Count; i++)
        {
            if (Splits.Hidden(i)) continue;   // any%: a split runs skip
            var s = list[i];
            bool cur = Splits.Running && !Splits.Finished && i == current;
            string delta = "", time = "";
            if (s.Skipped && s.Time < 0f)
            {
                // a skipped split says so in the time column, or the +/- one without it
                if (showTime) time = $"<color={Dim}>skipped</color>";
                else delta = $"<color={Dim}>skipped</color>";
            }
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
            rows.Add(new[]
            {
                cur ? $"<b>{s.Name}</b>" : s.Time >= 0f || s.Skipped ? s.Name : $"<color=#B0B0B0>{s.Name}</color>",
                delta, time,
                $"<color={Dim}>{(s.Pb >= 0f ? Splits.Fmt(s.Pb) : "-")}</color>",
                $"<color={Gold}>{(s.Gold >= 0f ? Splits.Fmt(s.Gold) : "-")}</color>",
            });
        }
        int last = rows.Count;
        if (bestRun || bestSeg)
        {
            // best run, and the best possible run from the best segments
            float sob = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                if (Splits.Hidden(i)) continue;
                if (list[i].Gold < 0f) { sob = -1f; break; }
                sob += list[i].Gold;
            }
            if (bestRun && end != null && end.Pb >= 0f) rows.Add(new[] { $"<color={Dim}>best run</color>", "", "", $"<color={Dim}>{Splits.Fmt(end.Pb)}</color>", "" });
            if (bestSeg && sob >= 0f) rows.Add(new[] { $"<color={Dim}>sum of best</color>", "", "", "", $"<color={Gold}>{Splits.Fmt(sob)}</color>" });
        }

        var shown = new[] { true, showDelta, showTime, bestRun, bestSeg };
        var pos = new int[5];
        int x = 0;
        for (int c = 0; c < 5; c++)
        {
            if (!shown[c]) { pos[c] = -1; continue; }
            pos[c] = x;
            float w = 0f;
            for (int r = 0; r < rows.Count; r++)
            {
                string cell = rows[r][c];
                if (r < first) cell = $"<size=70%>{cell}</size>";
                else if (r >= first && r < last && c > 0) cell = $"<b>{cell}</b>";   // room for the time in bold
                w = Mathf.Max(w, Width(cell));
            }
            x += Mathf.Max(c == 0 ? 240 : 130, Mathf.CeilToInt(w) + 24);
        }
        for (int r = 0; r < rows.Count; r++)
        {
            bool head = r < first;
            if (head) sb.Append($"<size=70%><color={Dim}>");
            for (int c = 0; c < 5; c++)
                if (pos[c] >= 0 && rows[r][c] != "") sb.Append(c == 0 ? "" : $"<pos={pos[c]}>").Append(rows[r][c]);
            if (head) sb.Append("</color></size>");
            sb.Append('\n');
        }
        splits.text = sb.ToString();
    }

    // how wide a bit of the splits' text is, in the units <pos> uses (cached: the same strings come back every frame)
    static readonly System.Collections.Generic.Dictionary<string, float> widths = new();

    static float Width(string text)
    {
        if (text == "") return 0f;
        if (widths.TryGetValue(text, out var w)) return w;
        if (widths.Count > 2000) widths.Clear();
        w = splits.GetPreferredValues(text).x;
        widths[text] = w;
        return w;
    }

    // ---- speed and fps ----

    static Vector3 lastPos;
    static bool hasLast;
    static float speed, fps;
    static float lastDir = float.NaN;   // the way nate last moved, degrees (N = +z)

    internal static void Moved() => hasLast = false;   // a state put nate somewhere else

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
                // a load or teleport isn't speed (Moved, and anything over 2 km a frame); a fling on the ocean past
                // the map's edge goes 1000s of m/s, so no lower limit
                if (d.magnitude < 2000f) speed = Mathf.Lerp(speed, v, Mathf.Clamp01(gdt * 5f));
            }
            lastPos = p; hasLast = true;
        }
        else hasLast = false;

        var sb = new StringBuilder();
        if (Core.ShowSpeed.Value) sb.Append(speed.ToString("0.00", CultureInfo.InvariantCulture)).Append(" m/s\n");
        if (Core.ShowFps.Value) sb.Append(Mathf.RoundToInt(fps)).Append(" fps\n");
        if (Core.ShowCoords.Value && inGame)
        {
            // carried, nate's walking code is off and stops updating PlayerMovement.pos: his hips say where he is
            var pmc = PlayerMovement.me;
            var c = Fly.Carrying && pmc != null && pmc.hipPivot != null ? pmc.hipPivot.position : PlayerMovement.pos;
            sb.Append(string.Format(CultureInfo.InvariantCulture, "x {0:0.0}  y {1:0.0}  z {2:0.0}\n", c.x, c.y, c.z));
        }
        if (Core.ShowAngle.Value && inGame && CamAngle.Now(out float yaw, out float pitch))
        {
            sb.Append(string.Format(CultureInfo.InvariantCulture, "yaw {0:0.00}  pitch {1:0.00}\n", yaw, pitch));
            for (int i = 0; i < CamAngle.Marks.Count; i++)
            {
                var mk = CamAngle.Marks[i];
                sb.Append(string.Format(CultureInfo.InvariantCulture, "<color=#FFD84A>{0} off  yaw {1:+0.00;-0.00}  pitch {2:+0.00;-0.00}</color>\n",
                    i + 1, Mathf.DeltaAngle(mk.Yaw, yaw), pitch - mk.Pitch));
            }
        }
        if (Core.ShowCompass.Value && inGame)
        {
            // the strip is at the top middle (CompassStrip); here which shoulder the camera is over. camRight on is
            // the left one (it looks 20° left of camAng instead of 20° right)
            var pmc = PlayerMovement.me;
            if (pmc != null) sb.Append(pmc.camRight ? "left shoulder\n" : "right shoulder\n");
        }
        if (Core.ShowFling.Value && inGame)
        {
            if (FlingWatch.Parts > 0) sb.Append(string.Format(CultureInfo.InvariantCulture, "in water {0} parts, push {1:0.0}k\n", FlingWatch.Parts, FlingWatch.Push / 1000f));
            if (FlingWatch.Last != null) sb.Append("<color=#FFD84A>").Append(FlingWatch.Last).Append("</color>\n");
        }
        if ((Core.ShowLoops.Value || Core.ShowKill.Value) && inGame) Glitch(sb);
        if (inGame) Routes.Info(sb);
        if (!Mathf.Approximately(Core.SlowMo.Value, 1f)) sb.Append("speed ×").Append(Core.SlowMo.Value.ToString("0.00", CultureInfo.InvariantCulture)).Append('\n');
        info.gameObject.SetActive(inGame && sb.Length > 0);
        info.text = sb.ToString();
    }

    // ---- the map's loop and the kill height (water glitch) ----

    const float SouthLine = -128f, NorthLine = 3328f;   // BestRegionLoader.CheckForFloatingPointFix

    static void Glitch(StringBuilder sb)
    {
        var pm = PlayerMovement.me;
        if (pm == null) return;
        // carried, PlayerMovement.pos stops updating: his hips say where he is
        var p = Fly.Carrying && pm.hipPivot != null ? pm.hipPivot.position : PlayerMovement.pos;
        var save = SaveGod.theSave;
        if (Core.ShowLoops.Value && save != null)
        {
            // past z -128 / 3328 the game moves nate (and all that's loose) 3328 m back into the map, 1300 m up / down,
            // and counts it in the save. below 0 every campfire and checkpoint does nothing, the ending too. it only
            // does that while the area around nate is all loaded (fullyLoaded); till then he can go on past the line
            int loops = save.zLoops;
            sb.Append(loops >= 0 ? $"map loops {loops}: campfires on\n" : $"<color=#FF6A5A>map loops {loops}: campfires off, no ending</color>\n");
            bool armed = BestRegionLoader.fullyLoaded;
            string past = p.z < SouthLine ? string.Format(CultureInfo.InvariantCulture, ", {0:0} m past the south line", SouthLine - p.z)
                : p.z > NorthLine ? string.Format(CultureInfo.InvariantCulture, ", {0:0} m past the north line", p.z - NorthLine) : "";
            if (past != "" && armed) sb.Append("<color=#FF6A5A>map loop armed").Append(past).Append("</color>\n");
            else sb.Append(armed ? "map loop armed (area loaded)" : "map loop held (area loading)").Append(past).Append('\n');
        }
        if (Core.ShowKill.Value)
        {
            // PlayerMovement.YLoopCheck: a fallen nate under this sloped plane (y < ~0.4 z - 196) is put 300 m over the
            // ground under him, or 2000 m up with nothing there, speed kept
            var plane = pm.yKillPlane;
            var n = plane.normal;
            if (Mathf.Abs(n.y) > 0.01f)
            {
                float ky = -(n.x * p.x + n.z * p.z + plane.distance) / n.y;
                float above = p.y - ky;
                sb.Append(string.Format(CultureInfo.InvariantCulture, above > 0f ? "kill height y {0:0}, {1:0} m above\n" : "<color=#FFD84A>kill height y {0:0}, {1:0} m below</color>\n", ky, Mathf.Abs(above)));
            }
        }
    }

    // ---- compass, top middle: 60° either side of where the camera looks, a character per 5°, N is +z ----

    static readonly string[] Points8 = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
    static readonly string[] Points16 = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
    internal static string Point16(float yaw) => Points16[Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) / 22.5f) % 16];

    static void CompassStrip(bool inGame)
    {
        float yaw = 0f;
        bool comp = inGame && Core.ShowCompass.Value && CamAngle.Now(out yaw, out _);
        bool moving = inGame && Core.ShowMoving.Value;
        compass.gameObject.SetActive(comp || moving);
        if (!comp && !moving) return;
        var sb = new StringBuilder();
        if (comp) sb.Append(Compass(yaw)).Append(string.Format(CultureInfo.InvariantCulture, "\n<size=80%>{0} {1:0.0}</size>", Point16(yaw), yaw));
        if (moving) sb.Append(comp ? "\n" : "").Append("<size=80%><color=#7CE0FF>").Append(Moving()).Append("</color></size>");
        compass.text = sb.ToString();
    }

    // which way nate is going over the ground (a fling carries on that way); stopped, the way he last went
    static string Moving()
    {
        var mv = FlingWatch.Moving;
        if (mv.magnitude > 0.2f)
        {
            lastDir = Mathf.Repeat(Mathf.Atan2(mv.x, mv.y) * Mathf.Rad2Deg, 360f);
            return string.Format(CultureInfo.InvariantCulture, "moving {0} {1:0.0}, {2:0.0} m/s", Point16(lastDir), lastDir, mv.magnitude);
        }
        return !float.IsNaN(lastDir) ? string.Format(CultureInfo.InvariantCulture, "stopped, last moved {0} {1:0.0}", Point16(lastDir), lastDir) : "not moving";
    }

    static string Compass(float yaw)
    {
        const int Half = 12, Step = 5;
        var row = new char[Half * 2 + 1];
        for (int i = 0; i < row.Length; i++)
        {
            float a = Mathf.Repeat(Mathf.Round(yaw / Step) * Step + (i - Half) * Step, 360f);
            row[i] = Mathf.RoundToInt(a) % 15 == 0 ? '.' : ' ';
        }
        for (int p = 0; p < 8; p++)
        {
            int at = Half + Mathf.RoundToInt(Mathf.DeltaAngle(yaw, p * 45f) / Step);
            string name = Points8[p];
            for (int c = 0; c < name.Length; c++)
                if (at + c >= 0 && at + c < row.Length) row[at + c] = name[c];
        }
        // the middle (where the camera looks) in gold
        var sb = new StringBuilder("<mspace=0.6em>");
        sb.Append(row, 0, Half).Append("<color=#FFD84A>").Append(row[Half] == ' ' || row[Half] == '.' ? '|' : row[Half]).Append("</color>").Append(row, Half + 1, Half);
        return sb.Append("</mspace>").ToString();
    }

    // ---- arrows: to the picked item (gold) and the picked player (their colour) ----

    class Pointer { public Image Arrow; public TextMeshProUGUI Label; }
    static Pointer itemPointer, playerPointer, fruitPointer;

    static void Arrows(bool inGame)
    {
        var it = inGame && Core.Arrow.Value ? Finder.Selected() : null;
        Point(itemPointer, it != null ? Finder.Pos(it) + Vector3.up * 0.3f : null, it?.Name, new Color(1f, 0.85f, 0.4f, 0.95f));
        var fr = inGame && Core.FruitArrow.Value ? Finder.Selected(true) : null;
        Point(fruitPointer, fr != null ? Finder.Pos(fr) + Vector3.up * 0.3f : null, fr?.Name, new Color(0.55f, 1f, 0.45f, 0.95f));
        var pl = inGame && Core.PlayerArrow.Value ? PlayerFinder.Selected() : null;
        var c = pl != null ? pl.Color : Color.white; c.a = 0.95f;
        Point(playerPointer, pl != null ? pl.Root.position + Vector3.up * 0.8f : null, pl?.Name, c);
    }

    // edgeOnly: only while the target is off screen (the glitch markers' crosses), with label as the text; false when
    // nothing shows
    static bool Point(Pointer p, Vector3? where, string name, Color color, bool edgeOnly = false, string label = null)
    {
        var cam = Camera.main;
        bool show = where.HasValue && cam != null;
        float w = Screen.width, h = Screen.height, k = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        float margin = 70f * k;
        var sp = show ? cam.WorldToScreenPoint(where.Value) : default;
        bool onScreen = sp.z > 0f && sp.x > margin && sp.x < w - margin && sp.y > margin && sp.y < h - margin;
        if (edgeOnly && onScreen) show = false;
        p.Arrow.gameObject.SetActive(show);
        p.Label.gameObject.SetActive(show);
        if (!show) return false;

        var target = where.Value;
        p.Arrow.color = color;
        if (label != null) p.Label.text = label;
        else p.Label.text = $"{name}  {Vector3.Distance(PlayerMovement.pos, target).ToString("0", CultureInfo.InvariantCulture)} m";

        var center = new Vector2(w / 2f, h / 2f);
        Vector2 at; float angle; Vector2 labelOff;
        if (onScreen)
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
        p.Label.rectTransform.anchoredPosition = OnScreen(p.Label, (at + labelOff) / k);
        return true;
    }

    // a label's spot moved in so all of it stays on screen (canvas units)
    static Vector2 OnScreen(TMP_Text t, Vector2 pos)
    {
        float k = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f, sw = Screen.width / k, sh = Screen.height / k;
        float half = t.preferredWidth * 0.5f + 10f, halfH = t.preferredHeight * 0.5f + 6f;
        pos.x = half * 2f < sw ? Mathf.Clamp(pos.x, half, sw - half) : sw * 0.5f;
        pos.y = Mathf.Clamp(pos.y, halfH, sh - halfH);
        return pos;
    }

    // ---- the marked camera angles: a numbered gold cross where the camera looked when each was marked ----

    static Image[] marks;
    static TextMeshProUGUI[] markNums;

    static void AngleMark(bool inGame)
    {
        var cam = Camera.main;
        float k = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        for (int i = 0; i < marks.Length; i++)
        {
            bool show = inGame && Core.ShowAngle.Value && i < CamAngle.Marks.Count && cam != null;
            Vector3 sp = default;
            if (show)
            {
                sp = cam.WorldToScreenPoint(cam.transform.position + CamAngle.Marks[i].Dir * 100f);
                show = sp.z > 0f;
            }
            marks[i].gameObject.SetActive(show);
            markNums[i].gameObject.SetActive(show);
            if (!show) continue;
            var at = new Vector2(sp.x, sp.y) / k;
            marks[i].rectTransform.anchoredPosition = at;
            markNums[i].rectTransform.anchoredPosition = at + new Vector2(22f, 22f);
        }
    }

    // ---- glitch markers: a cross in each one's colour where the camera should look, while nate is near its spot ----

    static Image[] gMarks;
    static TextMeshProUGUI[] gLabels;
    static bool dotWasOn, dotOurs;   // the game's centre dot (accessibility setting) lines the crosses up

    // turning glitch markers on turns the game's centre dot on if it's off; turning them off puts it back
    static void CenterDot()
    {
        var cfg = Menu.cfg;
        if (cfg == null) return;
        bool on = Core.ShowGlitchMarks.Value;
        if (on == dotWasOn) return;
        dotWasOn = on;
        if (on && !cfg.centerDot) { SetDot(cfg, true); dotOurs = true; }
        else if (!on && dotOurs) { if (cfg.centerDot) SetDot(cfg, false); dotOurs = false; }
    }

    static void SetDot(MenuCfg cfg, bool on)
    {
        cfg.centerDot = on;
        try { if (Menu.me != null && Menu.me.CenterDotToggle != null) Menu.me.CenterDotToggle.SetIsOnWithoutNotify(on); } catch { }
        DebugLog.Write("glitch markers: the game's centre dot " + (on ? "on" : "off again"));
    }

    static Pointer gPointer;
    // the setup is a chain: GlitchStage -1 waits for nate near the first marker's spot; k shows marker k (the one to
    // aim at) and k - 1; the crosshair within 3 degrees of marker k moves on to k + 1. a fling, or nate 100 m from every
    // marker's spot, ends it (Done). savestates keep the stage
    internal const int Done = -2;
    internal static int GlitchStage = -1;
    const float HitDeg = 3f, Wander = 100f;

    static void GlitchMarkers(bool inGame)
    {
        var cam = Camera.main;
        var pm = PlayerMovement.me;
        float k = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        bool on = inGame && Core.ShowGlitchMarks.Value && cam != null && pm != null && pm.hipPivot != null;
        var hip = on ? pm.hipPivot.position : Vector3.zero;
        CamAngle.Now(out float yaw, out float pitch);
        int n = Mathf.Min(gMarks.Length, GlitchMarks.List.Count);

        if (on && n > 0)
        {
            var first = GlitchMarks.List[0];
            bool nearFirst = new Vector2(hip.x - first.At.x, hip.z - first.At.z).magnitude <= first.Range && Mathf.Abs(hip.y - first.At.y) <= first.Height;
            bool nearAny = false;
            for (int i = 0; i < n; i++) nearAny |= Vector3.Distance(hip, GlitchMarks.List[i].At) < Wander;
            if (GlitchStage >= 0 && (FlingWatch.Flinging || !nearAny)) GlitchStage = Done;
            else if (GlitchStage == Done && nearFirst && !FlingWatch.Flinging) GlitchStage = 0;
            else if (GlitchStage == -1 && nearFirst) GlitchStage = 0;
            if (GlitchStage >= 0 && GlitchStage < n)
            {
                var m = GlitchMarks.List[GlitchStage];
                if (GlitchStage < n - 1 && Mathf.Abs(Mathf.DeltaAngle(m.Yaw, yaw)) <= HitDeg && Mathf.Abs(pitch - m.Pitch) <= HitDeg) GlitchStage++;
            }
        }
        int cur = on && GlitchStage >= 0 && GlitchStage < n ? GlitchStage : -1, prev = cur - 1;

        GlitchMarks.Marker shown = null;
        string text = null;
        float edge = 70f * k;   // as Point: closer to the edge than this, the arrow takes over
        for (int i = 0; i < gMarks.Length; i++)
        {
            var m = i < GlitchMarks.List.Count ? GlitchMarks.List[i] : null;
            bool show = i == cur || i == prev;
            Vector3 sp = show ? cam.WorldToScreenPoint(cam.transform.position + m.Dir * 100f) : default;
            bool visible = show && sp.z > 0f && sp.x > edge && sp.x < Screen.width - edge && sp.y > edge && sp.y < Screen.height - edge;
            gMarks[i].gameObject.SetActive(visible);
            gLabels[i].gameObject.SetActive(visible);
            if (!show) continue;
            string shoulder = m.Shoulder < 0 || (pm.camRight ? 1 : 0) == m.Shoulder ? ""
                : m.Shoulder == 1 ? "  <b>left shoulder (RB)</b>" : "  <b>right shoulder (RB)</b>";
            // the instruction, then how far off
            string t = string.Format(CultureInfo.InvariantCulture, "{0}\n<size=80%>yaw {1:+0.0;-0.0}  pitch {2:+0.0;-0.0}{3}</size>",
                m.Name, Mathf.DeltaAngle(m.Yaw, yaw), pitch - m.Pitch, shoulder);
            if (i == cur) { shown = m; text = t; }
            if (!visible) continue;
            var c = m.Color; c.a = i == cur ? 0.95f : 0.4f;
            gMarks[i].color = c;
            var lc = gLabels[i].color; lc.a = i == cur ? 1f : 0.45f;
            gLabels[i].color = lc;
            var at = new Vector2(sp.x, sp.y) / k;
            gMarks[i].rectTransform.anchoredPosition = at;
            gLabels[i].text = t;
            gLabels[i].rectTransform.anchoredPosition = OnScreen(gLabels[i], at + new Vector2(0f, -48f));
        }
        // the cross off screen: an arrow on the edge, pointing the way to turn, with the text
        Point(gPointer, shown != null ? cam.transform.position + shown.Dir * 100f : null, null, shown?.Color ?? Color.white, true, text);
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
        steps = Text("steps", src, 24, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(320f, -38f), new Vector2(400f, 40f));
        steps.color = new Color(0.69f, 0.69f, 0.69f, 0.9f);
        steps.gameObject.SetActive(false);
        splits = Text("splits", src, 22, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(30f, -78f), new Vector2(800f, 400f));
        info = Text("info", src, 24, TextAlignmentOptions.BottomRight, new Vector2(1f, 0f), new Vector2(-30f, 30f), new Vector2(500f, 320f));
        compass = Text("compass", src, 28, TextAlignmentOptions.Top, new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(900f, 80f));
        compass.gameObject.SetActive(false);
        flash = Text("flash", src, 30, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(800f, 50f));
        flash.color = new Color(1f, 0.85f, 0.4f, 0.9f);
        timer.gameObject.SetActive(false); splits.gameObject.SetActive(false); info.gameObject.SetActive(false);
        flash.gameObject.SetActive(false);
        var sprite = ArrowSprite();
        itemPointer = MakePointer("item", src, sprite);
        playerPointer = MakePointer("player", src, sprite);
        fruitPointer = MakePointer("fruit", src, sprite);
        gPointer = MakePointer("glitch marker", src, sprite);
        var cross = CrossSprite();
        marks = new Image[CamAngle.MaxMarks];
        markNums = new TextMeshProUGUI[CamAngle.MaxMarks];
        for (int i = 0; i < marks.Length; i++)
        {
            var g = new GameObject("angle mark " + (i + 1));
            g.transform.SetParent(root.transform, false);
            var img = g.AddComponent<Image>();
            img.sprite = cross;
            img.raycastTarget = false;
            img.color = new Color(1f, 0.85f, 0.4f, 0.95f);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(40f, 40f);
            g.SetActive(false);
            var num = Text("angle mark number " + (i + 1), src, 22, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, new Vector2(30f, 30f));
            num.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            num.color = img.color;
            num.text = (i + 1).ToString(CultureInfo.InvariantCulture);
            num.gameObject.SetActive(false);
            marks[i] = img; markNums[i] = num;
        }
        gMarks = new Image[GlitchMarks.Max];
        gLabels = new TextMeshProUGUI[GlitchMarks.Max];
        for (int i = 0; i < gMarks.Length; i++)
        {
            var col = i < GlitchMarks.List.Count ? GlitchMarks.List[i].Color : Color.white;
            var g = new GameObject("glitch marker " + (i + 1));
            g.transform.SetParent(root.transform, false);
            var img = g.AddComponent<Image>();
            img.sprite = cross;
            img.raycastTarget = false;
            img.color = col;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(48f, 48f);
            g.SetActive(false);
            var label = Text("glitch marker label " + (i + 1), src, 22, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, new Vector2(700f, 30f));
            label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.color = col;
            label.gameObject.SetActive(false);
            gMarks[i] = img; gLabels[i] = label;
        }
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
    // a thin cross with an open middle and a dark rim, readable on snow and on rock
    static Sprite CrossSprite()
    {
        const int n = 40, arm = 2, gap = 5;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        float c = (n - 1) / 2f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Abs(x - c), dy = Mathf.Abs(y - c);
                bool line = (dx <= arm && dy > gap) || (dy <= arm && dx > gap);
                bool rim = !line && ((dx <= arm + 1.5f && dy > gap - 1.5f) || (dy <= arm + 1.5f && dx > gap - 1.5f));
                tex.SetPixel(x, y, line ? Color.white : rim ? new Color(0f, 0f, 0f, 0.75f) : new Color(1f, 1f, 1f, 0f));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }

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
