# Screen reader support in Vaudeville: notes for the developers

## Why it's worth doing

Vaudeville is unusually well suited to blind players. It is a detective game played through conversation: you
type or speak your questions and the characters answer out loud. Very few games put that much of the experience
into sound and language already.

This mod shows it works. With it, a blind player using a screen reader can start a game, travel around the city
from the Studio map, question every character, keep notes, and make the accusation through to the end of a case.
The mod is a proof of concept from the outside. Built into the game, the same support would reach every blind
player on Steam with nothing to install, and it would keep working through updates.

## The game is already most of the way there

- **The menus already work with the keyboard.** Nearly every screen uses uGUI selection with arrow-key
  navigation, including the Studio map. A screen reader can follow that selection directly.
- **Unity 6.3 does the hard part.** Its Accessibility module publishes an accessibility tree to Windows, which
  NVDA, JAWS and Narrator read. The game only has to describe what is on screen.
- **Conversations are already accessible by design.** Typed questions and spoken replies need very little extra.

## Start small

Each step below helps blind players on its own, so the work can stop at any point and still be worthwhile.

1. **Turn vsync off when a screen reader is running.** One line of code, and it fixes the biggest problem
   found: with vsync on and the monitor off, the game slows to about 13 fps and every announcement takes
   seconds (section 1).
2. **Publish a tree for the menus that already navigate by keyboard.** Build one node per visible control and
   text, publish it with `AssistiveSupport.activeHierarchy`, and move screen reader focus
   (`SendLayoutChanged(node)`) when the uGUI selection changes. This covers the intro menu, options, saves, the
   Studio map and conversations. The mod's `src/Screens/GenericUiScreen.cs` does this generically in one file.
3. **Announce what appears on screen by itself:** loading status, unlock popups, messages, and who and where on
   arriving at a location (`SendAnnouncement`; see `src/Screens/LiveTexts.cs`).
4. **Add nodes for the things clicked with the mouse:** the accusation suspects, which are currently mouse-only.
   Activation can call the existing `activatePrimary`.
5. **Polish:** caret review in text fields (`src/Screens/TextFieldReview.cs`), labels for image-only buttons, and
   the navigation fixes listed in section 3.

Inside the game none of the mod's `src/Native/` folder is needed. It exists only because the build stripped the
Accessibility module's public API, which the game's own code would keep simply by using it.

## Help is available

The mod's author is an accessibility modder and is happy to help: answering questions, testing
builds with a screen reader, or doing the implementation. See the contact details at the end.

## Reference: detailed findings

Everything below was verified in Vaudeville 2.0.1 (Unity 6000.3.20f1, Windows) with NVDA. The mod's source shows
a working implementation of each point.

## 1. Vsync can make the game nearly unusable with a screen reader

Unity answers screen reader (UI Automation) requests once per frame, on the main thread. Vaudeville runs with
`QualitySettings.vSyncCount = 1`. On a machine whose display is off or idle (common for blind players, who often
have no use for a monitor), the game ran at a steady 12.9 fps although the hardware renders it at about 450 fps.

Measured effect:

- every screen reader property read took about 77 ms
- announcements took several seconds
- NVDA itself froze, for up to 18 seconds at a time

With vsync off, reads take a few milliseconds and the freezes disappear. Keypress to speech dropped from several
seconds to about 0.3 seconds.

Suggestion: when `AssistiveSupport.isScreenReaderEnabled` is true, turn vsync off, or offer it as a visible
setting. Tearing doesn't matter to a player who can't see the screen. The mod does this in
`src/Native/FrameRateUnlock.cs`.

## 2. Unity's Accessibility module behaviour worth knowing

- **Repeated focus requests are ignored.** `SendLayoutChanged(node)` or `SendScreenChanged(node)` with the same
  node as the previous call does nothing on Windows (this is documented). To re-announce a node, focus another node
  first, then the intended one on the next frame.
- **Focus is lost when the window regains focus.** After Alt+Tab back into the game, UI Automation focus lands on
  the window itself, not on the node that had it. Re-focus the node when `Application.isFocused` becomes true.
- **Changing a label or value is silent.** Setting `node.label` or `node.value` updates what the screen reader reads
  on request, but raises no event. A slider moved with the arrow keys therefore says nothing. Announce the new value
  with `AssistiveSupport.notificationDispatcher.SendAnnouncement` when the changed node has focus.
- **Text fields and sliders need `focusChanged`, but verify it fires.** On Windows, focusing a node does not focus
  the uGUI control. Unity's documentation says to handle `AccessibilityNode.focusChanged` and select the real
  `TMP_InputField` or `Slider`. In our testing `focusChanged` never fired for focus moves the game initiates. Since
  Vaudeville's menus already use uGUI selection, a simpler model works: keep the node focus and
  `EventSystem.currentSelectedGameObject` in step, in both directions.
