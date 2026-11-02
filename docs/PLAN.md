# Vaudeville Access — Plan

Written 2026-11-02, after the Phase 0 proof. This plan covers what the mod must make accessible, how it is built,
the order of work, and how each step is verified.

## Goal

A blind player can play Vaudeville from launch to epilogue with their own screen reader, getting the same
information and choices a sighted player gets, no more and no less.

Principles:

- **Unity's own screen reader support is the primary channel.** The mod builds a native accessibility hierarchy
  for each screen. Screen readers then read, navigate and activate real controls, with their own review commands,
  role and state wording, and braille.
- **Speech-only output is a fallback**, used where the native channel cannot reach: events outside the player's
  focus if native announcements prove too slow, and platforms Unity does not support.
- **Every screen reader on every platform the game ships on.** Steam lists Windows and Linux. Windows covers NVDA,
  JAWS and Narrator through UI Automation. Linux has no Unity backend at all (see Phase 7).
- **Parity, not invention.** Expose what is on screen. Do not add hints or information a sighted player lacks.
- **Every result is verified from evidence**, not from memory of what was heard (see Testing protocol).

## What Phase 0 proved

All verified 2026-11-02 against the running game, NVDA 2026.2rc2.

- **The native hierarchy works despite the stripped build.** Unity's linker removed the public building API
  (`new AccessibilityHierarchy()`, `CreateNode`, the `AssistiveSupport.activeHierarchy` setter, every node event
  accessor). Everything the engine itself calls survived. The mod allocates the objects raw, fills the fields the
  constructors would have set, and hands the hierarchy to `AccessibilityHierarchyService` under the engine lock.
  See `src/Native/NativeA11yProbe.cs`.
- **NVDA uses it fully.** From NVDA's own log: automatic announcement of the focused node, announcement after
  Alt+Tab back into the game, activation with NVDA+Numpad Enter reaching the mod's handler in the same
  millisecond, and renamed labels read back.
- **Labels and values update** through `src/Native/NodeText.cs`, which calls the engine's `Set*_Injected`
  functions directly. The interop property setters throw MissingMethodException in this build.
- **Frame rate is screen-reader latency.** Unity answers one UI Automation request per frame. With vsync on, the
  game ran at 12.9 fps against an idle display, making every NVDA read about 77 ms, announcements several seconds,
  and NVDA itself freeze for up to 18 seconds. `src/Native/FrameRateUnlock.cs` turns vsync off and caps at
  120 fps: 8 ms per read, no freezes since.
- **Focus quirks and their fixes:**
  - When the window regains focus, focus lands on the window pane, not the last node. The mod re-focuses the node.
  - Unity documents that repeated focus requests with the same node are ignored on Windows. Re-focusing therefore
    detours through another node for one frame.
- **Remaining latency is about 1.5 seconds** from the mod moving focus to NVDA speaking: 0.7 to 1.2 seconds inside
  Unity before the first UI Automation event, then about 0.6 seconds in NVDA.

## Phase 1 results (2026-11-02)

- **Native host and self-check:** `src/Native/A11yHost.cs` and `NativeApiCheck.cs`. The self-check is proven to
  fail on a missing member.
- **Instruments:** a UI dump 2 and 8 seconds after each scene load, on every screen rebuild and on Ctrl+F8;
  a binding dump per scene.
- **Spike A, text entry:** typing reaches the game's `TMP_InputField`. Unity's TextField node has no caret
  review, so `src/Screens/TextFieldReview.cs` speaks the character, line or word at the caret and removed text.
  Typed-character echo is left to the screen reader. Up/Down leave a field at its edges.
- **Spike B, announcements:** native announcements are spoken in 0.16 to 0.37 seconds. No speech library is
  needed on Windows.
- **Spike C, latency:** vsync off (no frame cap, by decision) gives about 440 fps and about 0.16 seconds from
  keypress to speech. The rest is Unity's own dispatch.
