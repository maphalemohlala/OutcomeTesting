# Deployment — portal fixes, checklist split, and what the reported faults actually were

Date: 2026-09-07
Target: `Env_AQ_Dev` (`org0b075da8`, environment `d50d27e8-cb3b-e718-b6e2-30aa92d944aa`)
Deployed by: `svc.automate.aq@ascotlloyd.co.uk`

Covers the batch of portal and app reports raised on 2026-09-07. The AD-089 write-path proof
from earlier the same day is a separate record: `2026-09-07-ad089-write-path-proof.md`.

## What was deployed, in order

| # | Step | Command | Result |
|---|---|---|---|
| 1 | Plug-in assembly, Release | `dotnet build -c Release` | 0 warnings |
| 2 | Plug-in types and Custom APIs | `registerall <orgUrl>` | 25 commands, all members of `OutcomeTesting` |
| 3 | Checklist seed | `importseed <orgUrl> data/v8-seed --confirm <orgUrl>` | 7 created, 117 updated |
| 4 | Code App | `pa app push` | Succeeded |
| 5 | Portal | `Deploy-Portal.ps1 -OrgUrl <orgUrl>` | Upload succeeded, 13 of 13 table permissions written and verified |

Local suites at the deployed commit: plug-ins 376/376, app 192/192, `tsc` clean, lint 9
warnings and 0 errors — the same nine that were there before.

## The two reported failures had one cause, and the message was the second fault

"Not saved – retry" on every answer, and "The site would not accept this submission from your
account", are the same thing: the Contact-anchored table permission chain refusing the write
with a 403 that carries no plug-in prefix.

The autosave path **never read the HTTP status at all**, so every non-2xx became "retry" —
advice that cannot work, because the next attempt is refused for the same reason. Both paths
now name what is actually missing: a Tax or AQS reviewer web role on the portal account, and
the review assigned to you. Neither tells a refused caller to try again.

**And the submit message's promise was false.** It said "your answers are still saved".
Answering and submitting travel the *same* permission chain, so an account refused at submit
was refused on every autosave too — the answers were never written. That is exactly the third
report: *"the page doesn't retain the data as it suggests… I did a submission yesterday, it
didn't save."* The message now says what it knows rather than reassuring.

## The environment's own state explains the rest, and none of it is a bug

Read by query, not inferred.

| Question | What DEV actually holds |
|---|---|
| Why is the Tax reviews page empty? | **There are no Tax reviews.** All 9 `al_reviewinstance` rows are AQS (`120910201`). Nothing has been routed to Tax. |
| Why can an "administrator" not see everything? | The `Administrators` web role appeared in **no** read rule — every "Restrict read" rule named the seven `AL Portal -` roles and stopped. Fixed, see below. |
| Which roles does the reporting account hold? | The CONTACT `Simunye.Radingwana@ascotlloyd.co.uk` holds `AL Portal - AQS Reviewer` and nothing else. **This turned out to be the wrong question** — see the addendum below. |
| Who holds the manager roles? | **Nobody.** `AL Portal - Portal Administrator` and `AL Portal - Outcome Testing Manager` are named in the page rules and held by no contact. |
| Were the table permissions or Web API settings wrong? | No. All 13 permissions carry their roles, and every `Webapi/*` setting is correct. Checked before anything was changed. |

The `mspp_entitypermission_webrole` and `mspp_webpageaccesscontrolrule_webrole` intersects are
both **empty**, and that is not a fault: on the enhanced data model the roles live inside the
component's `content` JSON, and the typed N:N is not materialised. Recorded because an empty
intersect looks exactly like a failed deployment and is the obvious wrong conclusion to reach.

## Changes

### Access

Every "Restrict read" rule gains the `Administrators` web role — five rules. An administrator
now reads every page rather than falling through all of them.

This does **not** give the reporting account administrator access. That account holds one
reviewer role; granting it a manager or administrator role is a decision about a real person's
access and was left to the project owner rather than taken here.

### The review lists lose the "Shell stage" notice by doing what it promised

`?mine=1` scopes each discipline's list to the reviews assigned to the signed-in contact, with
a toggle, honoured by paging, and an empty state that says which of the two views is empty.

