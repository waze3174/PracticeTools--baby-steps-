# PracticeTools (baby steps)

a melonloader mod for baby steps with everything you want for practice and speedruns in one place: fly cam, save / load a spot, in-game timer with automatic splits, campfire teleports, auto cutscene skip, an arrow to any item (and bring it to you), input display, speed, fps, slow motion, and in multiplayer an arrow to other players and teleport to them.

if you have flyandquicksave (the original or my freeze fix), caleb's in-game timer or his cutscene skipper installed, practicetools switches them off while it's running so nothing doubles up. their files aren't touched, take practicetools out and they work again.

## keys

- **F1** fly cam. two modes (fly tab):
  - **free cam**: fly around with WASD, Q / E down / up, shift / ctrl faster, scroll for speed, alt + scroll to zoom. left click teleports nate to the camera
  - **carry nate**: nate (and what he's holding, and his hat) hangs in front of the camera with no collisions and follows you through anything. F1 again drops him where he is
- **F9** save nate's spot (one per save slot, kept when you quit)
- **F10** go back to it

## gamepad

hold **B** and press:

- **d-pad up** fly cam
- **d-pad left** save spot
- **d-pad right** go back to it

flying with the pad: left stick moves, RT / LT up / down, L3 faster, right stick looks around. teleporting nate to the free cam is still left click only.

while B is held the minimap mod doesn't see the d-pad, so its buttons don't go off at the same time (B on its full map still clears tracking). its gamepad input is also switched off on the title screen and in cutscenes.

## controls

all three (keyboard key and gamepad button) can be changed on the **controls** tab: pick a row, then press the key or button you want within 5 seconds (esc cancels). **reset controls** puts them back. they're also in the cfg file.

everything else is on the **PRACTICE** page in the pause menu.

## the practice page

needs BabyStepsMenuLib (see install). without it, everything is in the cfg file.

- **timer**: show timer, show splits, size, forget best times
- **finder**: pick any item in the game, an arrow on screen points to it with the distance (live while it's loaded, its last spot when it isn't), and **bring it here** puts it in front of nate
- **teleport**: pick a campfire and go there, or back to your saved spot
- **display**: input display (the game's own stick and trigger graph), speed in m/s, fps, game speed (slow motion down to x0.1)
- **fly**: free cam or carry nate
- **misc**: auto skip cutscenes
- **controls**: change the keys and buttons above
- **multiplayer**: pick a player, an arrow in their colour points to them, teleport next to them (needs the baby steps multiplayer mod)

with my other mods rulebook or falltracker also installed, the pages open from one **MODS** button so the pause menu doesn't run off the screen.

## timer and splits

the timer is the game's own play time, the same one the ending shows. it sits top left (out of the minimap's way) and hides while paused, like the speed and fps (bottom right). when a run ends the final times stay up for 15 seconds over the ending.

splits are automatic. a run starts with a new game; each split ends when nate reaches a campfire, the last one at the credits:

| split | ends at |
|---|---|
| swamp | ep 1: hung over |
| chapter two | ep 2: over hung |
| the forest | ep 3: the jock |
| crater lake | ep 4: the party |
| the desert | ep 5: the fortress |
| the fortress | ep 6: the ice cream truck |
| the rolling hills | ep 7: the quarry |
| the snowy area | the credits |

your best run and best segments (gold) are saved, the splits show ahead / behind in green / red. names and campfires can be changed in `UserData/PracticeTools/splits.txt` (`name|campfire|...`, `end` = the credits). skip a campfire (glitch or not) and its split shows as skipped with no time or best segment. the next one you reach gets the time since your last split, and can still get a best segment. the run is kept, so if the game crashes (or you quit) just load the same save and it carries on from there. loading a save mid-game doesn't start a run.

## auto skip cutscenes

skips each cutscene as it starts, the same way winning the skip minigame does, so you never play the minigame. the hat scenes and the ending (the map with your path, then the credits) play as normal.

## install

1. install [melonloader](https://melonwiki.xyz/) 0.7.x for baby steps
2. drop `PracticeTools.dll` in `Baby Steps/Mods/`
3. optional, for the pause menu page: [BabyStepsMenuLib](https://github.com/caleborchard/BabyStepsMenuLib) (also comes with the multiplayer mod)
4. optional, for the multiplayer tab: the [baby steps multiplayer mod](https://github.com/caleborchard/Baby-Steps-Multiplayer-Mod-Client)

## files

everything is in `Baby Steps/UserData/PracticeTools/`:

- `PracticeTools.cfg` settings
- `splits.txt` split names, campfires, best times
- `spots.txt` your saved spots
- `run.txt` the run in progress

## build

needs the .net 6 sdk and the game with melonloader installed (for the references). `BabyStepsMenuLib.dll`, `BabyStepsMultiplayerClient.dll` and `BabyStepsNetworking.dll` are expected in the game's `Mods` folder to build.

```
dotnet build src/PracticeTools.csproj -c Release -p:GameDir="C:\path\to\Baby Steps"
```

## known issues

- the last split (credits) hasn't been tested on a full run yet
- carry mode: right after a drop nate's feet can take a step to sort themselves out
- teleporting to a campfire doesn't split, you have to walk into it
- free cam teleport has no gamepad button yet

## credits

- baby steps by gabe cuzzillo, maxi boch and bennett foddy
- fly cam and quicksave from flyandquicksave by caleb orchard (and my [freeze fix](https://github.com/waze3174/FlyAndQuicksave-freeze-fix--baby-steps-) of it), the timer and cutscene skip ideas from his [in-game timer](https://github.com/caleborchard/BabySteps-InGameTimer) and [cutscene skipper](https://github.com/caleborchard/CutsceneSkipperBBS). all rewritten here, not copied
- pause menu page made with [BabyStepsMenuLib](https://github.com/caleborchard/BabyStepsMenuLib) by caleb orchard (not included, used if installed)
- player arrows go through the [baby steps multiplayer mod](https://github.com/caleborchard/Baby-Steps-Multiplayer-Mod-Client) by caleb orchard (not included, used if installed)

tested on baby steps (steam) with melonloader 0.7.3, on steam deck.

not affiliated with or endorsed by gabe cuzzillo, maxi boch, bennett foddy or devolver digital.

## licence

cc0 1.0, do whatever you want with it. see `LICENSE`.
