# Deployment — table read coverage, and a web template that had been emptied

Date: 2026-09-11
Target: `Env_AQ_Dev` (`org0b075da8.crm11.dynamics.com`)
Deployed by: `svc.automate.aq@ascotlloyd.co.uk`

Follows the `al_outcome` read permission deployed earlier the same day. That permission
unblocked the regrade panel (AD-111), the sign-off final outcome and the grade columns;
auditing *why* it had been missing found two more faults of the same class, recorded as
AD-115 and AD-116.

## What was deployed, in order

| # | Step | Command | Result |
|---|---|---|---|
| 1 | Restore `OT Review List` | `pushwebtemplate <orgUrl> …016 <source>` | 31177 chars written; content column had been null |
| 2 | Table permissions | `restoretablepermissions <orgUrl> <sitePath>` | 16 written — `Review Route - read` **created**, 15 updated unchanged |
| 3 | Solution membership | `addsitetosolution <orgUrl> OutcomeTesting` | site 1, languages 1, 254 components |

## Verification

Read back from the environment, not from the tool's own counts.

| Check | Method | Result |
|---|---|---|
| Table permissions, source vs deployed | `fetch` `powerpagecomponent` type 18, compared field by field including role bindings | 16, identical |
| Table read coverage | every `<entity>`/`<link-entity>` in `web-templates/` vs permissions granting read | all 12 queried tables covered |
| Web templates, source vs deployed | `fetch` type 8, `content.source` compared to `*.webtemplate.source.html` | all 16 byte-identical |
| Solution membership | `solutioncomponent` count, and the new id by query | 511 → 512; `…063` present as component type 10433 |
| Web roles and page rules | `fetch` types 10 and 11 vs `webrole.yml`, `webpagerule.yml` | 10 roles, 6 rules, identical |
| Permission relationships | `relationships` on the five tables the permissions name | all five resolve to real relationships |
| Release gates | `Check-ComponentIds.ps1`, `Check-PortalSecurity.ps1` | 242 ids clean; 11 assertions pass |
| Plug-in suite | `dotnet test` | 682/682 |

The new assertion was proved in both directions rather than only observed passing: with
`Review-Route---read.tablepermission.yml` moved aside the gate exits 1 and names
`al_reviewroute` and the three templates that query it; restored, it exits 0.

## Impact of what was fixed

`al_reviewroute` had no table permission at any point. Three consequences, all silent:

- **The AQS claim queue was empty for every user.** `OT Review List` joins the route
  `link-type="inner"` to test `al_requiresaqsreview`, so with the route unreadable the join
  returned nothing, `unassigned.size` was 0, and the claim card never rendered. No AQS
  checker could claim a case through the portal. Case IO-300002 (Desmond Achebe, AQS only)
  was waiting in that queue when this was found.
- **The route filter was inert** on the case and review lists. It fails safe: the page
  validates the requested route id against the fetched list, and an always-empty list means
  the filter is dropped rather than misapplied.
- **The route column was blank** wherever it is joined `outer`.

`OT Review List` holding a null `content` meant `/tax-reviews` and `/aqs-reviews` were both
drawing the layout around an empty body, since one shared template renders both.

## Still open, deliberately not changed

- **`al_signoff` read is Parent-scoped to `AL Portal - T&C Supervisor` alone.** The sign-off
  history on `/case-details` and on the remediation page therefore renders empty for the
  other six roles, including `AL Portal - Outcome Testing Manager`, whose role is oversight.
  BR-008 is still met — a rejection reopens the action and `SignoffProgressPlugin` emits a
  notification carrying the notes — so what is missing is the history view, not the telling.
  Widening a read is OD-022 territory and is the project owner's call.
- **`AL Portal - Planner` has no holders in DEV.** Seeding, most likely, but it means that
  path has never been exercised.
- **IO-SEED-TXA-01 sits at Awaiting Recheck with a grade and no final outcome.** It cannot
  be closed from here: the final outcome is the supervisor's judgement under BR-005 and the
  reason is mandatory under AD-031, so neither is derivable from the data. One supervisor
  action, on the regrade panel that now renders. IO-300005 was the other and was closed by
  the project owner the same day — see the round below.

## Full `Deploy-Portal.ps1` run