Default stays "all", matching the Cases page. Making "mine" the default would show an empty
page to everyone not personally holding a review, which reads as a broken list rather than as
a scope.

### File Quality is now a section per team

Tax and AQS both reach a file quality outcome on the same file and they are not the same
judgement. One shared section made them share a row — or, where the AD-020 owner filter kept
the Tax team out of the AQS section, gave the Tax team nowhere to record an outcome at all.

`S-FQTAX` "File Quality: Tax" is new, owned by the Tax team, carrying the same three questions
as the AQS section. The AQS section is renamed to "File Quality: AQS"; its **code is
unchanged** on purpose, because `al_sectioncodekey` is an alternate key and re-coding it on a
seed import would create a second section and orphan every question under it.

Verified in DEV after import:

| Section | Code | Owner | Order |
|---|---|---|---|
| File Quality: Tax | `S-FQTAX` | Tax team | 2 |
| File Quality: AQS | `S-FQOUT` | AQS checker | 3 |

with `Q-FQTAX-01` Pass/Fail (mandatory), `Q-FQTAX-02` Multiline text, `Q-FQTAX-03` Yes/No
(mandatory) — the AQS wording exactly, so the two answers can be read against each other.

Fail reasons are scoped to the reviewing team. The category already **is** the split in V8, so
AQS is expressed as "not Tax check" rather than as a list of three: a category added later then
lands with AQS instead of vanishing from both pickers unnoticed.

### Fail reasons that a reader can actually see

The reasons block was emitted `hidden` and revealed by the autosave script — **which is only
emitted for an editable review**. On a submitted review, or one belonging to another reviewer,
the reasons a checker had recorded were invisible to everyone reading it. That is the report
"on the fail reason, it doesn't show the checked fields", and it is the one thing a read-only
view of a completed check must not do.

The decision is now made server-side from the record, and the client no longer closes what the
record opened. `NON_PASS` also gains "No" and "Pass with issues": an AML or CRA question
answered No previously offered no way to attach the `FR-AML` reason written for exactly that
answer.

### Smaller things

- **Favicon.** Ascot Lloyd's mark on both surfaces, from the `Logo-sm-64.png` web file the
  portal already deploys, plus a real `<title>` on the code app in place of "vite".
- **History.** `al_details` said `Status 120910583 -> 120910584`. `OptionLabels` resolves the
  label at **write** time, because an Audit Event is immutable (NFR-AUD-01) and no later fix
  can reach a row already written. Rows written before today keep their numbers.
- **Assignment email.** Carries the case link, built from `powerpagesite.primarydomainname`, so
  it is right per environment and a promotion out of DEV cannot mail a PROD checker a link into
  DEV. No link at all where there is no site.
- **Cases dashboard.** Five stages and a total; every card opens the filter it counted, and the
  cards honour the `mine=1` scope so a figure and the page it opens are the same number.

## Two pieces of tooling this needed

**`importseed`** — there is no `pac data import` in pac 2.11.2. The only first-party route for
the reference data under `data/` is the Configuration Migration Tool, a desktop GUI, so a seed
change could not be applied from a session at all. Schema-driven from `data_schema.xml`, so the
conversion cannot disagree with the file it reads.

Its first run failed on its first record, and the reason is worth keeping: **a package's ids
are not the environment's.** DEV was seeded through the CM tool, which mints its own, so a
Retrieve on the package id says "does not exist" for a row that does, and the Create then
collides with the alternate key it was about to duplicate. Records are matched on
`<logical name>code` instead, and every lookup is translated through a package-id-to-real-id
map. The result is visible above: `S-FQTAX` created under the package id, `S-FQOUT` updated in
place keeping the id it already had.

**`proveadoption`** was added earlier the same day; see the write-path proof record.

## Verification, after the fact

