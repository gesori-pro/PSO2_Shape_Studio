# PSO2 Shape Studio

[English](README.md) | [日本語](README.ja.md) | [한국어](README.ko.md) | [简体中文](README.zh-Hans.md) | **繁體中文**

PSO2 Shape Studio 是一款 Windows 桌面工具，透過服裝體型調整 AQM 檔（`_sa.aqm`）
微調 PSO2 與 PSO2:NGS 的角色體型。不需要 Blender，即可一邊預覽模型、
一邊以滑桿套用、編輯並儲存這些後製調整。

> **目前版本：** [1.2.0](https://github.com/gesori-pro/PSO2_Shape_Studio/releases/tag/v1.2.0)。
> 請從發佈頁面下載最新的免安裝 Windows x64 版本。

## 功能

- 開啟 `.aqp`、`.aqn`、`.ice` 來源的 PSO2 模型檔案。
- 載入舊版各種族的角色外觀檔（`.fdp`、`.fnp`、`.fhp`、`.fcp`）及其未加密
  變體，並套用其中的體型比例與顏色。
- 載入與儲存服裝體型調整動作（`_sa.aqm`）。
- 以 0.001 為單位、在 0.500–1.200 範圍內調整 `body_root` 的 Y 縮放（鞋底
  高度），並可搭配可開關的地面網格與半透明地面比對，低於世界 Y=0 的部分
  會清楚顯示交界。
- 以滑桿或直接輸入數值編輯縮放、位置與旋轉。在數值欄位上滾動滑鼠滾輪可
  微調；位置以 0.001 為步進，旋轉滑桿在中央附近會精確吸附到零。
- 以 `Ctrl+Z`、`Ctrl+Y` 或 `Ctrl+Shift+Z` 復原、重做體型編輯。
- 在選項視窗設定遊戲資料夾與預設主/副皮膚顏色，並可顯示或隱藏內建體型
  群組。
- 載入含 AQN 骨架的模型後，可從按字母排序的清單中，把可編輯骨骼加入為
  左右成對或單一骨骼群組。內建與新增的群組會顯示在同一個可捲動清單中。
- 拖曳分隔線即可調整側邊欄寬度，寬度會在下次啟動時還原，捲動區域也會保
  持檔案按鈕可及。
- 選擇本機 PSO2 遊戲資料夾、驗證其資料結構，並重建模型搜尋快取。
- 以名稱、ID、檔名或 MD5 搜尋可穿戴模型，並可將結果篩選為基底服、套裝、
  外套與舊版服裝（Totalwear）。
- 選擇套裝時，自動載入與其連結的外套與貼身衣。
- 在型錄資料可用時顯示英文（Global）與日文物品名稱。
- 從本機遊戲資料選擇 Type 1 / Type 2 皮膚貼圖。
- 從八種檢視區背景色中挑選，讓深色服裝更容易辨識。
- 顯示或隱藏支援的基底服、外套模型內含的裝飾部件。
- 應用程式介面可在英文（Global）、日文、韓文、簡體中文、繁體中文之間切換。
- 可用對貢獻者友善的 JSON 檔新增或覆寫介面語言。參見
  [在地化指南](LOCALIZATION.md)。

## 系統需求

- Windows x64
- 從原始碼建置時需要 [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- 模型搜尋與自動貼圖查詢需要本機安裝的 PSO2 或 PSO2:NGS

已解包的模型檔案不需設定遊戲資料夾即可直接開啟。

## 基本使用流程

1. 啟動 PSO2 Shape Studio。
2. 在主面板或選項視窗中選擇遊戲安裝資料夾、`pso2_bin` 或 `pso2_bin/data`。
3. 重新整理快取並搜尋可穿戴模型，或直接開啟已解包的模型檔案。
4. 視需要載入角色檔案或既有的 `_sa.aqm` 調整檔。
5. 以 S/P/R 滑桿編輯，或直接輸入數值。選項視窗可隱藏內建群組，或從已載
   入的模型加入骨骼。隨時可以復原或重設。
6. 將編輯結果儲存為 `_sa.aqm` 檔案。要讓遊戲實際套用，請依下一節的重新
   打包步驟操作。

## 將儲存的 _sa.aqm 套用到遊戲

Shape Studio 儲存的 `_sa.aqm` 是只作用於單一服裝的體型調整檔。遊戲不會
讀取獨立存在的這個檔案：必須取代該服裝 ICE 封存檔內原本的 `..._sa.aqm`
項目才會生效。以下步驟使用社群 ICE 工具
[Zamboni](https://github.com/Shadowth117/Zamboni) 重新打包。

1. **以原始項目名稱儲存。** 先載入服裝，再按「儲存 _sa.aqm…」——儲存對話
   框會依據載入的模型建議正確的名稱（例如 `pl_rbd_201630_bw.aqp` →
   `pl_rbd_201630_bw_sa.aqm`）。請保留這個名稱；用其他名稱儲存的檔案無法
   取代任何項目。
2. **找到服裝的遊戲檔案。** 將滑鼠移到搜尋結果上，即可看到該項目對應的
   ICE 封存檔完整路徑（`pso2_bin/data/...` 底下的 32 字元檔名）。已載入
   模型清單的工具提示也會顯示相同路徑。把該檔案複製到 `C:\work` 之類的短
   路徑工作資料夾——絕對不要直接編輯遊戲資料夾內的檔案。
3. **用 Zamboni 解開複本。** 把複製出來的檔案拖放到 Zamboni 上即可解包。
   模型資料位於 `group2` 資料夾，原本的 `..._sa.aqm` 也在其中。
4. **取代項目。** 用你儲存的檔案覆寫 `group2` 內的 `..._sa.aqm`。不要把備
   份副本留在解包出來的資料夾裡——資料夾內的所有檔案都會被原封不動地打
   包回去。
5. **重新打包。** 用 Zamboni 重新打包解出來的資料夾，安裝前確認輸出檔名
   已改回原本的 32 字元名稱（必要時重新命名）。工作路徑過長可能導致打包
   無聲失敗，這正是步驟 2 使用短資料夾的原因。
6. **安裝。** 建議使用
   [PSO2NGS Mod Manager](https://github.com/KizKizz/pso2_mod_manager) 這類
   會自動備份並可還原原始檔案的模組管理器。若手動安裝，請先自行備份原始
   遊戲檔案，再以重新打包後的封存檔覆寫。

注意事項：

- 調整只會套用到重新打包的那件服裝。
- 遊戲更新可能還原原始檔案；更新後請重新安裝模組。
- 安裝前可以先用 Shape Studio 開啟重新打包後的 ICE 檔預覽結果。
- 修改遊戲檔案的風險由使用者自行承擔。

## 攝影機操作

| 輸入 | 動作 |
| --- | --- |
| 左鍵或右鍵拖曳 | 旋轉角色 |
| `Ctrl` + 拖曳 | 垂直移動攝影機 |
| 滑鼠滾輪 | 縮放 |
| 按下中鍵 | 重設視角 |

## 從原始碼建置

連同子模組一起複製儲存庫：

```powershell
git clone --recurse-submodules https://github.com/gesori-pro/PSO2_Shape_Studio.git
cd PSO2_Shape_Studio
```

若複製時未包含子模組，請另外初始化：

```powershell
git submodule update --init --recursive
```

建置並測試 x64 Release 組態：

```powershell
dotnet build Pso2ShapeStudio.sln -c Release -p:Platform=x64
dotnet test Pso2ShapeStudio.sln -c Release -p:Platform=x64
```

從原始碼執行應用程式：

```powershell
dotnet run --project src/App/Pso2ShapeStudio.App.csproj -c Release -p:Platform=x64
```

建置發佈套件：

```powershell
./publish.ps1
```

指令碼會從 `src/App/Pso2ShapeStudio.App.csproj` 讀取版本號、執行測試，並將
免安裝封存檔輸出到 `dist/`。

## 遊戲資料與隱私

本儲存庫不包含 PSO2 遊戲素材、解包模型、貼圖與角色檔案。應用程式只讀取
使用者在本機選擇的檔案，不會上傳遊戲資料。

## 相依項目與致謝

- [PSO2-Aqua-Library](https://github.com/Shadowth117/PSO2-Aqua-Library) 是
  處理 PSO2 格式與 ICE 資料所需的原始碼相依項目。
- 內建的英日文物品名稱表由 PSO2NGS Mod Manager 的物品資料產生。

## 授權

PSO2 Shape Studio 依 GNU General Public License 第 3 版的條款散布。詳見
[LICENSE](LICENSE)。