- **Unity's `focusChanged` never fires** for focus moves in this game, so the documented text-field recipe isn't
  available. Following the game's uGUI selection replaces it.
- **Phase 2 started early:** the game's menus support keyboard navigation through uGUI selection, so
  `src/Screens/GenericUiScreen.cs` publishes any uGUI screen without per-screen code. It already covers the intro
  menu, Options, save selection, the Studio map, conversations and the story editor, including combo boxes. It
  keeps focus and selection in sync, guards against the game selecting hidden controls, and announces value
  changes.
- MelonLoader's console is hidden (`UserData\Loader.cfg`, `hide_console = true`). Ship that setting in Phase 7.

## How the game is played (sighted)

Taken from the decompiled code and scene list. Each item notes what the player must perceive and do.

1. **Title video** (`Titles`). Plays a video, then continues. Nothing to operate.
2. **Intro menu** (`IntroScreen`). Buttons: Launch, Workshop, Discord, Options, Credits, Quit. Shows warning and
   info messages in a message area (for example hardware warnings), plus Options and Credits panels.
3. **Options** (`Options`, `OptionsMenu`). Sliders for music, sound effects, dialogue volume and GPU/AI load (with
   an AI preset label and info text), a resolution toggle, language and microphone dropdowns, reset to defaults.
4. **Save game selection** (`Savegames`, `SaveGameButton`). One entry per save: story name, progress, date, latest
   unlock (an image), plot-twist icons. Launch in Story or Sandbox mode, Delete with confirmation.
5. **Loading the AI** (`OffWorldInit`, `LLMBootstrap`). A loading panel with status text while the local language
   model starts. Can fail, retry in safe mode, or show a failure hint. The player must know when they can play.
6. **The Studio hub** (`Studio`). A 3D room. The player looks around with the mouse and clicks objects
   (`Raycast` and `IRaycastTarget`: hover, then primary, secondary, tertiary activation). Objects:
   - The map, with one post-it per location (`MapLocation`). Locations are locked or unlocked. Hovering shows the
     location name (`LocationLabel`). Clicking travels there.
   - The notebook (`Notebook`, `NotebookSwitch`): the player's own notes. Pages with editable title and text, an
     index, back, forward, close.
   - The photo album (`PhotoAlbum`): thumbnails of evidence photos unlocked so far, opened into a viewer.
   - Accuse (`Studio.Accuse`), and a quit menu.
7. **Unlocks and messages.** A conversation can unlock a new location (`UnlockUI`: text, sound, location icon).
   General messages appear in `MessageUI` (title, body, close).
8. **Conversations at a location** (`SceneInteraction`, `Chat`, `CharacterPrompt`). One character per location
   scene (Police Station, Morgue, Circus Saxabar, Cabane Violette, Gravesen Manor, Country Club, Blackwoods, Coral,
   Public Library, Pascal's Groceries and others). The player types a question into a text field and sends it, or
   speaks it with the microphone. The character answers with the game's own voice (text-to-speech), so replies
   are already audible. Also: silence the character, back to the Studio, the character and location names.
9. **Accusation** (`Accusation`, `AccusationCharacter`, `ChoiceButton`). In the Accusation Room the player picks
   the culprit from the cast, then answers rounds of multiple-choice questions (why, what was witnessed, how). The
   game reacts with voiced feedback (right track, not convinced, nonsense).
10. **Epilogue** (`Epilogue`). A video.
11. **Workshop and story editor** (`WorkshopItemsUI`, `VSVE.UI.*`, `Scenes_WS`). Browse and play community stories
    (search field, tabs, paging, cards), and a full editor for writing stories. Workshop stories reuse the main
    flow through `WS_*` versions of each class.

## Architecture

The probe becomes four layers.

### Native host

One place that owns the hierarchy, built from the Phase 0 recipe.

- Builds nodes by hand-assembly. Wires behaviour by assigning the delegate fields directly: `invoked`,
  `focusChanged`, `incremented`, `decremented`, `dismissed`.
- Sets text through `NodeText`, and frames from each control's `RectTransform` screen corners (Unity's sample
  method).
