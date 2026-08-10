# PSO2 Shape Studio

[English](README.md) | **日本語** | [한국어](README.ko.md)

PSO2 Shape Studioは、PSO2およびPSO2:NGSのキャラクターモデルをプレビューし、
キャラクターの体型やコスチュームの体型補正をBlenderを使用せずに適用・編集できる
Windows向けデスクトップアプリケーションです。

> **開発状況：** プレリリース版です。モデルの読み込み、キャラクターの体型適用、
> テクスチャー検索、体型補正の基本機能は実装済みですが、レンダリングと対応形式は
> 引き続き調整中です。

## 機能

- `.aqp`、`.aqn`、`.ice`からPSO2モデルを開けます。
- キャラクタークリエイトデータ（`.fnp`、`.fhp`、`.fnpu`、`.fhpu`）を読み込み、
  体型とカラーを適用できます。
- コスチュームの体型補正モーション（`_sa.aqm`）を読み込み・保存できます。
- スケール、位置、回転を0.01刻みのスライダーで編集できます。
- `Ctrl+Z`、`Ctrl+Y`、`Ctrl+Shift+Z`で体型編集を元に戻す、またはやり直すことが
  できます。
- ローカルのPSO2ゲームフォルダーを指定し、データ構成を検証してモデル検索キャッシュを
  更新できます。
- 名前、ID、ファイル名、MD5でウェアモデルを検索し、ベースウェア、セットウェア、
  アウターウェア、旧仕様のコスチューム（トータルウェア）に絞り込めます。
- カタログにデータがあるアイテムは、英語（Global）名と日本語名で検索できます。
- ローカルのゲームデータからタイプ1・タイプ2のスキンテクスチャーを選択できます。
- アプリケーションの表示言語を英語（Global）、日本語、韓国語から選択できます。

## 動作要件

- Windows x64
- ソースからビルドする場合は
  [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- モデル検索とテクスチャーの自動検索を使用する場合は、ローカルにインストールされた
  PSO2またはPSO2:NGS

展開済みのモデルファイルは、ゲームフォルダーを設定しなくても直接開けます。

## 基本的な使い方

1. PSO2 Shape Studioを起動します。
2. ゲームのインストールフォルダー、`pso2_bin`、または`pso2_bin/data`を選択します。
3. キャッシュを更新してウェアモデルを検索するか、展開済みのモデルファイルを直接
   開きます。
4. 必要に応じてキャラクターファイルまたは既存の`_sa.aqm`を読み込みます。
5. S/P/Rスライダーを編集します。必要に応じて編集を元に戻す、またはリセットします。
6. 編集結果を`_sa.aqm`ファイルとして保存します。

## カメラ操作

| 入力 | 操作 |
| --- | --- |
| 左または右ドラッグ | キャラクターを回転 |
| `Ctrl` + ドラッグ | カメラを上下に移動 |
| マウスホイール | ズームイン・ズームアウト |
| ホイールクリック | 視点をリセット |

## ソースからのビルド

サブモジュールを含めてリポジトリをクローンします。

```powershell
git clone --recurse-submodules https://github.com/gesori-pro/PSO2_Shape_Studio.git
cd PSO2_Shape_Studio
```

サブモジュールなしでクローンした場合は、別途初期化します。

```powershell
git submodule update --init --recursive
```

x64のRelease構成をビルドしてテストします。

```powershell
dotnet build Pso2ShapeStudio.sln -c Release -p:Platform=x64
dotnet test Pso2ShapeStudio.sln -c Release -p:Platform=x64
```

ソースからアプリケーションを実行します。

```powershell
dotnet run --project src/App/Pso2ShapeStudio.App.csproj -c Release -p:Platform=x64
```

## ゲームデータとプライバシー

PSO2のゲームアセット、展開済みモデル、テクスチャー、キャラクターファイルは、
このリポジトリには含まれていません。本アプリケーションはローカルコンピューター上で
選択されたファイルを読み込み、ゲームデータをアップロードしません。

## 依存関係とクレジット

- [PSO2-Aqua-Library](https://github.com/Shadowth117/PSO2-Aqua-Library)は、
  PSO2形式とICEデータを扱うために必要なソース依存関係です。
- 内蔵されている英語・日本語のアイテム名テーブルは、PSO2NGS Mod Managerの
  アイテムデータから生成されています。

## ライセンス

PSO2 Shape StudioはGNU General Public License version 3の条件に基づいて
配布されます。詳細は[LICENSE](LICENSE)をご覧ください。
