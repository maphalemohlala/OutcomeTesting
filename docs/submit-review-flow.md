# Submit Review — cloud flow build steps

The portal cannot call `al_SubmitReview` directly. Microsoft's Power Pages Web API
documentation is explicit: *"Calling actions and functions by using the portals Web
API isn't supported."* A Custom API is an action, so the only supported route from a
page is a Power Automate cloud flow using the **When Power Pages calls a flow**
trigger.

```
review page  ->  cloud flow (run-only, web-role secured)  ->  al_SubmitReview  ->  Dataverse
```

Everything either side of the flow is built and deployed. The flow is the one piece
that must be created in the maker portal, because associating it with the site is
what generates the trigger id, and there is no CLI for that.

## What is already in place

| Piece | State |
|---|---|
| `al_SubmitReview` Custom API | Registered in Env_AQ_Dev, bound to `OutcomeTesting.Plugins.SubmitReviewPlugin` |
| Request parameters | `TargetId` (required), `IdempotencyKey` (required), `ExpectedRowVersion` (optional) — all Text |
| Response properties | `Status` (Text), `AuditEventId` (Text), `Conflict` (Boolean) |
| Review page submit panel | Live at `/review?id=` when the review is not yet submitted |
| Client script | Inlined in the `OT Review Detail` web template |
| Site setting | `OutcomeTesting/Flow/SubmitReview` — created, empty |

The page reads the trigger id from that site setting. While it is empty the panel
renders a plain message saying submitting is not configured, rather than failing.

## 1. Create the flow

Power Pages design studio → **Set up** → **Integrations** → **Cloud flows** →
**+ Create new flow**.

It must be a **solution-aware** flow; only solution flows can be attached to a site.
Create it inside the **OutcomeTesting** solution so it travels with everything else.

**Trigger — When Power Pages calls a flow.** Add three inputs, all type **Text**,
named exactly as below. The names must match the request body the page sends:

| Input | Notes |
|---|---|
| `TargetId` | The `al_reviewinstance` id |
| `IdempotencyKey` | Stable per attempt; a replay returns the original Audit Event |
| `ExpectedRowVersion` | May arrive empty; pass it through regardless |

**Action — Perform an unbound action (Microsoft Dataverse).**

- Action Name: `al_SubmitReview`
- `TargetId`: trigger input `TargetId`
- `IdempotencyKey`: trigger input `IdempotencyKey`
- `ExpectedRowVersion`: trigger input `ExpectedRowVersion`

**Response — Respond to Power Pages.** Return the three response properties:

```json
{
  "Status": "@{outputs('Perform_an_unbound_action')?['body/Status']}",
  "AuditEventId": "@{outputs('Perform_an_unbound_action')?['body/AuditEventId']}",
  "Conflict": "@{outputs('Perform_an_unbound_action')?['body/Conflict']}"
}
```

**Failure path.** Add a second **Respond to Power Pages** action configured to run
when the unbound action fails, returning the Dataverse error message in a field named
`Error`:

```json
{ "Error": "@{result('Perform_an_unbound_action')[0]['error']['message']}" }
```

This matters. The page branches on the plug-in's error prefixes — `CONFLICT:`,
`UNAUTHORIZED:`, `PRECONDITION:` — to show the right message. `PRECONDITION:` carries
the actionable detail, such as how many required questions are still unanswered. If
the flow swallows the error and returns 200 with no body, the user is told the
submission worked when it did not.

## 2. Attach the flow to the site

Design studio → **Set up** → **Integrations** → **Cloud flows** → **+ Add cloud flow**.

Select the flow, then **+ Add roles** and grant only:

- `AL Portal - Tax Reviewer`
- `AL Portal - AQS Reviewer`

Do not grant it to Authenticated Users. The plug-in checks the caller anyway, so a
wider grant would not let anyone submit someone else's review — but a role that has no
business submitting should not be able to reach the flow at all.

Adding the flow generates its trigger URL. Copy the guid that follows `/trigger/`.

## 3. Set the site setting

Portal Management app → **Site Settings** → `OutcomeTesting/Flow/SubmitReview` → set
the value to the guid from step 2, then clear the site cache.

Keep it in the site setting rather than in the template. The flow has a different
trigger id in every environment, so a hardcoded value would deploy TEST's id into PROD.

## 4. Test

Sign in as a checker with a review assigned to them, open `/review?id=<id>` and:

| Scenario | Expected |
|---|---|
| Required question unanswered | Refused, message names how many remain |
| All required answered | Submits, page reloads showing the locked state |
| Submit twice quickly | One Audit Event, not two — the idempotency key is reused |
| Submit an already-submitted review | Succeeds silently, `al_submittedon` unchanged |
| Another checker's review | `UNAUTHORIZED:`, mapped to a permission message |
| Record changed in another tab first | `CONFLICT:`, mapped to a reload-and-retry message |

Confirm in `al_auditevent` that each successful submission wrote exactly one row with
command `SubmitReview` (120910754) and the correlation id.

## 5. Promoting to TEST and PROD

Cloud flows must be **registered against the site in each environment**. Design studio
→ Set up → Cloud flows → *Cloud flows in this site* → the register icon. Skipping this
gives a forbidden error when the page calls the flow.

Then set that environment's `OutcomeTesting/Flow/SubmitReview` to its own trigger id.

## Notes

- A Power Automate licence is required; Microsoft recommends a Power Automate Process
  licence in production.
- The flow runs under its own connection, not the signed-in user's. That is why the
  plug-in checks `InitiatingUserId` against the record owner rather than trusting the
  caller — the flow identity must never become an authorisation bypass.
- The client script is progressive enhancement. Disabling the button while a request
  is in flight is a courtesy; every rule that matters is enforced server-side
  (NFR-SEC-01).
