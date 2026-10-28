using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace PracticeTools;

// every item in the game (hats, things to carry) has a GlobalObjectLoader that loads it near nate and keeps where it
// was left. the list comes from them; the arrow follows the item itself while it's loaded (carried, dropped, rolling),
// else the spot its loader keeps for it
static class Finder
{
    internal class Item { public string Key, Name; public GlobalObjectLoader Loader; public Grabable Fruit; public Vector3 At; public float Progress; public string Base, Save; public bool Eaten; }
    internal static readonly List<Item> Items = new();
    internal static readonly List<Item> FruitList = new();
    static float nextRefresh;
    static bool logged;

    // fruits have no loader: they load with their area. their eating cutscenes (BBConvoStarter isFruit) give the spots
    // without going there, if those are in the scene; a fruit itself once loaded moves the spot onto it. each one is
    // remembered (fruits.txt: name|x y z|key) so it stays in the list once its area unloads
    static string FruitFile => System.IO.Path.Combine(Core.Dir, "fruits.txt");
    static readonly Dictionary<string, Item> fruits = new();
    static bool fruitsRead;
    static int fruitsLogged;

    internal static void Update()
    {
        if (PlayerMovement.me == null) { Items.Clear(); FruitList.Clear(); return; }
        if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + 3f; Refresh(); }
    }

    static void Refresh()
    {
        var found = Object.FindObjectsByType<GlobalObjectLoader>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Items.Clear();
        var seen = new HashSet<string>();
        var loaded = new HashSet<int>();
        foreach (var l in found)
        {
            if (l == null) continue;
            if (l.loadedInstance != null) loaded.Add(l.loadedInstance.GetInstanceID());
            string key = string.IsNullOrEmpty(l.loadKey) ? l.gameObject.name : l.loadKey;
            if (!seen.Add(key)) continue;
            Items.Add(new Item { Key = key, Name = NameOf(l), Loader = l });
        }
        Fruits(loaded);
        FruitList.Clear();
        foreach (var f in fruits.Values) { f.Name = f.Base; FruitList.Add(f); }
        var path = Campfires();
        Order(Items, path);
        Order(FruitList, path);
        if (!logged && Items.Count > 0)
        {
            logged = true;
            DebugLog.Write($"{Items.Count} items:");
            foreach (var it in Items) DebugLog.Write($"  {it.Name}  (key {it.Key}, loader {it.Loader.gameObject.name}, at {Pos(it)}, {it.Progress:0} m in)");
        }
    }

    // spawn to the end of the game: how far along the campfires (spawn, 1 .. 9) the closest point is
    static void Order(List<Item> Items, Vector3[] path)
    {
        foreach (var it in Items) it.Progress = path == null ? 0f : Along(path, Pos(it));
        Items.Sort((a, b) => a.Progress != b.Progress ? a.Progress.CompareTo(b.Progress) : string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
        // same names: number them in that order
        var count = new Dictionary<string, int>();
        foreach (var it in Items) count[it.Name] = count.TryGetValue(it.Name, out int c) ? c + 1 : 1;
        var nth = new Dictionary<string, int>();
        foreach (var it in Items)
        {
            if (count[it.Name] < 2) continue;
            string bare = it.Name;
            nth[bare] = nth.TryGetValue(bare, out int k) ? k + 1 : 1;
            it.Name = bare + " " + nth[bare];
        }
        foreach (var it in Items) if (it.Eaten) it.Name += " (eaten)";
    }

    static void Fruits(HashSet<int> loaded)
    {
        if (!fruitsRead)
        {
            fruitsRead = true;
            try
            {
                if (System.IO.File.Exists(FruitFile))
                    foreach (var line in System.IO.File.ReadAllLines(FruitFile))
                    {
                        var f = line.Split('|');
                        if (line.StartsWith("#") || f.Length < 2) continue;
                        var v = f[1].Split(' ');
                        if (v.Length < 3) continue;
                        var at = new Vector3(Num(v[0]), Num(v[1]), Num(v[2]));
                        string key = f.Length > 2 && f[2].Length > 0 ? f[2] : FruitKey(f[0], at);
                        fruits[key] = new Item { Key = key, Base = f[0], At = at, Save = key.StartsWith("fruit:cutscene:") ? key.Substring(15) : null };
                    }
            }
            catch (System.Exception e) { Core.Inst.LoggerInstance.Warning("fruits.txt: " + e.Message); }
        }
        bool added = false;
        foreach (var c in Object.FindObjectsByType<BBConvoStarter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            try
            {
                if (c == null || !c.IsFruit()) continue;
                string save = c.GetSaveName();
                if (string.IsNullOrEmpty(save)) continue;
                string key = "fruit:cutscene:" + save;
                if (fruits.ContainsKey(key)) continue;
                var at = c.transform.position;
                fruits[key] = new Item { Key = key, Base = Pretty(c.gameObject.name), At = at, Save = save };
                added = true;
                if (fruitsLogged++ < 80) DebugLog.Write($"fruit cutscene found: {c.gameObject.name} (saved as {save}) at {at}");
            }
            catch (System.Exception e) { if (fruitsLogged++ < 80) Core.Inst.LoggerInstance.Warning("fruit cutscene: " + e.Message); }
        }
        foreach (var g in Object.FindObjectsByType<Grabable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (g == null || g.type != GrabableType.fruit || loaded.Contains(g.gameObject.GetInstanceID())) continue;
            string name = Pretty(g.gameObject.name);
            string save = null;
            try { if (g.cutsceneOnGrab != null) save = g.cutsceneOnGrab.GetSaveName(); } catch { }
            Item it = null;
            if (!string.IsNullOrEmpty(save)) fruits.TryGetValue("fruit:cutscene:" + save, out it);
            it ??= Match(g, name);
            var at = g.transform.position;
            if (it == null)
            {
                string key = string.IsNullOrEmpty(save) ? FruitKey(name, at) : "fruit:cutscene:" + save;
                it = new Item { Key = key, Base = name, At = at, Save = save };
                fruits[key] = it;
                added = true;
                if (fruitsLogged++ < 80) DebugLog.Write($"fruit found: {g.gameObject.name} at {at}{(save != null ? " (cutscene " + save + ")" : "")}");
            }
            else if (it.Fruit == null && (it.Base != name || Vector3.Distance(it.At, at) > 0.5f))
            {
                // the fruit itself: its name and spot over the cutscene's
                if (fruitsLogged++ < 80) DebugLog.Write($"fruit {it.Base} is {g.gameObject.name} at {at} ({Vector3.Distance(it.At, at):0} m from where it was kept)");
                it.Base = name; it.At = at; added = true;
            }
            it.Fruit = g;
        }
        // eaten: its cutscene has played on this save
        var played = SaveGod.theSave?.dialogsPlayed;
        foreach (var it in fruits.Values)
        {
            try { it.Eaten = played != null && it.Save != null && played.Contains(it.Save); } catch { it.Eaten = false; }
        }
        if (!added) return;
        try
        {
            var lines = new List<string> { "# fruits found so far (name|x y z|key). PracticeTools adds each one it finds" };
            foreach (var it in fruits.Values) lines.Add($"{it.Base}|{F(it.At.x)} {F(it.At.y)} {F(it.At.z)}|{it.Key}");
            System.IO.File.WriteAllLines(FruitFile, lines);
        }
        catch (System.Exception e) { Core.Inst.LoggerInstance.Warning("fruits.txt: " + e.Message); }
    }

    // a loaded fruit without a cutscene is the remembered one if it's the same object, or one of that name within 3 m
    static Item Match(Grabable g, string name)
    {
        Item best = null;
        float bestD = 3f;
        foreach (var it in fruits.Values)
        {
            if (it.Fruit != null && it.Fruit == g) return it;
            if (it.Base != name) continue;
            float d = Vector3.Distance(it.At, g.transform.position);
            if (d < bestD) { bestD = d; best = it; }
        }
        return best;
    }

    static string FruitKey(string name, Vector3 at) => $"fruit:{name}@{Mathf.RoundToInt(at.x)},{Mathf.RoundToInt(at.y)},{Mathf.RoundToInt(at.z)}";
    static string F(float v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    static float Num(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    static Vector3[] Campfires()
    {
        var d = Menu.me != null ? Menu.me.campfireDatas : null;
        if (d == null || d.Length < 2) return null;
        var p = new Vector3[d.Length];
        for (int i = 0; i < d.Length; i++) p[i] = d[i] != null ? d[i].pos : (i > 0 ? p[i - 1] : Vector3.zero);
        return p;
    }

    // metres along the line through the campfires to the point on it closest to `at`
    static float Along(Vector3[] path, Vector3 at)
    {
        float best = float.MaxValue, bestAlong = 0f, sum = 0f;
        for (int i = 0; i + 1 < path.Length; i++)
        {
            Vector3 a = path[i], ab = path[i + 1] - a;
            float len = ab.magnitude;
            float t = len < 0.01f ? 0f : Mathf.Clamp01(Vector3.Dot(at - a, ab) / (len * len));
            float d = (a + ab * t - at).sqrMagnitude;
            if (d < best) { best = d; bestAlong = sum + t * len; }
            sum += len;
        }
        return bestAlong;
    }

    static string NameOf(GlobalObjectLoader l) => Pretty(l.loadedInstance != null ? l.loadedInstance.name : l.gameObject.name);

    static string Pretty(string n)
    {
        n = System.Text.RegularExpressions.Regex.Replace(n, @"\s*\(\d+\)$", "");   // Apple (3) -> Apple
        n = n.Replace("(Clone)", "").Replace("Loader", "").Replace("Grabable", "").Replace("_", " ").Trim();
        // AlarmClock -> Alarm Clock
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < n.Length; i++)
        {
            if (i > 0 && char.IsUpper(n[i]) && char.IsLower(n[i - 1])) sb.Append(' ');
            sb.Append(n[i]);
        }
        n = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), " +", " ").Trim();
        return n.Length == 0 ? "?" : n;
    }

    internal static Vector3 Pos(Item it)
    {
        if (it.Loader == null)
        {
            var g = it.Fruit;
            return g != null && g.gameObject.activeInHierarchy ? g.transform.position : it.At;
        }
        var l = it.Loader;
        var inst = l.loadedInstance;
        return inst != null && inst.activeInHierarchy ? inst.transform.position : l.transform.position;
    }

    // tele-grab: a metre in front of nate (the way the camera faces). a loaded item is
    // switched off, moved and back on (so its physics restart there); an unloaded one gets its loader moved, so it
    // loads right here
    internal static void Bring(Item it)
    {
        var pm = PlayerMovement.me;
        if (pm == null || it == null) return;
        var l = it.Loader;
        var at = PlayerMovement.pos + Quaternion.Euler(0f, pm.camAng, 0f) * Vector3.forward + Vector3.up * 0.5f;
        if (l == null)
        {
            // a fruit can only come along while its area is loaded
            if (it.Fruit == null || !it.Fruit.gameObject.activeInHierarchy) { Hud.Flash("FRUIT NOT LOADED: GO CLOSER FIRST"); return; }
            l = null;
        }
        var inst = l != null ? l.loadedInstance : it.Fruit.gameObject;
        if (inst != null)
        {
            inst.SetActive(false);
            inst.transform.position = at;
            inst.SetActive(true);
            foreach (var rb in inst.GetComponentsInChildren<Rigidbody>())
                if (!rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }
        else l.transform.position = at;
        if (l == null) it.At = at;
        Physics.SyncTransforms();
        Hud.Flash(it.Name.ToUpperInvariant() + " BROUGHT HERE");
    }

    internal static Item Selected(bool fruit = false)
    {
        string k = fruit ? Core.ArrowFruit.Value : Core.ArrowItem.Value;
        foreach (var it in fruit ? FruitList : Items) if (it.Key == k) return it;
        return null;
    }

    internal static int SelectedIndex(bool fruit = false)
    {
        string k = fruit ? Core.ArrowFruit.Value : Core.ArrowItem.Value;
        var list = fruit ? FruitList : Items;
        for (int i = 0; i < list.Count; i++) if (list[i].Key == k) return i;
        return -1;
    }
}
