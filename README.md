# Lizard Tail

A Slay the Spire 2 mod that keeps several runs instead of one, multiplayer and solo. It is named after
Lizard Tail, the relic that brings you back from the dead, since that is what it does for an abandoned
run.

The game has a single multiplayer save. If your groups change from night to night, with different
friends and different subsets of them, you either finish a run in one sitting or abandon it to host
another. This mod adds a **Multiplayer runs** panel to the multiplayer menu. Park the active run,
host a new one, and later make the old run active again and press Load to continue it.

The game's solo save is single too, so a **Solo runs** panel on the main menu does the same for it:
park the run **Continue** would pick up, start another, and swap back later.

Some choices are final once made: a boss relic, an Ancient's offer, a card you remove. **Checkpoints**
let you try both ways. Take one from the pause menu before the choice, play on, and later go back to
it from the menu to take the other path. Where you were when you went back is kept as a checkpoint too,
so neither path is lost.

## Using it

Open **Multiplayer** from the main menu. The panel on the right lists:

- **Active**: the run the game's **Load** button continues, with who is in it (Steam names and
  characters), the act, floor and ascension, and when it was last saved. **Park this run** moves it
  into the parked list and frees the slot, so **Host** starts a new run.
- **Parked**: every run you have parked. **Make active** swaps it into the game's slot; whatever was
  active is parked first, so a swap never loses a run. **Trash** (press twice) moves a parked run to
  a trash folder.

With a controller, move right from the **Join** tile into the panel, and left to go back.

### Solo runs

The main menu has a **Solo runs** panel at its right edge. It starts closed, as a title and a **Show**
button that says how many runs are parked, and hides whenever the main menu's own buttons do. Open, it
works like the multiplayer one, for the solo save:

- **Active** is the run the main menu's **Continue** picks up. **Park this run** frees the slot, so
  the menu shows **Singleplayer** again in place of Continue and Abandon Run.
- **Parked** lists solo runs only. The two panels never show each other's runs.

A solo run has no other players, so its row shows the character, and nothing checks whose run it is.
With a controller, move right from the main menu's buttons into the panel, and left to go back.

### Checks before a swap

**Make active** checks the run before changing anything: that you are one of its players (multiplayer
only), that the game can load it with your current version and mods, and that it was not won or lost.
If any check
fails, nothing changes and the panel says why. The parked copy stays in the folder until the game
has saved newer progress of that run, so a swap that did not take can always be retried.

A parked run can show a note instead of being ready:

| Note | Meaning |
|---|---|
| Abandoned | You abandoned it from the menu. **Make active** picks it up again (see below). |
| This run ended | It was won or lost. The game deletes the save of a finished run, so it cannot be continued. |
| You are not in this run | Multiplayer only: only a player in a run's save can host it. |
| Saved by a newer version | The game cannot read it until you update. |
| This file is not a run save this mod can read | A damaged save the mod kept before the game removed or set it aside. It cannot be made active; Trash moves it out of the list. |

### Abandoning by mistake

If you press the game's own **Abandon** (or **Abandon Run**, for a solo run, on the main menu or from
inside the run), the mod keeps a copy of the run in that kind's parked list first.
That includes a save the game cannot load (from a newer game version, or with content from a mod you
turned off): the game sets those aside as `.corrupt` files, and the mod parks a copy before it does.
Such a copy shows as saved by a newer version, or fails Make active's load check, until you update the
game or turn that mod back on.

For a run the game could load, **Make active** brings it back: the game wrote an "abandoned" record
into your run history, which would make it delete the save again at the next launch, so the mod moves
that record aside (it keeps a copy) and the run continues. Your profile stats already counted the abandon and are not changed
back. A run that was won or lost is never copied.

### Who can play a parked run

The game's own rules apply when you press **Load** on a multiplayer run:

- You must be one of the run's players, because only someone in the save can host it.
- Only the run's players can join. Nobody new can be added to a run in progress.
- Any two or more of them can continue. The game asks you to confirm when someone is missing, and
  the missing players stay in the run for next time.

