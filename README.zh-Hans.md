# PSO2 Shape Studio

[English](README.md) | [日本語](README.ja.md) | [한국어](README.ko.md) | **简体中文** | [繁體中文](README.zh-Hant.md)

PSO2 Shape Studio 是一款 Windows 桌面工具，通过服装体型调整 AQM 文件
（`_sa.aqm`）微调 PSO2 与 PSO2:NGS 的角色体型。无需 Blender，即可一边预览
模型、一边用滑块应用、编辑并保存这些后期调整。

> **当前版本：** [1.3.0](https://github.com/gesori-pro/PSO2_Shape_Studio/releases/tag/v1.3.0)。
> 请从发布页面下载最新的免安装 Windows x64 版本。

## 功能

- 打开 `.aqp`、`.aqn`、`.ice` 来源的 PSO2 模型文件。
- 加载按性别与种族区分的角色外观文件（`.fdp`、`.fnp`、`.fhp`、`.fcp`、
  `.mdp`、`.mnp`、`.mhp`、`.mcp`）及其未加密变体，并应用其中的体型比例与
  颜色。男性与女性各自使用专用的体型比例表。
- 加载与保存服装体型调整动作（`_sa.aqm`）。
- 以 0.001 为步进、在 0.500–1.200 范围内调整 `body_root` 的 Y 缩放（鞋底
  高度），并可对照可开关的地面网格与半透明地面，低于世界 Y=0 的部分会清
  晰显示交界。
- 用滑块或直接输入数值编辑缩放、位置与旋转。在数值框上滚动鼠标滚轮可微
  调；位置以 0.001 为步进，旋转滑块在中央附近会精确吸附到零。
- 用 `Ctrl+Z`、`Ctrl+Y` 或 `Ctrl+Shift+Z` 撤销、重做体型编辑。
- 在选项窗口设置游戏数据文件夹与默认主/副皮肤颜色，并可显示或隐藏内置
  体型分组。
- 加载带 AQN 骨架的模型后，可从按字母排序的列表中，把可编辑骨骼添加为
  左右成对或单骨骼分组。内置与新增的分组显示在同一个可滚动列表中。
- 拖动分隔条即可调整侧边栏宽度，宽度会在下次启动时恢复，滚动区域也会保
  持文件按钮可用。
- 选择本机 PSO2 游戏文件夹、验证其数据结构，并重建模型搜索缓存。
- 按名称、ID、文件名或 MD5 搜索可穿戴模型，并可将结果筛选为基础服
  （Basewear）、套装（Setwear）、外套（Outerwear）与旧版服装
  （Costume/Totalwear）。
- 选择套装时，自动加载与其关联的外套与内衣。
- 在目录数据可用时显示英文（Global）与日文物品名称。
- 从本机游戏数据中选择 Type 1 / Type 2 皮肤贴图。
- 从八种视口背景色中挑选，让深色服装更容易辨认。
- 显示或隐藏受支持的基础服、外套模型附带的装饰部件。
- 应用界面可在英语（Global）、日语、韩语、简体中文、繁体中文之间切换。
- 可用对贡献者友好的 JSON 文件添加或覆盖界面语言。参见
  [本地化指南](LOCALIZATION.md)。

## 系统要求

- Windows x64
- 从源码构建时需要 [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- 模型搜索与自动贴图查找需要本机安装的 PSO2 或 PSO2:NGS

已解包的模型文件无需设置游戏文件夹即可直接打开。

## 基本使用流程

1. 启动 PSO2 Shape Studio。
2. 在主面板或选项窗口中选择游戏安装文件夹、`pso2_bin` 或 `pso2_bin/data`。
3. 刷新缓存并搜索可穿戴模型，或直接打开已解包的模型文件。
4. 按需加载角色文件或已有的 `_sa.aqm` 调整文件。
5. 用 S/P/R 滑块编辑，或直接输入数值。选项窗口可隐藏内置分组，或从已加
   载的模型添加骨骼。随时可以撤销或重置。
6. 将编辑结果保存为 `_sa.aqm` 文件。要让游戏实际生效，请按下一节的重新
   打包步骤操作。

## 将保存的 _sa.aqm 应用到游戏

Shape Studio 保存的 `_sa.aqm` 是只作用于单件服装的体型调整文件。游戏不会
读取独立存在的该文件：必须替换掉该服装 ICE 归档内原本的 `..._sa.aqm` 条目
才会生效。以下步骤使用社区 ICE 工具
[Zamboni](https://github.com/Shadowth117/Zamboni) 重新打包。

1. **按原始条目名称保存。** 先加载服装，再点「保存 _sa.aqm…」——保存对话
   框会根据加载的模型建议正确的名称（例如
   `pl_rbd_201630_bw.aqp` → `pl_rbd_201630_bw_sa.aqm`）。请保留这个名称；
   用其他名称保存的文件无法替换任何条目。
2. **找到服装的游戏文件。** 将鼠标悬停在搜索结果上，即可看到该条目对应的
   ICE 归档完整路径（`pso2_bin/data/...` 下的 32 位字符文件名）。已加载模
   型列表的工具提示也会显示相同路径。把该文件复制到 `C:\work` 之类的短路
   径工作文件夹——绝对不要直接编辑游戏文件夹内的文件。
3. **用 Zamboni 解开副本。** 把复制出来的文件拖放到 Zamboni 上即可解包。
   模型数据位于 `group2` 文件夹，原本的 `..._sa.aqm` 也在其中。
4. **替换条目。** 用你保存的文件覆盖 `group2` 内的 `..._sa.aqm`。不要把备
   份副本留在解包出来的文件夹里——文件夹内的所有文件都会被原样打包回去。
5. **重新打包。** 用 Zamboni 重新打包解出来的文件夹，安装前确认输出文件
   名已改回原本的 32 位字符名称（必要时重命名）。工作路径过长可能导致打
   包无声失败，这正是第 2 步使用短文件夹的原因。
6. **安装。** 推荐使用
   [PSO2NGS Mod Manager](https://github.com/KizKizz/pso2_mod_manager) 这类
   会自动备份并可还原原始文件的模组管理器。若手动安装，请先自行备份原始
   游戏文件，再用重新打包后的归档覆盖。

注意事项：

- 调整只作用于重新打包的那件服装。
- 游戏更新可能还原原始文件；更新后请重新安装模组。
- 安装前可以先用 Shape Studio 打开重新打包后的 ICE 文件预览效果。
- 修改游戏文件的风险由使用者自行承担。

## 相机操作

| 输入 | 动作 |
| --- | --- |
| 左键或右键拖动 | 旋转角色 |
| `Ctrl` + 拖动 | 垂直移动相机 |
| 鼠标滚轮 | 缩放 |
| 按下中键 | 重置视角 |

## 从源码构建

连同子模块一起克隆仓库：

```powershell
git clone --recurse-submodules https://github.com/gesori-pro/PSO2_Shape_Studio.git
cd PSO2_Shape_Studio
```

若克隆时未包含子模块，请另外初始化：

```powershell
git submodule update --init --recursive
```

构建并测试 x64 Release 配置：

```powershell
dotnet build Pso2ShapeStudio.sln -c Release -p:Platform=x64
dotnet test Pso2ShapeStudio.sln -c Release -p:Platform=x64
```

从源码运行应用：

```powershell
dotnet run --project src/App/Pso2ShapeStudio.App.csproj -c Release -p:Platform=x64
```

构建发布包：

```powershell
./publish.ps1
```

脚本会从 `src/App/Pso2ShapeStudio.App.csproj` 读取版本号、运行测试，并将
免安装归档输出到 `dist/`。

## 游戏数据与隐私

本仓库不包含 PSO2 游戏素材、解包模型、贴图与角色文件。应用只读取用户在
本机选择的文件，不会上传游戏数据。

## 依赖与致谢

- [PSO2-Aqua-Library](https://github.com/Shadowth117/PSO2-Aqua-Library) 是
  处理 PSO2 格式与 ICE 数据所需的源码依赖。
- 内置的英日文物品名称表由 PSO2NGS Mod Manager 的物品数据生成。

## 致谢

本项目建立在他人多年无私公开的逆向工程成果之上。

- **[Shadowth117](https://github.com/Shadowth117)** ——
  [PSO2-Aqua-Library](https://github.com/Shadowth117/PSO2-Aqua-Library)、
  [Aqua-Toolset](https://github.com/Shadowth117/Aqua-Toolset) 与
  [Zamboni](https://github.com/Shadowth117/Zamboni) 的作者。本程序打开的
  每一个 PSO2 文件，都是通过他的成果得以解读的。无尽感谢。
- **[dummycount](https://github.com/dummycount)** ——
  [blender_pso2_tools](https://github.com/dummycount/blender_pso2_tools)
  的作者，展示了 PSO2 模型在现代工具链中应有的处理方式。无尽感谢。

## 许可证

PSO2 Shape Studio 按 GNU General Public License 第 3 版的条款分发。详见
[LICENSE](LICENSE)。
