# Vaudeville Access

A mod that makes Vaudeville playable with a screen reader. It uses Unity's own screen reader support, so your
screen reader sees the game's menus and controls as real buttons, text fields, sliders and combo boxes, and you
use it with your screen reader's normal commands.

Version 0.1.0. This is an early release: tested with NVDA on Windows. JAWS and Narrator should work, since
they read the same Windows interface, but haven't been tested yet.

## What works

- The intro menu, options, credits, and save game selection.
- The Studio: the map of locations (locked locations are announced as unavailable), Notes, Options, Accuse
  and Quit.
- Conversations: the question box, with the text at the cursor read as you move through what you typed;
  the character's name and location on arrival; the conversation transcript, one item per exchange.
- The notebook.
- The accusation: choosing a suspect and answering the questions.
- The story editor and Story Vault menus.
- Loading messages, unlock popups and other messages are announced when they appear.

## Requirements

- Vaudeville for Windows (Steam).
- A screen reader: NVDA, JAWS or Narrator.

## Installing

1. Find the game folder: in Steam, right-click Vaudeville, choose Manage, then Browse local files.
2. Extract everything in this zip into that folder, so that `version.dll` sits next to `Vaudeville.exe`.
3. Start the game. **The first launch takes a minute or more with no sound**, while MelonLoader (the mod loader
   included in this package) prepares itself. Later launches are normal.

## Playing

- Use the game's own keys: the arrow keys move between controls, Enter activates, and in conversations you type
  your question and press Enter.
- Some controls can't be reached with the arrow keys, such as the Studio's Notes, Accuse, Options and Quit
  buttons. Use your screen reader's object navigation to reach them, and its activate command to press them.
  In NVDA: NVDA+Numpad Minus moves the navigator to the focus, NVDA+Numpad 4 and 6 move between items, and
  NVDA+Numpad Enter activates.
- In a text field, Left and Right read the character at the cursor, Up and Down read the line in multi-line
  fields, and Backspace reads what was deleted. Up or Down at the edge of a field moves to the next control.
  Whether typed characters are echoed is up to your screen reader's own setting.

## Known issues

These are problems in the game itself; they have been reported to its developers.

- The game holds the microphone open while Left Ctrl is held. Screen reader users often press Ctrl to stop
  speech, which can record and send a question by accident in a conversation.
- The arrow keys sometimes jump in an unexpected order, especially in the options and the story editor.
- Loading a location can take a long time while the game's AI starts.

## Settings

The mod turns the game's vsync off. With vsync on, and especially with the monitor switched off, the game can
slow down so much that every announcement takes seconds. To change this, edit `UserData\MelonPreferences.cfg`
in the game folder, under `[VaudevilleAccess]`:

- `DisableVsync = true` (default) or `false`.
- `DeveloperMode = false` (default). Developer tools; not needed to play.

## Uninstalling

Delete `version.dll` and the `MelonLoader`, `Mods` and `UserData` folders from the game folder.

## Source and feedback

Source code, developer notes and issue reports: https://github.com/ObjectInSpace/vaudeville-access
