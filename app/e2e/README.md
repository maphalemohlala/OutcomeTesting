# Portal end-to-end specs

Written for: whoever is next asked to prove a portal change actually works.

These drive a real browser against a live Power Pages site. They exist because the vitest
suite beside them reads the template **source** and cannot see the three things that have
actually broken this portal:

- whether the table permission exists at all,
- whether its web roles were written where the enhanced data model reads them (AD-195),
- whether a Liquid fetch returns anything once it does.

Each of those rendered a page that looked *empty* rather than *broken*, which is why they
survived a day of use apiece. A browser is the only thing that can tell those apart.

## Running them

Nothing about any environment is committed here. Set what you are pointing at:

| Variable | What it is |
|---|---|
| `OT_PORTAL_URL` | The portal's base URL. |
| `OT_CASE_MAPPED_TO_ME` | A case whose adviser maps to the signed-in user. |
| `OT_CASE_MAPPED_ELSEWHERE` | A case **with actions awaiting sign-off** whose adviser maps to somebody else. |
| `OT_REVIEW_URL` | An editable review page, for the managed-list specs. |
| `OT_ACTION_ID` | A remedial action id, for the Web API allowlist spec. |

Then:

```
npm run e2e:auth     # opens a browser, you sign in once, the session is saved
npm run e2e
```

`e2e:auth` writes `e2e/.auth/portal.json`. It is a live session, so it is gitignored and it
expires — when the specs start saying "looks signed out", run it again. To take the session
from a browser profile that is already signed in, without typing anything:

```
node e2e/capture-auth.mjs --profile "<path to a signed-in Chrome profile>"
```

## Choosing `OT_CASE_MAPPED_ELSEWHERE`

The trap worth naming. A case with **nothing pending** renders neither the supervisor panel
nor the refusal, so it passes every assertion here while proving nothing — it cannot tell a
working gate from a page that shows nobody anything. Pick a case that is genuinely waiting on
a sign-off, or the spec is decoration.

## What is deliberately not here

**Authorization.** The server is the boundary — `SignoffRequestPlugin`,
`RegradeRequestPlugin` — and it is tested where it lives, against a fake organization
service, including the negatives. These specs only check that the page does not offer
somebody a form they will be refused for filling in.

Nothing here writes. No spec signs a case off, completes an action or records an outcome:
they are read-only against a shared environment other people are using. The one exception is
the allowlist spec, which deliberately attempts a write it expects to be **refused** — if
that ever starts succeeding, the refusal is the finding.

## Unconfigured means skipped, not passed

A spec whose variables are unset skips and names the variable. A red run should mean the
product is wrong, never that a laptop is unset — a suite that cannot tell those apart is one
people learn to ignore, and then it protects nothing.
