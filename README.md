# NittyGriddy

**A table manager for Windows**

NittyGriddy arranges poker table windows into a grid of numbered slots across your monitors and lets you select and move tables with global hotkeys. It works for any kind of window (traders, dashboards), but it is built for multi-tabling.

It works purely at the window level: it moves, resizes and activates windows through the Windows window API. It never sends clicks or keystrokes to a poker client, never reads table contents, and has no HUD or betting hotkeys.

## Features

- **Slots on every monitor**: each display has its own grid (rows × columns, "fit N tables", or "fill with open tables", which resizes the grid to the tables that are open). Slots are numbered across all displays; the numbers are shown on the grid overlay and used by the hotkeys.
- **Automatic placement**:
  - New tables go to the first free slot
  - Drag a table onto a slot to snap it there; dropping on an occupied slot swaps the two tables (or stacks, your choice)
  - Tables re-snap when you change the grid
  - More tables than slots? They stack, least-occupied slot first; optionally cascaded so every title bar shows
  - *Arrange tables* keeps tables in the slot they are in and puts the rest in the nearest free slot (or, as an option, packs them into slots 1, 2, 3…)
  - Optionally close gaps when a table closes
- **Global hotkeys** (all rebindable):
  - Go to the table in slot 1–9; pressing again cycles through a stack
  - Move the active table to slot 1–9
  - Next / previous table
  - Bring all tables to the front (also a button and a tray-menu item)
  - Arrange all tables, snap the active window, turn the grid on or off, switch profile
- **Active-table border**: a coloured frame around the table that has focus
- **Profiles**: named layouts you can switch between from the window, the tray menu, or a hotkey; import and export as JSON
- **Settings save themselves**: everything is stored in `%APPDATA%\NittyGriddy\settings.json` as you change it and restored at the next start, including whether the grid was on
- **Tray icon**, minimise/close to tray, start minimised, start with Windows
- **Knows your poker clients**: table windows of GGPoker, HC Online (iPoker), CoinPoker and Unibet are recognised automatically, and their lobbies, tournament lobbies and other windows are left alone
- **Pinned tables**: a tournament can always go to the same slot
- **Your own rules**: for any other program, by program name, window class and title, with optional regular expressions

