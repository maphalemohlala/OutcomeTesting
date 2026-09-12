# Checklist Administration — Question Library UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An administrator can add, retire, edit and move checklist questions, and add, retire and update sections, from the Question library — without the `pac` CLI.

**Architecture:** One page, `QuestionLibraryPage`, gaining a modal-based editor and per-row controls, all calling the six commands from plan two. The UI shows and hides; the plug-in decides. No new visual language — this follows `SecurityModals`, the existing `Modal`, and the `.library__` classes already on the page.

**Tech Stack:** React 18 + TypeScript, Vitest, the existing `Modal` component and `useIntentKeys` hook.

**Spec:** `docs/superpowers/specs/2026-09-11-checklist-administration-design.md`, section 9.

**Depends on:** `docs/superpowers/plans/2026-09-11-checklist-administration-foundation.md` (complete) and `docs/superpowers/plans/2026-09-12-checklist-administration-commands.md` (the six commands and their typed wrappers must exist and be registered, or every control here fails before reaching Dataverse).

## Global Constraints

- **Deploy to `Env_AQ_Dev` only**, and to nothing else, unless the project owner names another environment (direction, 2026-09-12). This plan deploys nothing; the Code App is built and run, not pushed.
- The client is advisory. Every control here is mirrored by a server-side refusal, and the plug-in is the gate (AGENTS.md, AD-041).
- Controls are gated on `can('question.retire', 'Edit')`, the key the six commands check. No new resource key.
- Every command call passes an idempotency key from `useIntentKeys`, and releases it only on success. Minting one per attempt silently disables replay protection (NFR-REL-01).
- `npx tsc -b`, not `tsc --noEmit` — the latter checks nothing in this project.
- Do not import `useReviewDetail` or any generated service from a test; they pull in `@microsoft/power-apps`, which does not resolve under Vitest. Pure logic goes in its own module.
- Follow the existing `.library__` and `.security__` class conventions. This is an administration screen in an established design system, not a new surface.

**Branch:** continue on `feat/checklist-administration`.

---

### Task 1: The library reads in-force status, team and optional

`currentVersionByQuestion` picks the highest version number and never reads the effective dates, so a correctly retired question is listed today as though it were live, with a working Edit button. Sections carry three new columns the hook does not read at all.

**Files:**
- Create: `app/src/features/admin/libraryStatus.ts`
- Create: `app/src/features/admin/libraryStatus.test.ts`
- Modify: `app/src/features/admin/useQuestionLibrary.ts`

**Interfaces:**
- Produces: `inForce(from, to, asOf) -> boolean`, `OWNER_ROLE_LABEL: Record<number, string>`, and on `LibrarySection` / `LibraryQuestion` the new fields `retired: boolean`, `ownerRoleValue: number`, `isOptional: boolean`, `effectiveFrom`, `effectiveTo`

- [ ] **Step 1: Write the failing tests**

Create `app/src/features/admin/libraryStatus.test.ts`:

```typescript
import { describe, expect, it } from 'vitest';
import { inForce, OWNER_ROLE_LABEL } from './libraryStatus';

/**
 * Mirrors SectionRules.IsSectionEffective and ResponseRules.IsVersionEffective, which are
 * authoritative: in force from the start of effective-from until the start of effective-to.
 */
describe('inForce', () => {
  const today = new Date('2026-09-12');

  it('treats an undated row as in force', () => {
    expect(inForce(null, null, today)).toBe(true);
  });

  it('excludes a row dated out today', () => {
    expect(inForce(null, '2026-09-12', today)).toBe(false);
  });

  it('keeps a row dated out tomorrow', () => {
    expect(inForce(null, '2026-09-13', today)).toBe(true);
  });

  it('excludes a row not yet in force', () => {
    expect(inForce('2026-09-13', null, today)).toBe(false);
  });

  it('reads a timestamp as the day it falls on', () => {
    expect(inForce(null, '2026-09-12T23:59:00Z', today)).toBe(false);
  });
});

describe('OWNER_ROLE_LABEL', () => {
  it('names the three roles a section may be owned by', () => {
    expect(OWNER_ROLE_LABEL[120910100]).toBe('Tax');
    expect(OWNER_ROLE_LABEL[120910101]).toBe('AQS');
    expect(OWNER_ROLE_LABEL[120910105]).toBe('Both');
  });
});
```

- [ ] **Step 2: Run to verify failure**

```bash
cd app && npm test -- libraryStatus
```

Expected: FAIL — `Cannot find module './libraryStatus'`.

