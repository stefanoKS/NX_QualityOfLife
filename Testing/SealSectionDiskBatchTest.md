# Square / circle section batch test

Run **SealSectionDiskBatchTest.vb** in NX Modeling, in the same displayed
millimeter work part. This is a separate experimental implementation; the
sphere/Boolean test and read-only diagnostic remain available.

## Current version: confirmed v5.1.2, outlines deferred

At the user's request, both VB scripts have been restored byte-for-byte from
the saved, user-confirmed v5.1.2 baseline. The DraftingBody/DraftingCurve filter
and erase recovery remain, including the fix verified with the previously
failing feature-group order. The optional outline prompt, reserved-layer-256
requirement and all drafting-outline creation code have been removed.
Defaults remain square side 50 mm and circle radius 25 mm. Timing logs and
source-layer inheritance remain. The separate refresh test remains removed.

V5.5 failed at `ConvertToView` with "Object not qualified to be converted".
Outline implementation is deferred; no further outline changes are planned.
The restored code compiles against NX 2406, and hashes match the saved working
baseline and between both script copies. The sections below are version history.

日本語：ユーザー確認済みのv5.1.2に戻しました。フィーチャグループ順序に関する
表示消去の修正は維持し、輪郭表示の追加はすべて削除しました。輪郭機能は当面保留です。
レイヤ256の予約は不要です。正方形50 mm・円の半径25 mm、計測ログ、レイヤ継承は維持します。

## Withdrawn version v5.5: confirmed proxy filter plus drafting outlines

The user confirmed the v5.1.2 filter works; the successful run log is
`SealSection_20261115_130957_627_117475fb.log`. The filter prevents drafting-view
representations from entering the model erase list. Model faces, edges and
curves remain included. It addresses the demonstrated DraftingBody failure;
it does not impose a feature-group order or promise a performance improvement.

V5.5 preserves the confirmed isolation, recovery, base-view creation and cutting
boundary routines unchanged. An English/Japanese Yes/No/Cancel prompt defaults
to **Yes** for outlines. Layer **256 must be empty and not the work layer**;
otherwise the journal stops before model changes with an explanation.

All section views are created, isolated, updated and checked first. Only then
does the script add four square lines or one circle for each completed sector.
It maps the projected CSYS center into the drawing view, creates the outline
and converts each new curve to a view-dependent drawing object. The new objects
are never fed into the section erase loop. Layer 256 is enabled in the target
view; length, view ownership and the projected CSYS grid position are checked.
The same batch undo mark covers failures in this final outline pass.

The outlines are nonparametric drafting geometry: view dependence is intended
to keep them with the view, but they are **not expression-linked to CSYS/size
changes**. Regenerate after those changes. Existing layer-256 geometry is never
silently deleted; free that reserved layer before another outlined batch, or
choose No. Defaults remain square side 50 mm and circle radius 25 mm.

日本語：確認済みの表示分離修正を維持し、全ビューの作成・更新完了後に輪郭を追加します。
既定は「はい」です。レイヤ256を空け、別の作業レイヤを使用してください。
輪郭はビューに属する図面曲線で、CSYSや切断寸法との自動連動はありません。
これらを変更した場合は図面を再作成してください。

Compilation against NX 2406 passes. Source comparison verifies that the confirmed
core routines were not changed. The new outline pass still requires a live NX
test: use two rotated CSYS, verify square and circle output, move a view, update
the drawing and check DWG/DXF output. Runtime checks are not a substitute for
visual verification. The historical rollback notes below describe older builds.

## Version 5.1.2: drafting representation filter (confirmed working)

The diagnostic run `SealSection_20261115_130335_347_d35c0670.log` isolated the
failing object: `NXOpen.Drawings.DraftingBody`, tag 94734, layer 10. The candidate
list contained one drafting body at sector 1 and two at sector 2. An individual
erase of that drawing representation failed with NX 630035. Layer enumeration
therefore cannot be treated as a collection of model objects only.

V5.1.2 filters `Drawings.DraftingBody` and `Drawings.DraftingCurve` before building
the model erase list and logs their count. Original faces, edges, curves and
other model candidates remain included. The recovery/diagnostic path is retained.
No feature reordering, global body blanking or outline additions are introduced.
The model bodies still use the existing view-builder exclusions.

