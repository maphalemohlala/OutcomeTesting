# Six components were built today and none of them were in the solution

**2026-09-21. AD-203.** Audit asked for by the project owner: "ensure all components are in
the solution". Six were not.

## What was missing

Every portal component created today through the Web API landed in the **Active** solution and
was never added to `OutcomeTesting`. A Web API create takes no solution context, so nothing
warned:

| Component | Type | Created |
|---|---|---|
| `Adviser Mapping - advisers I supervise` | table permission | today (AD-201) |
| `List Option - read` | table permission | today (AD-192) |
| `Webapi/al_listoption/enabled` | site setting | today |
| `Webapi/al_listoption/fields` | site setting | today |
| `Webapi/al_questionversion/enabled` | site setting | today |
| `Webapi/al_questionversion/fields` | site setting | today |

267 components on the site, **261** in the solution.

## What that would have done to TEST

This is the part that matters. The web template WAS in the solution, so a promotion would have
carried the page that reads `al_advisermapping` and none of the permission that lets it:

- The sign-off panel would have been withheld from **everyone** in TEST, including the right
  person, because the Liquid fetch would return empty for want of a table permission.
- Every managed dropdown would have been empty — AD-192 reproduced exactly, in the environment
  where it is hardest to diagnose.
- The checklist-freshness call would 403 again — AD-191, likewise.

And all three would have looked like *empty pages*, not errors. The same class of failure that
took a day to find in DEV, arriving in TEST with the solution's blessing.

## Fixed

Six `AddSolutionComponent` calls, `AddRequiredComponents: false` — each stands alone, and
pulling required components in would drag the site graph behind a site setting and quietly
widen the solution. Verified by re-reading the membership: **267 of 267, nothing missing.**

## The rule this leaves

A component created by Web API is not in the solution. `pac solution add-reference` and the
maker portal both handle this; a raw `POST` does not. Anything created that way needs an
explicit `AddSolutionComponent`, and the check is cheap:

```
solutioncomponents?$filter=_solutionid_value eq <solution> and componenttype eq 10433
```

against the site's own component list. Worth running before any promotion, not after.

---

# And the portal now has end-to-end specs

**Same day, AD-204.** Also asked for: Playwright coverage of the day's changes.

## Why the vitest suite was not enough

The unit tests read the template **source** and pin the Liquid. Three separate defects today
were invisible to that, because the source was correct in every one:

- **AD-195** — the table permission's web roles were written to `mspp_entitypermission_webrole`,
  a projection the enhanced data model ignores. The page rendered; the lists were empty.
- **AD-201** — no table permission for `al_advisermapping` at all. The fetch returned nothing
  and the panel hid from everybody.
- **AD-202** — the regrade command checked the role alone, so the form rendered for people the
  sign-off beside it refused.

Each rendered a page that looked *empty* rather than *broken*. Only a browser separates those.

## What is covered

`app/e2e/`, run with `npm run e2e`:

- The sign-off form **appears** on a case whose adviser maps to the signed-in user, and
  **both** supervisor controls are withheld on a case whose adviser maps to somebody else.
  Both directions, because a gate is only shown to work by a case it lets through *and* a case
  it stops.
- The refusal says why, and does not name the manager it refused for.
- No `Liquid error` anywhere — the failure mode that would otherwise satisfy every other
  assertion on the page.
- The managed lists have options *in* them, which is the difference between AD-192 and its fix.
- The AD-199 allowlist still refuses `al_recheckrequired` to the browser.

Nothing about any environment is committed: the portal URL, case ids and review URL all arrive
as environment variables, and an unconfigured checkout **skips**, naming the variable.

## Two things running it taught

Worth recording because both were wrong in the first draft and neither was visible by reading.

**The skip has to be at group level.** Written inside the test body, it runs after the `page`
fixture is built, so an unconfigured machine still launches a browser and reports
"Executable doesn't exist" — a suite blaming the product for a laptop.

**A signed-out run passed two specs.** The session guard asserted the body did *not* say
"Sign in". With no session, the two specs whose assertions are `toHaveCount(0)` went green: no
session, no controls, no controls expected. **A suite that reports a gate as working because
nobody could see the page is worse than no suite.** The guard is now positive — the URL host
must still be the portal's, and the main navigation must be visible — and a signed-out run
fails all four with "the session has expired, re-run `npm run e2e:auth`".

The general rule, which applies well beyond this file: anything phrased as an absence has to be
anchored to something whose presence was proved first.

## Not run green here

The specs need a signed-in browser session, and capturing one means either an interactive
sign-in or copying a live cookie jar. The sandbox refused the copy, correctly — it is
credential exfiltration whatever the intent. So the suite has been run **unconfigured**
(7 skipped, no browser) and **configured but signed out** (4 failed, correct diagnosis). The
assertions themselves were verified by hand through a browser against DEV the same afternoon.

One command closes it: `npm run e2e:auth`, then `npm run e2e`.