### Checkpoints

During a run, the pause menu has a **Checkpoint** panel at its right edge. Type a label (or keep the
suggested one) and press **Save checkpoint**. A checkpoint is a copy of the game's save as it was last
written, and the game writes it at two moments:

- on entering a room, before the fight or event starts, and
- once a fight is won (rewards still waiting) or an Ancient's choice is made.

Choices themselves are saved only at the next of those moments, so a checkpoint taken while a choice is
on screen (the rewards after a boss, an event's options) goes back to before it. The panel says which
moment it will save, for example "Floor 17, after the boss fight, rewards waiting" or "Floor 18,
entering the boss fight". Click the label box to type; with a controller, move right from the pause
menu's buttons to **Save checkpoint** and keep the suggested label.

To go back, **Save & Quit**, then open **Solo runs** on the main menu (or **Multiplayer runs** on the
multiplayer menu for a run you host). The **Checkpoints** section lists them, the active run's first:

- **Restore** makes the checkpoint the game's save, so **Continue** (or **Load**) picks up from that
  moment. If the active run is the same one, where you were is kept first as a checkpoint named
  "Before going back to …" (with the game's previous save beside it), unless an identical checkpoint
  already exists; if it is another run, that run is parked. Your own labels are never changed. Parked
  copies of the restored run become checkpoints too, since the parked list clears copies of the active
  run once the game saves past them. The checkpoint itself stays, so you can go back to it again.
- **Rename** edits the label in place. **Trash** (press twice) moves a checkpoint to the trash folder.

A run that was won, lost or abandoned after the checkpoint can still be restored. Its result is moved
aside (a copy is kept) so the game does not delete the save again at the next launch. The game's
statistics keep counting the first result, and count the new one too when the restored run ends.

In multiplayer, only the host keeps the save, so only the host can take and restore checkpoints, and
everyone rejoins through **Load** as usual.

## Shadowing an earlier run of a seed

Replaying a seed and want to make the same choices you made last time? Press **F6** during a run to
open the shadow picker. Anything you chose in the run you are shadowing is marked with a green glow
as you reach it, so you can follow the same line as far as you like and then go your own way.

The game already records every finished run in its own history, so this works on seeds you played
long before installing the mod. Nothing has to be recorded first.

- The picker lists every earlier run of the seed you are playing, oldest first, with how far each
  got and how it ended. Pick any of them — the earliest, the latest, or one in the middle.
- It opens on the run that got **furthest**, a win breaking a tie. Not the first run: a seed you
  have replayed is mostly false starts abandoned on floor one or two, and shadowing one of those
  would show you almost nothing.
- Only runs played by the character you are playing now are listed, since card rewards differ
  entirely between characters. In co-op only your own choices are shadowed, never a teammate's.
- **Hide the glow** keeps your choice but stops drawing the marks, for when you want to play a floor
  honestly without being told what you did last time.
- The marks stop once you go deeper than the shadowed run ever got.

Card rewards are marked today. The remaining screens — shops, events, campfires, chests, boss relics
and the map — are the same mechanism applied per screen and are still to come. The map will be
marked by row rather than by node: the game records which floor you took but not which column.

## Your normal saves stay in use

Out of the box, the game switches to a separate, modded profile as soon as any mod loads. Its
one-time copy of your progress into that profile does not survive the Steam Cloud sync at the same
launch, so the first modded launch can land on a blank profile at ascension 0.

Lizard Tail keeps the game on your normal profile, with your progress and your normal run saves,
as long as every loaded mod declares that it does not affect gameplay. Load any mod that does, and the
game keeps its separate modded profile as usual. Spirebird's "unify save paths" option does the same
for every mod; with both installed, whichever applies keeps the normal profile, and they never work
against each other.

## Where things are kept

Parked runs are files under `%APPDATA%\SlayTheSpire2\steam\<your id>\mod_data\LizardTail\`, in a
folder named like the game's own profile folder: `profile<N>\` while the normal profile is in use, or
`modded\profile<N>\` while the game keeps separate modded saves. The panel lists runs parked under
either, so switching between them never hides one. Solo runs are kept apart, in a `solo` folder inside
that profile folder with the same folders in it. The game's log names the exact folders when the
main menu and the multiplayer menu first open.

- `slots\`: parked runs, one file per copy (`<run start>-<save time>.save`). Nothing here is ever
  overwritten.
- `checkpoints\`: checkpoints, named the same way, each with a `.json` file beside it holding its label
  and when it was taken.
- `trash\`: runs you trashed, and older copies of a run the game has since saved past.
- `revived-history\`: history records moved aside when an abandoned or finished run is picked up again.

Damaged saves the mod kept are named `damaged-…` or `set-aside-…` instead. One more file sits directly
in `mod_data\LizardTail\`: `names.json`, the Steam names of the people in your runs, remembered so they
still show when Steam is offline or someone leaves your friends list.

They stay on this PC. Steam Cloud does not sync them, and the game never cleans that folder.

The active run is changed only through the game's own save store, so Steam Cloud stays in step. A
plain file swap would be undone at the next launch, when the game syncs the cloud copy back. After a
swap the mod checks that Steam Cloud took the new run, and that a revived run's abandon record is gone
from the cloud as well as this PC. Being offline is not a problem (Steam queues the change), but if
Steam refuses the write, usually because its cloud space for the game is full, the panel says so: the
next launch would undo the swap. The run stays parked and visible, so it can be made active again
after freeing space or turning off Steam Cloud for the game. Until the cloud has a run, the panel marks
it, and none of its parked copies are hidden or cleared.

Swaps are refused while they would be unsafe: once Load, Host or Continue has been pressed, with a popup open,
during a run, or while the game is saving. They are also refused while the game's own save file is
damaged (the panel then shows its backup); pressing Load once lets the game repair it or set it aside.
The main menu reads the solo save whenever it draws its buttons, so a damaged solo save is repaired or
set aside without a press.

## Installing

Put `LizardTail.dll` and `LizardTail.json` (from a [release](https://github.com/jerryhebert/sts2-lizard-tail/releases),
or built yourself as below) in a `mods/LizardTail/` folder inside your Slay the Spire 2 install, and
restart the game. The mod declares `affects_gameplay: false`, so your friends do not need
it to join your games.

Lizard Tail was called Save Slots before v0.2.0. If you had that version, delete `mods/SaveSlots/`
so the two do not both load. Runs you parked carry over by themselves: the first time the new version
needs them, it renames `mod_data\SaveSlots\` to `mod_data\LizardTail\`.

## Building

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download) (9.0.318 or a later 9.0 release,
pinned in `global.json`) and a copy of the game, whose assemblies the mod compiles against. The project
finds a Steam install automatically; if yours lives elsewhere, copy
`LizardTail/Directory.Build.props.example` to `LizardTail/Directory.Build.props` (it is git-ignored) and
set `Sts2Path`. Building also installs the mod into the game's `mods` folder (skipped with a warning
while the game is running).

```bash
dotnet build LizardTail/LizardTail.csproj -c Release
```

```bash
pwsh ./scripts/Run-Checks.ps1
```

Runs the unit tests, the dependency audit and a Release build. Add `-SkipBuild` on a machine without the
game installed; the unit tests need only the SDK.

The pure logic (reading saves, history records, which copies are listed or stale, file names,
wording) is in `LizardTail/LizardTailCode/Core` and is unit-tested without the game.
`LizardTailCode/Game` holds the save-store operations, the picker and the Harmony patches.

## License and attribution

[MIT](LICENSE). Slay the Spire 2 is the property of Mega Crit; this project is an unaffiliated fan mod
and contains no game code or assets. The project was scaffolded from
[Alchyr's mod template](https://github.com/Alchyr/ModTemplate-StS2).