This directly addresses the object type demonstrated by the diagnostic. It does
not yet prove that every feature order works, or explain exactly why reordering
SURFACE avoids the problem. Both script copies compile and match; test the
previously failing order and check visual isolation before restoring outlines.

日本語：診断により、消去対象に図面ビューのDraftingBodyが混入していたことを確認しました。
図面表現のDraftingBody・DraftingCurveだけを対象外にし、元の面・エッジ・曲線は維持します。
フィーチャの順序は変更しません。以前失敗した順序で動作と表示分離を確認してください。

## Version 5.1.1: erase recovery and object-level diagnostics

The user observed that moving SURFACE to the top makes generation succeed.
The failed-order log reports NX 630035 in `ViewDependentDisplayManager.Erase`
at sector 2, after section generation, with no outline code running. This is
the same failing operation as in the outline experiments. Feature reordering
can affect database enumeration/update state; the exact offending object or
dependency has not yet been established. The journal does not reorder features
or require SURFACE to be first. CSYS timestamp sorting is unchanged.

The exclusion list still includes faces and edges and is deduplicated by tag.
The log now records the selected group timestamps and erase candidate counts by
runtime type for each view. An invisible undo mark surrounds each erase attempt.
For NX 630035 only, the failed attempt is undone before retrying both halves of
the object array. All candidates must succeed; none are silently skipped. If a
single object fails, `ERASE FAILED OBJECT` identifies its type, tag, layer,
journal identifier and owning body where applicable. The complete batch then
rolls back. Other error codes propagate without this retry. Recovery may be
slower on a failing batch; this is not a performance optimization.

This can recover from a multi-object erase failure, or identify the specific
object that still needs a fix. It is not proof that feature-order dependence is
resolved. Compiled against NX 2406; live verification is outstanding. Test with
SURFACE in the previously failing position first and retain the new log. Confirm
visual isolation before retrying any outline addition. Outlines remain disabled.

日本語：SURFACEグループの位置で結果が変わる問題を調査するため、表示消去に失敗した
処理を取り消してから対象を分割し、全オブジェクトの消去を再試行します。
消去できないオブジェクトは省略せず、識別情報をログに出して一括処理を取り消します。
フィーチャの順序は変更しません。輪郭追加はまだ無効です。

## Baseline rollback history

All outline experiments and visibility optimizations have been removed from both
VB scripts. `3D_to_Draft/1-5_Drafting.vb` is an exact copy of this journal.
The code matches the committed v5.1 generator apart from the requested default
values and explanatory text: square side 50 mm and circle radius 25 mm.
Timing logs and inherited source layers remain. Cutting boundaries are hidden;
there is no outline prompt or reserved layer. The removed refresh test has not
been restored; references to it below are historical only.

The latest failure occurred during sector-2 isolation, before drafting outlines
were created. This rollback restores the previously successful implementation;
it does not establish that the same model/session will now run successfully.
If the error recurs, retain the log and investigate the failing erase objects
without adding further outline changes to this baseline.

日本語：輪郭表示の追加と高速化の変更をすべて取り消しました。既定値は正方形の一辺
50 mm、円の半径25 mmです。計測ログと元ボディのレイヤ継承は維持します。
輪郭は非表示です。同じモデルでの再実行成功を保証するものではありません。

## Version 5.1: automatic timing log / 自動処理時間ログ

Run the journal normally. Every run creates a unique UTF-8 `.log` file in
`%LOCALAPPDATA%\NX_QualityOfLife\Logs` (normally
`C:\Users\stefa\AppData\Local\NX_QualityOfLife\Logs` on this computer).
The filename includes a UTC timestamp and a unique suffix. Existing logs are
never overwritten. The full path appears in the Listing Window at startup and
completion, and in the final success/error message. No extra input is needed.

The log measures source collection, boundary construction and its initial test,
each sector and source-layer intersection, fallback proximity checks, curve
validation, layer/attribute assignment, drawing creation, view isolation,
view commit/update and final model/view updates. Sector, layer and body details
identify the slow calls. During fallback distance checks it also logs progress
every 100 faces; an individual NX call may still take time between entries.

