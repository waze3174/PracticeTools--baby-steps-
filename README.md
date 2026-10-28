# PracticeTools (baby steps)

a melonloader mod for baby steps with everything you want for practice and speedruns in one place: fly cam, savestates (30 slots, with a full load that puts the world back too), in-game timer with automatic splits, campfire teleports, route maps, auto cutscene skip, an arrow to any item or fruit (and bring it to you), input display, speed, fps, coordinates, compass, camera angle marks, water glitch helpers, slow motion, and in multiplayer an arrow to other players and teleport to them.

if you have flyandquicksave (the original or my freeze fix), caleb's in-game timer, his cutscene skipper, the sunglasses toggle or the item blinker installed, practicetools switches them off while it's running so nothing doubles up. their files aren't touched, take practicetools out and they work again.

## keys

- **F1** fly cam. two modes (fly tab):
  - **free cam**: fly around with WASD, space / ctrl up / down (E / Q work too), shift faster, hold the right mouse button for much faster, scroll for speed, alt + scroll to zoom. left click teleports nate to the camera
  - **carry nate**: nate (and what he's holding, and his hat) hangs in front of the camera with no collisions and follows you through anything. F1 again drops him where he is
- **F9** save a state in the picked savestate slot
- **F10** load it
- **F6** sunglasses view without wearing the sunglasses (again to go back)
- **F5** mark the camera angle (up to 5 marks, press twice quickly to clear them)

## gamepad

hold **B** and press:

- **d-pad up** fly cam
- **d-pad left** save state
- **d-pad right** load state
- **d-pad down** sunglasses view
- **R3** mark the camera angle

flying with the pad: left stick moves, RT / LT up / down, L3 faster, right stick looks around. teleporting nate to the free cam is still left click only.

while B is held the minimap mod doesn't see the d-pad, so its buttons don't go off at the same time (B on its full map still clears tracking). its gamepad input is also switched off on the title screen and in cutscenes.

## controls

all of them (keyboard key and gamepad button, and the B modifier) can be changed on the **controls** tab: pick a row, then press the key or button you want within 5 seconds (esc cancels). **reset controls** puts them back. they're also in the cfg file.

everything else is on the **PRACTICE** page in the pause menu.

## the practice page

needs BabyStepsMenuLib (see install). without it, everything is in the cfg file.

- **timer**: show timer, show splits, and a toggle for each splits column (+/-, time, best run, best segments)
- **finder**: pick any item in the game, an arrow on screen points to it with the distance (live while it's loaded, its last spot when it isn't), and **bring it here** puts it in front of nate. fruits have their own list, ordered from the start of the game to the end, with their own arrow (bring here only works while the fruit is loaded)
- **teleport**: pick a campfire and go there, or load the picked state
- **savestates**: pick a slot, save, load, full load, describe it, delete it (see below)
- **routes**: record a route and see it drawn over the world (see below)
- **display**: input display (the game's own stick and trigger graph), speed in m/s, fps, coordinates, camera angle, compass with the shoulder the camera is over, which way nate is moving, steps taken
- **glitch**: water glitch helpers: fling info, map loops, kill height, glitch markers (see below)
- **fly**: free cam or carry nate
- **misc**: auto skip cutscenes, item blinker, timer size, game speed (slow motion down to x0.1)
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

your best run and best segments (gold) are saved, the splits show ahead / behind in green / red. the splits are columns: **+/-** against your best run (live on the split you're on), **time** for this run, then the **best run**'s time and your **best seg** (gold) for each split, with your best run and sum of best under them. every column has its own toggle on the timer tab (all on by default). names and campfires can be changed in `UserData/PracticeTools/splits.txt` (`name|campfire|...`, `end` = the credits). skip a campfire (glitch or not) and its split shows as skipped with no time or best segment. the next one you reach gets the time since your last split, and can still get a best segment. the run is kept, so if the game crashes (or you quit) just load the same save and it carries on from there. loading a save mid-game doesn't start a run.

## savestates

30 slots, shared by every save. a state keeps nate's whole pose (every bone, the ragdoll's velocities), how he's walking, the camera, the run in progress and a copy of the save file as it was.

- **load** (F10) puts nate and the camera back, loading the area first when he's far away
- **full load** also puts the state's copy of the save file back and restarts from the title (press continue): the world is as it was when you saved (items, cutscenes seen, time of day, your path on the map...), then nate's pose goes on top. the save it replaced is kept as `states/before-full-load-<slot>.sav`
- **describe**: type a short note for the slot (keyboard, or steam's keyboard on a gamepad)
- saving while paused saves nate as he was when you paused

## routes

5 route slots. **start recording** lays a line behind nate as he goes (swimming it sits on the water), and you can show any route over the world on real attempts. **pause** leaves a marker where nate is, and recording only goes on once he's back at it, so scouting doesn't leave lines jumping across the map. recording never pauses by itself, fly paths count too. a savestate saved while recording puts the route back as it was then.

## water glitch helpers

on the glitch tab:

- **fling info**: how much of nate is in water and the water's push, and the last fling (top speed, steps pushed, metres over water, height). each fling is written to the log too
- **map loops**: how many times the map has looped north / south (below 0 no campfire starts, the ending neither), and whether the loop is armed
- **kill height**: the height where the game puts a fallen nate back up, and how far above it he is
- **glitch markers**: a chain of camera angles for the setup. near a marked spot a cross in the marker's colour shows where to look, with how far off you are and which shoulder it wants. turns the game's centre dot on while it's on. the markers are in `glitch-markers.txt`, change them or add your own

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
- `states/` your savestates
- `routes/` your routes
- `run.txt` the run in progress
- `fruits.txt` fruit spots found so far
- `glitch-markers.txt` the glitch markers

## build

needs the .net 6 sdk and the game with melonloader installed (for the references). `BabyStepsMenuLib.dll`, `BabyStepsMultiplayerClient.dll` and `BabyStepsNetworking.dll` are expected in the game's `Mods` folder to build.

```
dotnet build src/PracticeTools.csproj -c Release -p:GameDir="C:\path\to\Baby Steps"
```

## fixes for the game

- loading: a prop the area loader left half loaded could make later loads of it die for good, so teleports hung forever and the ground sometimes never came in (falling through the world). practicetools picks those up and starts them again
- the camera could freeze for good after a far load (the area loader's drawing step throwing on a broken prop). those props are loaded again
- teleporting out of fly cam no longer leaves nate frozen

## known issues

- the last split (credits) hasn't been tested on a full run yet
- carry mode: right after a drop nate's feet can take a step to sort themselves out
- teleporting to a campfire doesn't split, you have to walk into it
- free cam teleport has no gamepad button yet
- a fruit only shows in the finder once its area has loaded once (they have no loader of their own to find them by)

## credits

- baby steps by gabe cuzzillo, maxi boch and bennett foddy
- fly cam and quicksave from flyandquicksave by caleb orchard (and my [freeze fix](https://github.com/waze3174/FlyAndQuicksave-freeze-fix--baby-steps-) of it), the timer and cutscene skip ideas from his [in-game timer](https://github.com/caleborchard/BabySteps-InGameTimer) and [cutscene skipper](https://github.com/caleborchard/CutsceneSkipperBBS). all rewritten here, not copied
- the sunglasses view idea from sunglassestoggle by linktheferret, the item blinker idea from caleb orchard's bbsitemblinker. rewritten here too
- pause menu page made with [BabyStepsMenuLib](https://github.com/caleborchard/BabyStepsMenuLib) by caleb orchard (not included, used if installed)
- player arrows go through the [baby steps multiplayer mod](https://github.com/caleborchard/Baby-Steps-Multiplayer-Mod-Client) by caleb orchard (not included, used if installed)

tested on baby steps (steam) with melonloader 0.7.3, on steam deck.

not affiliated with or endorsed by gabe cuzzillo, maxi boch, bennett foddy or devolver digital.

## licence

cc0 1.0, do whatever you want with it. see `LICENSE`.
