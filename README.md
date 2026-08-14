# PSO2 Shape Studio

**English** | [日本語](README.ja.md) | [한국어](README.ko.md) | [简体中文](README.zh-Hans.md) | [繁體中文](README.zh-Hant.md)

PSO2 Shape Studio is a Windows desktop tool for users who fine-tune PSO2 and
PSO2:NGS character body shapes with outfit shape-adjust AQM files (`_sa.aqm`).
It provides a model preview and sliders for applying, editing, and saving those
post-adjustments without using Blender.

> **Current version:** [1.1.0](https://github.com/gesori-pro/PSO2_Shape_Studio/releases/tag/v1.1.0).
> Download the latest self-contained Windows x64 build from the release page.

## Features

- Open PSO2 model files from `.aqp`, `.aqn`, and `.ice` sources.
- Load legacy race-specific character appearance files (`.fdp`, `.fnp`, `.fhp`,
  and `.fcp`) and their unencrypted variants, then apply their body proportions
  and colors.
- Load and save outfit shape-adjust motions (`_sa.aqm`).
- Adjust sole height through `body_root` Y scale from 0.500 to 1.200 in 0.001
  steps and compare it against a toggleable floor grid with a solid translucent
  surface that reveals intersections below world Y=0.
- Edit scale, position, and rotation with sliders or direct numeric input. Use
  the mouse wheel over a numeric field for fine adjustments; position uses
  0.001 steps, and rotation sliders snap exactly to zero near their center.
- Undo and redo shape edits with `Ctrl+Z`, `Ctrl+Y`, or `Ctrl+Shift+Z`.
- Use the Options window to configure the game-data folder and default main/sub
  skin colors, and to show or hide the built-in shape groups.
- After loading a model with an AQN skeleton, add editable bones as L/R pairs or
  single-bone groups selected from an alphabetically sorted list. Built-in and
  added groups are shown together in one scrollable list.
- Resize the sidebar by dragging its divider. The sidebar width is restored on
  the next launch, and its scrolling area keeps the file buttons accessible.
- Select a local PSO2 game folder, validate its data layout, and rebuild the
  model-search cache.
- Search wearable models by name, ID, file name, or MD5 and filter the results
  to Basewear, Setwear, Outerwear, and classic Costume/Totalwear.
- Automatically load Outerwear and Innerwear linked to a selected Setwear item.
- Use English (Global) and Japanese item names where catalog data is available.
- Select Type 1 and Type 2 skin textures from the local game data.
- Choose from eight viewport background colors for better outfit visibility.
- Show or hide the ornament parts included in supported Basewear and Outerwear models.
- Switch the application UI between English (Global), Japanese, Korean, Simplified Chinese, and Traditional Chinese.
- Add or override interface languages with contributor-friendly JSON files. See
  [Localization guide](LOCALIZATION.md).

## Requirements

- Windows x64
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) when building
  from source
- A local PSO2 or PSO2:NGS installation for model search and automatic texture
  lookup

Extracted model files can be opened directly without configuring a game folder.

## Basic workflow

1. Start PSO2 Shape Studio.
2. Select the game installation folder, `pso2_bin`, or `pso2_bin/data` from the
   main panel or the Options window.
3. Refresh the cache and search for a wearable model, or open extracted model
   files directly.
4. Optionally load a character file or an existing `_sa.aqm` adjustment.
5. Edit the available S/P/R sliders or enter values directly. The Options window
   can hide built-in groups or add bones from the loaded model. Use undo or reset
   whenever needed.
6. Save the resulting shape adjustment as an `_sa.aqm` file. To make the
   game use it, follow the repacking steps in the next section.

## Applying a saved _sa.aqm to the game

The `_sa.aqm` written by Shape Studio is a per-outfit shape adjustment. The
game never reads it as a loose file: it only takes effect after it replaces
the original `..._sa.aqm` entry inside that outfit's ICE archive. The steps
below repack the archive with the community ICE tool
[Zamboni](https://github.com/Shadowth117/Zamboni).

1. **Save under the original entry name.** Load the outfit first, then use
   *Save _sa.aqm…* — the dialog suggests the correct name derived from the
   loaded model (for example `pl_rbd_201630_bw.aqp` →
   `pl_rbd_201630_bw_sa.aqm`). Keep that name; a file saved under any other
   name will not replace anything.
2. **Locate the outfit's game file.** Hover over a search result to see the
   full path of the ICE archive it resolves to (a 32-character file name
   under `pso2_bin/data/...`). The loaded-model list shows the same path as
   a tooltip. Copy that file into a short working folder such as `C:\work` —
   never edit files inside the game folder directly.
3. **Extract the copy with Zamboni.** Drag the copied file onto Zamboni to
   unpack it. Model data lands in the `group2` folder, which contains the
   original `..._sa.aqm`.
4. **Replace the entry.** Overwrite the `..._sa.aqm` inside `group2` with
   your saved file. Do not keep backup copies inside the extracted folders —
   everything in them is packed back into the archive.
5. **Repack.** Repack the extracted folder with Zamboni, and make sure the
   result carries the original 32-character file name before installing
   (rename it if needed). Very long working paths can make repacking fail
   silently, which is why the short folder in step 2 matters.
6. **Install.** The safe route is a mod manager such as
   [PSO2NGS Mod Manager](https://github.com/KizKizz/pso2_mod_manager),
   which backs up originals and can restore them later. Installing by hand
   means backing up the original game file yourself, then overwriting it
   with the repacked archive.

Notes:

- The adjustment only affects the outfit it was repacked into.
- Game updates can restore original files; reinstall the mod afterwards.
- You can preview the result by opening the repacked ICE in Shape Studio
  before installing it.
- Modifying game files is at your own risk.

## Camera controls

| Input | Action |
| --- | --- |
| Left or right drag | Rotate the character |
| `Ctrl` + drag | Move the camera vertically |
| Mouse wheel | Zoom in or out |
| Middle click | Reset the view |

## Build from source

Clone the repository with its submodules:

```powershell
git clone --recurse-submodules https://github.com/gesori-pro/PSO2_Shape_Studio.git
cd PSO2_Shape_Studio
```

If the repository was cloned without submodules, initialize them separately:

```powershell
git submodule update --init --recursive
```

Build and test the x64 Release configuration:

```powershell
dotnet build Pso2ShapeStudio.sln -c Release -p:Platform=x64
dotnet test Pso2ShapeStudio.sln -c Release -p:Platform=x64
```

Run the application from source:

```powershell
dotnet run --project src/App/Pso2ShapeStudio.App.csproj -c Release -p:Platform=x64
```

Build the release package:

```powershell
./publish.ps1
```

The script takes the version from `src/App/Pso2ShapeStudio.App.csproj`, runs the
tests, and writes a self-contained archive to `dist/`.

## Game data and privacy

PSO2 game assets, extracted models, textures, and character files are not
included in this repository. The application reads files selected on the local
computer and does not upload game data.

## Dependencies and credits

- [PSO2-Aqua-Library](https://github.com/Shadowth117/PSO2-Aqua-Library) is the
  required source dependency for PSO2 formats and ICE data.
- The embedded English and Japanese item-name table is generated from
  PSO2NGS Mod Manager item data.

## License

PSO2 Shape Studio is distributed under the terms of the GNU General Public
License version 3. See [LICENSE](LICENSE) for details.
