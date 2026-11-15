# Batch seal sections test

Run `SealSectionBatchTest.vb` in NX Modeling with a displayed millimeter work part.
This is a testing journal. The Split Body recovery revision has been compiled
against the installed NX 2406 assemblies; it has not yet been validated on the
door model. The available desktop connection did not expose the NX session.

**Current test status:** the user's latest run verified the sphere but Split Body
also failed with NX 670030 on FACE 1595 while the measured distance was 58.160543
mm for a 60 mm radius. This batch revision is not a verified solution. Before
another full batch run, use `SealSectionFaceDiagnostic.vb` to investigate the
conflicting proximity and cropping results. See `SealSectionFaceDiagnostic.md`.

The diagnostic has now confirmed on-face boundary points inside the sphere for
FACE 1595 and FACE 71, with agreement between both distance APIs. The proposed
next test is `SealSectionDiskBatchTest.vb`, which creates local intersection
curves with a bounded disk instead of cropping sheets with the sphere. This
original Boolean/Split version remains unresolved.

## Input order

1. Select the **surface feature group**. Nested groups are included; unique sheet
   bodies in the work part are used.
2. Select the **CSYS feature group**. Datum CSYS features are sorted by ascending
   feature timestamp.
3. Enter the **sphere radius**, in mm. The diameter is twice this value.
4. Enter drawing width and height, each defaulting to **5000 mm**.
5. Enter the gap between cells and at drawing edges, defaulting to **10 mm**.

The journal creates the spheres, local sheets, section curves and drawing views.
There is no additional curve or sphere selection. Views use CSYS X horizontally
and the CSYS XY plane for sectioning. Only the sector's curves are displayed in
its drawing view. The grid fills left to right, then top to bottom, at 1:1 scale.
Each cell reserves one sphere diameter plus the gap. A large drawing can therefore
place all 30 sectors in one row. Insufficient capacity is reported before creation.

## Recovery from intersection gaps

Each generated sphere is checked against its actual analytic face geometry.
Its solid type, center and radius must agree with the requested values to
0.01 mm before cropping begins.

The first attempt intersects each source sheet separately with the sphere, with
Keep Target and Keep Tool enabled. If NX throws an exception, the attempt is
undone before recovery starts.

Recovery examines the source faces individually. The proximity filter excludes
a face when its minimum distance, reduced by the reported measurement
uncertainty, exceeds the radius plus 0.01 mm. Measurements use the NX 2406
`AskMinimumDist3` API with optimization level 2 and its accuracy output.
Each remaining face is extracted as an associative sheet copy and intersected
with the same sphere. The source is not hidden by Extract Face. The radius and
0.01 mm Boolean tolerance are unchanged. All resulting sheet bodies are passed
to the associative section feature.

This recovery can avoid problems involving connections between multiple faces;
it does not repair defective individual faces or guarantee a successful Boolean.
NX **670030** ("Tool body completely outside target body") is handled separately.
The unsuccessful Boolean is undone, then the actual target sheet is measured
again. The empty result is accepted only if the reported uncertainty is at most
0.01 mm and the distance minus uncertainty is at least radius minus 0.01 mm.
This allows no-overlap and boundary-contact results within the modeling tolerance.
The unused extracted copy is also undone, preserving prior successful faces.
The log records each accepted empty result. A source with no local output is
skipped; a sector with no local output retains an empty grid cell.

If that check disagrees or is inconclusive, the face is not skipped. A conflicting
NX 670030 or an intersection-gap error (NX 670362) on a single-face copy now
triggers **Split Body recovery** after the failed Boolean has been undone:

1. If the copy's exact bounding-box corners all lie strictly inside the sphere,
   retain the complete associative copy. This sufficient containment test avoids
   requesting a split where the sphere boundary never crosses the sheet.
2. Otherwise, split the associative copy using all the sphere's faces as the
   cutting tool. The original source sheet is never a Split Body target.
3. Require a successful feature without reported errors/warnings and at least
   two sheet pieces. Classify each piece of this completed split by its minimum
   distance to the sphere center, including the reported measurement uncertainty.
4. Retain all pieces with a point more than 0.01 mm inside the sphere. Exclude
   exterior/boundary pieces at that tolerance from the section inputs. All split
   pieces remain hidden in Modeling; excluded pieces are not deleted from the
   Split Body feature. Ambiguous results abort rather than guess.

This uses a different modeling operation; it is not a guaranteed repair for
defective geometry. If Split Body also fails, the batch rolls back. No larger
tolerance, changed sphere radius or skipped conflicting face is used to force
success. The log records the sector,
source, face index/tag/identifier, measured distance, and NX error code when
available. No unresolved face is silently omitted.
Recovery may take longer and create more features than whole-sheet intersection.

## Verification in NX

- First rerun the same inputs that produced the intersection-gap error. Look for
  `Sphere geometry verified` and then `Split Body recovered ... interior pieces`
  or a precise failure in the Listing Window. For the reported failure, use the
  same groups and a 60 mm sphere radius; sector 1's face 348 was measured at
  58.727354 mm from the center and must not be accepted as wholly outside.
- Confirm the original sheets remain present and unchanged, and compare a
  recovered sector against a manual section at the same CSYS and sphere radius.
  Check for missing, duplicate or out-of-radius curves, including disconnected
  portions of the same sheet and sheets wholly inside the sphere.
- Confirm the drawing contains curves only, at 1:1, in ascending CSYS timestamp
  order with the CSYS origins at the cell centers.
- Retest an ordinary successful sheet, a wholly outside sheet, boundary contact,
  a wholly inside face and a face with multiple disconnected interior regions.
  Only a distance-confirmed NX 670030 may be skipped after a Boolean failure.
  A conflicting distance must enter recovery, and unsuccessful recovery must
  roll back the batch rather than omit geometry. Confirm that empty attempts
  leave no unused extracted sheet behind.

The distance-based inclusion decisions are made when the journal runs. Created
features retain their NX associations, but a later radius/CSYS/source change
does not automatically rescan excluded bodies or faces. For changes that alter
which geometry belongs in a sector, regenerate the batch; dynamic reselection
is not implemented in this test.
