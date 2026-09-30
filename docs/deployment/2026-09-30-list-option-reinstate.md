# Reinstate on Dropdown options - fixed, DEV and TEST (1.0.15.0)

Date: 2026-09-30
Commit: `6cf25b5`

## What was wrong

On the Code App's **Dropdown options** page, pressing **Reinstate** on a retired option did
nothing: no error was shown, the page reloaded, and the option still read Retired. It was
reported on TEST, where "Test 1" had been retired at 08:49Z that day.

`reinstateListOption` sent `{ al_effectiveto: undefined }`. The Power Apps client serialises an
update with `JSON.stringify`, which drops an undefined key, so the PATCH body was `{}`.
Dataverse accepted it and changed nothing. The **Change** form had the same fault: a blanked
start date, retired date or sort order was never cleared.

## The fix

Cleared values are sent as `null`. `optionUpdateFields` and `REINSTATE_FIELDS` in
`app/src/features/admin/listOptions.ts` shape the update, and
`listOptionWrites.test.ts` checks the serialised body (5 tests; they fail before the fix).

## Proved in DEV (Web API, as `svc.automate.aq`, on throwaway options, deleted afterwards)

| Request | `al_effectiveto` afterwards |
|---|---|
| Create, retired 2026-09-30 | `2026-09-30` |
| `PATCH {}`, the old Reinstate | `2026-09-30`: 204, but still retired |
| `PATCH {"al_effectiveto": null}`, the fixed Reinstate | `null`: offered again |

The browser click itself was not driven, because the saved session no longer opens the Power
Apps player. Code App `index-DbzPS1uy.js` was pushed to DEV.

## TEST - 1.0.15.0

DEV bumped to 1.0.15.0 and exported managed (`artifacts/2026-09-30-test-promotion-b/`); the
package carries `index-DbzPS1uy.js` and the same plug-in assembly (`f05b55d1...`).

`scripts/Import-Solution.ps1` **reported "Import complete" but imported nothing**: `pac` could
not connect (AADSTS50173, tokens revoked 06:38Z) and still exited 0, and the step check then
passed against 1.0.14.0's steps. So the solution version must be read back after every import.
It was re-imported through the registration tool's `webapimany` as `ImportSolutionAsync`
(`PublishWorkflows: true`); job `1eecab29-b4bc-f111-aaae-6045bd0aeb46` succeeded (statuscode 30).

| Check (read back from TEST) | Result |
|---|---|
| Solution | 1.0.15.0 managed |
| Plug-in steps | 33, none disabled |
| Code App `appversion` | 2026-09-30T09:49:31Z |
| Portal components vs DEV, by id | 293 of 297 identical; the rest are TEST's own `Authentication/*` settings, plus `AllowContactMappingWithEmail` and "New Role", which exist only in TEST |

"Test 1" is still retired on TEST. It can now be reinstated from the page.