- [ ] **Step 3: Write the module**

Create `app/src/features/admin/libraryStatus.ts` with `inForce` reducing both dates to their UTC day (copy the `dayOf` approach from `app/src/features/reviews/versionEffective.ts` rather than inventing a second one), and `OWNER_ROLE_LABEL` mapping `120910100` → `Tax`, `120910101` → `AQS`, `120910105` → `Both`.

- [ ] **Step 4: Teach the hook to use it**

In `useQuestionLibrary.ts`:

- select `al_effectivefrom`, `al_effectiveto`, `al_isoptional` on the sections query;
- keep `currentVersionByQuestion` picking the highest version number — it is the *current* version whether or not it is in force — and add `retired: !inForce(version.al_effectivefrom, version.al_effectiveto, new Date())` to `LibraryQuestion`;
- add `retired`, `ownerRoleValue` and `isOptional` to `LibrarySection`;
- do **not** filter retired rows out. The library is where an administrator sees what was retired; Task 6 groups them.

- [ ] **Step 5: Verify and commit**

```bash
cd app && npx tsc -b && npm test
git add app/src/features/admin/libraryStatus.ts app/src/features/admin/libraryStatus.test.ts app/src/features/admin/useQuestionLibrary.ts
git commit -m "feat(admin): the library reads in-force status, team and optional

A retired question was listed as live with a working Edit button, because the
hook picked the highest version number and never read the dates.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: The protected codes, mirrored client-side

The eight codes are advisory here — the plug-in refuses regardless — but the control should not be offered where it cannot work.

**Files:**
- Create: `app/src/features/admin/protectedQuestions.ts`
- Create: `app/src/features/admin/protectedQuestions.test.ts`

- [ ] **Step 1: Write the failing test**

```typescript
import { describe, expect, it } from 'vitest';
import { PROTECTED_QUESTION_CODES, protectedReason } from './protectedQuestions';

/**
 * Mirrors ChecklistGuards in plugins/OutcomeTesting.Plugins, which is authoritative: this
 * copy only hides a control the server would refuse anyway (AD-041).
 */
describe('protectedQuestions', () => {
  it('carries exactly the eight codes the plug-in assembly guards', () => {
    expect([...PROTECTED_QUESTION_CODES].sort()).toEqual(
      [
        'Q-FQ-01',
        'Q-FQ-02',
        'Q-FQ-03',
        'Q-FQTAX-01',
        'Q-FQTAX-02',
        'Q-FQTAX-03',
        'Q-GR-01',
        'Q-TAX-02',
      ].sort(),
    );
  });

  it('explains what a protected code is needed for', () => {
    expect(protectedReason('Q-GR-01')).toContain('grade');
  });

  it('returns null for an ordinary question', () => {
    expect(protectedReason('Q-E1-01')).toBeNull();
  });

  it('matches without regard to case', () => {
    expect(protectedReason('q-fq-01')).not.toBeNull();
  });
});
```

- [ ] **Step 2: Write the module, then pin it to the C# source**

Create `protectedQuestions.ts` with the eight codes and a short reason each. Then add a test that reads the C# and fails if the two lists diverge — this is the thing that stops the mirror going stale:

```typescript
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { PROTECTED_QUESTION_CODES } from './protectedQuestions';