At the end, find **TIMING SUMMARY / 処理時間集計**:

- **Stages ranked by SELF time** lists the most expensive stages first.
  `self_s` excludes timed child operations, avoiding double-counting.
  `inclusive_s` includes children; `max_call_s` is the longest single call.
- **Slowest processing calls** lists up to 20 individual calls with context.
  These include child work but exclude input-dialog waiting. Parent and child
  calls overlap, so do not add these rows together.
- Total time and input-dialog waiting are reported separately. Unscoped overhead
  covers work outside a timing scope, including some logging/setup costs.
- **RUN RESULT** distinguishes `SUCCESS`, `FAILED` and `CANCELLED`. Failures retain
  the diagnostic messages and measure the batch rollback when applicable.

Each `START`, `END` and progress entry is flushed immediately, so the file can be
opened during the run. The tab-separated event rows use UTC timestamps and
Stopwatch durations in seconds. A `START` without a matching `END` identifies an
operation still running, or interrupted if NX stopped. `END` alone does not mean
the operation succeeded: check the error messages and final run result. A forced
NX/process termination may leave no final summary.

If the preferred folder cannot be used, the logger tries
`%TEMP%\NX_QualityOfLife\Logs`. File-write errors do not abort geometry processing;
the final notice reports them and the timing summary remains available in the
Listing Window. Instrumentation adds some overhead; it measures performance and
does not itself optimize the geometry operations.

日本語：通常どおり実行すると、上記フォルダに実行ごとのログファイルを自動作成します。
保存先は情報ウィンドウと終了メッセージに表示されます。最後の「処理時間集計」で、
時間のかかった処理と断面・レイヤを確認できます。`self_s`は子処理を除いた時間、
`inclusive_s`は子処理を含む時間です。入力待ちは別集計で、処理別上位からも除外します。
実行中もログを開いて進捗を確認できます。ログの書込みに失敗しても形状処理は継続します。
原因調査には、代表的なモデルで1回実行した後の`.log`ファイルを共有してください。

V5.1 compiles against the installed NX 2406 assemblies. Isolated logger tests
check live flushing, nested timings, UTF-8, cancellation/failure summaries and
file-write failure handling. Actual NX processing times require a live model run.

## Version 5: square by default, circle optional

The filename remains **SealSectionDiskBatchTest.vb**. After choosing the source
and CSYS groups, choose **Square / 正方形** (first and selected by default) or
**Circle / 円** (second). The choice applies to every sector in the batch.

- Square asks for the **full side length / 一辺の長さ**, default **50 mm**.
  Four associative lines connect smart corner points at local CSYS coordinates
  `(±W/2, ±W/2, 0)`. A Bounded Plane fills the loop. Its center is the CSYS origin,
  and its edges follow the CSYS X/Y axes as the CSYS moves or rotates.
- Circle asks for the **radius / 半径**, default **25 mm**. It retains the
  previously working associative circle and circular Bounded Plane construction.

For 120 mm square width, X and Y each range from -60 to +60 mm. A radius-60 circle
has the same total width/height, but excludes the square's corners. For example,
local `(50,50,0)` is inside the square but outside the circle (distance about
70.71 mm). The difference changes the actual section curves, not just their display.

日本語：正方形の入力値は「一辺の全長」です。120 mmの場合、CSYS原点を中心に
X・Y方向へそれぞれ±60 mmの範囲になります。円の入力値は「半径」です。
半径60 mmの円と一辺120 mmの正方形は幅・高さが同じですが、正方形には円の外側の
四隅も含まれます。どちらもCSYSのXY平面上に作成します。

The full side length or radius is stored in a shared NX expression, logged at
startup. Square half-width expressions depend on that full-width expression.
Grid pitch is square side + gap, or circle diameter + gap. Existing source-layer
inheritance, view-dependent isolation and unique view names are retained.
The section features keep their existing `SEAL_DISK_SECTOR_...` names for refresh
compatibility and additionally record `SEAL_BOUNDARY_SHAPE` as Square or Circle.
Refresh v2.2 is shape-independent and works with this metadata/output structure.

