using System.Collections.Generic;
using UnityEngine;

namespace PracticeTools;

// the other players in the baby steps multiplayer mod (optional: only touched when it's installed). their position comes
// over the network, so it's known anywhere on the map
static class PlayerFinder
{
    internal class Player { public string Name; public Transform Root; public Color Color; }
    internal static readonly List<Player> Players = new();
    internal static bool Available = true;   // the multiplayer mod is there (off on the first sign it isn't)
    static float nextRefresh;

    internal static void Update()
    {
        if (!Available || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 1f;
        try { Refresh(); }
        catch (System.Exception e) when (e is System.IO.FileNotFoundException || e is System.TypeLoadException || e is System.MissingMethodException || e is System.MissingFieldException)
        { Available = false; Players.Clear(); }
    }

    // separate method so a missing multiplayer mod only fails here
    static void Refresh()
    {
        Players.Clear();
        var nm = BabyStepsMultiplayerClient.Core.networkManager;
        if (nm == null) return;
        foreach (var kv in nm.players)
        {
            var p = kv.Value;
            if (p == null || !p.firstAppearanceApplication || p.rootBone == null) continue;
            Players.Add(new Player { Name = string.IsNullOrEmpty(p.displayName) ? "Nate" : p.displayName, Root = p.rootBone, Color = p.playerColor });
        }
        Players.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
    }

    internal static Player Selected()
    {
        string n = Core.ArrowPlayer.Value;
        foreach (var p in Players) if (p.Name == n && p.Root != null) return p;
        return null;
    }

    // a spot next to them: a metre and a half to the side we're coming from, so we don't land inside them
    internal static Vector3 Near(Player p)
    {
        var at = p.Root.position;
        var from = Il2Cpp.PlayerMovement.pos - at; from.y = 0f;
        if (from.sqrMagnitude < 0.01f) from = Vector3.forward;
        return at + from.normalized * 1.5f + Vector3.up * 0.3f;
    }

    internal static int SelectedIndex()
    {
        string n = Core.ArrowPlayer.Value;
        for (int i = 0; i < Players.Count; i++) if (Players[i].Name == n) return i;
        return -1;
    }
}
