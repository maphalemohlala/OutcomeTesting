# A second identity for UAT: what to provision, and why it cannot be faked

Twelve rows of the app checklist — APP-002, 003, 026, 033, 034, 037, 100, 110, 120, 126,
159 and 160 — all ask the same question in different words: *does this refuse somebody who
may not do it?* Every one of them is blocked on the same missing thing, a caller who is not
the operator.

This is the runbook for unblocking them. It is written for whoever provisions the account
and for the project owner deciding whether to ask.

## Why a second account is genuinely required

Not an assumption. Five routes were tried against DEV and each is closed:

| Route | Result |
| --- | --- |
| Use an account that already exists in DEV | **All five** interactive users in DEV hold **System Administrator**. `PermissionHelpers.EnsureAppPermission` short-circuits on its first line for one of those — the deliberate break-glass so that assigning roles can never lock everyone out. No existing account can reach the gate at all. |
| Impersonate another user (`MSCRMCallerID`) | **Ignored, not refused.** Proved: `WhoAmI` with the header set comes back as the operator, HTTP 200, no warning. Dataverse honours caller impersonation only for a service principal authenticating with a client secret; on a licensed user's interactive token it is silently dropped. |
| Impersonate by Entra object id (`CallerObjectId`) | Server-side error. |
| Connect as an application user instead | DEV has 148 application users and **every one is Microsoft first-party**. There is no app registration for this solution whose secret we hold. |
| Strip System Administrator off a service account and put it back afterwards | **Unrecoverable from inside DEV.** Neither shipped security role — `Outcome Testing App User` nor `Outcome Testing App Admin` — grants `prvAssignRole` or write on `systemuserroles`. An account de-admined this way cannot restore itself, and would need a tenant administrator to fix. Not worth the risk for a test. |
| Sign in to the portal as a contact instead | `Authentication/Registration/LocalLoginEnabled` is **false**. The portal accepts Entra sign-in only, so a contact is not a way around needing an account either. |

The tooling now refuses rather than pretends: `webapi` and `callapi` accept `--as <systemuserid>`,
ask the server who it thinks is calling over the same transport, and **stop** unless the
answer is the impersonated user. That flag will start working by itself the day this
solution has its own application user.

## What to ask for

One account. It does not need a mailbox anyone reads, and it should not be a real person's.

- **An Entra account** in the tenant, e.g. `<test-checker>@<company-domain>` — a generic
  name, not a colleague's.
- **A licence** that includes Power Apps, so it can open the Code App.
- **In DEV only.** Nothing here should be provisioned in TEST or PROD.
- **Dataverse security roles: `Basic User` + `Outcome Testing App User`.**
  Explicitly **NOT** System Administrator — that is the whole point, and an account that
  has it tests nothing.

Then, from this repo:

```
OutcomeTesting.Registration.exe grantapprole <orgUrl> <test-checker>@<company-domain> --confirm <orgUrl>
OutcomeTesting.Registration.exe grantrole    <orgUrl> <test-checker>@<company-domain> "AL Portal - Tax Reviewer" --confirm <orgUrl>
```

The password needs to reach whoever runs the tests, for the browser half. The CLI half can
be driven from a second `pac`/OAuth profile signed in as that account.

## One role covers nine of the twelve

`AL Portal - Tax Reviewer` is the useful choice, because of what it is granted — and much
more because of what it is not. Read off the live `al_pagepermission` matrix, it holds
`page.dashboard: View`, `page.cases: Edit`, `page.reviews: Edit`, and **nothing else at
all**. No intake, no exports, no question library, no adviser mapping, no security page, no
due date, no assign, no sign-off, no regrade.

So a single account on a single role is a legitimate negative case for nine rows at once,
and the remaining three are reached by changing that account's state rather than by needing
more accounts.

| Row | What to set up | Drive it with | Expected |
| --- | --- | --- | --- |
| APP-002 | Deactivate the account's `al_userrolemapping` row (`al_SetRoleAssignmentActive`), leaving it registered but role-less | Open the Code App; call any gated command | `UNAUTHORIZED: You have no application role assigned. Ask an administrator to assign one in Security configuration.` |
| APP-003 | Remove `Basic User`, keep `Outcome Testing App User` | Open the Code App | A readable message, **not** a raw `SecLib::CheckPrivilege` fault. This has bitten three times before — see the `app-roles-need-basic-user` note |
| APP-026 | Tax Reviewer; a case assigned to somebody else | Open that case in the app | Readable; no edit controls |
| APP-033 | Tax Reviewer, unassigned case | `al_UpdateCaseDetails` | Refused server-side (`page.cases` is Edit for this role, so the refusal must come from the **assignment** check, not the RBAC gate — that distinction is the row) |
| APP-034 | As APP-033 but Tax fields only | `al_UpdateCaseDetails` | Refused |
| APP-037 | Tax Reviewer | `al_UpdateCaseDetails` touching `al_duedate` | Refused naming `case.duedate` — the second, field-level gate |
| APP-100 | Tax Reviewer | `al_ImportCases` | Refused naming `page.imports`; page unavailable |
| APP-110 | Tax Reviewer | `al_CreateExportBatch`, `al_GenerateExport` | Refused naming `export.generate`; page unavailable |
| APP-120 | Tax Reviewer | `al_AddQuestion` | Refused naming `question.retire` |
| APP-126 | Tax Reviewer | Open adviser mapping | Page unavailable (`page.admin.advisers` needs Manage) |
| APP-159 | Tax Reviewer | `al_AssignUserRole`, `al_SetPagePermission` | Refused naming `permission.manage`; page unavailable |
| APP-160 | Tax Reviewer | Navigate straight to `#/reports` | Refused **server-side**, not merely hidden from the menu — this row is only answered by calling past the UI |

Every refusal above should arrive as a sentence a person can act on, prefixed
`UNAUTHORIZED:`. A row where the command succeeds, or fails with a platform privilege
fault instead, is a finding.

## Afterwards

Leave the account in place — these rows will be re-run. If it is retired, deactivate it
(`al_SetUserActive`) rather than deleting it, which is the sanctioned route for a leaver
(OD-010) and is itself worth one more test.