describe('the mirror matches the plug-in assembly', () => {
  it('guards the same codes ChecklistGuards does', () => {
    const csharp = readFileSync(
      new URL('../../../../plugins/OutcomeTesting.Plugins/ChecklistGuards.cs', import.meta.url),
      'utf8',
    );
    const inCsharp = [...csharp.matchAll(/\{ "(Q-[A-Z0-9-]+)",/g)].map((m) => m[1]).sort();

    expect(inCsharp).toEqual([...PROTECTED_QUESTION_CODES].sort());
  });
});
```

Check the relative path resolves from this file's location before assuming it; adjust the number of `../` segments to match.

- [ ] **Step 3: Verify and commit**

```bash
cd app && npx tsc -b && npm test
git add app/src/features/admin/protectedQuestions.ts app/src/features/admin/protectedQuestions.test.ts
git commit -m "feat(admin): mirror the protected question codes, pinned to the C# source"
```

---

### Task 3: The question modal

Replaces the in-row editor. Edits wording, section, response type, mandatory and display order, and dispatches by what changed.

**Files:**
- Create: `app/src/features/admin/QuestionModal.tsx`
- Create: `app/src/features/admin/questionModalIntent.ts`
- Create: `app/src/features/admin/questionModalIntent.test.ts`
- Modify: `app/src/features/admin/QuestionLibraryPage.tsx`, `QuestionLibraryPage.css`

**Interfaces:**
- Produces: `intentFor(draft, original) -> 'none' | 'version' | 'move'`

- [ ] **Step 1: Write the failing tests for the dispatch rule**

The decision of which command to call is the part worth testing; the form itself is markup.

```typescript
import { describe, expect, it } from 'vitest';
import { intentFor } from './questionModalIntent';

const original = {
  wording: 'Is the advice suitable?',
  sectionId: 'sec-1',
  responseType: 120910006,
  mandatory: true,
  displayOrder: 3,
};

describe('intentFor', () => {
  it('does nothing when nothing changed', () => {
    expect(intentFor({ ...original }, original)).toBe('none');
  });

  it('creates a new version when the wording changed', () => {
    expect(intentFor({ ...original, wording: 'Is it suitable?' }, original)).toBe('version');
  });

  it('creates a new version when the response type changed', () => {
    expect(intentFor({ ...original, responseType: 120910008 }, original)).toBe('version');
  });

  it('creates a new version when mandatory changed', () => {
    expect(intentFor({ ...original, mandatory: false }, original)).toBe('version');
  });

  it('creates a new version when the display order changed', () => {
    expect(intentFor({ ...original, displayOrder: 1 }, original)).toBe('version');
  });

  it('moves when the section changed', () => {
    expect(intentFor({ ...original, sectionId: 'sec-2' }, original)).toBe('move');
  });

  it('moves when the section changed and so did everything else', () => {
    // A move carries the wording forward from the retired version, so the move wins and
    // the caller is told the other edits are not applied.
    expect(
      intentFor({ ...original, sectionId: 'sec-2', wording: 'Changed', mandatory: false }, original),
    ).toBe('move');
  });

  it('ignores whitespace-only differences in wording', () => {
    expect(intentFor({ ...original, wording: '  Is the advice suitable?  ' }, original)).toBe(
      'none',
    );
  });
});
```

- [ ] **Step 2: Write `intentFor`, run the tests**

```bash
cd app && npm test -- questionModalIntent
```

Expected: FAIL first, then PASS, 8 tests.

- [ ] **Step 3: Build the modal**

`QuestionModal.tsx` renders `<Modal title=…>` containing a wording textarea, a section `<select>` (all sections in force, from the hook), a response-type `<select>` from `RESPONSE_TYPE_OPTIONS`, a mandatory checkbox and a display-order number input. It takes `mode: 'add' | 'edit'`.

On save:

- `add` → `addQuestion({ sectionId, questionCode, name, wording, responseType, mandatory, displayOrder, effectiveFrom, idempotencyKey })`
- `edit` and `intentFor` is `'version'` → `retireAndSucceedQuestion({ questionId, newWording, responseType, mandatory, displayOrder, idempotencyKey })`
- `edit` and `intentFor` is `'move'` → `moveQuestion({ questionId, targetSectionId, newQuestionCode, reason, idempotencyKey })`
- `'none'` → close without calling anything

**The modal must say what saving will do**, before it is pressed: an edit creates a new version and retires the old one, and a move retires the question here and creates it there with a new code. A section change reveals the new-code field and the reason field, both required.

Release the intent key only on success.

- [ ] **Step 4: Wire it into the page and remove the in-row editor**

`QuestionRow` loses its inline `editing` state and gains an **Edit** button that opens the modal. **Add question** goes in each section header, opening the same modal in `add` mode with the section fixed.

- [ ] **Step 5: Verify and commit**

```bash
cd app && npx tsc -b && npm test
git add app/src/features/admin/
git commit -m "feat(admin): the question modal, dispatching by what changed"
```

---

### Task 4: Retire a question

**Files:**
- Create: `app/src/features/admin/RetireModal.tsx`
- Modify: `QuestionLibraryPage.tsx`

- [ ] **Step 1: Build the confirm**

`RetireModal` takes a subject (`'question' | 'section'`), a name, a **required** reason textarea and a stops-from date defaulting to today. It refuses to submit with an empty reason, saying so, rather than letting the server refuse a round trip later.

- [ ] **Step 2: Add the control, gated twice**

On each question row, beside Edit, a **Retire** button shown when `canEdit` and the code is not protected. A protected question shows, in its place, a chip reading **Required by grading** whose `title` is `protectedReason(code)`.

- [ ] **Step 3: Verify and commit**

```bash
cd app && npx tsc -b && npm test
git add app/src/features/admin/
git commit -m "feat(admin): retire a question, with a mandatory reason"
```

---

### Task 5: Section controls

**Files:**
- Create: `app/src/features/admin/SectionModal.tsx`
- Modify: `QuestionLibraryPage.tsx`, `QuestionLibraryPage.css`

- [ ] **Step 1: Build the section modal**

Takes `mode: 'add' | 'edit'` and renders: name, help text, a **team** `<select>` of Tax / AQS / Both, display order, and an **Optional / Required** toggle. In `add` mode it also renders a repeatable question list — wording, response type, mandatory — so a section arrives with its questions, and marshals them as `questions` for `addSection`.

Two sentences the modal must carry, because neither is recoverable by guessing:

- beside **team**: changing it reaches submitted reviews, because owner role is not versioned — a section switched from Tax to AQS stops rendering in submitted Tax reviews and starts rendering in AQS ones that never answered it. **Both** adds a discipline without taking one away.
- beside **Optional**: it takes effect on every unsubmitted review immediately; there is no effective date on the flag.

- [ ] **Step 2: Add the controls**

**Add section** at the top of the library. **Edit section** and **Retire section** in each section header, the retire reusing `RetireModal` with subject `'section'`.

- [ ] **Step 3: Verify and commit**

```bash
cd app && npx tsc -b && npm test
git add app/src/features/admin/
git commit -m "feat(admin): add, edit and retire sections, including the team"
```

---

### Task 6: Show what is retired, and who owns what

**Files:**
- Modify: `QuestionLibraryPage.tsx`, `QuestionLibraryPage.css`

- [ ] **Step 1: Group retired content**

Within each section, questions in force render as now; retired ones move into a collapsed `<details>` headed **Retired (n)**. A retired section renders the whole section inside such a block, at the end of the list. Retired rows carry no Edit or Retire control — there is nothing to do to them, and offering an action that would fail is worse than offering none.

- [ ] **Step 2: Show team and optional in every section header**

Each header shows its team (`Tax`, `AQS`, `Both`) and, where set, an **Optional** marker. "Who answers this" is the first thing an administrator needs and is invisible today.

- [ ] **Step 3: Verify and commit**

```bash
cd app && npx tsc -b && npm test
git add app/src/features/admin/
git commit -m "feat(admin): group retired content and show each section's team"
```

---

### Task 7: Run it, and prove the whole loop

- [ ] **Step 1: Build and run the app against DEV**

```bash
cd app && npx tsc -b && npm run build && npm run dev
```

- [ ] **Step 2: Exercise every control once, as an administrator**

Add a scratch section owned by **Both** with two questions; confirm it appears in the library with its team. Edit it to Optional. Add a question to it. Edit that question's wording and confirm the version number rises. Move it to another section. Retire it, and confirm it drops into the Retired block. Retire the scratch section.

Then open a Tax review and an AQS review in the Code App and confirm the Both section renders in each and accepts an answer in each — **this is the end-to-end proof the foundation plan could not make**, because nothing could set a section to Both until now.

- [ ] **Step 3: Confirm a protected question cannot be retired**

Find `Q-GR-01` in the library. Confirm it shows the **Required by grading** chip and no Retire control. Then call `retireQuestion` against it directly from the browser console, to confirm the **server** refuses it too — the UI is advisory and the plug-in is the gate.

- [ ] **Step 4: Write the deployment note**

`docs/deployment/2026-09-12-checklist-administration-ui.md`, recording what was exercised, the ids created and retired, and the end-to-end Both proof. Follow the house pattern: what ran, what was verified, what was not.

- [ ] **Step 5: Commit**

```bash
git add docs/deployment/2026-09-12-checklist-administration-ui.md
git commit -m "docs: record the checklist administration UI proof"
```

---

## Done when

- An administrator can add, edit, retire and move a question, and add, edit and retire a section including its team and optional flag, entirely from the Question library.
- A protected question offers no Retire control, and the server refuses one anyway.
- Retired content is visible as retired and cannot be edited.
- A Both section renders in a Tax review and an AQS review and accepts an answer in each.
- `npx tsc -b` and the app suite pass.

## Deliberately not in this plan

- **Un-retire.** Anything dated out stays out; putting it back is an add.
- **Reordering by drag.** Display order is a number field; a drag-reorder writes a new version per question moved, which is a separate decision.
- **Administering the checklist from the portal.** PP-09 keeps this in the Code App.
- **Any promotion beyond DEV.**
