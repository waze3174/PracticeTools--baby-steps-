using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace PracticeTools;

// route maps: a line laid behind nate's feet as he goes, kept in slots (UserData/PracticeTools/routes/<n>.txt) and
// drawn over the world to follow on real attempts. pause leaves a marker where nate is, and recording only goes on
// once he's back at it, so scouting doesn't leave lines jumping across the map. it never pauses by itself (that's up
// to the player, fly paths are fine too); a savestate from this recording puts the route back as it was then.
// a file: "desc|<text>", "paused|x y z" while paused, then one point a line, about a metre apart (~22 KB a km)
static class Routes
{
    internal const int Count = 5;
    internal const float ResumeRange = 2.5f;
    const float Spacing = 1f, Lift = 0.15f, Width = 0.075f, SaveEvery = 30f;
    const int Chunk = 500;   // points a line piece: while recording only the last piece changes
    static readonly CultureInfo C = CultureInfo.InvariantCulture;

    internal class Route
    {
        public string Desc = "";
        public readonly List<Vector3> Points = new();
        public Vector3? Marker;   // paused here
        public float Length;
    }

    static readonly Route[] routes = new Route[Count];
    internal static MelonPreferences_Entry<int> Selected;
    internal static int Sel => Mathf.Clamp(Selected.Value, 0, Count - 1);
    internal static Route Get(int i) => i >= 0 && i < Count ? routes[i] : null;   // null: empty slot

    internal static int RecSlot = -1;
    internal static bool Recording => RecSlot >= 0 && routes[RecSlot] != null;
    internal static bool Paused => Recording && routes[RecSlot].Marker != null;
    static float saveAt;
    static bool dirty;

    static string Dir => Path.Combine(Core.Dir, "routes");
    static string FileOf(int i) => Path.Combine(Dir, $"{i + 1}.txt");

    internal static void Init()
    {
        Selected = Core.Cat.CreateEntry("RouteSlot", 0, "Route slot", "The route slot the Routes tab shows and records into (0 = slot 1).");
        Directory.CreateDirectory(Dir);
        for (int i = 0; i < Count; i++) routes[i] = Read(i);
    }

    static Route Read(int i)
    {
        try
        {
            if (!File.Exists(FileOf(i))) return null;
            var r = new Route();
            foreach (var line in File.ReadAllLines(FileOf(i)))
            {
                if (line.StartsWith("desc|")) { r.Desc = line.Substring(5); continue; }
                if (line.StartsWith("paused|")) { if (Vec(line.Substring(7), out var m)) r.Marker = m; continue; }
                if (!Vec(line, out var p)) continue;
                if (r.Points.Count > 0) r.Length += Vector3.Distance(r.Points[r.Points.Count - 1], p);
                r.Points.Add(p);
            }
            return r;
        }
        catch (System.Exception e) { Core.Inst.LoggerInstance.Warning($"route {i + 1}: " + e.Message); return null; }
    }

    static bool Vec(string s, out Vector3 v)
    {
        v = default;
        var f = s.Split(' ');
        if (f.Length < 3 || !float.TryParse(f[0], NumberStyles.Float, C, out v.x) || !float.TryParse(f[1], NumberStyles.Float, C, out v.y)
            || !float.TryParse(f[2], NumberStyles.Float, C, out v.z)) return false;
        return true;
    }

    static void Save(int i)
    {
        var r = routes[i];
        if (r == null) return;
        var sb = new StringBuilder();
        sb.Append("desc|").Append(r.Desc).Append('\n');
        if (r.Marker is Vector3 m) sb.Append("paused|").Append(Fmt(m)).Append('\n');
        foreach (var p in r.Points) sb.Append(Fmt(p)).Append('\n');
        try { File.WriteAllText(FileOf(i), sb.ToString()); dirty = false; }
        catch (System.Exception e) { Core.Inst.LoggerInstance.Warning($"route {i + 1}: " + e.Message); }
    }

