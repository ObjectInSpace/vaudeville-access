# Screen reader support in Vaudeville: notes for the developers

This mod adds screen reader support to Vaudeville from the outside. The better outcome is for the game to support
screen readers itself, and Unity 6.3's Accessibility module makes that practical. These notes collect what we have
learned so far, so that work can start from evidence rather than from scratch.

Everything here was verified in Vaudeville 2.0.1 (Unity 6000.3.20f1, Windows) with NVDA. The mod's source shows a
working implementation of each point.

## 1. Vsync can make the game nearly unusable with a screen reader

Unity answers screen reader (UI Automation) requests once per frame, on the main thread. Vaudeville runs with
`QualitySettings.vSyncCount = 1`. On a machine whose display is off or idle (common for blind players, who often
have no use for a monitor), the game ran at a steady 12.9 fps although the hardware renders it at about 450 fps.

Measured effect:

- every screen reader property read took about 77 ms
- announcements took several seconds
- NVDA itself froze, for up to 18 seconds at a time

With vsync off and `Application.targetFrameRate = 120`, reads take about 8 ms and the freezes disappear.

Suggestion: when `AssistiveSupport.isScreenReaderEnabled` is true, turn vsync off and cap the frame rate instead.
Tearing doesn't matter to a player who can't see the screen. The mod does this in `src/Native/FrameRateUnlock.cs`.

## 2. Unity's Accessibility module behaviour worth knowing

- **Repeated focus requests are ignored.** `SendLayoutChanged(node)` or `SendScreenChanged(node)` with the same
  node as the previous call does nothing on Windows (this is documented). To re-announce a node, focus another node
  first, then the intended one on the next frame.
- **Focus is lost when the window regains focus.** After Alt+Tab back into the game, UI Automation focus lands on
  the window itself, not on the node that had it. Re-focus the node when `Application.isFocused` becomes true.
- **Changing a label or value is silent.** Setting `node.label` or `node.value` updates what the screen reader reads
  on request, but raises no event. A slider moved with the arrow keys therefore says nothing. Announce the new value
  with `AssistiveSupport.notificationDispatcher.SendAnnouncement` when the changed node has focus.
- **Text fields and sliders need `focusChanged`.** On Windows, focusing a node does not focus the uGUI control. Per
  Unity's documentation, handle `AccessibilityNode.focusChanged` and select the real `TMP_InputField` or `Slider`
  so keyboard input reaches it.
- **Sliders adjust only with numeric values.** `incremented` and `decremented` fire on Windows only when
  `node.value` is a number.
- **Timing.** Announcements reach NVDA in about 0.17 seconds. A focus change is spoken in about 0.65 seconds.
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
- **Buttons without text.** The Story Vault's first button (`SteamSubscribe`) has no text, so it has no name a screen
  reader can read. Image-only buttons need a label in their accessibility node.

## 4. To be continued

These notes grow as the mod covers more of the game: the Studio hub, conversations, the accusation, and the
Workshop.
