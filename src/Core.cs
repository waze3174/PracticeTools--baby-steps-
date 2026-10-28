using System;
using System.IO;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(PracticeTools.Core), "PracticeTools", "1.5.0", "waze3174")]
[assembly: MelonGame]
[assembly: MelonOptionalDependencies("BabyStepsMenuLib", "BabyStepsMultiplayerClient", "BabyStepsNetworking")]

namespace PracticeTools;

// practice tools for baby steps in one mod: fly cam or carry nate (F1), save / load a spot (F9 / F10), all remappable and
// on a gamepad with B + d-pad, in-game timer with automatic splits, campfire teleports, auto cutscene skip, an arrow to
// any item or (multiplayer) player, input display, speed, fps and slow motion.
// takes over from FlyAndQuicksave, BBSIGT and CutsceneSkipperBBS when they're installed (they'd double up).
// settings on a PRACTICE page in the pause menu (with BabyStepsMenuLib) or UserData/PracticeTools/PracticeTools.cfg
public class Core : MelonMod
{
    internal static Core Inst;
    internal static MelonPreferences_Category Cat;
    internal static MelonPreferences_Entry<bool> CarryMode, ShowTimer, ShowSplits, ShowDelta, ShowTime, ShowBestRun, ShowGold, AutoSkip, ItemBlinker, Arrow, PlayerArrow, InputDisplay, ShowSpeed, ShowFps, ShowCoords, ShowAngle, ShowCompass, ShowFling, ShowLoops, ShowKill, ShowSteps, AnyPercent, ShowGlitchMarks, ShowRoute, ShowMoving, FruitArrow;
    internal static MelonPreferences_Entry<float> SlowMo, TimerScale;
    internal static MelonPreferences_Entry<string> ArrowItem, ArrowPlayer, ArrowFruit;
    internal static string Dir;

    public override void OnInitializeMelon()
    {
        Inst = this;
        Dir = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "PracticeTools");
        Directory.CreateDirectory(Dir);
        Cat = MelonPreferences.CreateCategory("PracticeTools");
        Cat.SetFilePath(Path.Combine(Dir, "PracticeTools.cfg"));
        CarryMode = Cat.CreateEntry("CarryMode", false, "Carry nate", "F1 fly mode. false = free camera (left click teleports nate to it), true = nate is carried in front of the camera with no collisions and dropped on F1 again.");
        ShowTimer = Cat.CreateEntry("ShowTimer", false, "Show timer", "The game's own play time (the one the ending shows), top right.");
        TimerScale = Cat.CreateEntry("TimerScale", 1f, "Timer size", "Size of the timer and splits, 0.5 to 2.");
        ShowSplits = Cat.CreateEntry("ShowSplits", false, "Show splits", "Splits under the timer, split automatically at each new campfire and at the end. Compared to your best run.");
        ShowDelta = Cat.CreateEntry("ShowDelta", true, "+/-", "How far ahead (green) or behind (red) your best run you are at each split, live on the split you're on.");
        ShowTime = Cat.CreateEntry("ShowTime", true, "Time", "This run's time at each split.");
        ShowBestRun = Cat.CreateEntry("ShowBestRun", true, "Best run", "Your best run's time at each split, in a column next to this run's, and its final time under the splits.");
        ShowGold = Cat.CreateEntry("ShowGold", true, "Best segments", "Your best segment for each split, in gold, and the sum of best under the splits.");
        AutoSkip = Cat.CreateEntry("AutoSkip", false, "Auto skip cutscenes", "Skips cutscenes as they start, without the skip minigame (not the hat ones).");
        Arrow = Cat.CreateEntry("Arrow", false, "Guide arrow", "An arrow on screen pointing to the item picked in ArrowItem.");
        ArrowItem = Cat.CreateEntry("ArrowItem", "", "Arrow item", "The item the arrow points to (picked on the menu page).");
        FruitArrow = Cat.CreateEntry("FruitArrow", false, "Fruit arrow", "An arrow on screen pointing to the fruit picked in ArrowFruit.");
        ArrowFruit = Cat.CreateEntry("ArrowFruit", "", "Arrow fruit", "The fruit the fruit arrow points to (picked on the menu page).");
        PlayerArrow = Cat.CreateEntry("PlayerArrow", false, "Player arrow", "Multiplayer: an arrow on screen pointing to the player picked in ArrowPlayer.");
        ArrowPlayer = Cat.CreateEntry("ArrowPlayer", "", "Arrow player", "The player the player arrow points to, by name (picked on the menu page).");
        InputDisplay = Cat.CreateEntry("InputDisplay", false, "Input display", "The game's own stick and trigger graph.");
        ShowSpeed = Cat.CreateEntry("ShowSpeed", false, "Show speed", "Nate's speed over the ground in m/s.");
        ShowFps = Cat.CreateEntry("ShowFps", false, "Show fps", "Frames per second.");
        ItemBlinker = Cat.CreateEntry("ItemBlinker", false, "Item blinker", "Items nate has picked up blink like the pedometer while they lie around.");
        ShowCoords = Cat.CreateEntry("ShowCoords", false, "Show coordinates", "Nate's position (x, y up, z), bottom right.");
        ShowAngle = Cat.CreateEntry("ShowAngle", false, "Show camera angle", "The camera's yaw and pitch in degrees, bottom right, and how far off the marked angle it is.");
        ShowCompass = Cat.CreateEntry("ShowCompass", false, "Show compass", "Where the camera looks: a compass strip with the heading in degrees at the top middle (N is +z), and which shoulder the camera is over, bottom right.");
        ShowFling = Cat.CreateEntry("ShowFling", false, "Show fling info", "Water glitch: how much of nate is in water and the water's push, and the last fling (top speed, steps pushed, metres over water, height), bottom right. Each fling is logged too.");
        ShowLoops = Cat.CreateEntry("ShowLoops", false, "Show map loops", "Water glitch: how many times the map has looped north / south (the save's zLoops; below 0 no campfire starts, the ending neither), and whether the loop is armed (the area around nate is loaded) or held while it loads, bottom right.");
        ShowKill = Cat.CreateEntry("ShowKill", false, "Show kill height", "Water glitch: the height where the game puts a fallen nate back up (300 m over the ground, or 2000 m with nothing under him), at nate's spot, and how far above it he is, bottom right.");
        AnyPercent = Cat.CreateEntry("AnyPercentSplits", true, "Any% splits", "Which best times the splits show. Off (full run): every split, a skipped one just gives its time to the next. On (any%): a split a run skipped is left out for good, its best segment is part of the next split's, so best run and sum of best only count the splits still done. Both are kept up on every run.");
        ShowGlitchMarks = Cat.CreateEntry("ShowGlitchMarks", false, "Glitch markers", "Water glitch setup: near a marked spot, a cross in the marker's colour shows where the camera should look, with how far off it is and which shoulder it wants. Turns the game's centre dot on (back off with the markers if it was off). Markers live in UserData/PracticeTools/glitch-markers.txt.");
        ShowMoving = Cat.CreateEntry("ShowMoving", false, "Moving direction", "Which way nate is going over the ground (16-point name and degrees, N is +z) and how fast, under the compass (or in its place when the compass is off). A fling carries on that way.");
        ShowRoute = Cat.CreateEntry("ShowRoute", true, "Show route", "Draws the route picked on the Routes tab over the world (the one being recorded always shows). Routes live in UserData/PracticeTools/routes.");
        ShowSteps = Cat.CreateEntry("ShowSteps", false, "Show steps taken", "The save's step count, next to the timer.");
        SlowMo = Cat.CreateEntry("SlowMo", 1f, "Game speed", "1 = normal, down to 0.1 for slow motion.");

