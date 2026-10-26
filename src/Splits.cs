using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace PracticeTools;

// the timer is the game's own play time (SaveGod.GetPreciseTimePlayed, what the ending shows). splits go automatically:
// each split ends at a campfire (the game's episodes start at campfires 1 to 8, 9 is the end) or at the credits.
// a run counts from a new game (play time starts near 0); loading another save mid-run stops counting until the next
// new game. names, campfires, best run and best segments live in UserData/PracticeTools/splits.txt
// (name|campfire or end|best run split|best segment)
static class Splits
{
    internal class Split { public string Name; public int Camp = -1; public float Pb = -1f, Gold = -1f, Time = -1f; }   // Camp -1 = the end
    internal static readonly List<Split> List = new();
    internal static int Current;          // the split being run
    internal static bool Running;         // a run from a new game is going
    internal static bool Finished;
    internal static float Now;            // play time this frame, -1 when there's none
    static float last = -1f;

    static readonly (string name, int camp)[] Defaults =
    {
        ("Swamp", 1), ("Chapter Two", 2), ("The Forest", 3), ("Crater Lake", 4), ("The Desert", 5), ("The Fortress", 6),
        ("The Rolling Hills", 7), ("The Snowy Area", -1),
    };
    static string File_ => Path.Combine(Core.Dir, "splits.txt");

    internal static void Load()
    {
        List.Clear();
        var c = CultureInfo.InvariantCulture;
        try
        {
            // 0.1.0 test builds wrote the file without campfires: start over
            if (File.Exists(File_) && File.ReadAllText(File_).Contains("|campfire|"))
                foreach (var line in File.ReadAllLines(File_))
                {
                    var f = line.Split('|');
                    if (f.Length < 1 || f[0].Trim() == "" || f[0].StartsWith("#")) continue;
                    var s = new Split { Name = f[0].Trim() };
                    if (f.Length > 1 && int.TryParse(f[1].Trim(), NumberStyles.Integer, c, out var camp)) s.Camp = camp;
                    if (f.Length > 2 && float.TryParse(f[2], NumberStyles.Float, c, out var pb)) s.Pb = pb;
                    if (f.Length > 3 && float.TryParse(f[3], NumberStyles.Float, c, out var g)) s.Gold = g;
                    List.Add(s);
                }
        }
        catch { List.Clear(); }
        if (List.Count == 0) { foreach (var (n, camp) in Defaults) List.Add(new Split { Name = n, Camp = camp }); Save(); }
    }

    static void Save()
    {
        var c = CultureInfo.InvariantCulture;
        var lines = new List<string>
        {
            "# name|campfire|best run split|best segment (seconds, -1 = none)",
            "# a split ends when nate reaches that campfire (1 to 8 start the episodes, 9 = thanks for playing), end = the credits",
        };
        foreach (var s in List) lines.Add(string.Format(c, "{0}|{1}|{2:0.000}|{3:0.000}", s.Name, s.Camp < 0 ? "end" : s.Camp.ToString(c), s.Pb, s.Gold));
        try { File.WriteAllLines(File_, lines); } catch { }
    }

    internal static void ResetBests()
    {
        foreach (var s in List) { s.Pb = -1f; s.Gold = -1f; }
        Save();
    }

    static bool loggedCamps;

    internal static void Update()
    {
        var god = SaveGod.me;
        Now = god != null && PlayerMovement.me != null ? Mathf.Max(0f, god.GetPreciseTimePlayed()) : -1f;
        if (Now < 0f) { last = -1f; return; }
        if (!loggedCamps) LogCamps();

        // a new game: play time starts again near 0
        if (Now < 3f && (last < 0f || last > Now + 0.5f || !Running && !Finished)) Start();
        // play time going back (another save loaded): this isn't the same run any more
        else if (last >= 0f && Now < last - 1f) { Running = false; Finished = false; }
        last = Now;

        if (Running && !Finished && Current < List.Count && List[Current].Camp < 0)
        {
            var m = Menu.me;
            if (m != null && m.creditsShower != null && m.creditsShower.activeInHierarchy) Mark(Current);
        }
    }

    static void Start()
    {
        Running = true; Finished = false; Current = 0;
        foreach (var s in List) s.Time = -1f;
    }

    // nate reached campfire `ind`: the splits that end there (or earlier, if one was skipped) are done
    internal static void OnCampfire(int ind)
    {
        Core.Inst.LoggerInstance.Msg($"campfire {ind} reached at {Fmt(Now)}");
        while (Running && !Finished && Current < List.Count && List[Current].Camp >= 0 && List[Current].Camp <= ind) Mark(Current);
    }

    static void Mark(int i)
    {
        if (i != Current || Now < 0f) return;
        var s = List[i];
        s.Time = Now;
        float seg = Now - (i > 0 ? List[i - 1].Time : 0f);
        if (s.Gold < 0f || seg < s.Gold) s.Gold = seg;
        Current++;
        if (Current >= List.Count)
        {
            Finished = true;
            var end = List[List.Count - 1];
            if (end.Pb < 0f || end.Time < end.Pb) foreach (var x in List) x.Pb = x.Time;
        }
        Save();
    }

    static void LogCamps()
    {
        var m = Menu.me;
        if (m == null || m.campfireDatas == null) return;
        loggedCamps = true;
        for (int i = 0; i < m.campfireDatas.Length; i++)
        {
            var d = m.campfireDatas[i];
            if (d != null) Core.Inst.LoggerInstance.Msg($"campfire {i}: {d.title} at {d.pos}");
        }
    }

    internal static string Fmt(float t)
    {
        if (t < 0f) return "-";
        int h = (int)(t / 3600f), m = (int)(t % 3600f / 60f), s = (int)(t % 60f), cs = (int)(t * 100f % 100f);
        return h > 0 ? $"{h}:{m:D2}:{s:D2}.{cs:D2}" : $"{m:D2}:{s:D2}.{cs:D2}";
    }

    // + behind / - ahead, short
    internal static string Delta(float d)
    {
        string sign = d < 0f ? "-" : "+";
        d = Mathf.Abs(d);
        return d >= 60f ? sign + Fmt(d) : sign + d.ToString("0.0", CultureInfo.InvariantCulture);
    }

    [HarmonyPatch(typeof(Menu), nameof(Menu.CampFireCo))]
    static class CampfirePatch
    {
        static void Prefix(int campInd)
        {
            try { OnCampfire(campInd); } catch (Exception e) { Core.Inst.LoggerInstance.Error("splits: " + e.Message); }
        }
    }
}
