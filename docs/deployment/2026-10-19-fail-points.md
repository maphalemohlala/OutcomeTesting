# File Quality fail points in the Question library, and no side stripes - 1.0.29.0

Commits `f90e3d8` (fail points, AD-233), `2822d62` (side stripes, AD-234).

- The Question library has a **File Quality - Fail points** block: add, reword, reorder,
  retire and reinstate. No delete.
- A retired point is not part of any check in progress, ticked or not. The portal check page,
  the app review screen and the completed-check PDF leave it out. A new tick on one is refused.
  It owes no remedial action, and the submit unticks it before the review locks. A submitted
  review keeps its ticks.
- Cards, notices and labels lose their coloured side stripe in the Code App and the portal.

Tests: 1,849 plug-in tests and 1,382 app tests passed.

## DEV (`org0b075da8`)

- `pushassembly`: 402,432 bytes, sha256 `e8ceb035f982d51e...`, read back identical.
- `pushwebtemplate` OT Review Detail (258,786 chars) and `pushwebfile` outcome-testing.css
  (66,659 bytes). Before the first push both matched the repository's HEAD.
- Code App: `index-Cee28nZ4.js`, `pa app push`.
- Version PATCH to **1.0.29.0** and managed `ExportSolution`, both by the agent. The package
  (616 files) carries the plug-in with the same sha256, the new page rule, the new stylesheet
  and `index-Cee28nZ4.js`. `brandpackage` made the OTIS copy (7 labels, 612 files identical).
- **Not checked on screen:** the saved portal session had expired, and the two sign-in windows
  opened for a new one closed unused.

## TEST (`org37995f36`)

- `ImportSolutionAsync` by the agent: **1.0.29.0** (06:42:05Z), all 35 steps enabled, plug-in
  sha256 `e8ceb035f982d51e...`, Code App republished 06:41:43Z.
- **Still old on the portal:** OT Review Detail and outcome-testing.css. Both have an unmanaged
  layer from earlier direct pushes, which hides the managed import. The import updated them
  (modified 06:41:45Z) but TEST still serves the previous versions. Re-pushing them was refused
  to the agent ("Modify Shared Resources"), so **the owner runs**, from the repo root:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
$tool='plugins\OutcomeTesting.Registration\bin\Debug\net8.0\OutcomeTesting.Registration.dll'
$web='powerpages\outcome-testing---outcometesting'
dotnet $tool pushwebtemplate 'https://org37995f36.crm11.dynamics.com' a1000000-0000-4000-8000-00000000001b "$web\web-templates\ot-review-detail\OT-Review-Detail.webtemplate.source.html"
dotnet $tool pushwebfile 'https://org37995f36.crm11.dynamics.com' a1000000-0000-4000-8000-000000000050 "$web\web-files\outcome-testing.css"
```

  Until then TEST's check page still shows a retired fail point that a check in progress had
  ticked. The server side is already live there: the submit unticks it and owes no remedial
  action for it.

## PROD / OTIS (`org3461d426`)

- `ImportSolutionAsync` by the agent: **OTIS 1.0.29.0** (06:46:53Z), all 35 steps enabled.
- Read back: OT Review Detail carries the new rule, outcome-testing.css has the tile dots and no
  stripes, plug-in sha256 `e8ceb035f982d51e...`. PROD has no masking layer on either.
- The Code App version read was refused ("Production Reads").