On the first square, temporary intersections check the two center-axis lengths
against the entered width, and a diagonal against `width × sqrt(2)` (169.7056 mm
for width 120). Circle retains its center-diameter check. Each temporary test is
undone. Output samples are checked in CSYS coordinates against the square's X/Y
limits or the circle's radial limit and against the XY plane.

The user confirmed v4.2's circular workflow. V5 compiles against installed NX 2406;
square/circle containment regression cases and both bilingual selector layouts
were checked outside NX. Actual square feature creation and associative updates
still require a live NX run. First test with one or two rotated CSYS, then move
a CSYS, update and refresh the views. Verify the square stays centered/aligned,
and that the extra corner geometry appears compared with a circle of equal span.

## Version 4.2: source-layer inheritance with view-dependent erasure

The v4.1 run completed but displayed other sectors' curves in each view. Shared
category layers cannot isolate sectors by themselves. The previous implementation
put curves into `BaseViewBuilder.HiddenObjects`; the installed NX documentation
describes that builder as component visibility, and the run showed it did not
provide the required curve isolation.

V4.2 keeps body exclusions in that builder, but uses the documented
`view.DependentDisplay.Erase(...)` operation for individual objects after setting
layer visibility. This is a persistent view-specific display edit, not deletion
or global blanking. Own-sector curves have their view-specific erasures removed;
other non-body objects on visible category layers are erased in that view.
The view is then updated and positioned. Source layers and curve associativity
are retained. The view records `SEAL_VISIBILITY_MODE=VIEW_DEPENDENT_ERASE`.

**Repair the completed v4/v4.1 sheet first:** open it in Drafting and run
`SealSectionDraftRefreshTest.vb` v2.2. It uses the existing metadata and curves;
no new sheet or intersection features are needed. It preserves each CSYS origin's
current drawing position while updating the view bounds.

After the final view updates, both journals query visible objects and reject
any returned model curve that does not belong to that sector. This is a guard
against cross-sector leakage, not proof of completeness: NX may report drafting
proxies instead of model curves and exclude clipped/hidden objects from the query.
Check the profiles visually, update the views again, and verify the exported
DWG/DXF. V4.2/v2.2 compile against NX 2406; this display fix needs live verification.

The reported v4 run generated curves for all 30 sectors, then NX error 925035
stopped drawing sector 7 because `SECTOR_<timestamp>` was already a view name.
The batch rolled back. V4.1 checks existing general/modeling/drafting view names
and adds a suffix when needed: `SECTOR_111`, `SECTOR_111_2`, `SECTOR_111_3`, etc.
It also retries specifically on NX 925035 if NX reserves a name not enumerated
in those collections. Existing views are never renamed or deleted. Chosen names
are logged. Refresh v2.1 uses the stored batch ID and CSYS timestamp for these
views, so a suffix (or manual view rename) does not lose the sector association.

Assign the **actual source bodies** to category layers before running the journal:
for example layer **1 = body panels**, **2 = door panels**, **3 = seals**. This
works even when layer 1 is also the current work layer. The source is `Body.Layer`,
not the work layer, feature-group name, or the layer of an arbitrary face.

**Layer separation is optional.** You may leave every source body on layer 1
(or any one common layer). The journal creates one intersection per sector for
that source layer, assigns all its curves to the same layer, and isolates each
view using its own curve objects. It does not require different layers for
different bodies or sectors. In this case DraftSight cannot distinguish panel
categories by layer, because the input did not distinguish them either.

Input prompts, selection instructions, dimension guidance and buttons in the
group picker are shown in both English and Japanese. The four `3D_to_Draft` VB
journals also have bilingual input dialogs and input-validation guidance.
They are saved as UTF-8 with BOM so Japanese text is preserved by the Windows
journal compiler. Diagnostic logs and NX-provided exception text are not fully
translated. The refresh journal requires no input; its start-up guidance is bilingual.

Each sector now has a separate associative intersection feature for each source
layer. All curves returned by that feature receive that layer number. A feature
containing several sheet/solid bodies is handled body by body during collection;
bodies on different layers enter different buckets. Overlapping categories stay
separate. No proximity-based guess is used to assign curve ownership.