    static string Fmt(Vector3 p) => string.Format(C, "{0:0.00} {1:0.00} {2:0.00}", p.x, p.y, p.z);

    // between nate's feet, his hips when there are no feet. swimming, on the water's surface: under it the water hides
    // the line (nate floats belly down, his feet under)
    static bool Where(out Vector3 p)
    {
        if (!Feet(out p)) return false;
        try
        {
            var wos = PlayerMovement.me.waterObjects;
            if (wos != null)
                foreach (var wo in wos)
                {
                    if (wo == null || wo.submergedVolume == 0f) continue;
                    float h = wo.GetWaterHeightSingle(p);
                    if (h > p.y && h - p.y < 30f) p.y = h;
                    break;
                }
        }
        catch { }
        return true;
    }

    static bool Feet(out Vector3 p)
    {
        p = default;
        var pm = PlayerMovement.me;
        if (pm == null || pm.hipPivot == null) return false;
        var feet = pm.feet;
        if (feet != null && feet.Length >= 2 && feet[0] != null && feet[1] != null && feet[0].bone != null && feet[1].bone != null)
            p = (feet[0].bone.position + feet[1].bone.position) * 0.5f;
        else p = pm.hipPivot.position;
        return true;
    }

    internal static float ToMarker()
    {
        if (!Paused || !Where(out var p)) return -1f;
        return Vector3.Distance(p, routes[RecSlot].Marker.Value);
    }

    // ---- the buttons ----

    internal static void Start(int i)
    {
        if (!Where(out var p)) { Hud.Flash("LOAD A SAVE FIRST"); return; }
        if (Recording) Stop();
        var r = new Route { Desc = routes[i]?.Desc ?? "" };
        r.Points.Add(p);
        routes[i] = r;
        RecSlot = i;
        Save(i);
        Hud.Flash($"RECORDING ROUTE {i + 1}");
    }

    internal static void Stop()
    {
        if (!Recording) return;
        int i = RecSlot;
        Save(i);
        RecSlot = -1;
        Hud.Flash($"ROUTE {i + 1} SAVED");
    }

    internal static void Pause()
    {
        if (!Recording || Paused) return;
        if (!Where(out var p)) return;
        var r = routes[RecSlot];
        Add(r, p, true);
        r.Marker = p;
        Save(RecSlot);
        Hud.Flash("ROUTE PAUSED: COME BACK TO THE MARKER TO RESUME");
    }

    internal static void Resume()
    {
        if (!Paused) return;
        float d = ToMarker();
        if (d < 0f) return;
        if (d > ResumeRange) { Hud.Flash(string.Format(C, "GO BACK TO THE MARKER ({0:0} M)", d)); return; }
        var r = routes[RecSlot];
        r.Marker = null;
        Save(RecSlot);
        Hud.Flash("RECORDING AGAIN");
    }

    // a saved route goes on from its marker (or its end): paused there until nate gets to it
    internal static void Continue(int i)
    {
        var r = routes[i];
        if (r == null || r.Points.Count == 0) return;
        if (Recording) Stop();
        r.Marker ??= r.Points[r.Points.Count - 1];
        RecSlot = i;
        Save(i);
        Hud.Flash($"ROUTE {i + 1}: GO TO THE MARKER, THEN RESUME");
    }

    internal static void Delete(int i)
    {
        if (RecSlot == i) RecSlot = -1;
        routes[i] = null;
        try { File.Delete(FileOf(i)); } catch (System.Exception e) { Core.Inst.LoggerInstance.Warning($"route {i + 1}: " + e.Message); }
    }

    internal static void SetDesc(int i, string desc)
    {
        var r = routes[i];
        if (r == null) return;
        r.Desc = desc.Replace('\n', ' ').Replace('|', '/');
        Save(i);
    }

