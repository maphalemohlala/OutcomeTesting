# The submit refusal the portal replaced with "Common Data Service error occurred." — DEV, 2026-09-13

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. **DEV only.** TEST and PROD are untouched, by project
owner direction of 2026-09-12.

Reported by the project owner: submitting a review with questions unanswered "still throws
*This review was not submitted, and nothing has been changed. The server said: Common Data
Service error occurred. (status 400)* instead of showing which questions were not answered."

**One step is outstanding and needs the project owner to run it** — see *What you have to run*
below. Until it does, the page still shows the generic sentence.

---

## The server was saying the right thing all along

The plug-in trace for the reported attempts:

```
PRECONDITION: Complete all questions marked Required before submitting.
1 of 36 required questions are unanswered: Q-CRP-04.
[unanswered:f1fe33f8-64a1-f111-b8dd-e4fade069307]
```

and a minute earlier, three of them: `Q-GR-02, Q-CD-04, Q-CRP-04`. Exactly the message the
page is written to display, thrown as an `InvalidPluginExecutionException`, with the question
version ids in the bracketed tail the page reads to mark the rows.

None of it reached the browser. **Two separate defects stood between the two.**

## Defect 1 — the site setting that had never been created

Power Pages returns a plug-in's own message to the Web API caller only when the site setting
`Webapi/error/innererror` is `true`. Its documented default is `False`, and this site had 12
`Webapi/*` settings, none of them that one. So every refusal on the site arrived as the
platform's generic `Common Data Service error occurred.` and a 400, whatever it actually was.

Every `friendlyError` on the site reads `error.innererror.message` first. All three templates
were written for a setting that did not exist. This is why the unanswered-rows work of
`757d646` could not have worked in a browser: the message it parses was never sent.

Confirmed against Microsoft's documentation (*Overview of the Power Pages portals Web API*,
"Site settings for the Web API"): `Webapi/error/innererror` — "Enables or disables InnerError.
Default: False. Valid values: True, False."

## Defect 2 — the page cut the message before reading it

Found while fixing the first, and it would have been the next report.

`serverMessage` capped every message at 300 characters, and `friendlyError` then read the
`[unanswered:…]` tail from the **end** of what was left. The tail is where the question ids
are, so past 300 characters it was simply gone:

| Unanswered questions | Message length | Tail survives a 300 cap |
|---|---|---|
| 1 | 174 | yes |
| 3 | 266 | yes |
| **4** | **311** | **no** |
| 6 | 403 | no |
| 10 | 588 | no |

One and three fitted, which is what made it look like the feature worked. From four upward the
rows would go unmarked and the text on screen would end mid-identifier.

The cap now belongs to each branch that puts text on screen, applied **after** the tail is
taken off. A platform fault is still capped at 300.

## What changed

| Piece | Change |
|---|---|
| `powerpages/…/sitesetting.yml` | `Webapi/error/innererror` = `true` added, with the reasoning and why it is safe here |
| `OT Review Detail`, submit parser | `cap()` extracted and applied per branch instead of inside `serverMessage` |

No plug-in change. The plug-in was already correct.

**Why exposing inner errors is safe on this site.** These messages are written to be read by
the person who hit them: they carry a `VALIDATION`, `PRECONDITION`, `CONFLICT` or
`UNAUTHORIZED` prefix, say what to do next, and never expose a table name, query, id or stack
trace (PP-16). Anything that is *not* one of ours is collapsed to one line and capped before
display.

## What ran

| | Step | Result |
|---|---|---|
| 1 | Submit-error smoke test against the real parser and a genuine `innererror` body | **29 checks passed** at 1, 3, 4, 6 and 10 unanswered questions |
| 2 | The same test against the **pre-fix** template | **9 failed** — every check at 4, 6 and 10, and none at 1 or 3. The defect and the fix, both confirmed |
| 3 | Script blocks parsed with the Liquid stripped | 4 blocks, all parse |
| 4 | Liquid balance | 455 opens / 455 closes |
| 5 | `pushwebtemplate … OT Review Detail` | `a1000000-…-01b`, **99982 → 100940 chars** |

The smoke test asserts the whole chain: the body Power Pages sends once the setting is on, the
message shown (prefix and tail stripped, codes named), the rows marked and no others, the
status line reading "Required - not answered", and that conflicts, 403s and long platform
faults still route as they did.

## What you have to run

The registration tool's `setsitesetting` was refused by this session's permission gate, the
same way `queueroutedcases --confirm` was on 2026-09-10. In PowerShell, from
`plugins/OutcomeTesting.Registration`:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
$U='https://org0b075da8.crm11.dynamics.com/'
dotnet run -- setsitesetting $U Webapi/error/innererror true --confirm $U
```

Then read it back:

```powershell
dotnet run -- fetch $U "<fetch><entity name='mspp_sitesetting'><attribute name='mspp_name'/><attribute name='mspp_value'/><filter><condition attribute='mspp_name' operator='eq' value='Webapi/error/innererror'/></filter></entity></fetch>"
```

Site settings are cached, so give it a few minutes or clear the site's server-side cache before
retesting the submit.

## Not done here

- **The setting itself in DEV** — the one step above.
- No client-side pre-check of unanswered questions was added. The server's list is the
  authority on which questions are required, and with the setting on it arrives and marks the
  rows. A pre-check would duplicate that rule in the page.
- The identical 300-character cap in `OT Remediation`'s parser is left alone: no refusal on
  that page carries a bracketed tail, so nothing is lost to it.
- Committed on `feat/checklist-administration`. Merging to `main` is the project owner's call.