- Manages focus: the focus request (with the duplicate-request detour), restore on window focus, and moving focus
  off nodes that disappear.
- Handles modals the way Unity's sample does: while a dialog is open, every node outside it is set inactive.
- Rebuilds when a scene loads, at the end of the frame so layout positions are final. Reacts to the screen reader
  being switched on or off.
- **Startup self-check.** Verifies every stripped-API member the recipe relies on exists, and fails loudly and
  audibly if a game update breaks one. A silent failure here would leave the player with nothing.

### Screen adapters

One adapter per game screen, mapping the game's own objects to nodes:

- label from the game's own text (localized), role from the control type
- value and state from the control (toggle state, slider value, locked or unlocked)
- activation calls the game's own method (`Button.onClick`, `IRaycastTarget.activatePrimary`, `SaveGameButton.Launch`)

Adapters never re-implement game logic. They describe it and call into it.

### Announcements

For events outside the player's focus: unlocks, loading status, accusation feedback, errors.

- Native first: Unity's `SendAnnouncementNotification` through the engine call, the method No, I'm Not a Human
  used.
- That game measured multi-second delays, but it may have had the same vsync problem. **Re-measure before
  choosing.** If native announcements are slow, use UnityAccessibilityLib (UniversalSpeech) for announcements only,
  behind one interface.

### Platform layer

The frame-rate unlock, the hidden MelonLoader console, hotkeys read through Win32, and later the Linux route.

## Work phases

Each phase ends with an exit check verified from evidence. Order follows the player's path, with the riskiest
unknowns tested first.

### Phase 1 — Foundation and risk spikes

Turn the probe into the native host, build the instruments, and settle the three unknowns that could change the
architecture.

- **Project hygiene:** git repository, README, gitignore (decompiled code stays out), build stamp in every log.
- **Instruments:**
  - A live UI dump: every active canvas, selectable, text and raycast target in the current scene, with names,
    text, interactable state and screen rectangles. Adapters are written against live scene data, not guesses.
  - An input-binding dump of the game's Input System actions, before choosing any mod hotkey. Today's
    Ctrl+F9 to F12 are placeholders.
- **Spike A: typing into the question box.** The core of the game. Build a TextField node over the chat input,
  wire `focusChanged` to select the real `TMP_InputField`, and test typing, character echo, reading back the text,
  and sending. Unity's own approach mirrors the field's text as the node's value, with no caret review, so check
  what NVDA does in practice. If typing doesn't work natively, decide the fallback here, before building
  conversations on it.
- **Spike B: announcement latency.** Measure native announcements at 120 fps against UnityAccessibilityLib, and
  choose.
- **Spike C: the 0.7 to 1.2 second focus delay inside Unity.** Find where it goes, for example by sending focus
  through `SendScreenChanged` instead, or by changing the frame cap, and reduce it if possible.
- Hide the MelonLoader console from players.

**Exit check:** native host with self-check replaces the probe; UI dump and binding dump exist; written decisions
on text entry and announcements; latency measured and minimized.

### Phase 2 — Front end

The path from launch to the Studio.

- Intro menu buttons, warning and info messages, Credits panel.
- Options: sliders as Slider nodes with increment and decrement, dropdowns with their options, the resolution
  toggle, reset to defaults.
- Save game selection: each save as a node labelled with story name, progress and date. Launch modes, delete and
  its confirmation.
- AI loading: announce loading, ready, failure and safe-mode retry, with the game's status text.

**Exit check:** a player can start or continue a game and reach the Studio with NVDA alone, confirmed from the
NVDA log.

### Phase 3 — The Studio hub

- One node per hub object (map locations, notebook, photo album, accuse, quit), activated through the game's own
  `IRaycastTarget` methods, not by moving the mouse.