    static void Add(Route r, Vector3 p, bool always)
    {
        if (r.Points.Count > 0)
        {
            float d = Vector3.Distance(r.Points[r.Points.Count - 1], p);
            if (d < Spacing && !always) return;
            if (d < 0.01f) return;
            r.Length += d;
        }
        r.Points.Add(p);
        dirty = true;
    }

    // ---- once a frame ----

    internal static void Update()
    {
        if (Recording) Record();
        Draw();
    }

    static void Record()
    {
        var r = routes[RecSlot];
        // a state load: one saved while recording this route puts it back as it was then (Restore); nothing is
        // recorded until the load is done
        if (States.Loading) return;
        if (!Where(out var p)) return;
        if (r.Marker == null) Add(r, p, false);
        if (dirty && Time.unscaledTime > saveAt) { saveAt = Time.unscaledTime + SaveEvery; Save(RecSlot); }
    }

    // ---- savestates ----
    // route|slot|points|last point|marker or -: the route being recorded when the state was saved
    internal static void Snapshot(StringBuilder sb)
    {
        if (!Recording) return;
        var r = routes[RecSlot];
        if (r.Points.Count == 0) return;
        sb.Append("route|").Append(RecSlot.ToString(C)).Append('|').Append(r.Points.Count.ToString(C)).Append('|')
          .Append(Fmt(r.Points[r.Points.Count - 1])).Append('|').Append(r.Marker is Vector3 m ? Fmt(m) : "-").Append('\n');
    }

    // the route still being recorded goes back to where it was at the save: the points since are dropped. only that
    // same route, and only if it's the one from the save (its point there matches); routes not being recorded stay as
    // they are
    internal static void Restore(string[] f)
    {
        if (f == null || f.Length < 5 || !Recording) return;
        if (!int.TryParse(f[1], NumberStyles.Integer, C, out int slot) || slot != RecSlot) return;
        if (!int.TryParse(f[2], NumberStyles.Integer, C, out int count) || !Vec(f[3], out var last)) return;
        var r = routes[RecSlot];
        if (count < 1 || count > r.Points.Count || Vector3.Distance(r.Points[count - 1], last) > 0.05f) return;
        r.Points.RemoveRange(count, r.Points.Count - count);
        r.Length = 0f;
        for (int i = 1; i < r.Points.Count; i++) r.Length += Vector3.Distance(r.Points[i - 1], r.Points[i]);
        r.Marker = f[4] != "-" && Vec(f[4], out var m) ? m : null;
        Save(RecSlot);
        DebugLog.Write($"route {RecSlot + 1}: back to the state's {count} points");
    }

    internal static void Info(StringBuilder sb)
    {
        if (!Recording) return;
        var r = routes[RecSlot];
        if (r.Marker != null)
        {
            float d = ToMarker();
            sb.Append(d >= 0f && d <= ResumeRange
                ? $"<color=#7CE0FF>route {RecSlot + 1} paused: at the marker, resume in the menu</color>\n"
                : string.Format(C, "<color=#7CE0FF>route {0} paused: marker {1:0} m away</color>\n", RecSlot + 1, d));
        }
        else sb.Append(string.Format(C, "<color=#FF6A5A>REC</color> route {0}  {1:0.00} km\n", RecSlot + 1, r.Length / 1000f));
    }

    // ---- drawing ----

    static GameObject root;
    static Material mat;
    static readonly Color LineColor = new(1f, 0.62f, 0.12f, 0.95f), MarkerColor = new(0.45f, 0.88f, 1f, 0.9f);

    class Drawn { public Route Route; public int Points; public readonly List<LineRenderer> Pieces = new(); public LineRenderer Beam; }
    static readonly Drawn[] drawn = new Drawn[Count];
    static readonly bool[] shown = new bool[Count];
    static bool toldNoShader;