Views enable the relevant category layers and explicitly exclude every other
displayable object on those layers. This includes other sectors' curves, unrelated
model curves, and disk construction. Bodies remain excluded within each view.
Global body visibility, source-body layers and the work layer are unchanged.
The old requirement for one empty layer per sector is removed.

Features store `SEAL_BATCH_ID`, `SEAL_SECTOR_TIMESTAMP` and `SEAL_SOURCE_LAYER`.
Views store the batch ID, timestamp and `SEAL_LAYER_MODE=SOURCE`. These identify
the output without mixing separate runs using the same CSYS group.

**Regenerate with v4 to obtain inheritance.** Refreshing a v1-v3 drawing repairs
its display but cannot recover source categories from its combined intersection
features. The generator creates a new sheet; it does not replace old sheets.

After a geometry edit that changes output entities, run the updated refresh
journal on the generated sheet. It reads the current feature outputs, reapplies
their recorded category layers and rebuilds view exclusions. If source bodies
are moved to different layers, or group membership changes, regenerate the batch;
layer classification is captured at generation time, not dynamically linked.
New unrelated objects added later to category layers also require a view refresh.

DWG/DXF translation is a separate step: verify the exported entity layers in
DraftSight. This change does not create alphabetic layer names or EntityGroups.

The diagnostic confirmed that FACE 1595 and FACE 71 each have a checked point
on their trimmed boundary inside the 60 mm sphere. It did not explain why NX
Intersect and Split Body report no overlap. This test avoids those two operations
rather than treating their conflicting errors as proof of an outside face.

## Why circle mode gives the former sphere section

For the CSYS XY plane through the sphere center, the portion of that plane inside
the sphere is a disk of the same radius. Therefore:

`sheet ∩ sphere-volume ∩ CSYS-XY-plane = sheet ∩ bounded-disk`

In circle mode the journal creates an associative circle and a Bounded Plane feature, then uses
**Intersection Curve** between that bounded face and the faces of source sheet
or solid bodies. For solids this produces surface section contours, including
interior walls where intersected, not filled areas or automatic hatching.
It produces local section curves directly. There is no 3D sphere, extracted
panel copy, Boolean Intersect, Split Body or Trim Sheet operation in this test.
The original sheets are only reference inputs and are not modified or hidden.

This equivalence assumes the section plane passes through the sphere center,
as in the requested workflow. It would need adjustment for an offset plane.

## Selection and inputs

1. Source body feature group, for example **SURFACE**. Nested groups are included.
   Both sheet and solid bodies are accepted. Every body returned by each feature's
   `GetBodies()` is inspected, including multi-sheet offset results. Duplicate
   references to the same body tag are removed; different overlapping bodies are
   not merged or discarded. Assembly occurrence bodies remain unsupported.
2. CSYS feature group, for example **CSYS**. All active Datum CSYS features are
   sorted by ascending feature timestamp.
3. Cutting shape: **Square / 正方形** (default), then **Circle / 円**.
4. Square full side length, default **50 mm**, or circle radius, default **25 mm**.
5. Drawing width, default **5000 mm**.
6. Drawing height, default **5000 mm**.
7. Gap between sectors and at sheet edges, default **10 mm**.

The journal builds the curves and creates the drawing automatically. It uses
1:1 views, CSYS X horizontal, and CSYS XY as the view plane. Views contain only
their sector's output curves. Rows fill left to right, then top to bottom in
CSYS timestamp order. Each cell reserves a square side or circle diameter plus
the gap. With 30 sectors, side 120 (or radius 60), gap 10 and a 5000 mm width,
a 30 × 1 grid is expected. Insufficient drawing
space is rejected before model creation.

Version 2 disables hidden-line processing for model curves and hides work-part
bodies in each generated view's own hidden-object list before committing it.
After layer isolation, each drafting view is explicitly updated before locating
the CSYS origin in the grid, and updated again after placement. Global body
visibility is unchanged. Section geometry is updated before entering Drafting,
and only the newly created views are regenerated at the end.

All selected sheets and solids in each source-layer bucket are included in its associative face rules; the
journal does not exclude them using the disputed distance test. Body-face rules
resolve the faces of the selected bodies on update. The circle uses three smart
points tied to the CSYS and a shared radius expression, whose name is logged.
The bounded face references the circle feature, and the intersection references
the selected source bodies and bounded disk.

