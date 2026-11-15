# Refresh existing disk-sector views

Open the generated sheet in **Drafting**, then run
**SealSectionDraftRefreshTest.vb**. There are no selections or dimensions to enter.
The journal reuses the existing section curves and views; it does not regenerate
sectors or create another drawing sheet.

The same v2.2 refresh also supports generator v5 square sections: it works from
the sector feature outputs and metadata, independent of the cutting boundary's
shape. It does not change the square width or circle radius or rebuild the grid.

**Version 2.2 repairs the v4/v4.1 shared-layer visibility problem.** The old
generator completed but showed other sectors' curves in each view. This repair
uses NX's `DependentDisplay.Erase` for individual objects rather than relying
on the component-oriented hidden-object builder to exclude model curves.
Expected heading: `=== SEAL SECTION DRAFT REFRESH v2.2 (VIEW ERASURE) ===`.

The reported symptom was incomplete section curves that reappeared after hiding
the panel bodies and updating the drafting view. The generation script had
enabled hidden-line processing for model curves (`IncludeModelCurvesOption.Yes`).
The installed NX 2406 API documentation defines this as processing the curves
for hidden-line removal, not merely including them in the drawing. The symptom
suggests interaction with bodies and/or the view's cached display calculation.
The precise internal NX cause has not been reproduced independently.

This repair:

- Disables hidden-line processing for model curves in the affected views
  (`IncludeModelCurvesOption.No`). It does not remove the curves from the view.
- Adds all work-part bodies to each affected view's hidden-object list, leaving
  global Modeling visibility unchanged.
- For v4 source-layer drawings, finds current intersection outputs using batch
  ID and CSYS timestamp attributes. Reapplies each feature's recorded source layer
  (including new curves created by geometry updates), enables those layers and
  applies view-dependent erasures to other non-body objects on them. Own-sector
  curves have their erasures removed. These are display edits only; no curve
  geometry is deleted or globally blanked. All curve layers are restored before
  any view's exclusion list is built.
- For v1-v3 drawings, retains the single-sector-layer repair behavior.
- Explicitly regenerates view bounds and hidden-line display.
- Preserves the CSYS origin's existing drawing position as view bounds change.
- After the final regeneration, queries visible objects and rejects returned
  model curves outside the sector's recorded output set. This check does not
  prove profile completeness, and drafting proxies may not be model-curve tags.

Only generated Base Views on the current sheet are eligible. Refresh v2.1 reads
the stored timestamp from v4/v4.1 views rather than parsing their names; suffixes
such as `SECTOR_111_2` and manual renames are supported. Intersection features
with batch metadata are also recognized after a manual rename.
The timestamp must resolve to an existing Datum CSYS. V4 views must have matching
batch/sector metadata and current output curves. Legacy views must have one
visible layer matching a `SEAL_DISK_SECTOR_...` Intersection Curve feature, and
their names must still be exactly `SECTOR_<timestamp>` because they lack metadata.
Unexpected states stop the repair rather than guess ownership.
Failures after editing starts undo this repair's view/layer changes; existing section
geometry remains intact. Already-hidden bodies remain hidden globally, and
already-visible bodies remain visible globally.

For v4 this rebuilds the body's hidden-object list and reapplies curve erasures
intentionally; manual erasures on own-sector curves are removed to restore the
generated curve-only definition. Other view-dependent style edits are not cleared. It does not
unblank globally hidden curves. The recorded source layers reflect generation
time: moving source bodies to other layers requires regenerating the batch.
This repair does not convert old single-layer output into inherited-layer output.

Both the v2.2 repair and v4.2 generator compile against NX 2406. Live verification
of source-layer isolation is pending. After running the repair, check all profiles, show the panels in
Modeling if they were manually hidden, return to Drafting and update the views.
The curves should remain complete and the views should remain aligned in their
original grid. This is the regression test for the reported visibility problem.