Run after the targeted steps above, as the reconcile. Both gates passed (242 ids; 11
assertions), 16 permission files were moved aside and 0 permission records needed stripping
from the manifest — it was already stripped from an earlier run, which is the state the
README says to leave it in. Upload succeeded in 41.7s; `restoretablepermissions` wrote 16;
verification by query reported **16 in source, 16 deployed, and nothing else**. Exit 0.

Re-checked afterwards rather than assumed: all 16 `OT *` web templates are still
byte-identical to source, `OT Review List` included. An upload is the thing most likely to
undo the restore in step 1, so that check is the point.

### One failed record, and why it is not a fault

The upload printed:

```
Updating table powerpagecomponent with record ID:f065878a-f8a9-f111-aaac-e4fade069307
FAILED due to Entity 'powerpagecomponent' With Id = f065878a-... Does Not Exist
```

That id is the `annotationid` in `web-files/outcome-testing.css.webfile.yml`, and the
manifest still tracks it under the web file content records. It is a **standard-model
leftover**: on this enhanced-model site the CSS bytes live in the component's `filecontent`
file column (`4977a36d-…`, `filecontent_name: outcome-testing.css`), not in an `annotation`.
Querying `annotation` for `objectid = …050` returns nothing at all, which is the enhanced
model behaving correctly rather than a missing file.

The stylesheet itself deployed: the component's `modifiedon` is `2026-09-11T08:40:31Z`,
written by this run. Nothing was lost, and the upload reported success overall.

It will print the same line on every future deploy. The fix is a manifest rebuild, which
`pac pages download` does truthfully — but a download also **re-arms the table-permission
section that `Deploy-Portal.ps1` deliberately strips** (see the README), so it is not worth
doing for cosmetics alone. Do it as part of the next deliberate download, not on its own.


## Second round, same day — what the first round's fixes exposed

Recording the final outcome on IO-300005 through the regrade panel, now that it renders,
surfaced three things. One was a real defect, one was a cosmetic defect, and one was the
form working as designed and being wrong about it.

| # | Step | Command | Result |
|---|---|---|---|
| 4 | Change tracking | `setchangetracking <orgUrl> <12 tables>` | 11 already on; `al_caseassignment` enabled and published |
| 5 | Stylesheet | `pushwebfile <orgUrl> …050 outcome-testing.css` | 44809 bytes |
| 6 | Remediation template | `pushwebtemplate <orgUrl> …019 <source>` | 88699 chars |

### The regrade had worked; the page had not caught up (AD-117)

Reported as "it showed that it was done successfully, but the status did not change". It had
succeeded in full — `al_finaloutcome` Pass, `al_regradedon` and `al_finalisedon` stamped,
case `Closed`, initial outcome preserved, and an audit event keyed
`portal-regrade-36d49fef…` written at 08:46:01Z, which is the portal path and not a direct
write.

The page reloads 800ms after the PATCH returns. That was too early. A regrade PATCHes
`contact.al_regraderequest`; `RegradeRequestPlugin` writes `al_outcome` and `al_outcomecase`.
**The portal did not write those rows**, so its render cache is not invalidated by the write
path — it learns through Dataverse change tracking (AD-094), which is polled. Change
tracking was verified enabled on every portal-read table while diagnosing this, so the
mechanism is present; it is simply not synchronous. The delay is now 2500ms and each of the
three write paths on the page says the view may not have caught up, because a longer delay
narrows that window and cannot close it.

### The Reason and Notes fields had no borders (AD-117)

`.ot-field input, .ot-field select` carried the border, padding and background and
`textarea` was never in the selector, so the regrade **Reason** and the sign-off **Notes**
drew as unmarked areas of page. Added, with a vertical-only resize. The `.ot-checklist`
textareas stay borderless — that component replicates a paper form.

### A settled check now keeps its rows (AD-118)

AD-114(c) collapsed a check whose every action had been approved. On a case where *all*
checks are approved that left the form with no numbered rows at all: IO-300005 showed only
"Every issue raised on this case has been remediated and approved", so the T&C Supervisor
recording the final outcome could not see the issue or fail reason behind any of the
twenty-three remedial actions being signed off. Reversed on the project owner's direction.
Every action is drawn, a closed check says so in its heading and is set back with muted
heading text, and its rows stay at full contrast because they are the record being checked.