- Map locations state locked or unlocked. The order follows the map.
- Notebook: page title and text as editable fields (using Spike A's answer), index, back, forward, close, page
  number.
- Photo album: one node per unlocked photo, using its `ScreenName`. Whether photos need longer descriptions is an
  open question (see below).
- Unlock popups and `MessageUI` dialogs announced; dialogs as modals.

**Exit check:** travel to any unlocked location, read and write notes, browse photos, start an accusation, all
from the NVDA log.

### Phase 4 — Conversations

- The question field (Spike A), send, microphone toggle, silence, back to the Studio.
- The character's name and location announced on arrival.
- Ready and busy state: whether the character is still answering, or ready for the next question.
- A transcript node for reviewing earlier exchanges, if the game's `chatText` holds them. Sighted players can read
  the chat log, so this is parity, not an addition.
- Unlocks announced as they happen during conversation.

**Exit check:** hold a full conversation by typing, hear the replies, review the transcript, and leave.

### Phase 5 — Accusation and epilogue

- Culprit selection: one node per cast member (`AccusationCharacter`), activated through the game's method.
- Question rounds: the question text, then each `ChoiceButton` as a node in its grid.
- Feedback announced. Round and score if the game shows them.
- Epilogue: check whether the video has text or subtitles that need exposing.

**Exit check:** complete a case from accusation to epilogue.

### Phase 6 — Workshop and story editor

Lower priority, and a separate decision.

- Workshop browser: search field, tabs, cards, paging, launch.
- Workshop stories reuse the main adapters through the `WS_*` classes. Verify each one.
- The story editor is a large form application. Assess separately whether to support it.

### Phase 7 — Platforms, screen readers and release

- **Screen readers:** test Narrator (Unity's official target) and JAWS on Windows, alongside NVDA.
- **Linux:** Unity's module does nothing there, so it needs speech-only output (PRISM to Speech Dispatcher and
  Orca). Gated on having a way to test on Linux. Shipping untested speech risks silence for exactly the players it
  is meant for.
- **Packaging:** MelonLoader plus the mod, an installer or instructions, licences for any shipped libraries.
  Release on the ObjectInSpace GitHub.
- **Player notes:** the vsync setting, the NVDA navigator setting, and the keys.

## Testing protocol

- **NVDA's Input/output log is the record.** The tester enables it for test sessions. Claude reads what was spoken
  and which keys arrived, with timestamps, from `%TEMP%\nvda.log`. The log records typed keys, so it is switched
  back to Info afterwards.
- **The mod log and the UI Automation tools add the other side.** `tools/Dump-UiaTree.ps1` shows what Windows
  sees, and `tools/Watch-UiaEvents.ps1` shows what Unity sends and when.
- **Background versus focused tests.** Background tests (frame rate, tree dumps, outside invoke) run without the
  tester. Tests that need the game in front say so first, list the exact keys, and wait for the tester.
- **Known tester setting.** The tester's NVDA navigator does not follow focus, so navigator commands need
  NVDA+Numpad Minus first.
- **Deploy check.** Every build logs its hash, and the deployed file is compared against the build before testing.

## Open questions and risks

- **Text entry quality.** Spike A decides it. The node has no caret review in Unity's design.
- **Latency.** About 1.5 seconds per focus announcement today (Spike C).
- **Photo evidence.** Photos have names but no descriptions. A sighted player sees the image, so parity may mean
  describing them. Decide once the album is reachable.
- **Microphone input.** The game supports voice questions. Blind players can use it too, but it needs mic
  selection and status (recording or not) announced.
- **Game updates.** The hand-assembly depends on field names in the game build. The startup self-check turns a
  breakage into a clear message rather than silence.
- **Steam launch on the test machine.** Steam does not start the game here, and a direct launch needs
  `steam_appid.txt`. That file is a local workaround, not something to ship.
- **Gamepad navigation.** Options, the map and the accusation have controller support through uGUI selection.
  If it covers whole screens, adapters can follow the game's own selection rather than inventing an order.
  Confirm with the UI dump.
