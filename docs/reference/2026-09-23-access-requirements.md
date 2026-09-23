# Role-Scoped Access Requirements

> **Provenance.** Supplied by the project owner on 2026-09-23 in the delivery session, as four
> numbered sections. Checked in verbatim under the AD-104 rule: a supplied source is the
> executable spec, and a transcription of it is not.
>
> The text arrived ending mid-list, at "a status such as: Remedial Action Required". Asked what
> else belonged in that list, the project owner answered **"Just remedial action"**, so the list
> is complete as it stands. That answer, and the others given the same day, are recorded in the
> design, not edited into the text below.
>
> ID mapping to this project's own register: `knowledge/requirements-index.md`, AR-01 to AR-04,
> one per numbered section. This pack supersedes OD-022 and AD-056 by project owner direction
> (2026-09-23). Design: `docs/superpowers/specs/2026-09-23-role-scoped-access-design.md`.

1. Tax Team Manager and Tax Administrator Access
The Tax Team Managers, Gill Philpott and Clare Hook, will hold Tax Team administrative permissions.
They must be able to:
View all cases requiring a Tax Review.
Allocate unassigned Tax Review cases to individual Tax Specialists.
Reallocate cases where required.
View the progress and status of all Tax Review cases.
Monitor individual and overall Tax Specialist workloads.
Route completed Tax Reviews to AQS for the next stage of the review.
2. Tax Specialist View
From within the Tax Review portal, Tax Specialists should only be able to see cases that have been allocated to them by a Tax Team Manager.
Tax Specialists must be able to:
View their own allocated caseload.
Access the client and case information required to complete the Tax Review.
Record and complete the outcome of the Tax Review.
Route the completed Tax Review to AQS for the next stage of the review.
Tax Specialists must not be able to:
View cases allocated to another Tax Specialist.
View unassigned Tax Review cases.
Allocate or reallocate cases.
Access cases outside their assigned caseload unless they hold the appropriate administrative permissions.
3. AQS Reviewer View
AQS reviewers will require the ability to allocate unassigned cases to themselves.
They should only be able to see:
Cases already allocated to them.
Unassigned cases awaiting AQS allocation.
Cases where the Tax Review has been completed and the case has been routed to AQS for the next stage of the review.
The portal must clearly distinguish between:
New cases awaiting initial AQS allocation.
Cases where the Tax Review has been completed and the case is awaiting AQS allocation.
Cases already allocated to the individual AQS reviewer.
Cases where the AQS review is in progress.
Cases where the AQS review has been completed.
Cases that have already received a Tax Review must be clearly flagged and prioritised within the AQS allocation queue. These cases may have been held in OTIS for longer than cases that have not required a Tax Review, so it is important that their overall age and elapsed review time remain visible.
4. Adviser View

Advisers should only be able to see:
Their own cases.
Cases where all required Tax and AQS review stages have been completed.
Cases that have been formally issued to them with all of the gradings in each section of the form completed and those cases which are awaiting remedial action and the remedial form to complete where relevant.
Actions awaiting their own completion or sign-off.
Actions awaiting sign-off by their T&C Manager, where it is appropriate for the adviser to see the status.
Advisers must not be able to see:
Cases belonging to another adviser.
Client information relating to another adviser’s clients.
Cases awaiting a Tax Review.
Cases awaiting an AQS review.
Cases where a Tax or AQS review is still in progress.
Draft review findings or outputs that have not yet been formally issued.
Other advisers’ remedial actions or review outcomes.
The portal should only release a case to the adviser once all applicable review stages have been completed and the case has moved to a status such as:
Remedial Action Required
