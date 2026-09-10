# Outcome Testing Solution Requirements

> **Provenance.** Supplied by the project owner on 2026-09-09 as
> `outcome_testing_solution_requirements.md`, consolidating the 8 September 2026 meeting and
> the workflow demonstration that followed it. Checked in verbatim under the AD-104 rule: a
> supplied source is the executable spec, and a transcription of it is not.
>
> One substitution, recorded rather than made silently: the §7.1 workflow diagram arrived with
> its arrows mangled to `â` by a UTF-8 round trip. They are set as `↓` below. Nothing else is
> changed, including the numbering and the British spellings.
>
> ID mapping to this project's own register: `knowledge/requirements-index.md`, MR-01 to MR-18.
> Gap assessment against the built solution: `docs/2026-09-10-requirements-gap-assessment.md`.

## 1. Purpose

This document consolidates the changes and features identified during the 8 September meeting and the subsequent workflow demonstration. The requirements cover checklist configuration, remediation, supervisor sign-off, reporting, notifications, permissions, usability, testing, and exports.

## 2. Priority Summary

### Must Have Before UAT

1. Mirror the Version 8 checklist structure and ordering.
2. Automatically populate failed items into remediation.
3. Redesign the remediation form around required actions.
4. Introduce supervisor sign-off and final outcome regrading.
5. Track initial and final outcomes separately.
6. Resolve remaining portal, account, and role permissions.
7. Route users to the first page they are authorised to access.
8. Correct remediation form alignment and styling.
9. Verify reviewer assignment and submission rules.
10. Prepare the corrected solution in the test environment.

### Should Have

1. Implement workflow email notifications using business-approved templates.
2. Add direct case or action links to notifications.
3. Complete the simplified dashboard and drill-down behaviour.
4. Improve remediation and checklist navigation.
5. Support structured defect recording during UAT.

### Later or Backlog

1. Direct Power BI or database reporting integration.
2. Extended management reporting.
3. Trail Light export automation and reconciliation enhancements.

---

## 3. Dashboard and Case Navigation

### 3.1 Simplified Dashboard

The dashboard must use a less cluttered layout consisting of four primary summary cards and horizontal charts for the remaining metrics.

### 3.2 Drill-Down Behaviour

Each dashboard card or chart metric must remain selectable. Selecting a metric must:

- Apply the relevant filter.
- Open the corresponding case work list.
- Display only cases represented by the selected metric.

### 3.3 Workflow Status Visibility

The dashboard should clearly distinguish between:

- Open cases.
- Available for allocation.
- Assigned reviews.
- Awaiting remediation.
- Awaiting supervisor sign-off.
- Completed cases.

### 3.4 Outcome Charts

Outcome reporting must distinguish between the initial review outcome and the final outcome after remediation and sign-off.

The supported outcome categories are:

- Pass.
- Pass with Issues.
- Insufficient Evidence.
- Potential Harm.
- Not Yet Graded, where applicable.

---

## 4. Checklist Structure and Configuration

### 4.1 Version 8 Alignment

The implemented checklist must mirror the approved Version 8 checker checklist.

The application must preserve the hierarchy and sequence of:

1. Section.
2. Subsection.
3. Question.

### 4.2 Explicit Ordering

The data model and form must support explicit ordering for:

- Sections.
- Subsections.
- Questions within each subsection.

Questions must not rely on creation date, record ID, alphabetical order, or another implicit sequence.

### 4.3 Section Placement

The placement and ordering of the following areas must be reviewed and corrected against Version 8:

- File Quality.
- Tax.
- Trust.
- Advice.
- Process.
- AML and CRA checking.
- Client objectives and information.
- Risk and capacity.
- Costs and value.
- Research and recommendation rationale.

File Quality content and its outcome must appear in the correct sequence relative to the tax check.

### 4.4 Question Configuration

Administrators must continue to be able to:

- Edit question wording.
- Configure response types.
- Mark questions as mandatory or optional.
- Add questions without changing application code.
- Preserve previous versions for historic responses.

### 4.5 Failure Reason Behaviour

Conditional failure reasons must appear only where required by the approved checklist design. The meeting specifically identified File Quality as requiring this behaviour rather than applying it indiscriminately to every section.

---

## 5. Review Allocation and Submission

### 5.1 AQS Allocation

AQS reviewers must be able to:

- View available unassigned AQS cases.
- Select cases according to available capacity.
- View cases already assigned to the signed-in reviewer.
- Open and complete an assigned review.

### 5.2 Tax Allocation

Tax reviewers must be able to view tax cases but must only work on cases explicitly assigned to them.

### 5.3 Sequential Review

Where both tax and AQS checks are required:

1. The tax reviewer completes and submits the tax section.
2. The case progresses to the AQS stage.
3. The AQS reviewer completes the AQS section.

Users may view earlier sections where authorised but must not edit sections without the relevant permissions.

### 5.4 Submission Validation

