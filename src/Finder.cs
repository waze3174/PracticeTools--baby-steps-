using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace PracticeTools;

// every item in the game (hats, things to carry) has a GlobalObjectLoader that loads it near nate and keeps where it
// was left. the list comes from them; the arrow follows the item itself while it's loaded (carried, dropped, rolling),
// else the spot its loader keeps for it
static class Finder
{
    internal class Item { public string Key, Name; public GlobalObjectLoader Loader; }
    internal static readonly List<Item> Items = new();
    static float nextRefresh;
    static bool logged;

    internal static void Update()
    {
        if (PlayerMovement.me == null) { if (Items.Count > 0) Items.Clear(); return; }
        if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + 3f; Refresh(); }
    }

    static void Refresh()
    {
        var found = Object.FindObjectsByType<GlobalObjectLoader>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Items.Clear();
        var seen = new HashSet<string>();
        foreach (var l in found)
        {
            if (l == null) continue;
            string key = string.IsNullOrEmpty(l.loadKey) ? l.gameObject.name : l.loadKey;
            if (!seen.Add(key)) continue;
            Items.Add(new Item { Key = key, Name = NameOf(l), Loader = l });
        }
        Items.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
        // same names: number them
        for (int i = 0; i < Items.Count; i++)
        {
            int n = 1;
            for (int j = i + 1; j < Items.Count && Items[j].Name == Items[i].Name; j++) Items[j].Name += " " + (++n);
            if (n > 1) Items[i].Name += " 1";
        }
        if (!logged && Items.Count > 0)
        {
            logged = true;
            Core.Inst.LoggerInstance.Msg($"{Items.Count} items:");
            foreach (var it in Items) Core.Inst.LoggerInstance.Msg($"  {it.Name}  (key {it.Key}, loader {it.Loader.gameObject.name}, at {Pos(it)})");
        }
    }

    static string NameOf(GlobalObjectLoader l)
    {
        string n = l.loadedInstance != null ? l.loadedInstance.name : l.gameObject.name;
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
        var l = it.Loader;
        if (l == null) return Vector3.zero;
        var inst = l.loadedInstance;
        return inst != null && inst.activeInHierarchy ? inst.transform.position : l.transform.position;
    }

    // tele-grab: a metre in front of nate (the way the camera faces). a loaded item is
    // switched off, moved and back on (so its physics restart there); an unloaded one gets its loader moved, so it
    // loads right here
    internal static void Bring(Item it)
    {
        var pm = PlayerMovement.me;
        var l = it?.Loader;
        if (pm == null || l == null) return;
        var at = PlayerMovement.pos + Quaternion.Euler(0f, pm.camAng, 0f) * Vector3.forward + Vector3.up * 0.5f;
        var inst = l.loadedInstance;
        if (inst != null)
        {
            inst.SetActive(false);
            inst.transform.position = at;
            inst.SetActive(true);
            foreach (var rb in inst.GetComponentsInChildren<Rigidbody>())
                if (!rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }
        else l.transform.position = at;
        Physics.SyncTransforms();
        Hud.Flash(it.Name.ToUpperInvariant() + " BROUGHT HERE");
    }

    internal static Item Selected()
    {
        string k = Core.ArrowItem.Value;
        foreach (var it in Items) if (it.Key == k) return it;
        return null;
    }

    internal static int SelectedIndex()
    {
        string k = Core.ArrowItem.Value;
        for (int i = 0; i < Items.Count; i++) if (Items[i].Key == k) return i;
        return -1;
    }
}
