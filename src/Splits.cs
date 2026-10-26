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
// a run counts from a new game (play time starts near 0). the run is kept in run.txt, so loading its save again (after
// a crash, or from the title screen) carries on: splits after the loaded play time are undone. another save slot
// stops counting until the next
// new game. names, campfires, best run and best segments live in UserData/PracticeTools/splits.txt
// (name|campfire or end|best run split|best segment)
static class Splits
{
    internal class Split { public string Name; public int Camp = -1; public float Pb = -1f, Gold = -1f, Time = -1f; public bool Skipped; }   // Camp -1 = the end
    internal static readonly List<Split> List = new();
    internal static int Current;          // the split being run
    internal static bool Running;         // a run from a new game is going
    internal static bool Finished;
    internal static float FinishedAt = -100f;   // unscaled time the run ended, for the HUD to keep it up a while
    internal static float Now;            // play time this frame, -1 when there's none
    static float last = -1f;
    static int runSlot = -1;          // the save slot the run is on
    static int Slot => SaveGod.me != null ? SaveGod.me.slot : -1;
    static string RunFile => Path.Combine(Core.Dir, "run.txt");

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
        // a save loaded (from the title screen, or play time going back): the run's own slot carries on from there
        else if (last < 0f || Now < last - 1f) Loaded();
        last = Now;

    }

    static void Start()
    {
        Running = true; Finished = false; Current = 0; runSlot = Slot;
        foreach (var s in List) { s.Time = -1f; s.Skipped = false; }
        SaveRun();
    }

    // the end credits started: the end split is done (campfires still to go are skipped)
    static void OnCredits()
    {
        if (!Running || Finished || Now < 0f || Current >= List.Count) return;
        int end = List.FindIndex(Current, s => s.Camp < 0);
        if (end < 0) return;
        Core.Inst.LoggerInstance.Msg($"credits at {Fmt(Now)}");
        Reach(end);
    }

    internal static EndCredits Credits;   // the ending (the map with your path, then the credits), once it has started

    [HarmonyPatch(typeof(EndCredits), "Update")]
    static class CreditsPatch
    {
        static void Postfix(EndCredits __instance)
        {
            try { if (__instance.started) { Credits = __instance; OnCredits(); } } catch (Exception e) { Core.Inst.LoggerInstance.Error("splits: " + e.Message); }
        }
    }

    static void Loaded()
    {
        if (Running && !Finished && runSlot == Slot) { RollBack(); return; }
        Running = false; Finished = false;
        if (ResumeRun()) Core.Inst.LoggerInstance.Msg($"run on slot {runSlot} carries on at {Fmt(Now)}, split {Current + 1}");
    }

    // the save is from before some splits: those aren't done any more
    static void RollBack()
    {
        int done = 0;
        for (int i = 0; i < List.Count; i++)
        {
            var s = List[i];
            if (s.Time > Now + 5f) { s.Time = -1f; s.Skipped = false; }   // a campfire save can sit a moment before its split
            else if (s.Time >= 0f) done = i + 1;
        }
        for (int i = done; i < List.Count; i++) List[i].Skipped = false;
        Current = done;
        SaveRun();
    }

    // run.txt: slot, then one line per split: time (-1 = not yet) | skipped
    static void SaveRun()
    {
        try
        {
            if (!Running || Finished) { if (File.Exists(RunFile)) File.Delete(RunFile); return; }
            var c = CultureInfo.InvariantCulture;
            var lines = new List<string> { "slot|" + runSlot.ToString(c) };
            foreach (var s in List) lines.Add(string.Format(c, "{0:0.000}|{1}", s.Time, s.Skipped ? 1 : 0));
            File.WriteAllLines(RunFile, lines);
        }
        catch { }
    }

    static bool ResumeRun()
    {
        try
        {
            if (!File.Exists(RunFile)) return false;
            var lines = File.ReadAllLines(RunFile);
            var c = CultureInfo.InvariantCulture;
            if (lines.Length != List.Count + 1 || !lines[0].StartsWith("slot|")) return false;
            if (!int.TryParse(lines[0].Substring(5), NumberStyles.Integer, c, out int slot) || slot != Slot) return false;
            for (int i = 0; i < List.Count; i++)
            {
                var f = lines[i + 1].Split('|');
                List[i].Time = float.TryParse(f[0], NumberStyles.Float, c, out var t) ? t : -1f;
                List[i].Skipped = f.Length > 1 && f[1].Trim() == "1";
            }
            Running = true; runSlot = slot;
            RollBack();
            return true;
        }
        catch { return false; }
    }

    // nate reached campfire `ind`: the split that ends there is done. campfires skipped on the way (a glitch, or
    // walking past one) skip their splits: no time, and the time goes to the split reached
    internal static void OnCampfire(int ind)
    {
        Core.Inst.LoggerInstance.Msg($"campfire {ind} reached at {Fmt(Now)}");
        if (!Running || Finished) return;
        int reached = -1;
        for (int i = Current; i < List.Count && List[i].Camp >= 0 && List[i].Camp <= ind; i++) reached = i;
        if (reached >= 0) Reach(reached);
    }

    static void Reach(int i)
    {
        if (Now < 0f) return;
        while (Current < i) { List[Current].Skipped = true; List[Current].Time = -1f; Current++; }
        Mark(i);
    }

    // a segment's time: from the last split that was reached (skipped ones in between count towards it)
    internal static float Segment(int i)
    {
        var s = List[i];
        if (s.Time < 0f) return -1f;
        for (int j = i - 1; j >= 0; j--) if (List[j].Time >= 0f) return s.Time - List[j].Time;
        return s.Time;
    }

    static void Mark(int i)
    {
        if (i != Current || Now < 0f) return;
        var s = List[i];
        s.Time = Now;
        float seg = Segment(i);
        if (seg >= 0f && (s.Gold < 0f || seg < s.Gold)) s.Gold = seg;
        Current++;
        if (Current >= List.Count)
        {
            Finished = true;
            FinishedAt = UnityEngine.Time.unscaledTime;
            var end = List[List.Count - 1];
            if (end.Pb < 0f || end.Time < end.Pb) foreach (var x in List) x.Pb = x.Time;
        }
        Save();
        SaveRun();
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
        if (d < 60f) return sign + d.ToString("0.0", CultureInfo.InvariantCulture);
        // short: +2:43.2 (+1:02:43 past an hour)
        int h = (int)(d / 3600f), m = (int)(d % 3600f / 60f), sec = (int)(d % 60f), ds = (int)(d * 10f % 10f);
        return h > 0 ? $"{sign}{h}:{m:D2}:{sec:D2}" : $"{sign}{m}:{sec:D2}.{ds}";
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