Cheap to reverse because nothing in those rows is an input — the adviser's answers, the
sign-off and the regrade are separate panels below the grid, so a settled row was already
read-only and the only question was whether it was drawn.

### Verification

Liquid tag balance was checked explicitly before deploying, since removing an
`unless`/`endunless` pair is how a template of this size breaks silently: no unclosed tags,
34/34 comment pairs. Both gates passed. All 16 deployed templates re-verified byte-identical
to source after each push.


## Third round — the regrade that recorded and read as though it had not

| # | Step | Command | Result |
|---|---|---|---|
| 7 | Remediation, case detail, layout | `pushwebtemplate` x3 | 90555 / 30712 / 2156 chars |

Two faults, reported together against IO-300005.

**The portal drew the regraded outcome blank while the Code App showed the case closed.**
The row was correct throughout. The tell is that the cell drew *blank* rather than as a dash:
`{% if outcome and outcome.al_finaloutcome %}` passes on the object and `.label` returns an
empty string, because the platform does not always supply a formatted value for an option
set. `OT Outcome Label` exists for this and carries a numeric fallback, and only
`OT Case List` was using it. The four raw reads on `OT Remediation` and `OT Case Detail` now
go through it, passed `final` alone so they stay the regraded grade. The Code App reads
Dataverse directly, which is why it was never affected.

**The Reason and Notes borders had shipped twice and reached nobody.** `OT Layout` links
`/outcome-testing.css?v=N` precisely so a browser holding the old file picks up a change, and
the version was not bumped either time. Now `?v=11`, and the instruction in the layout says
to bump on every change rather than a "material" one.

Verification: Liquid balance checked across all 48 web templates (none unclosed, comment
pairs matched) before deploying; both gates pass; all 16 deployed templates re-verified
byte-identical to source, with `?v=11` and the include confirmed live by query.


## Fourth round — unknown tag 'endcomment'

The AD-119 change shipped with `{% if outcome.al_finaloutcome %}` written inside a Liquid
comment as an illustration, and the remediation page answered **unknown tag 'endcomment'**.
DotLiquid tokenises tags inside a comment block, so the unclosed `if` consumed the
`{% endcomment %}` — and the error names a tag that is not the problem.

The templates already wrote `(% if %)` with parentheses in prose for precisely this reason.
The convention was real and unenforced, so it held by imitation until a comment was added
without noticing it.

Both gates passed and the upload succeeded, because neither read the template as Liquid.
Assertion 12 now does (AD-120), and it was proved in both directions: reintroducing the brace
fails the gate naming the file, line and tag; removing it passes.

| # | Step | Command | Result |
|---|---|---|---|
| 8 | Remediation template | `pushwebtemplate <orgUrl> …019 <source>` | 90555 chars |

Verified after: all 16 deployed templates byte-identical to source, and the live
`OT Remediation` re-scanned from the environment — 0 tags inside comments, comment depth 0
at end of file.


## Fifth round — why the Reason box was still bare

| # | Step | Command | Result |
|---|---|---|---|
| 9 | Stylesheet | `pushwebfile <orgUrl> …050 outcome-testing.css` | 45812 bytes |
| 10 | Layout | `pushwebtemplate <orgUrl> …010 <source>` | `?v=12` confirmed live by query |

Reported as borders showing "only when you click on the field". That is the signature of a
base rule losing while the focus rule wins: what appeared on click was the focus outline, not
a border.

`OT Remediation` wraps its entire form in `.ot-checklist` — from line 400 to line 1041, so
the sign-off and regrade panels are both inside it — and that component strips its own fields
bare on purpose, because it replicates a paper form. `.ot-checklist textarea` and
`.ot-field textarea` match the Reason box at **identical specificity (0,2,0)**, and the
checklist block sits later in the stylesheet, so it won.

The two earlier attempts were each correct and each insufficient: AD-117 added `textarea` to
a selector that was already being overridden, and AD-119 bumped the cache version so that
losing rule finally reached the browser — which is what made the real cause visible at all.
The `.ot-field` rules now also name `.ot-checklist`, taking them to (0,3,0), so the result no
longer depends on rule order.
