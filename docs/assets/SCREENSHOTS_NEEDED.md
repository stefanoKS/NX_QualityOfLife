# Real NX screenshots still needed / 今後追加したいNX実画面

[Home / ホーム](../../README.md) · [Installation / 導入](../INSTALLATION.md) · [Command guide / 操作ガイド](../COMMAND_GUIDE.md) · [English](#english) · [日本語](#japanese)

<a id="english"></a>
## English

**No real NX screenshots are included in this documentation revision.** NX 2406 was installed/running, but interactive desktop capture was unavailable during the unattended documentation work. The supplied SVGs are deliberately labeled **schematics**, not reconstructed screenshots. No live journal execution is claimed.

Capture the following on an approved, nonconfidential sample part. Include the **NX release/build, UI language, active application and role** in the capture notes; labels vary between releases. Do not expose customer geometry, user paths, licensing details or other sensitive information. A screenshot records one state; it does not by itself prove associativity or completeness.

| Priority | Genuine capture to add | Show/verify |
|---|---|---|
| 1 | Journal Play | Actual route/dialog, selected production `.vb`, suitable work part; capture before executing |
| 1 | Customize and ribbon placement | Ctrl+1 result, New Item/New Button category, New User Command and the custom tab/group |
| 1 | Edit Action and button image | Journal File type, correct `.vb` and matching supplied `.bmp`; use an example installation path |
| 1 | Saved user role | Release-specific role save/export controls and restored tab after normal restart |
| 2 | 1-1 before/after | Composite group, resulting full circles and Listing Window with any skipped composites |
| 2 | 1-2 before/after | Arc group, visible centers and `ARC_CENTER_…` features with parent relationship |
| 2 | 1-3 path | Ordered source points, through-points spline and feature settings |
| 1 | 1-4 selector and frame | Three selected inputs, default first-point checkbox, close-up labeled X/Y/Z and actual XY plane |
| 1 | 1-5 input sequence | Source/CSYS groups, Square/Circle selector and the two size prompts showing side versus radius |
| 1 | 1-5 model and drawing | Local intersection features plus 1:1 grid; manual section comparison and two source categories |
| 1 | 1-5 isolation and update pair | Two neighboring views with no cross-sector leakage, bodies visible in Modeling, then after a source/CSYS change and any refresh |
| 2 | Optional export acceptance | Only after manual DWG/DXF export: receiving CAD units/scale/layers and profiles; not an automated export claim |

For update pairs, record the actual edit, NX update command, any optional refresh, and whether regeneration was needed. Record unsuccessful cases honestly. Add only reviewed local PNG captures to this folder, with bilingual captions and links from the relevant section; **do not create empty placeholder image files**.

### Illustration inventory

All **26 original SVGs** live in this folder and are linked from the guides. Their labels/captions use English and Japanese.

| Coverage | Files |
|---|---|
| Overall workflow (1) | [workflow-overview.svg](workflow-overview.svg) |
| Installation (5) | [install-download.svg](install-download.svg), [install-files.svg](install-files.svg), [install-journal-play.svg](install-journal-play.svg), [install-ribbon.svg](install-ribbon.svg), [install-role.svg](install-role.svg) |
| 1-1 (3) | [11-chain-samples.svg](11-chain-samples.svg), [11-topology.svg](11-topology.svg), [11-batch.svg](11-batch.svg) |
| 1-2 (3) | [12-centers.svg](12-centers.svg), [12-update.svg](12-update.svg), [12-navigator.svg](12-navigator.svg) |
| 1-3 (3) | [13-order.svg](13-order.svg), [13-through-points.svg](13-through-points.svg), [13-update.svg](13-update.svg) |
| 1-4 (3) | [14-inputs.svg](14-inputs.svg), [14-frame.svg](14-frame.svg), [14-array.svg](14-array.svg) |
| 1-5 (8) | [15-assembly.svg](15-assembly.svg), [15-csys.svg](15-csys.svg), [15-plane.svg](15-plane.svg), [15-boundaries.svg](15-boundaries.svg), [15-profiles.svg](15-profiles.svg), [15-grid.svg](15-grid.svg), [15-layers.svg](15-layers.svg), [15-pipeline.svg](15-pipeline.svg) |

**Maintenance:** retain each diagram's self-contained SVG, `title`/`desc`, viewBox, readable bilingual labels and light-background palette. Use command-prefixed names for future illustrations and add new command/language anchors to the existing index. Before publishing, validate relative links, SVG XML and rendered labels; compare behavior/defaults with the production journal, not only experimental notes.

<a id="japanese"></a>
## 日本語

**今回の文書にはNXの実画面を含めていません。** NX 2406のインストールと起動は確認できましたが、無人実行中に対話的なデスクトップ撮影を利用できませんでした。配布SVGは再現スクリーンショットではなく、意図的に**模式図**と明記しています。NX内での実行確認も主張していません。

承認された機密情報のないサンプルで撮影し、**NXの版・ビルド、表示言語、アプリケーション、ロール**を記録してください。顧客形状、個人のパス、ライセンス情報などを含めないでください。1枚の画面だけでは関連付けや形状の完全性の証明にはなりません。

| 優先度 | 追加する実画面 | 確認する内容 |
|---|---|---|
| 1 | ジャーナル再生 | 実際の経路・ダイアログ、本体の`.vb`、適切な作業パート。実行前を撮影 |
| 1 | カスタマイズとリボン配置 | Ctrl+1、New Item/New Button、New User Command、タブ・グループ |
| 1 | アクション編集と画像 | Journal File、正しい`.vb`と配布BMP。導入例のパスを使用 |
| 1 | ユーザロール保存 | その版の保存・書出し操作と、通常再起動後に復元されたタブ |
| 2 | 1-1の前後 | 複合曲線グループ、作成した真円、スキップを含む情報ウィンドウ |
| 2 | 1-2の前後 | 円弧グループ、中心点、`ARC_CENTER_…`と親の関連 |
| 2 | 1-3の経路 | 点の順序、点を通るスプライン、フィーチャ設定 |
| 1 | 1-4の入力と座標系 | 3入力、最初の点の既定チェック、X/Y/Zと実際のXY平面の拡大 |
| 1 | 1-5の入力順 | ボディ・座標系のグループ、正方形／円の選択、一辺と半径の各入力 |
| 1 | 1-5のモデルと図面 | 局所交線、1:1格子、手動断面との比較、2分類のレイヤ |
| 1 | 1-5の分離と更新前後 | 隣接ビューの混入がない状態、モデリングでの元ボディ表示、形状・CSYS変更と必要な表示更新後 |
| 2 | 任意の出力確認 | 手動DWG/DXF出力後の受取り側CADの単位・縮尺・レイヤ・輪郭。自動出力とは表現しない |

更新前後の画像には、実際の編集、NXの更新操作、任意の表示更新、再生成の要否を記録します。失敗した場合もそのまま記録してください。確認済みPNGだけをこのフォルダへ追加し、日英キャプションと該当ガイドからのリンクを付けます。**空の仮画像は作成しないでください。**

### 図の一覧と保守

新規SVGは**26点**です。[上の一覧](#illustration-inventory)に、全体1点、導入5点、1-1〜1-4各3点、1-5用8点の全ファイルを記載しています。すべて英語・日本語のラベル／キャプション付きで、ガイドから参照しています。

保守時は自己完結したSVG、`title`/`desc`、viewBox、読みやすい日英ラベル、淡い背景の配色を維持してください。今後の図はコマンド番号付きで命名し、既存の一覧に言語別アンカーを追加します。公開前に相対リンク、SVGのXML、表示ラベルを確認し、実験メモだけでなく本体ジャーナルと動作・既定値を照合してください。
