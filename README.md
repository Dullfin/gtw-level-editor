# GTW Level Editor

An in-game level editor for **Get To Work**, as a BepInEx 5 plugin. Build a course out of curved ramps, platforms, launchers, cogs, pendulums and moving pieces. Then press one key to play it with the game's own physics, camera, grabbing and respawns.

- Free-form spline ramps: flat, gutter, half-pipe, quarter-pipe or walled, with banking and loops.
- The game's launchers rebuilt for the editor: the slingshot, the wall slingshot and the giant cog.
- Moving platforms, pieces rotating around a shared pivot, and swinging pendulums.
- Slippery (ice) and Sticky surfaces.
- Start, checkpoints, finish with a timer and best time, kill zones and a kill height.
- Move / rotate / scale handles, undo and redo, copy and paste.
- Levels save as single JSON files that are easy to share.

Custom levels run in an empty Doinkler world, and **the game's saves are switched off while one is running**. Your own progress, positions and times are never touched.

## Installing

You need Get To Work with [BepInEx 5](https://github.com/BepInEx/BepInEx) installed.

1. Download `GTWLevelEditor.dll` from the [latest release](https://github.com/Dullfin/gtw-level-editor/releases/latest) and put it in `Get To Work\BepInEx\plugins`. Or build it yourself (see [Building](#building)), which copies the DLL there for you.
2. To get the example levels, open the Level Editor menu in game, click **Import level…** and pick the files from `Example-Levels.zip` in the release, or from [`Levels/`](Levels). You can also copy them into `Get To Work\BepInEx\LevelEditor\Levels\` yourself.

## Included levels

| Level | What it is |
|---|---|
| **Demo Course** | A short tour of every object type: ramps, a launcher, a wall launcher, a cog, bumpers, a jump pad and a kill zone. The editor's scripted demo builds this course live. |
| **Clockwork Spire** | A vertical course about 2–3 minutes long. It starts with a corkscrew drop to the canyon floor, then a pendulum bridge. Two launchers climb a cliff, and a third fires you into a ramp that throws you onto an ice summit. A cog flings you into a long plunge. A loop leads to a kicker with a jump pad for the final leap onto the finish. It has two checkpoints. |

## Getting started

1. On the title screen, click **Level Editor** (below The Doinkler Portfolio and The Doinkler Special).
2. Pick a world for the sky and lighting, then click **New level**, or **Play** / **Edit** a saved level.
3. You start in the editor, flying. Place objects at the crosshair.
4. Press **F8** to play: you spawn on the Start pad and the timer starts. Press **F8** again to go back to editing.
5. Leave from the pause menu by quitting to the title screen.

The editor has two modes:
- **Flying** (the default) is for building: fly around and place things at the crosshair.
- **Mouse mode** (**Tab**) shows the cursor and the side panel. Use it for fine-tuning: select objects, drag their handles and edit exact values.

The bar at the top of the screen shows what each number key places. In mouse mode you can click it.

## Controls

### Flying

| Key | Action |
|---|---|
| Move controls | Fly |
| Space / Left Ctrl | Fly up / down |
| Left Shift | Fly faster |
| 1 to 0 | Platform, Ramp, Wedge, Cylinder, Sphere, Jump pad, Bumper, Launcher, Wall launcher, Cog launcher |
| Shift + 1 to 5 | Checkpoint, Start, Finish, Kill zone, Pendulum |
| Left mouse | Place the object |
| Right mouse | Pick up the object you're aiming at, to move it |
| Delete | Delete the object you're aiming at |
| Q / E | Turn 15° |
| R / F | Tilt 15° |
| T | Reset rotation |
| Mouse wheel | Resize (Shift: height only; Alt: distance in the air) |
| [ / ] | Lower / raise the launch speed of a jump pad, bumper or launcher (Shift: steps of 5) |
| G | Toggle 0.5 m grid snapping |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| Ctrl+D | Pick up a copy of the aimed object |
| Ctrl+C / Ctrl+V | Copy the aimed object / hold a copy to place |

### Drawing ramps (key 2)

Click to drop points, and the track bends smoothly through them. A preview follows the crosshair.

| Key | Action |
|---|---|
| Left mouse | Add a point. Click the last point again to finish |
| Enter | Finish the ramp |
| Backspace / Ctrl+Z | Remove the last point |
| Mouse wheel | Raise / lower the next point (0.5 m steps) |
| Shift + wheel | Width of the next point |
| Q / E | Bank the next point by 5° (Shift: 15°) |
| T | Reset height and bank |
| Alt + wheel | Distance of the point when aiming at the sky |

Start a ramp on the end of another ramp (the marker turns green) and it continues smoothly with the same width and bank.

A selected ramp's panel has:
- **Shape:** Flat, Gutter, Half-pipe, Quarter-pipe or Walls, plus the wall height or gutter depth.
- **Thickness.**
- **Free twist:** lets the track roll with the curve. Turn it on for loops. Leave it off for turns and spirals, so the track's top side keeps facing the sky.
- **Points:** each point's exact position, width and bank. You can also insert or delete points.

### Mouse mode (Tab)

| Key | Action |
|---|---|
| Left click | Select an object (click empty space to deselect) |
| W / E / R | Move / rotate / scale handles |
| Drag an arrow, ring or box | Move along an axis, rotate, or scale. The yellow square moves along the ground; the white centre box scales evenly |
| Drag a ramp's white points | Reshape the ramp (hold Shift to move a point up or down) |
| Delete | Delete the chosen ramp point, or the selected object |
| Hold right mouse | Look around |
| Mouse wheel | Move the camera forward / back |
| Ctrl+Z / Ctrl+Y | Undo / redo (panel edits included) |
| Ctrl+D | Duplicate the selection |
| Ctrl+C / Ctrl+V | Copy / paste |

The panel shows the selected object's position, rotation, size, colour, surface, launch speed and motion. It also has save, load and the level's kill height.

## Objects

- **Platform, Wedge, Cylinder, Sphere, Ramp:** solid ground you can roll on and grab. Each one's surface can be:
  - **Normal.**
  - **Slippery:** ice. You keep your speed and can steer only a little, so place it where the player arrives fast.
  - **Sticky:** grab it to stick on, even upside down.
- **Launcher:** the game's slingshot. Roll onto the orange carriage and hold Grab. It locks you on, shoots along its arrow and throws you at its launch speed. Tilt it with Rotation X. The panel sets the travel and launch speed, and a yellow line previews the path while editing.
- **Wall launcher:** the same slingshot mounted on a wall.
- **Cog launcher:** the game's giant spinning cog. Grab it to ride it, and let go to fly off with its speed. Negative spin turns it the other way.
- **Pendulum:** the swinging pendulum from Doinkler 4-5. It hangs from the point you place it. The bob is sticky, so you can ride it.
- **Jump pad:** launches you along its up direction.
- **Bumper:** pushes you away from its centre. Jump pads and bumpers use the same maths as the game's own.
- **Start / Checkpoint / Finish:** where you spawn, where you respawn, and where the timer stops. They're only visible while editing. In play mode they're invisible and you pass straight through them, so place them on the ground the player rolls over.
- **Kill zone:** respawns you at the last checkpoint. It's only visible while editing. Levels also have an optional kill height.

### Moving pieces

Platforms, wedges, cylinders, spheres, ramps, jump pads, bumpers and pendulums can move in play mode. Select one in mouse mode and choose a **Motion**:

- **Move:** travels by an offset and back, with a travel time and a pause at each end.
- **Rotate:** turns around a pivot and axis. Pieces that share the same pivot, axis and speed turn together as one assembly, like Doinkler 6-1. **Copy motion** / **Paste motion** shares the settings.
- **Swing:** swings around a pivot with an angle and a period.
- **Start offset** (0–1) staggers the timing of identical pieces.

While editing, paths are drawn in blue. In play mode, standing on a moving piece carries you, and you keep its momentum when you leave.

## Saving and sharing levels

Levels are saved as JSON in `Get To Work\BepInEx\LevelEditor\Levels\`, one file per level. The title screen's Level Editor menu manages them:

- **Export** (on a level's row) saves a copy of the level file wherever you choose, ready to send to someone.
- **Import level…** picks a level file and adds it to your saved levels. If you already have a level with that name, the import gets " (2)" added to its name.
- **Delete** (on a level's row, then confirm) moves the level file to the Windows Recycle Bin, so you can still restore it.
- **Open levels folder** opens the folder in Explorer.

Files saved by versions before 0.4 contain no objects (an old bug lost them on save), so they open as empty levels.

## Settings

`Get To Work\BepInEx\config\com.Dullfin.GTWLevelEditor.cfg`:

| Setting | Default | What it does |
|---|---|---|
| `Keys/ToggleEditor` | F8 | Switch between editing and playing |
| `Keys/TogglePanel` | Tab | Mouse mode and the panel |
| `Editor/UiScale` | 1 | Editor UI size. The UI already scales with screen height, so it looks the same at 1080p and 4K. Raise it to make the UI bigger |
| `Editor/FlyVerticalSpeed` | 8 | Fly up / down speed in m/s |
| `Surfaces/SlipperyControl` | 0.3 | Steering kept on Slippery surfaces: 0 = pure ice, 1 = normal ground |
| `Debug/EnableDebugCommands` | false | Development aid; see below |

## Building

```powershell
dotnet build -c Release
```

The project compiles against the game's own DLLs and BepInEx's core DLLs, so no NuGet packages are needed. It expects the game in the default Steam folder.
- `-p:GameDir="D:\Games\Get To Work"`: use a different game folder.
- `-p:DeployToGame=false`: build without copying the DLL into `BepInEx\plugins`.

Close the game before building, or the copy fails because the DLL is in use.

## Debug commands

With `EnableDebugCommands` on, the game runs lines written to `BepInEx\LevelEditor\Debug\commands.txt` and writes results and screenshots to the same folder. These are for development and testing.

<details>
<summary>Command list</summary>

| Command | What it does |
|---|---|
| `new <world>`, `edit <name>`, `play <name>`, `load <name>`, `save <name>` | Start or load levels |
| `toggle`, `panel`, `titlemenu`, `title`, `quit` | Switch modes, open menus, leave |
| `place <kind> x y z`, `ramp <profile> x y z ...` | Place objects |
| `select <index\|last>`, `info`, `json`, `set <field> <value>`, `surface <type>`, `motion ...` | Inspect and change the selection |
| `handles`, `gizmo move\|rotate\|scale`, `drag x1 y1 x2 y2 [frames]`, `undo`, `redo` | Fake mouse-mode edits |
| `tp x y z`, `tpon <index> [above]`, `vel x y z`, `speed`, `gravity` | Move and inspect the player |
| `look yaw pitch`, `campos x y z`, `screenshot <name>` | Camera and screenshots |
| `autoforward on\|follow\|off` | Act as if forward is held: along the camera, or along the direction of travel |
| `autograb on [angle] [dirX dirZ]\|off` | Hold grab on sticky launchers and let go of cogs at a set angle |
| `track <seconds> <file>` | Log a run and stop on finish, death, ragdoll or getting stuck |
| `demo`, `waitdemo` | Play the scripted demo that builds Demo Course |
| `wait <s>`, `waitfor title\|loaded\|player`, `status`, `dump`, `explore`, `find`, `tree`, `colliders`, `fonts` | Scripting and scene inspection |

</details>

## Limitations

- Objects are simple shapes, not the game's own models.
- Leaving mouse mode returns the camera to the player.
