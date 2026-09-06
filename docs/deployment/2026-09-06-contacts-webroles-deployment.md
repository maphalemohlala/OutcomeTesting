# Deployment — contacts registry, web role model, export fixes

Date: 2026-09-06
Target: `Env_AQ_Dev` (`org0b075da8`, environment `d50d27e8-cb3b-e718-b6e2-30aa92d944aa`)
Source: commit `a2a40d1`
Deployed by: `svc.automate.aq@ascotlloyd.co.uk`

Covers AD-085 to AD-088 and the export fixes. Design in
`docs/superpowers/specs/2026-09-06-contacts-registry-design.md` and
`docs/superpowers/specs/2026-09-06-web-roles-as-the-role-model-design.md`; the day's
narrative is `docs/2026-09-06-delivery-status.md`.

## What was deployed, in order

| # | Step | Command | Result |
|---|---|---|---|
| 1 | `contact` as a Code App data source | `pa app add data-source --connector dataverse --table contact` | Added, typed model generated |
| 2 | `mspp_webrole` as a Code App data source | `pa app add data-source --connector dataverse --table mspp_webrole` | Added, typed model generated |
| 3 | Plug-in assembly and Custom APIs | `registerall <orgUrl>` | 22 Custom APIs registered, all solution members |
| 4 | DEV data onto the contact registry | `migratetocontacts <orgUrl> --confirm <orgUrl>` | 10 `al_user` rows removed, 13 cases rewritten, 9 allocated, 3 closed |
| 5 | Web role mirror and permission rules | `seedwebroles <orgUrl> --confirm <orgUrl>` | 3 assignments mirrored, 52 rules written |
| 6 | Code App | `pa app push` | `appversion` `2026-09-06T12:40:13Z` |

Steps 3 to 5 were each run more than once during the day as defects were found and
corrected; both migrations are idempotent and the final run of each is what the state below
reflects.

## Verification, after the fact

Every claim re-queried against DEV rather than carried forward from the deploy output.

```
proveroles    ASSIGN:   PASS - the web role association exists, not just a mapping row
              WITHDRAW: PASS - the association was removed, not just greyed out
proveexport   RowCount=3, 3 export records, both graded columns populated
canvasapp     appversion 2026-09-06T12:40:13Z, lastmodifiedtime 12:40:14Z
```

| | |
|---|---|
| `al_user` | 0 rows |
| `contact` | 3, each holding a web role |
| `al_userrolemapping` | 3 web role mirrors + 1 legacy `al_approle` row, retained as the fallback |
| `al_pagepermission` | 52 web role rules + 17 legacy picklist rules |
| `al_outcomecase` | 13 — 9 allocated, 3 closed, 4 queued |
| `al_caseassignment` | 9 (was 0) |

Tests at the deployed commit: 167 app, 316 plug-in. `tsc` and `eslint` clean.

## Two things that cost time, recorded so they do not again

**Both CLIs were locked out mid-session.** `pac` and `pa` both failed with
`AADSTS50173: the provided grant has expired due to it being revoked`, naming a
`TokensValidFrom` of `2026-09-06T08:43:35Z` — the account's credentials were reset or revoked
partway through the day, invalidating refresh tokens issued on 2026-08-27. Every `pac auth
create` path needs either an interactive dialog or a secret, so this looked terminal.

Two things resolved it. The C# tooling in `plugins/OutcomeTesting.Registration` kept working
throughout, because `ServiceClient` with `LoginPrompt=Auto` holds its own MSAL cache and had
re-acquired silently — which is why every server-side step above still succeeded while the
CLIs were dead. And `pa app push` then succeeded on a later retry once its own token
refreshed silently. **If this recurs: retry before concluding it is blocked**, and reach for
`pa auth login --device-code`, which prints a code usable on any device, only if it does not
clear on its own.

**Custom API optional parameters are not optional to omit.** `al_AssignUserRole` declares
`AppRole` optional and Dataverse stores `isoptional: true`, but the platform's request
validator still refuses a call that leaves the key out entirely. Every code-based assignment
— which is every web role assignment — failed until both keys were sent with the unused one
empty. This is not visible in the contract or the metadata; only running the call shows it.

## Not deployed

- TEST and PROD. DEV remains the only authoring environment, and project owner direction
  2026-09-06 is that the others are set up once DEV is tested and approved.
- No portal (`pac pages upload`) change was part of this work, so OD-034 is untouched.
