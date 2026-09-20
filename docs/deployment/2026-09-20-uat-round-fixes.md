# Four defects found by running the UAT checklist, and their fixes

**Date:** 2026-09-20
**Environment:** `Env_AQ_Dev` (`org0b075da8.crm11.dynamics.com`) only. Promotion to TEST/PROD is a separate decision.
**Branch:** `feat/change-batch-sep-2026`
**Commits:** `85957cf` (F4), `95274b7` (F6), `c423d11` (F7), `49dc593` (F5)
**Decisions:** AD-176, AD-177, AD-178, AD-179

---

## How they were found

A cached portal session made the browser rows of `docs/testing/e2e-checklist-portal.md`
reachable for the first time. All four are defects the command-layer testing could not
have found, because all four are about what reaches the page.

Two further suspicions were investigated and **dropped rather than filed**, which is worth
recording because both looked serious:

- *A contact with no web role can read every case.* The portal's own diagnostics
  (`RTPEWE08`) showed the signed-in account holds five job roles plus Administrators. The
  access was legitimate. PRT-005 still needs a genuinely role-less contact.
- *User-scoped views never match.* `/cases?mine=1` and My Work both returned 0 while the
  user demonstrably held a review. It was the AD-094 cache; both were correct nine minutes
  later. This became F5.

One suspected blocker was the test method, not the product: a mandatory multi-select
renders as unnamed checkboxes (`input.cc-box`), which a radio-only sweep missed.

---

## F4 — the checker empty states never rendered

Both checker cells came out **completely empty** on the case list and on case detail —
no wording, no markup. This site's Liquid takes the TRUE branch of
`{% if <null attribute> != blank %}` and then renders the null as nothing, so neither
"Not yet allocated" nor "No check of this type" was ever reached.

The drift test added with AD-173 passed throughout. It asserted the two sentences were
present in the template source. They were — in branches nothing could reach. **A test that
reads the source is not a test that the feature works**, and this is the cost of that
distinction. The test now asserts how emptiness is decided (AD-176).

Fixing the blanks exposed a second defect underneath. Case detail decided the two states by
counting review instances, so a Tax then AQS case whose AQS leg had not been claimed read
"No check of this type" while the case list read "Not yet allocated" about the same case.
A review row that does not exist yet is not a check nobody owes. Both surfaces now decide
from the route (AD-177).

Verified in DEV after upload:

| Case | Route | Tax checker | AQS checker |
|---|---|---|---|
| 900000003 | Tax then AQS | `svc automate aq` | Not yet allocated |
| 900000004 | Tax then AQS | Not yet allocated | Not yet allocated |
| 900000001 | AQS only | No check of this type | Service Account |

List and detail agree on all three.

## F6 — case detail showed the wrong identifier

The field labelled **IO reference** bound `c.al_casereference`, so it showed the TaskID.
Case 900000003 displayed `900000003` where `al_ioreference` holds `90000003-90000103`.
The import maps ClientRef correctly (APP-020); only this binding was wrong.

`al_ioreference` was not in the case fetch either, so the binding alone would have rendered
an em dash on every case — which reads as "this case has no IO reference" rather than as a
missing attribute. Both halves are now asserted.

## F7 — one person, two names

`al_AssignCase` stamped `al_taxcheckername` from the **systemuser** ("svc automate aq")
while a portal claim stamped `al_aqscheckername` from the **contact** ("Service Account").
Both appeared in the checker columns of one case list row, beside an Adviser column reading
"Service Account".

`ResolveAssignee` already retrieved the contact with `fullname` and discarded it. It now
carries it, and the allocation stamps it with the same `contact.fullname ?? user.fullname`
fallback `ClaimCasePlugin` has always used — which is what `StampCheckerName`'s own comment
already claimed it did (AD-178).

`UserName` is unchanged and still carries the Dataverse name, because AD-144's provisioning
refusal names the identity Dataverse itself would refuse.

## F5 — saved work that reads as lost work

Three answers saved on a Tax review, the page said "Saved 16:31", all three reached
`al_response` — and a reload a minute later rendered the form **completely empty**. They
appeared about nine minutes afterwards.

The cause is AD-094 and is not ours: the page reads `al_response` through Liquid, the write
is made by `AnswerRequestPlugin` as the application user, and a plug-in write never
invalidates the portal's data cache. This site has no setting that reaches it —
`Header/OutputCache/Enabled` and `Footer/OutputCache/Enabled` are the only cache settings
present, and neither governs the data cache. A cache-busting query string does not reach it
either; that was tested.

Nothing is lost, and a submit from a page that looks empty still succeeds because the check
reads the stored rows. What is wrong is that it **reads** as lost work, and the obvious
response — type it all again — is the worst one available.

The page now remembers which answers it saved, per review, per tab, and says so when the
server has not caught up. It does **not** re-apply the values (AD-179): an answer on screen
that the server did not send is indistinguishable from one it did, and a reviewer could not
tell which they were reading.

Verified in DEV across three reloads:

| Reload | Server rendered | Notice |
|---|---|---|
| straight after a save | nothing | shown, counting one answer |
| after the cache expired | the answer | hidden |
| one answer current, one just saved | the older one | shown, counting only the one behind |

The first live run read *"One answer… **They** are saved… enter **them** again"*, so the
wording now agrees in number.

---

## Deployment steps

| # | Step | Result |
|---|---|---|
| 1 | `npx vitest run` (app) | 668 passed |
| 2 | `dotnet test` (plug-ins) | 1296 passed |
| 3 | `pac powerpages download` → diff against local | equivalent: differences were a BOM, YAML quoting, web-role list ordering and table-permission **filenames**; every `adx_entitypermissionid` matched, so the upload updates by id |
| 4 | `pac powerpages upload --modelVersion Enhanced` | four uploads, 17–20s each |
| 5 | `dotnet build -c Release` then `pushassembly` | 293,376 bytes |
| 6 | sha256 of `pluginassembly.content` vs the local DLL | `e2ab2537…9384` — **match** |
| 7 | Live verification in DEV | the tables above |

## Left alone

- Case 900000003 still holds `al_taxcheckername = "svc automate aq"` from before the F7
  fix. It is seed data and re-stamping it would prove nothing.
- **F1** (a wrong `TargetId` on the remediation commands leaks a raw platform fault) is
  still open. It was found in an earlier round and is not part of this batch.
- **F8** is ASP.NET request validation, not ours, and nothing leaks through it.