| Check | Result |
|---|---|
| Access rules | All five "Restrict read" rules carry `c53b2908` (`Administrators`) in their deployed `content` |
| Web templates | The uploaded source carries `is_nonpass`, `Assigned to me`, `ot-cases-dashboard` and `rel="icon"`; the `<strong>Shell stage.</strong>` banner is gone |
| Sections | 12, with `File Quality: Tax` / Tax team / order 2 and `File Quality: AQS` / AQS checker / order 3 |
| Tax file quality questions | 3, correct response types and mandatory flags |
| Table permissions | 13 in source, 13 in the environment, nothing else |

## Addendum — the sign-in does not reach the contact that holds the role

Added after the report *"I hold an AQS reviewer permission, still I cannot see the AQS review
page"*, which everything above says should have worked.

Every piece of configuration checks out by query, and each was checked rather than assumed:

| Checked | Result |
|---|---|
| Contact ↔ role association | Present: `Sims Rad` ↔ `AL Portal - AQS Reviewer` |
| The associated component | A genuine, Active `Web Role` on this site — not an id collision |
| Web roles' website | All ten belong to the one site |
| The AQS page's rule | Names `…0091`, and the cascading Home rule names it too |
| Every page's publishing state | Published, one website |
| Rule ↔ publishing state | Empty, and irrelevant: that relationship governs Grant Change, not Restrict Read |

So the configuration is not the fault. **The sign-in is.**

`adx_externalidentity` holds exactly **one** record in this environment:

| Username | Provider | Contact |
|---|---|---|
| `e044a8e9-34ac-4503-8da4-e9573ccd234b` | `https://sts.windows.net/4abde4fc-…/` (Entra, the Ascot Lloyd tenant) | **Dev Account** (`svc.automate.aq-dev@ascotlloyd.co.uk`) |

**An Entra sign-in to this portal lands on the `Dev Account` contact**, and `Dev Account` holds
`AL Portal - Tax Reviewer`. The `Sims Rad` contact, which holds the AQS role, is reached only by
the local forms login `Sims` — its `adx_identity_username`.

That single fact accounts for every report in this batch, in order:

- *"Signed in as an administrator, but I can only see tax review page"* — resolved to
  `Dev Account`, which holds Tax Reviewer and nothing else. The Tax page is the one page it can
  read.
- *"Signed in again as an AQS reviewer, still cannot see the AQS Reviews page"* — the AQS role
  was granted to `Sims Rad`. Granting a role to a contact the session never resolves to changes
  nothing about that session.
- *"Not saved – retry" on every answer* — the write permission is Contact-scoped through
  `contact_al_reviewinstance`. `Sims Rad` is the assigned contact on IO-100001, IO-100004 and
  IO-100007; a session that is `Dev Account` is refused on all three. Which is the 403 the
  message was mistranslating as "retry".

**Nothing was changed to fix this.** Rebinding an external identity, or granting a person's role
to a service account, is a decision about a real person's access in a shared environment, and
the three ways of resolving it are not equivalent:

1. Move the external identity onto the `Sims Rad` contact — correct, and it takes the portal
   sign-in away from `Dev Account`.
2. Grant `AL Portal - AQS Reviewer` to `Dev Account` — quickest, and conflates a named person
   with a service account in every audit row that follows.
3. Sign in with the local `Sims` username instead of Entra — changes nothing, and proves the
   diagnosis in one attempt.

The Home page now prints the contact and the roles the portal resolved, so this is a one-glance
check rather than an investigation. That is what makes the difference between the three options
a decision someone can take rather than a guess.

## Still owed

- **The stale `annotationid` in `outcome-testing.css.webfile.yml`.** It produced the same
  `FAILED … Does Not Exist` line in this upload as in the last one, exactly as the previous
  deployment record predicted. It is a leftover of a Standard-model download and should be
  removed so a real failure is not lost in a familiar one. Not done here: it is outside what
  was asked for, and it is now recorded twice.
- **Nobody holds a manager or administrator portal role.** The page rules name
  `AL Portal - Portal Administrator` and `AL Portal - Outcome Testing Manager`; no contact
  holds either. Until one does, "an administrator sees everything" is true of the rules and of
  nobody in particular.
- **There is no Tax data in DEV.** The Tax pages, the Tax file quality section and the Tax fail
  reasons cannot be seen working until a case is routed to Tax.
