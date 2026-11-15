# Layers and editing workflow

Version 4.2 of SealSectionDiskBatchTest implements numeric source-layer inheritance,
separate intersections per source layer and view-dependent erasures for isolation.
V4/v4.1's HiddenObjects-based curve exclusions failed in the user's run; use the
v2.2 refresh journal to repair those completed drawings. Layers identify panel
categories; individual view erasures separate sectors sharing those categories.

An NX object has one numeric layer, not simultaneous category and visibility
layers. Reserving layers 1-199 for categories and 200-256 for sectors therefore
requires separate curve objects or moving the drafting curves to sector layers.
Separate copies introduce another associative chain and still need export
mapping to recover the category of each drafting curve. Moving the same curve
would replace its category layer. The current implementation keeps the category
layer and uses persistent view-dependent erasures for sector visibility.
The refresh journal supports this representation as well as legacy single-layer
views. Export mapping and DraftSight grouping below remain recommendations,
not implemented export features. See SealSectionDiskBatchTest.md for testing
instructions and the current verification limits.

## Source layer versus work layer

The useful source property is `Body.Layer`: the layer on which that parent body
currently resides. `Part.Layers.WorkLayer` is the active destination for newly
created objects; it does not identify which panel a curve came from. A feature
can return several bodies, and those bodies can have different layers.

Assign source bodies to meaningful category layers before sectioning. Example:

| NX body layer | Intended DWG layer name | DraftSight group example |
|---|---|---|
| 1 | BODY_PANEL | A-A_BODY_PANEL |
| 2 | DOOR_PANEL | A-A_DOOR_PANEL |
| 3 | SEAL | A-A_SEAL |
| 4 | OTHER | A-A_OTHER |

These numbers and names are recommendations only. Existing company standards
should take precedence. If every parent is on layer 1, literal layer inheritance
cannot distinguish its engineering role. Source layers or a deliberate mapping
from feature groups/attributes must supply that information.

## Recommendation for DraftSight

Use category layers for visibility and locking across the drawing. Use a named
EntityGroup for each sector/category to select and move that local profile as a
unit. For example, unlock BODY_PANEL, select A-A_BODY_PANEL, and move it while
DOOR_PANEL and SEAL remain locked. Connected and disconnected entities can be
grouped without changing their geometry. Keep each such group on one category
layer so its editing behavior is predictable.

EntityGroups fit independently adjusted seal sections. Blocks are an alternative
when a defined insertion point or reusable symbol behavior is valuable; their
shared-definition behavior requires care if copied sections should later differ.
Use a unique block definition per sector/category if they must be independent.

Polyline joining solves a different problem: it creates connected geometry.
It should be optional and only applied to chains that actually connect. Do not
bridge gaps between different panels or approximate splines merely to make
moving a profile easier. EntityGroups already provide that selection behavior.

DraftSight's [EntityGroup documentation](https://help.solidworks.com/2024/english/DraftSight/html/chapter_groups.htm)
describes moving/rotating members together, and its
[layer documentation](https://help.solidworks.com/2024/english/DraftSight/html/hlpid_adjust_layer.htm)
describes locking to prevent editing. Its
[2020 feature matrix](https://www.draftsight.com/sites/draftsight/files/2020-06/DraftSight_Feature_Matrix.pdf)
lists layers, blocks and groups in Standard. Command/UI details depend on the
installed version. No DraftSight API, LISP or Mechanical-only feature is assumed.

## Implementation and remaining export work

The old single Intersection Curve feature combined every source category,
so it did not provide an unambiguous per-curve source-layer assignment.
Version 4 implements steps 1-4 below; step 5 remains an export acceptance task:

1. Partition source bodies by their actual layer, then create separate associative
   intersections per sector/source-layer bucket. Record category and sector
   metadata. Where body identity matters within a category, use per-body features
   as well. Avoid guessing ownership from nearest geometry; overlapping panels
   can make that ambiguous.
2. Assign the resulting curves to the relevant parent layer. Define behavior for
   buckets with no intersection explicitly; arbitrary NX errors must not cause
   a panel to be silently omitted.
3. Isolate views by their own output objects, not just layers. With shared category
   layers, other sectors' curves and unrelated curves on those layers must be
   excluded from each view. Continue the successful body exclusion and disabled
   curve hidden-line processing.
4. Store sector/category attributes on the output and view. Update the refresh
   journal to recognize this representation; its existing single-layer check is
   specific to versions 1–3.
5. Verify a small DWG/DXF export in the installed translator and DraftSight.
   Do not assume that NX modeling groups become DraftSight EntityGroups, or that
   model-layer inheritance automatically produces the intended drawing-entity
   layers. Confirm the mapping and grouping in the actual export.

The first export acceptance sample should contain two sectors, a body panel,
door panel and seal. Check layer locking, independent group moves, 1:1 units,
arc/spline fidelity and that no neighboring sector's curves appear in a view.
Automatic DWG grouping would be a separate export/postprocessing feature, not a
guaranteed consequence of grouping NX features.

One layer per sector/category (for example A-A_BODY_PANEL) is an alternative when
each sector must be lockable independently using only layers. It creates many
layers and, inside NX, competes with the 256-layer limit. Category layers plus
sector/category groups scale better for the intended repeated-section workflow.

Layer assignment is initially a generation-time choice. Moving a source body to
a different layer later would need an explicit synchronization/rebuild operation;
geometric associativity alone does not guarantee that display layers follow it.
