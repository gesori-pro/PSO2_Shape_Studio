# PSO2 Shape Studio

**English** | [日本語](README.ja.md) | [한국어](README.ko.md)

PSO2 Shape Studio is a Windows desktop application for previewing PSO2 and
PSO2:NGS character models, applying character proportions, and editing outfit
shape adjustments without using Blender.

> **Development status:** Pre-release. Core model loading, character shaping,
> texture lookup, and shape-adjust workflows are implemented, but rendering and
> format coverage are still being refined.

## Features

- Open PSO2 model files from `.aqp`, `.aqn`, and `.ice` sources.
- Load character appearance files (`.fnp`, `.fhp`, `.fnpu`, and `.fhpu`) and
  apply their body proportions and colors.
- Load and save outfit shape-adjust motions (`_sa.aqm`).
- Edit scale, position, and rotation values with 0.01-step sliders.
- Undo and redo shape edits with `Ctrl+Z`, `Ctrl+Y`, or `Ctrl+Shift+Z`.
- Select a local PSO2 game folder, validate its data layout, and rebuild the
  model-search cache.
- Search wearable models by name, ID, file name, or MD5 and filter the results
  to Basewear, Setwear, Outerwear, and classic Costume/Totalwear.
- Use English (Global) and Japanese item names where catalog data is available.
- Select Type 1 and Type 2 skin textures from the local game data.
- Switch the application UI between English (Global), Japanese, and Korean.

## Requirements

- Windows x64
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) when building
  from source
- A local PSO2 or PSO2:NGS installation for model search and automatic texture
  lookup

Extracted model files can be opened directly without configuring a game folder.

## Basic workflow

1. Start PSO2 Shape Studio.
2. Select the game installation folder, `pso2_bin`, or `pso2_bin/data`.
3. Refresh the cache and search for a wearable model, or open extracted model
   files directly.
4. Optionally load a character file or an existing `_sa.aqm` adjustment.
5. Edit the available S/P/R sliders. Use undo or reset whenever needed.
6. Save the resulting shape adjustment as an `_sa.aqm` file.

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