        Binds.Init();
        States.Init();
        Routes.Init();
        Splits.Load();
        GlitchMarks.Load();
        Fly.Install(HarmonyInstance);
        try { PracticeMenu.Announce(); menuOk = true; }
        catch (Exception e) when (e is FileNotFoundException || e is TypeLoadException || e is MissingMethodException) { }
    }

    public override void OnLateInitializeMelon()
    {
        Takeover.Run(LoggerInstance);
        try { Binds.MuteMinimap(HarmonyInstance, LoggerInstance); } catch (Exception e) { LoggerInstance.Warning("minimap: " + e.Message); }
        try { Fly.GuardBabyBlocks(HarmonyInstance, LoggerInstance); } catch (Exception e) { LoggerInstance.Warning("babyblocks: " + e.Message); }
        try { CamWatch.Install(HarmonyInstance); } catch (Exception e) { LoggerInstance.Warning("camera watch: " + e.Message); }
        try { LoadFix.Install(HarmonyInstance); } catch (Exception e) { LoggerInstance.Warning("state load fix: " + e.Message); }
        if (!menuOk) { LoggerInstance.Msg("BabyStepsMenuLib not installed: settings only in UserData/PracticeTools/PracticeTools.cfg"); return; }
        try { BuildMenu(); }
        catch (Exception e) when (e is FileNotFoundException || e is TypeLoadException || e is MissingMethodException)
        { menuOk = false; LoggerInstance.Msg("BabyStepsMenuLib not installed: settings only in UserData/PracticeTools/PracticeTools.cfg"); }
    }

    static bool menuOk;   // the PRACTICE page exists (BabyStepsMenuLib installed)
    void BuildMenu() => PracticeMenu.Build(LoggerInstance, HarmonyInstance);
    static void MenuUpdate() => PracticeMenu.Update();
    static void HubLateUpdate() => MenuHub.LateUpdate();

    internal static void Changed() => Cat.SaveToFile(false);

    public override void OnUpdate()
    {
        Safe("binds", Binds.Update);
        Safe("typing", Typing.Update);
        Safe("loads", Fly.TrackLoads);
        Safe("fly", Fly.Update);
        Safe("camera loader", CamWatch.Loader);
        Safe("camera", CamWatch.Update);
        Safe("states", States.Update);
        Safe("routes", Routes.Update);
        Safe("sunglasses", Sunglasses.Update);
        Safe("blinker", Blinker.Update);
        Safe("angle", CamAngle.Update);
        Safe("timer", Splits.Update);
        Safe("skip", Skipper.Update);
        Safe("finder", Finder.Update);
        Safe("players", PlayerFinder.Update);
        Safe("display", Hud.Update);
        if (menuOk) try { MenuUpdate(); } catch (Exception e) { menuOk = false; LoggerInstance.Error("menu: " + e); }
    }

    public override void OnFixedUpdate()
    {
        Safe("bone guard", States.Guard);
        Safe("fling", FlingWatch.Step);
    }

    public override void OnLateUpdate()
    {
        Safe("bone guard", States.Guard);
        Safe("camera", CamWatch.LateUpdate);
        if (menuOk) Safe("menu hub", HubLateUpdate);
    }

    // one broken feature turns itself off instead of spamming the log every frame
    static readonly System.Collections.Generic.HashSet<string> broken = new();
    static void Safe(string what, Action a)
    {
        if (broken.Contains(what)) return;
        try { a(); }
        catch (Exception e) { broken.Add(what); Inst.LoggerInstance.Error($"{what} off: {e}"); }
    }
}