A review response must only be saved or submitted when:

- The signed-in user has the required reviewer role.
- The review or case is assigned to that user where assignment is required.
- The user has the necessary page and data permissions.

The interface must display a clear, user-friendly message when a condition is not met.

---

## 6. Remediation Workflow

### 6.1 Failed-Item Population

When a review contains a Fail, issue, or Insufficient Evidence result, the relevant items must automatically populate the remediation record or form.

For each relevant item, the remediation experience must display:

- The failed question or check.
- The recorded response.
- The failure reason.
- Reviewer comments.
- The required remedial action or guidance.
- The original outcome type.
- Relevant positive context where useful.

### 6.2 Targeted Remediation Experience

The remediation form must act as a focused action form. Advisers or planners must not need to navigate repeatedly between the complete review and the remediation form to determine what needs to be corrected.

The remediation form must not reproduce the entire review unnecessarily. It must prioritise the headline failure points and required actions.

### 6.3 Adviser Remediation Actions

The assigned adviser or remediation recipient must be able to record one or more remedial actions, including:

- What has been done or will be done.
- Target completion date.
- Whether client contact is required.
- Whether a recheck is required.
- Whether the advice changed.
- Supporting comments or evidence, where applicable.

### 6.4 Review Evidence

Authorised users must be able to:

- Open the relevant review.
- View recorded responses.
- Identify the failed questions or areas.
- Save or download the review as a PDF where this capability is retained.

### 6.5 Remediation Statuses

The workflow must clearly distinguish between:

- Awaiting remediation.
- Remediation in progress.
- Remediation submitted.
- Awaiting supervisor sign-off.
- Rejected for further action.
- Completed with a final outcome.

---

## 7. Supervisor Sign-Off and Final Outcome

### 7.1 Required Workflow

Remediation completion must not automatically mark the case as successfully resolved.

The required workflow is:

```text
Initial Review Outcome
        ↓
Adviser Remediation
        ↓
Supervisor or T&C Manager Review
        ↓
Final Outcome
```

### 7.2 Supervisor Actions

The authorised supervisor or T&C manager must be able to:

- Approve the submitted remediation.
- Reject the submitted remediation.
- Add a decision note.
- Explain what must be corrected when rejecting remediation.
- Record the final regraded outcome.

### 7.3 Final Outcome Options

The final decision must support the relevant outcome values:

- Pass.
- Pass with Issues.
- Insufficient Evidence.
- Potential Harm.

For an Insufficient Evidence outcome:

- If the missing evidence is satisfactorily resolved, the supervisor must be able to regrade the case to Pass.
- If the required evidence cannot be found and the result warrants escalation, the supervisor must be able to regrade the case to Potential Harm.

### 7.4 Decision Data

The solution must record:

- Initial outcome.
- Remediation status.
- Supervisor decision.
- Supervisor comments.
- Rejection reason, where applicable.
- Final outcome.
- Decision date.
- Decision maker.

---

## 8. Reporting and Management Information

### 8.1 Initial and Final Outcomes

Reporting must preserve and distinguish:

- The initial outcome produced by the review.
- The final outcome recorded after remediation and supervisor sign-off.

A final outcome must not overwrite the initial outcome.

### 8.2 Required Reporting Areas

Management reporting must support the agreed operational areas, including:

- Recorded and finalised outcomes.
- Open remediation.
- Overdue remediation.
- Outcome volumes.
- Remediation ageing.
- Sign-off accountability.
- Completed cases.
- Final outcome distribution.

### 8.3 Filtering and Export

Users must be able to filter completed cases using relevant criteria such as:

- Check date.
- Product.
- Outcome.
- Case status.
- Readiness for allocation.

Filtered results must be exportable.

### 8.4 Full Data Export

The application must retain the capability to export system data across the relevant application tables for external reporting, subject to permissions.

### 8.5 Reporting Backlog

High-level requirements for direct database or Power BI reporting must be documented and added to the relevant reporting backlog for prioritisation.

---

## 9. Notifications

### 9.1 Required Notification Events

The solution should support notifications for:

- A new review assignment.
- A remediation assignment.
- Remediation submission.
- Supervisor approval required.
- Remediation rejection.
- Workflow or outcome completion.

### 9.2 Notification Content

Notifications must use business-approved templates and should include:

- Relevant case information.
- The action required from the recipient.
- A direct link to the case, review, remediation, or sign-off action.
- Appropriate wording for the recipient's role.

### 9.3 Sender Account

Automated messages must use a dedicated system email address rather than an individual's mailbox.

### 9.4 Outstanding Business Inputs

The business team must provide:

- Notification templates for remediation recipients.
- Notification templates for reviewers and supervisors.
- The dedicated sender address.

---

## 10. Security, Roles, and Permissions

### 10.1 Reviewer Permissions

AQS and tax reviewer accounts must be able to:

- Access their permitted work areas.
- Open assigned reviews.
- Save responses.
- Submit reviews.

