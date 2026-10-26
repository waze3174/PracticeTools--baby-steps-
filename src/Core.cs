using System;
using System.IO;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(PracticeTools.Core), "PracticeTools", "1.0.0", "waze3174")]
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
    internal static MelonPreferences_Entry<bool> CarryMode, ShowTimer, ShowSplits, AutoSkip, Arrow, PlayerArrow, InputDisplay, ShowSpeed, ShowFps;
    internal static MelonPreferences_Entry<float> SlowMo, TimerScale;
    internal static MelonPreferences_Entry<string> ArrowItem, ArrowPlayer;
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
        AutoSkip = Cat.CreateEntry("AutoSkip", false, "Auto skip cutscenes", "Skips cutscenes as they start, without the skip minigame (not the hat ones).");
        Arrow = Cat.CreateEntry("Arrow", false, "Guide arrow", "An arrow on screen pointing to the item picked in ArrowItem.");
        ArrowItem = Cat.CreateEntry("ArrowItem", "", "Arrow item", "The item the arrow points to (picked on the menu page).");
        PlayerArrow = Cat.CreateEntry("PlayerArrow", false, "Player arrow", "Multiplayer: an arrow on screen pointing to the player picked in ArrowPlayer.");
        ArrowPlayer = Cat.CreateEntry("ArrowPlayer", "", "Arrow player", "The player the player arrow points to, by name (picked on the menu page).");
        InputDisplay = Cat.CreateEntry("InputDisplay", false, "Input display", "The game's own stick and trigger graph.");
        ShowSpeed = Cat.CreateEntry("ShowSpeed", false, "Show speed", "Nate's speed over the ground in m/s.");
        ShowFps = Cat.CreateEntry("ShowFps", false, "Show fps", "Frames per second.");
        SlowMo = Cat.CreateEntry("SlowMo", 1f, "Game speed", "1 = normal, down to 0.1 for slow motion.");

        Binds.Init();
        Splits.Load();
        Fly.Install(HarmonyInstance);
        try { PracticeMenu.Announce(); menuOk = true; }
        catch (Exception e) when (e is FileNotFoundException || e is TypeLoadException || e is MissingMethodException) { }
    }

    public override void OnLateInitializeMelon()
    {
        Takeover.Run(LoggerInstance);
        try { Binds.MuteMinimap(HarmonyInstance, LoggerInstance); } catch (Exception e) { LoggerInstance.Warning("minimap: " + e.Message); }
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
        Safe("fly", Fly.Update);
        Safe("timer", Splits.Update);
        Safe("skip", Skipper.Update);
        Safe("finder", Finder.Update);
        Safe("players", PlayerFinder.Update);
        Safe("display", Hud.Update);
        if (menuOk) try { MenuUpdate(); } catch (Exception e) { menuOk = false; LoggerInstance.Error("menu: " + e.Message); }
    }

    public override void OnLateUpdate()
    {
        if (menuOk) try { HubLateUpdate(); } catch (Exception e) { LoggerInstance.Error("menu: " + e.Message); }
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