    static void Draw()
    {
        bool inGame = PlayerMovement.me != null;
        for (int i = 0; i < Count; i++)
        {
            bool show = inGame && routes[i] != null && ((Core.ShowRoute.Value && i == Sel) || i == RecSlot);
            if (show != shown[i])
            {
                shown[i] = show;
                DebugLog.Write(show ? $"route {i + 1} drawn: {routes[i].Points.Count} points{(i == RecSlot ? " (recording)" : "")}" : $"route {i + 1} hidden");
            }
            if (!show) { Hide(i); continue; }
            if (!Ready()) { if (!toldNoShader) { toldNoShader = true; Core.Inst.LoggerInstance.Warning("routes: no shader for the line"); } return; }
            Show(i, routes[i]);
        }
    }

    static bool Ready()
    {
        if (root != null && mat != null) return true;
        if (root == null) { root = new GameObject("PracticeTools routes"); Object.DontDestroyOnLoad(root); }
        if (mat == null)
        {
            Shader sh = null;
            foreach (var n in new[] { "Sprites/Default", "UI/Default", "Hidden/Internal-Colored" }) { sh = Shader.Find(n); if (sh != null) break; }
            if (sh == null) return false;
            mat = new Material(sh) { hideFlags = HideFlags.DontUnloadUnusedAsset };
        }
        return true;
    }

    static void Hide(int i)
    {
        var d = drawn[i];
        if (d == null) return;
        foreach (var lr in d.Pieces) if (lr != null) lr.gameObject.SetActive(false);
        if (d.Beam != null) d.Beam.gameObject.SetActive(false);
    }

    static void Show(int i, Route r)
    {
        var d = drawn[i] ??= new Drawn();
        if (d.Route != r) { d.Route = r; d.Points = 0; foreach (var lr in d.Pieces) if (lr != null) lr.positionCount = 0; }
        int n = r.Points.Count;
        if (d.Points > n) d.Points = 0;
        // pieces share their end point, so the line has no gaps
        int pieces = n < 2 ? 0 : (n - 2) / (Chunk - 1) + 1;
        for (int k = 0; k < pieces; k++)
        {
            int from = k * (Chunk - 1), to = Mathf.Min(n, from + Chunk);
            while (d.Pieces.Count <= k) d.Pieces.Add(Line($"route {i + 1} piece {d.Pieces.Count + 1}", LineColor, Width));
            var lr = d.Pieces[k];
            if (lr == null) { lr = d.Pieces[k] = Line($"route {i + 1} piece {k + 1}", LineColor, Width); }
            if (!lr.gameObject.activeSelf) lr.gameObject.SetActive(true);
            // a scene load can take the material, or the line along with it: a fresh one is drawn whole again
            if (lr.sharedMaterial == null) { lr.sharedMaterial = mat; lr.positionCount = 0; }
            if (to <= d.Points && lr.positionCount == to - from) continue;   // unchanged
            lr.positionCount = to - from;
            for (int j = from; j < to; j++) lr.SetPosition(j - from, r.Points[j] + Vector3.up * Lift);
        }
        for (int k = pieces; k < d.Pieces.Count; k++) if (d.Pieces[k] != null) d.Pieces[k].gameObject.SetActive(false);
        d.Points = n;

        // the pause marker: a tall post, seen from far off
        if (r.Marker is Vector3 m)
        {
            if (d.Beam == null) d.Beam = Line($"route {i + 1} marker", MarkerColor, 0.35f);
            if (d.Beam.sharedMaterial == null) d.Beam.sharedMaterial = mat;
            if (!d.Beam.gameObject.activeSelf) d.Beam.gameObject.SetActive(true);
            d.Beam.positionCount = 2;
            d.Beam.SetPosition(0, m);
            d.Beam.SetPosition(1, m + Vector3.up * 40f);
        }
        else if (d.Beam != null) d.Beam.gameObject.SetActive(false);
    }

    static LineRenderer Line(string name, Color col, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.sharedMaterial = mat;
        lr.startColor = lr.endColor = col;
        lr.widthMultiplier = width;
        lr.numCornerVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.positionCount = 0;
        return lr;
    }
}
