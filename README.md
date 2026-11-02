# Vaudeville Access

A screen reader accessibility mod for [Vaudeville](https://store.steampowered.com/app/2240920/Vaudeville/)
(Bumblebee Studios), built on Unity 6.3's own screen reader support.

Status: early development. Phase 0 (proof that the native accessibility hierarchy works in this game) is done.
See [docs/PLAN.md](docs/PLAN.md) for the plan.

## How it works

Vaudeville is a Unity 6000.3 IL2CPP game. Unity 6.3 can publish an accessibility hierarchy to the operating
system's accessibility API (UI Automation on Windows), which NVDA, JAWS and Narrator read. The game itself does not
use it, and its build stripped the public API for creating one. The mod builds the hierarchy by hand from the parts
the engine still calls, then describes each game screen through it.

The mod also turns vsync off. Unity answers screen reader requests once per
frame, and with vsync on an idle or switched-off display can hold the game near 13 fps, which makes every
announcement take seconds.

## Requirements

- Vaudeville for Windows (Steam).
- [MelonLoader](https://github.com/LavaGang/MelonLoader) 0.7.3, x64.
- A screen reader: NVDA, JAWS or Narrator.

## Building

Requires the .NET SDK and a MelonLoader install in the game that has generated its interop assemblies (run the
game once after installing MelonLoader).

```
dotnet build VaudevilleAccess.csproj -c Release
```

The build copies `VaudevilleAccess.dll` into the game's `Mods` folder. Override the game location with
`-p:GameDir="path\to\Vaudeville"`.

## Settings

In `UserData\MelonPreferences.cfg`, category `VaudevilleAccess`:

- `DisableVsync` (default true): turn vsync off.

## Developer tools

- `tools/Dump-UiaTree.ps1`: prints the game window's UI Automation tree, as screen readers see it.
- `tools/Watch-UiaEvents.ps1`: logs the UI Automation events the game raises.
- `tools/Time-UiaReads.ps1`: times UI Automation reads against the game.