### 10.2 Role Validation

The configured permissions must be validated for the following roles:

- Administrator.
- Advisory Mediation.
- AQS Reviewer.
- Outcome Testing Manager.
- Planner.
- Portal Administrator.
- T&C Supervisor.
- Tax Reviewer.

### 10.3 Landing Page Routing

Users without dashboard access must not be routed to an inaccessible dashboard.

After sign-in, the application must route the user to the first page or work area that the user is authorised to access.

### 10.4 Server-Side Enforcement

Page and capability permissions must be enforced server-side and must not rely only on hiding interface controls.

### 10.5 Audit History

Permission changes, role assignments, case reassignment, status changes, and workflow decisions must remain auditable.

---

## 11. Case Reassignment and Continuity

### 11.1 Reassignment

Authorised users must be able to change the assigned reviewer or case owner.

### 11.2 Preserved Work

Reassignment must not reset the case or remove previous work. The new assignee must be able to continue from the current workflow state.

### 11.3 Auditability

The history must retain:

- The changed field.
- Previous value.
- New value.
- Person making the change.
- Reason for the change.
- Resulting assignment or routing action.

### 11.4 Multiple Daily Uploads

The operational requirement for multiple Intelligent Office uploads in one day remains to be confirmed. No final limit was stated in the meeting notes.

---

## 12. Intelligent Office Intake

### 12.1 Upload Process

The Case Intake area must support upload of the agreed Intelligent Office CSV or Excel extract.

### 12.2 Validation

The intake process must provide visibility of:

- Uploaded extract.
- Row count.
- Processing or completion status.
- Validation exceptions.
- Resolution of invalid rows before or during import, as designed.

### 12.3 Test Version

The newer Intelligent Office export version must be implemented and tested in the test environment before production readiness is confirmed.

---

## 13. Trail Light Export

### 13.1 Export Contract

The outcome-test export must be tested against the agreed Trail Light file specification and column contract.

### 13.2 Export Batches

The application must support generation of dated export batches containing the required closed-case data for reconciliation and downstream use.

### 13.3 Readiness Requirement

The end-of-day export must be validated before results are sent to Trail Light.

---

## 14. User Interface and Usability

### 14.1 Form Navigation

Users must be able to identify:

- The current section and subsection.
- Required questions.
- Failed questions.
- Outstanding actions.
- Current workflow and sign-off status.

### 14.2 Remediation Form Styling

The remediation form requires:

- Correct control alignment.
- Consistent spacing.
- Consistent styling.
- Clear grouping of failed items and actions.
- Clear placement of supervisor decision controls.

### 14.3 User-Friendly Errors

Permission, assignment, validation, and submission failures must produce specific messages explaining what prevented the action and what condition must be corrected.

---

## 15. Test and UAT Readiness

### 15.1 Pre-Test Corrections

Before broader testing begins, the team must complete:

1. Version 8 checklist ordering.
2. Correct section and subsection placement.
3. Failed-item population into remediation.
4. Remediation form redesign.
5. Supervisor regrading and final outcome tracking.
6. Portal and reviewer permission fixes.
7. Landing-page routing fixes.
8. Remediation form alignment and styling.
9. Verification of users, roles, assignments, and environments.

### 15.2 Focused Review

A smaller focused walkthrough must be conducted with the corrected form before the wider end-to-end demonstration.

The review must confirm:

- Checklist sequence.
- Failed-point visibility.
- Remediation usability.
- Supervisor decision behaviour.
- Role and assignment permissions.
- Final outcome recording.

### 15.3 Test Environment

The corrected solution must be deployed to the test environment for hands-on testing.

### 15.4 Defect Tracking

Testers must record defects and usability feedback in the agreed shared tracking mechanism.

### 15.5 End-to-End Scenario

The end-to-end test must cover:

1. Intelligent Office intake.
2. Case validation and import.
3. Case allocation.
4. Tax review, where required.
5. AQS review.
6. Failure and remediation creation.
7. Adviser or planner remediation.
8. Supervisor sign-off or rejection.
9. Final outcome regrading.
10. Notifications.
11. Reporting and export.

---

## 16. Consolidated Acceptance Criteria

The solution is ready for wider UAT when:

- The checklist matches Version 8 in structure and sequence.
- Reviewer accounts can save and submit assigned reviews.
- Failed items automatically appear in remediation.
- The remediation recipient can understand and complete required actions without repeatedly navigating back to the full review.
- A supervisor can approve or reject remediation.
- A supervisor can record the final outcome.
- Initial and final outcomes are stored separately.
- Dashboard and reporting views represent the correct workflow and outcome states.
- Users are routed only to pages they are authorised to access.
- Role, page, assignment, and data permissions operate consistently in the test environment.
- Notifications use approved templates, a dedicated sender, and direct action links.
- The Trail Light export passes validation against the agreed specification.
- Identified defects can be recorded and tracked during UAT.