## Checks and limitations

- Before processing the panel, temporary center-axis intersections check the
  selected boundary. Square checks X, Y and diagonal lengths; circle checks its
  diameter. The temporary planes and curves are undone. Failure stops the journal.
- Square edge lengths and center containment, or circle center, radius,
  circumference and center containment, are checked. Output curves are sampled
  at 33 parameter positions to reject obvious out-of-plane or out-of-boundary
  output. This sampling is a sanity check, not a
  proof of whole-curve containment or completeness.
- The bounded-face Intersection Curve operation supplies the geometric clipping.
  The analytic diameter test checks this behavior on a simple case in the running
  NX version; it does not establish correctness for all imported panel geometry.
- Feature errors/warnings stop and roll back the batch. A successfully committed
  feature returning no curves contributes no curves to the sector. If NX throws
  during a layer intersection, the local attempt is undone first. The journal
  then measures every source face against the bounded disk face using
  `AskMinimumDist3` level 2. It skips that category only if all distances exceed
  0.01 mm plus reported accuracy, the witness coordinates agree with each distance,
  and both witnesses lie on their respective trimmed faces. A failed query,
  contact within tolerance, or inconsistent witness preserves the original error
  and rolls back the batch. The NX error alone never classifies a panel as outside.
  This check compares surfaces for solids, not the solid's interior volume.
  Categories skipped as empty have no intersection feature; regenerate if later
  edits bring them into the disk. A fully empty sector leaves its grid slot empty.
  Coplanar face overlap, which is an area rather than a unique section curve, may
  require inspection. Distance checking after a failure can be slow on large parts.
- Existing feature associations support updates, but adding/removing input-group
  members or changing grid dimensions/order requires regenerating the batch.
  Changing radius updates disk geometry; grid placement/capacity is calculated
  at generation time, so regenerate the layout when the radius changes.

## Verification status

The user's NX run passed the disk diameter test (119.99999999999966 mm) and
generated curves for all 30 sectors, in timestamp order 111 through 140. The
run then failed while positioning drawing sector 1 because `MapModelToDrawing`
requires a two-element output array. That array declaration has been corrected
from `drawingPoint(2)` (three elements in VB) to `drawingPoint(1)` (two elements).
The failed run rolled back the batch. Drawing placement and final output still
require another live run and visual verification.

Compiled against the installed NX 2406 assemblies, without System.Core. The
existing FormUtilities deprecation warning remains. Direct execution through
the available desktop connection has not been possible.

The user subsequently generated the drawing but observed incomplete curve
display, corrected manually by hiding bodies and updating a view. Version 2's
curve-only display changes address this symptom; live verification is pending.

The user confirmed that the version-2 disk workflow works. Version 3 adds solid
body eligibility and input audit logging. It reports the number of bodies
returned by each feature, the unique accepted count, and sheet/solid totals.
The pre-existing all-bodies collection also handles multi-sheet features.
The new solid-body path is compile-checked but still needs a model test, ideally
with a solid containing a hole and a multi-sheet offset feature.

Expected current log heading: `=== DISK SECTION BATCH TEST v5 (SQUARE / CIRCLE) ===`.
Version 5 retains source-layer inheritance and the confirmed v4.2 view-erasure approach.
The generator and refresh journal compile against installed NX 2406 assemblies;
the new geometry/display paths have not been executed in NX here.

For the first v4 acceptance test, use two CSYS and bodies on layers 1 and 2,
with the work layer also set to 1. Include an unrelated model curve on layer 1,
a solid with a hole, and a multi-sheet feature if available. Check each output
curve's layer; both views must show only their own section and no construction
geometry. Keep source bodies visible and update both views. Verify an absent
category is either reported as verified empty or fails with a diagnostic, never
silently treated as outside. Modify a source or CSYS, update and run refresh;
check layers and isolation again. Export a small sample and verify layer locking
in DraftSight before exporting all 30 sectors.
Compare sector 1 with a manual XY section restricted to a
60 mm radius, including the locations near FACE 1595 and FACE 71. Verify no
missing or extra segments and test updates after moving a source CSYS before
treating the test as production-ready.