![Grid overlay](https://i.imgur.com/GsItVUK.png)

## Install

**Download: [latest release](https://github.com/robinabganpat/NittyGriddy/releases/latest).** Each release is built from its tagged source by a [GitHub Actions workflow](.github/workflows/release.yml), which also uploads the files to VirusTotal; the release notes list the SHA-256 and the scan result of each file.

**The portable zip is the recommended download.** Both need 64-bit Windows 10 or 11.

- **Portable zip** (`…-portable.zip`), recommended: unpack it to a folder of your choice, for example `%LOCALAPPDATA%\Programs\NittyGriddy`, and run `NittyGriddy.exe`. Pin it to Start or the taskbar if you like. It needs the [.NET 9 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/9.0); Windows offers the download if it is missing. No antivirus engine on VirusTotal flags it.
- **Installer** (`…-setup.exe`): installs for your user account only, without administrator rights, into `%LOCALAPPDATA%\Programs\NittyGriddy`, and adds a Start menu entry and an uninstaller. If the .NET 9 Desktop Runtime is missing, it offers to open Microsoft's download page.

  **Why the installer may be flagged:** a handful of antivirus engines that judge files by machine learning rather than by known malware, Microsoft Defender among them, flag the installer, typically as `Trojan:Win32/Wacatac!ml`. The zip, which contains exactly the same program, is clean. The flag is about the installer wrapper: it is not code-signed, and unsigned installers built with common tools are something malware uses too. If Defender blocks the installer, use the zip.

**Coming from version 1.0?** That was a separate kind of install. Remove it first under *Settings > Apps > Installed apps* (it is listed as NittyGriddy 1.0.0.0); otherwise both versions stay installed. A layout saved with 1.0 can be imported: see [Configuration Files](#configuration-files).

To update a 2.x install, run the newer installer over the old one, or replace the unpacked folder. Your settings live in `%APPDATA%\NittyGriddy` and are kept. Uninstalling leaves that folder in place; delete it yourself if you want everything gone.

### Checking the download

The files are not code-signed, so Windows SmartScreen may say *Windows protected your PC* the first time. *More info* then *Run anyway* starts it. Before you do, you can check that the file is the one published here:

```powershell
Get-FileHash .\NittyGriddy-2.0.0-setup.exe -Algorithm SHA256
```

The result must equal the SHA-256 in the release notes (the same values are in `SHA256SUMS.txt`, attached to each release). The *report* link opens VirusTotal's scan of the file with exactly that hash. A page saying the file is unknown means nobody has uploaded it yet; you can upload your download there yourself, and it will land on that same page if it is the genuine file.

Or build it yourself from this source: see [Building from source](#building-from-source).

## Quick Start

1. Start NittyGriddy from the Start menu, or `NittyGriddy.exe` from the unpacked zip.

   On a first start, a **Get started** panel at the top of the window leads through the steps below and ticks each one as you do it. *Hide* removes it; the **Behaviour** tab has a button to bring it back.

2. **Tables** tab: the supported poker clients are already listed and switched on (Unibet is off, see below). Nothing to set up for those. For another program, click *Pick an open window…*.

3. **Layout** tab: set the grid for each display. *Fill with open tables* gives a display one slot per table, as large as possible: one table fills it, four make 2 × 2, and when a table closes the others grow into the space. The preview shows the slots and their numbers; changes apply immediately.

4. Turn the grid on with the switch at the top left (or `Ctrl+Alt+G`), then click **Arrange tables**. Turning the grid on does not move anything by itself; the status line says how many tables are waiting for a slot.

5. Press `Alt+1`: the table in slot 1 comes to the front.

6. NittyGriddy has to keep running while you play. Closing the window quits it unless *Keep running in the notification area when the window is closed* is ticked on the **Behaviour** tab.

## Default Hotkeys

| Action | Hotkey | Active |
|---|---|---|
| Turn grid on / off | `Ctrl+Alt+G` | always |
| Arrange all tables | `Alt+Home` | grid on |
| Snap active table to nearest slot | not set | grid on |
| Next / previous table | `Alt+PgDn` / `Alt+PgUp` | grid on |
| Bring all tables to the front | `Alt+0` | grid on |
| Go to table in slot 1–9 | `Alt+1` … `Alt+9` | grid on |
| Move active table to slot 1–9 | `Alt+Shift+1` … `Alt+Shift+9` | grid on |
| Switch to next profile | not set | grid on |

Hotkeys are system-wide. Table hotkeys are only registered while the grid is on, so they do not take keys away from other programs when you are not playing. Change them on the **Hotkeys** tab: click a box and press the keys; Backspace clears.

The defaults avoid `Ctrl+Alt+<digit>` on purpose: on many keyboard layouts `Ctrl+Alt` is `AltGr`, and such a hotkey would swallow characters like `€`.

## Supported Poker Clients

A client is recognised by its program name, and its tables are told apart from its other windows by window class and title. Status of each built-in rule:

| Client | Program | How tables are recognised | Checked |
|---|---|---|---|
| GGPoker | `GGnet` | Title shape: `… - Table 33`, `… - $0.02 / $0.05`, `Rush & Cash …`, `Spin & Gold …` | Tournament tables against the live client; other formats from titles reported by other users |
| HC Online (iPoker) | `PokerClient` | Title `<name> <table id> \| <game> \| …` | Tournament tables against the live Holland Casino client; cash and Twister formats from other iPoker clients |
| CoinPoker | `CoinPoker` | Window class: tables are Unity windows, the lobby is not | Against the live client |
| Unibet Poker | `Unibet Poker` | Title contains blinds (`/`) or starts `Texas Hold'em` | From a 2024 report; **off by default** |

The **Tables** tab lists the open tables and, below them, the other windows of these programs that were *not* treated as tables. If a table shows up in the second list, edit that client's rule.

Things to know:

- **Unibet** prohibits helper software in its terms, and other table managers dropped Unibet for that reason. NittyGriddy only moves windows, but whether that is acceptable is Unibet's call. The rule is there; switching it on is your decision.
- **CoinPoker** titles every table just `CoinPoker`. Its tables are managed like any other, but cannot be pinned by tournament: nothing at window level says which tournament a table belongs to.
- Program names are read from the system's process list. No handle to a poker client's process is opened.
- Built-in rules update with NittyGriddy. Once you edit one, it is yours and no longer updates.
- Other sites on the GGPoker or iPoker networks should work where the program is named `GGnet` or `PokerClient`.

## Pinned Tables

On the **Tables** tab, under *Pinned tables*, enter part of a table title and a slot number. Every table whose title contains that text goes to that slot, in the current profile: when it opens, and whenever you press *Arrange tables*.

Use the part of the title that stays the same while you play. A GGPoker tournament table is titled `<tournament> : Buy-in … - Blinds … - Table 33…`; the blinds and table number change, so pin on `<tournament> : `. The **Pin…** button next to an open table fills this in for you.

- A table already sitting in a pinned slot moves to a free slot when the pinned table arrives.
- Other tables leave pinned slots empty while any other empty slot exists; they use one before stacking.
- If you drag a pinned table elsewhere, it stays there until you press *Arrange tables* or change the layout.
- Closing gaps (the optional behaviour) never moves a pinned slot.

## Your Own Rules

A window is a table when it matches any rule that is switched on. Within a rule, every part that is filled in must match: program (exact name), window class (starts with), title (contains), and "but not when the title contains" to leave out lobbies. With *regular expressions* ticked, class and titles are patterns instead.

A rule with only a title can match any program's window, a browser tab for instance, so give it a program or class where you can.

## Configuration Files

Settings are saved automatically; there is nothing to save or load by hand. To move a layout between machines or share it, use the **⋯** menu next to the profile picker: *Export to file…* and *Import from file…*. Files saved by version 1.0 can be imported the same way.

`NittyGriddy.exe --settings <path>` uses a different settings file.

## Known Limitations

- Layouts are stored per display device name (`\\.\DISPLAY1`, …). Windows can renumber displays when monitors are re-plugged.
- Only slots 1–9 have hotkeys.
- On a display set to *Fill with open tables*, the number of slots follows the number of tables, so slot numbers on the displays after it shift as tables open and close. Pins and slot hotkeys refer to slot numbers; with pins, use a fixed grid on the displays after it.
- Windows can refuse to change focus while the program in front has a menu open; a slot hotkey pressed at that moment may not bring the table forward.
- NittyGriddy cannot tell that a table is waiting for you to act. That would require reading the client, which it deliberately does not do.

## Building from source

You need Windows 10 or 11 and the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (a newer SDK works too, as long as the .NET 9 Desktop Runtime is installed). Nothing else: no Visual Studio, no other tools.

```powershell
git clone https://github.com/robinabganpat/NittyGriddy.git
cd NittyGriddy

# run it
dotnet run --project App/App.csproj

# or build once and start the program directly
dotnet build NittyGriddy.sln
.\App\bin\Debug\net9.0-windows\NittyGriddy.exe

# run the tests
dotnet test NittyGriddy.sln
```

Good to know:

- Only one NittyGriddy runs at a time. Starting it again just brings the running one to the front, and a build fails with a "file is being used" error while it is running. It may be in the notification area: right-click its icon and choose *Exit*.
- To try things without touching your real settings, give it its own settings file: `dotnet run --project App/App.csproj -- --settings C:\temp\nittygriddy-test.json`
- The solution opens in Visual Studio 2022 and in JetBrains Rider.

### Building the installer and zip

```powershell
winget install --id JRSoftware.InnoSetup -e      # once; needed for the installer only
powershell -ExecutionPolicy Bypass -File tools\build-release.ps1
```

This runs the tests, publishes a release build, and writes the installer, the portable zip and `SHA256SUMS.txt` to `artifacts\release\<version>\`. The version comes from `<Version>` in `App/App.csproj`. Without Inno Setup the zip is still built and the installer is skipped.

The installer script is `installer/NittyGriddy.iss`.

### Publishing a release

Releases are built by GitHub Actions ([`.github/workflows/release.yml`](.github/workflows/release.yml)):

1. Set `<Version>` in `App/App.csproj`, commit and push.
2. Tag that commit with the same version and push the tag:

   ```powershell
   git tag v2.1.0
   git push origin v2.1.0
   ```

The workflow checks that the tag matches `<Version>`, runs the tests, builds the zip and the installer with `tools/build-release.ps1`, uploads both to VirusTotal and waits for the scans (`tools/virustotal-scan.ps1`), and publishes a GitHub release with the files, `SHA256SUMS.txt` and notes listing each file's hash and scan result (`tools/release-notes.ps1`). It waits up to 20 minutes for VirusTotal. Running the workflow by hand (*Actions > Release > Run workflow*) builds and scans without publishing; the files are kept as a workflow artifact.

The scan needs a free VirusTotal API key, stored once as a repository secret:

```powershell
gh secret set VT_API_KEY      # paste the key from virustotal.com > your profile > API key
```

Without the secret the release is still published, with report links but no scan results. A scan that fails or takes longer than 20 minutes is a warning on the run, not a failure; the report link fills in once VirusTotal finishes.

Every push and pull request also runs the tests ([`.github/workflows/build.yml`](.github/workflows/build.yml)).

## Requirements

- 64-bit Windows 10 or 11
- .NET 9 Desktop Runtime (the installer checks for it and points to the download)

## License

GNU General Public License v3.0