- **Text fields have no caret review.** A `TextField` node exposes only its value, so a screen reader cannot read
  the character or line at the caret, and arrow keys inside a field are silent. Announce the character at the
  caret on Left and Right, the line on Up and Down in multi-line fields, and removed text on Backspace and Delete.
  Leave typed-character echo to the screen reader, which already handles it.
- **Sliders adjust only with numeric values.** `incremented` and `decremented` fire on Windows only when
  `node.value` is a number.
- **Timing.** With vsync off (about 440 fps on the test machine), a keypress that moves focus is spoken by NVDA
  in about 0.16 seconds. Announcements take a similar time. With vsync on and an idle display it was seconds.
- **Build stripping.** Vaudeville's build stripped the Accessibility module's public API because the game never
  references it. Code inside the game that uses the API keeps it, so this is only an obstacle for a mod.

## 3. Keyboard navigation in the game's own UI

Screen reader users drive menus with the keyboard. Most of Vaudeville's menus already support it through uGUI
selection, which is a good base. Problems found so far:

- **Options menu order.** The controls use automatic navigation, which picks neighbours by screen position. From
  the dialogue slider, Up jumps to the "Check offers and bundles" banner at the top of the screen. Explicit
  navigation, or a vertical order within the panel, would make the menu predictable.
- **Story Vault (Editor & Stories).** The scene uses the legacy `StandaloneInputModule` and starts with nothing
  selected, so the arrow keys and Enter do nothing at all. Setting `EventSystem.firstSelectedGameObject` to the first
  button would make it keyboard operable.
- **Navigation into hidden panels.** In the story editor, after switching to the Characters tab, Down moves the
  selection onto the General tab's text fields, which are hidden but still active and interactable. The highlight
  disappears for a sighted player; a screen reader user hears nothing, or the hidden field's text. Deactivate or
  make non-interactable the panels that aren't showing, or give the tabs explicit navigation.
- **Leaving a text field.** In the conversation's question field the arrows move the caret and Escape leaves the
  whole conversation, so a keyboard user has no way to leave the field for the OK button except sending the
  question. Up and Down could move to the neighbouring control, since they do nothing in a single-line field.
- **Text fields without labels.** The question field is described only by its placeholder ("Type here or speak
  into your microphone..."), which disappears once text is typed. Give fields a label that stays.
- **Buttons without text.** The Story Vault's first button (`SteamSubscribe`) has no text, so it has no name a screen
  reader can read. Image-only buttons need a label in their accessibility node.

## 4. Screen by screen

- **Save game selection.** Each card's meaning is spread over separate texts (story, progress, date), and each
  DELETE button looks the same as the others. A card's node should carry all three ("Story, progress 58%,
  2 November 2026"), and each Delete button should name the save it deletes.
- **Studio map.** The location post-its are already uGUI Selectables with explicit navigation, which makes the
  map keyboard navigable. Locked locations are non-interactable, which is right, but their node should still exist
  and say it is locked, as a sighted player sees it. Use `MapLocation.LocationName` as the label.
- **Studio navigation.** The map's explicit navigation skips locked locations, so a keyboard user never reaches
  them, while a sighted player sees them. The Quit, Accuse, Options and Notes buttons also appear unreachable from
  the map with the arrow keys. Every item on screen should be reachable by keyboard.
- **Push-to-talk on Left Ctrl.** Screen reader users press Ctrl constantly to stop speech. If holding Left Ctrl
  records from the microphone, every Ctrl press in a conversation may record and send a question. In one test
  session the game heard "you" and sent it to the character without the player meaning to ask anything. Consider
  another key, or a setting to change it.
- **Accusation room.** The suspects are 3D objects chosen with the mouse (`AccusationCharacter`, raycast), with no
  uGUI Selectable, so keyboard and screen reader users have nothing to move to. Each suspect needs a node labelled
  with `CharacterName` whose `invoked` calls `activatePrimary`. If keyboard selection is added, keep it in step with
  the node focus, as for the rest of the UI.
- **Text that changes in place.** The AI loading overlay (`OffWorldInit`), `MessageLabel`, the unlock popup
  (`UnlockUI`) and `MessageUI` appear or change without any focus change. Announce them with `SendAnnouncement`
  when they appear.
- **Image-only buttons.** Return, Open Notebook and Silence in conversations, and Steam Subscribe in the Story
  Vault, have no text. Their nodes need labels.

## Contact

Open an issue at https://github.com/ObjectInSpace/vaudeville-access/issues.

The mod's source is MIT licensed: borrow anything that helps.
