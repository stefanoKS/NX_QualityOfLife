# Investigate the conflicting sphere/face result

Run `SealSectionFaceDiagnostic.vb` in the same millimeter work part after the
failed batch has rolled back. There are no selections or dimension prompts.
Read the output between `SEAL FACE DIAGNOSTIC v1` and `END DIAGNOSTIC` in the
Listing Window.

This journal only queries existing geometry. It does not create, modify, hide,
delete or save model objects. It opens and writes to the Listing Window.

The defaults reproduce the supplied report:

- Source body `UNPARAMETERIZED_FEATURE(1)`.
- Datum CSYS timestamp `111`, with a check against the reported center.
- Sphere radius `60 mm` (used mathematically; no sphere is created).
- Face label `1595` (latest failure), `71` (earlier failure), and `66`
  (successful control from the latest log).

Face labels come from the journal identifiers, not the unstable index returned
by `GetFaces()` or the session-specific tag. Missing identifiers are reported;
the journal never substitutes another body, CSYS or face.

For each face the report compares:

1. `AskMinimumDist3`, using the explicit CSYS origin coordinates.
2. `Evalsf.AskMinimumFaceDist`, whose documented result respects face boundaries.
3. `AskPointContainment` for both returned witness points on that particular face.
4. The distance calculated directly from each witness coordinate, compared with
   the API's reported distance.

The distinction between the general distance query and the bounded evaluator
is being investigated, not assumed to be the root cause. If the bounded query
and containment test confirm a point inside the sphere, ignoring that face
would omit potentially relevant geometry. The diagnostic does not fix a
Boolean failure, nor does a single closest point prove whole-face containment.

API contracts were checked against the installed NX 2406 XML/assemblies and
the published [UF surface evaluator reference](https://www.ugapi.com/doc/ufun/uf_eval/global.html#UF_EVALSF_ask_minimum_face_dist)
and [UF point containment reference](https://www.ugapi.com/doc/UFun/uf_modl/global.html#UF_MODL_ask_point_containment).
The diagnostic compiles against NX 2406 without System.Core or WinForms. It has
not been run on the user's model through the available desktop connection.
