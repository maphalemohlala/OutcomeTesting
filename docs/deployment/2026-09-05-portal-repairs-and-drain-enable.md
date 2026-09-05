# Deployment — portal repairs and PP-15 switch-on, 2026-09-05

Target: `Env_AQ_Dev`, as `svc.automate.aq@ascotlloyd.co.uk`. Three writes, each verified by
FetchXML afterwards rather than taken from the command's own output.

---

## 1. OD-035 closed — `/case-details` repointed

```
repointwebpage … case-details "OT Case Detail Page"
  a1000000-…-032 (root):    a1000000-…-0022 -> a1000000-…-002b
  a1000000-…-042 (content): a1000000-…-0022 -> a1000000-…-002b
```

Verified after: both rows now report `mspp_pagetemplateid = OT Case Detail Page`.

**One correction to the 2026-09-04 record.** It said the two web pages carried a **null**
page template. They did not — both pointed at `a1000000-…-0022`, the colliding id, which is
now the `OT Outcome Label` **web template**. The lookup was populated but aimed at a row of
the wrong component type, which is why a projection of it rendered blank and read as null.
The distinction matters for diagnosing the next one: a broken page here looks like an empty
lookup, but the underlying fault is a lookup pointing at a component that is no longer a page
template.

## 2. OD-033 half closed — `Administrators` over-grant cleared

```
setwebroleauth … "Administrators" false
  Web role 'Administrators' (c53b2908-…): authenticatedusersrole True -> False
```

Verified after: `Administrators` reads `No`, matching `webrole.yml`. `Authenticated Users`
correctly remains `Yes` — that is the stock role and source declares it so.

**`Checker` is untouched and still `Yes`**, because that half is a decision, not a
correction: it is declared nowhere in source, so there is nothing to reconcile it against.
See the register.

## 3. PP-15 switched on — the drain step is registered

```
registerstep … NotificationDrainPlugin Create al_notification 40 "" async svc.automate.aq@ascotlloyd.co.uk
  runs as systemuser 3c6f441a-…
  created sdkmessageprocessingstep: 76bbe7e0-…
  added to the OutcomeTesting solution
```

Verified after: **Post-operation, Asynchronous, Enabled**, on Create of `al_notification`.
Both preconditions were met first — mailbox `Yes`/`Success`, and the sending identity settled
as the approved account by project owner direction.

**But PP-15 is switched on, not proven.** `al_notification` holds **zero rows** — not a
drained outbox, an empty one. The emitters have never produced a notification in DEV since
the table was created on 2026-09-03, so:

- there is no `Pending` backlog to work through, and the `MaxRows: 1` procedure has nothing
  to run against;
- **no notification has ever travelled the full path** — emitter, outbox row, async drain,
  server-side email. The drain is registered and the mailbox is approved, and neither of
  those is evidence that an email arrives.

The honest next step is to cause one qualifying event in DEV — an allocation is the cheapest,
since `NotificationEmitterPlugin` fires on Create of `al_caseassignment` — then watch the row
appear, go `Sent`, and confirm the email lands. That is a real business write, so it is a
deliberate act for whoever owns DEV data, not a side effect of this deployment.

## 4. A tool fix this forced: portal reads need FetchXML

`repointwebpage` failed on its first run with "No page template named 'OT Case Detail Page'"
against a row that exists. The cause is worth carrying: **`QueryExpression` returns nothing
against the enhanced-data-model `mspp_*` tables on this site**, while a FetchXML query with
the identical equality filter returns the row immediately. The registration tool's shared
`FindId` helper uses `QueryExpression`, so it silently reports "not found" for any portal
component.

Both portal commands now read through a `PortalRows` helper built on `FetchExpression`. The
rest of the tool is unchanged — `FindId` is fine for the `systemuser`, `plugintype` and
`customapi` tables it was written for. Anything new that reads an `mspp_*` table must use
FetchXML, or it will report a row that exists as missing.

## What is now open in DEV

- **OD-034** — `pac pages upload` still cannot complete, so portal deployment still has no
  working CLI path. These repair commands are the interim route.
- **OD-033 second half** — the undeclared `Checker` role, still auto-granted to every
  authenticated user, still granting nothing.
- **PP-15 end-to-end proof** — see section 3.
