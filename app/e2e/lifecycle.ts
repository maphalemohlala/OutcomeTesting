import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { Frame, Locator, Page } from '@playwright/test';
import { env, expect, expectNoLiquidError, expectSignedIn } from './portal';
import { TAX_CHECK_REQUIRED_YES, parseCaseCsv } from '../src/features/imports/caseUpload';

/**
 * Helpers for the PROD full-lifecycle spec (`prod-lifecycle.e2e.ts`).
 *
 * Everything here that WRITES is reached only through that spec, and that spec runs only
 * behind `lifecycleGate()`. Nothing about any environment is written down: hosts, the Code
 * App and the tester arrive as environment variables, as they do for the rest of the suite.
 *
 * Selectors are the ones the templates and the app actually render, read from
 * `powerpages/.../web-templates/ot-review-detail`, `ot-remediation` and `app/src/features`.
 */

// Loaded as an ES module, where __dirname does not exist - the config derives it the same way.
const HERE = dirname(fileURLToPath(import.meta.url));

/* ---------------------------------------------------------------- the gate */

/** The one value that lets the lifecycle spec write. Anything else and it skips. */
export const WRITE_FLAG = 'OT_E2E_WRITE';
export const WRITE_VALUE = 'PROD-LIFECYCLE';
/** The portal host the person running it means to write to; must equal OT_PORTAL_URL's host. */
export const TARGET = 'OT_E2E_TARGET';
/** The tester's own mailbox: adviser, paraplanner, checker and T&C Manager on the test case. */
export const TESTER_EMAIL = 'OT_E2E_TESTER_EMAIL';
export const PORTAL = 'OT_PORTAL_URL';
export const CODEAPP = 'OT_CODEAPP_URL';
/** Optional: this environment's product name, for the manager web role. Both are accepted otherwise. */
export const PRODUCT = 'OT_E2E_PRODUCT';

const EMAIL_SHAPE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** A host from a host or a URL, lower-cased, with no scheme, path or trailing dot. */
export function hostOf(value: string): string {
  const trimmed = value.trim().toLowerCase();
  const withScheme = /^[a-z]+:\/\//.test(trimmed) ? trimmed : `https://${trimmed}`;
  try {
    return new URL(withScheme).host.replace(/\.$/, '');
  } catch {
    return trimmed;
  }
}

/**
 * Why the lifecycle spec must not run, or null when it may.
 *
 * Every condition is a reason to SKIP, never to fail: the default `npm run e2e` has none of
 * these set, and it must report the spec as skipped and write nothing.
 */
export function lifecycleGate(): string | null {
  if (env(WRITE_FLAG) !== WRITE_VALUE) {
    return `writes to a live environment; skipped unless ${WRITE_FLAG}=${WRITE_VALUE} - see app/e2e/PROD-LIFECYCLE.md`;
  }

  const missing = [PORTAL, CODEAPP, TARGET, TESTER_EMAIL].filter((name) => env(name) === undefined);
  if (missing.length > 0) {
    return `not configured: ${missing.join(', ')} - see app/e2e/PROD-LIFECYCLE.md`;
  }

  const portalHost = hostOf(env(PORTAL) as string);
  const target = hostOf(env(TARGET) as string);
  if (portalHost !== target) {
    return `${TARGET} is "${target}" but ${PORTAL} points at "${portalHost}"; they must name the same host`;
  }

  if (!EMAIL_SHAPE.test(env(TESTER_EMAIL) as string)) {
    return `${TESTER_EMAIL} is not an email address`;
  }

  return null;
}

/** A gated value; only call after `lifecycleGate()` returned null. */
export function gated(name: string): string {
  const value = env(name);
  if (value === undefined) {
    throw new Error(`${name} is not set; the lifecycle gate should have skipped this run`);
  }
  return value;
}

export function portalBase(): string {
  return gated(PORTAL).replace(/\/+$/, '');
}

export function testerEmail(): string {
  return gated(TESTER_EMAIL).trim();
}

export function sameEmail(a: string | null | undefined, b: string | null | undefined): boolean {
  return (a ?? '').trim().toLowerCase() === (b ?? '').trim().toLowerCase() && (a ?? '').trim() !== '';
}

export const SESSION_FILE = resolve(HERE, '.auth/portal.json');

/* ------------------------------------------------------------- the test case */

/** What the case says about itself, so nobody actions it and anyone can find it. */
export const CLIENT_NAME = 'E2E TEST - do not action';

/**
 * Names that are deliberately NOT the tester's. People are identified by email (the owner's
 * 2026-10-02 direction): the remediation must still reach the tester through the email, and
 * the People page must still show them once. A name that matched would hide a name match.
 */
export const ADVISER_NAME = 'E2E TEST Adviser';
export const PARAPLANNER_NAME = 'E2E TEST Paraplanner';

const pad = (n: number) => String(n).padStart(2, '0');

/** 999 + yyMMddHHmm, local time: unique per minute and recognisably a test. */
export function caseReference(now: Date = new Date()): string {
  return `999${pad(now.getFullYear() % 100)}${pad(now.getMonth() + 1)}${pad(now.getDate())}${pad(now.getHours())}${pad(now.getMinutes())}`;
}

export function isoDay(now: Date = new Date()): string {
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

function csvCell(value: string): string {
  return /[",\r\n]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value;
}

/**
 * One Intelligent Office task extract row for the test case, under the sample extract's own
 * header, routed Tax then AQS by its one stamped checklist item ("Tax Check").
 *
 * Checked with the app's own parser before it is returned: a file the Code App would refuse
 * is a defect in this spec, and saying so here is better than an upload error in PROD.
 */
export function extractCsv(ref: string, email: string, now: Date = new Date()): string {
  const sample = readFileSync(resolve(HERE, '../../data/io-task-extract-sample.csv'), 'utf8');
  const header = sample.split(/\r?\n/)[0].split(',');
  const day = isoDay(now);

  const values: Record<string, string> = {
    ActivityType: 'Task',
    TaskCategory: 'E2E TEST',
    TaskType: 'Pre-Advice Check Required',
    Subject: CLIENT_NAME,
    LegalEntity: 'Organisation',
    Group1: 'Organisation',
    AssignedBy: PARAPLANNER_NAME,
    ParaplannerEmail: email,
    Client: CLIENT_NAME,
    ClientRef: `E2E-${ref}`,
    AdviserName: ADVISER_NAME,
    AdviserEmail: email,
    Status: 'Not Started',
    StartDate: day,
    DueDate: day,
    Notes: 'E2E TEST - do not action. Automated lifecycle run; see app/e2e/PROD-LIFECYCLE.md.',
    CreatedDate: day,
    CreatedBy: PARAPLANNER_NAME,
    ServiceCaseSequentialRef: `E2E-${ref}`,
    VisibleToClient: 'No',
    TaskID: ref,
    WorkflowName: 'Pre-Check',
    ChecklistItem1: 'Tax Check',
    CompletedBy1: PARAPLANNER_NAME,
    CompletionDate1: `${day}T09:00:00`,
  };

  for (const key of Object.keys(values)) {
    if (!header.includes(key)) {
      throw new Error(`the sample extract has no "${key}" column - data/io-task-extract-sample.csv changed`);
    }
  }

  const csv = `${header.join(',')}\r\n${header.map((h) => csvCell(values[h] ?? '')).join(',')}\r\n`;

  const parsed = parseCaseCsv(csv);
  if (parsed.fatal || parsed.invalid.length > 0 || parsed.valid.length !== 1) {
    throw new Error(`the generated extract does not pass the Code App's own check: ${
      parsed.fatal ?? parsed.invalid.map((row) => row.reason).join('; ')}`);
  }
  const record = parsed.valid[0].record;
  if (record.al_taxcheckrequired !== TAX_CHECK_REQUIRED_YES || record.al_casereference !== ref) {
    throw new Error('the generated extract would not route Tax then AQS under its own reference');
  }

  return csv;
}

/* ------------------------------------------------------------------ the log */

export interface StepRecord {
  title: string;
  status: string;
  at: string;
  durationMs: number;
  error?: string;
}

export interface RunLog {
  ref: string;
  startedAt: string;
  updatedAt: string;
  portal: string;
  codeApp: string;
  target: string;
  testerEmail: string;
  testerName?: string;
  portalRoles?: string[];
  caseId?: string;
  taxReviewId?: string;
  aqsReviewId?: string;
  importBatch?: string;
  notes: string[];
  steps: StepRecord[];
}

export const RUNS_DIR = resolve(HERE, '.runs');

/**
 * The record of one run, written to `e2e/.runs/<ref>.json` after every step and whenever an
 * id is learned, so a run that dies half-way still says what it created.
 */
export class RunLogger {
  readonly data: RunLog;

  constructor(ref: string) {
    const now = new Date().toISOString();
    this.data = {
      ref,
      startedAt: now,
      updatedAt: now,
      portal: env(PORTAL) ?? '',
      codeApp: env(CODEAPP) ?? '',
      target: env(TARGET) ?? '',
      testerEmail: env(TESTER_EMAIL) ?? '',
      notes: [],
      steps: [],
    };
  }

  get file(): string {
    return resolve(RUNS_DIR, `${this.data.ref}.json`);
  }

  set(values: Partial<RunLog>): void {
    Object.assign(this.data, values);
    this.write();
  }

  note(text: string): void {
    this.data.notes.push(`${new Date().toISOString()} ${text}`);
    this.write();
  }

  step(record: StepRecord): void {
    this.data.steps.push(record);
    this.write();
  }

  write(): void {
    this.data.updatedAt = new Date().toISOString();
    mkdirSync(RUNS_DIR, { recursive: true });
    writeFileSync(this.file, `${JSON.stringify(this.data, null, 2)}\n`, 'utf8');
  }
}

export function sessionFilePresent(): boolean {
  return existsSync(SESSION_FILE);
}

/* ---------------------------------------------------------------- waiting */

/** A short pause inside a poll; never a fixed wait for something to have happened. */
async function pause(page: Page, ms: number): Promise<void> {
  await page.waitForTimeout(ms);
}

const RERUN_AUTH = 'run `npm run e2e:auth` from app/ with OT_PORTAL_URL set to this portal, then retry';

/* ------------------------------------------------------------- the Code App */

/**
 * The frame the Code App runs in: the one holding React's root, two iframes inside the
 * player. Fails with the re-sign-in instruction when Entra has sent the browser to a
 * sign-in page instead, which is how an expired session looks from here.
 */
export async function openCodeApp(page: Page): Promise<Frame> {
  await page.goto(gated(CODEAPP));
  const deadline = Date.now() + 90_000;

  while (Date.now() < deadline) {
    const host = new URL(page.url()).host.toLowerCase();
    if (host.includes('login.microsoftonline.com') || host.includes('login.live.com')) {
      throw new Error(`the Code App sent the browser to Entra sign-in (${host}) - the session in e2e/.auth/portal.json has expired; ${RERUN_AUTH}`);
    }

    for (const frame of page.frames()) {
      const state = await frame
        .evaluate(() => ({
          root: !!document.querySelector('#root nav, #root main'),
          text: (document.body?.innerText ?? '').slice(0, 4000),
        }))
        .catch(() => null);
      if (!state) { continue; }
      if (/Enter password|Pick an account|Sign in to your account/i.test(state.text)) {
        throw new Error(`the Code App is asking for a sign-in ("Enter password" / "Pick an account") - the Entra cookies have expired; ${RERUN_AUTH}`);
      }
      if (state.root) { return frame; }
    }

    await pause(page, 1_000);
  }

  throw new Error(`the Code App never rendered at ${gated(CODEAPP)} - check OT_CODEAPP_URL, or the session has expired (${RERUN_AUTH})`);
}

/** HashRouter navigation inside the app frame. */
export async function go(frame: Frame, hash: string): Promise<void> {
  await frame.evaluate((target) => { window.location.hash = target; }, hash);
}

/** Navigates away and back, which remounts the page and re-reads Dataverse. */
export async function reopen(frame: Frame, hash: string): Promise<void> {
  await go(frame, '#/__e2e_reload__');
  await go(frame, hash);
}

/**
 * Fails, naming the permission, when a route renders RequirePermission's "No access" screen
 * (PageUnavailable, which lists "Permission: <resource> (<level>)" as what blocks it).
 */
export async function expectNotUnavailable(frame: Frame, what: string): Promise<void> {
  const unavailable = frame.locator('section.page-unavailable');
  if ((await unavailable.count()) > 0 && (await unavailable.first().isVisible().catch(() => false))) {
    const said = (await unavailable.first().innerText()).replace(/\s+/g, ' ').trim();
    throw new Error(`${what}: the Code App shows "No access" - ${said.slice(0, 400)}`);
  }
}

/** The case id from the worklist, found by searching the reference. Polls while Dataverse catches up. */
export async function findCaseId(frame: Frame, ref: string, timeoutMs = 120_000): Promise<string> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    await reopen(frame, `#/cases?q=${encodeURIComponent(ref)}`);
    await frame.locator('table.worklist').waitFor({ timeout: 60_000 });
    const link = frame.locator('table.worklist tbody th a', { hasText: new RegExp(`^${ref}$`) });
    if ((await link.count()) > 0) {
      const href = (await link.first().getAttribute('href')) ?? '';
      const id = /#\/cases\/([0-9a-f-]{36})/i.exec(href)?.[1];
      if (id) { return id.toLowerCase(); }
    }
    await frame.page().waitForTimeout(5_000);
  }
  throw new Error(`case ${ref} is not on the Code App worklist`);
}

export async function openCase(frame: Frame, caseId: string): Promise<void> {
  await reopen(frame, `#/cases/${caseId}`);
  await frame.locator('section[aria-label="Case summary"]').waitFor({ timeout: 60_000 });
}

/** One value from the case summary strip, by its label. */
export async function summaryValue(frame: Frame, label: string): Promise<string> {
  const item = frame
    .locator('section[aria-label="Case summary"] .case-detail__summary-item')
    .filter({ has: frame.locator('.case-detail__summary-label', { hasText: new RegExp(`^${label}$`) }) });
  const text = await item.locator('.stage, .case-detail__summary-value').first().innerText();
  return text.trim();
}

/** Re-reads the case until its status is one of `wanted`, returning the status seen. */
export async function waitCaseStatus(frame: Frame, caseId: string, wanted: readonly string[], timeoutMs = 180_000): Promise<string> {
  const deadline = Date.now() + timeoutMs;
  let seen = '';
  while (Date.now() < deadline) {
    await openCase(frame, caseId);
    seen = await summaryValue(frame, 'Status');
    if (wanted.includes(seen)) { return seen; }
    await frame.page().waitForTimeout(5_000);
  }
  throw new Error(`the Code App shows the case at "${seen}", not ${wanted.join(' / ')}`);
}

export interface CheckRow {
  id: string;
  reference: string;
  type: string;
  status: string;
  owner: string;
}

/** "Checks on this case", as the case page lists them. */
export async function checksOnCase(frame: Frame): Promise<CheckRow[]> {
  const table = frame.locator('table.case-detail__checks-table');
  if ((await table.count()) === 0) { return []; }
  return table.locator('tbody tr').evaluateAll((rows) =>
    rows.map((row) => {
      const link = row.querySelector('th a');
      const href = link?.getAttribute('href') ?? '';
      const cells = [...row.querySelectorAll('td')].map((td) => (td.textContent ?? '').trim());
      return {
        id: (/#\/reviews\/([0-9a-f-]{36})\//i.exec(href)?.[1] ?? '').toLowerCase(),
        reference: (row.querySelector('th')?.textContent ?? '').trim(),
        type: cells[0] ?? '',
        status: cells[1] ?? '',
        owner: cells[2] ?? '',
      };
    }));
}

/**
 * Allocates one check through the case's edit modal - the same al_AssignCase the app's own
 * Save button sends - and returns the review it opened.
 */
export async function allocateCheck(
  frame: Frame,
  caseId: string,
  discipline: 'Tax' | 'AQS',
  email: string,
  reason: string,
): Promise<CheckRow> {
  await openCase(frame, caseId);
  await frame.getByRole('button', { name: 'Edit case details' }).click();
  const dialog = frame.getByRole('dialog', { name: 'Edit case details' });
  await dialog.waitFor({ timeout: 30_000 });

  const select = dialog.locator(`#case-edit-checker-${discipline}`);
  try {
    await select.waitFor({ timeout: 45_000 });
  } catch {
    const checkers = dialog.locator('fieldset', { has: frame.locator('legend', { hasText: /^Checkers$/ }) });
    const said = (await checkers.count()) > 0 ? (await checkers.innerText()).replace(/\s+/g, ' ').trim() : 'no Checkers section at all - the account lacks the allocate permission';
    throw new Error(`the ${discipline} check cannot be allocated from this account: ${said}`);
  }

  const values = await select.locator('option').evaluateAll((options) =>
    options.map((option) => (option as HTMLOptionElement).value));
  const value = values.find((candidate) => sameEmail(candidate, email));
  if (!value) {
    throw new Error(`${email} is not offered as a ${discipline} checker - the tester must be an active person in the directory`);
  }
  await select.selectOption({ value });
  await dialog.locator('#case-edit-reason').fill(reason);
  await dialog.getByRole('button', { name: 'Save changes' }).click();

  // The modal closes on success and stays open with its error otherwise.
  const deadline = Date.now() + 90_000;
  while (Date.now() < deadline && (await dialog.isVisible().catch(() => false))) {
    const error = dialog.locator('[role="alert"]');
    if ((await error.count()) > 0 && (await error.first().isVisible())) {
      throw new Error(`allocating the ${discipline} check was refused: ${(await error.first().innerText()).trim()}`);
    }
    await frame.page().waitForTimeout(500);
  }
  if (await dialog.isVisible().catch(() => false)) {
    throw new Error(`the edit modal never closed after allocating the ${discipline} check`);
  }

  // And the check it opened, once the case re-reads it.
  const until = Date.now() + 120_000;
  while (Date.now() < until) {
    await openCase(frame, caseId);
    await frame.locator('#panel-checks').waitFor({ timeout: 30_000 });
    const found = (await checksOnCase(frame)).find((row) => row.type === discipline && row.id !== '');
    if (found && found.owner !== 'Unassigned') { return found; }
    await frame.page().waitForTimeout(5_000);
  }
  throw new Error(`no allocated ${discipline} check appeared under "Checks on this case"`);
}

/* ------------------------------------------------------------- the portal */

export interface PortalIdentity {
  name: string;
  email: string;
  roles: string[] | null;
  source: string;
}

/**
 * Who the portal session is, read off a page the portal renders for every signed-in user.
 *
 * The site's Access Denied page (OT Access Denied) prints "You are signed in as <name>
 * (<email>)" and "Your roles on this site: ..." - the roles the access rules are actually
 * evaluated against. It is read at its own address first, then through Profile, which is
 * refused to every role and so renders the same page.
 */
export async function readPortalIdentity(page: Page): Promise<PortalIdentity> {
  const portal = portalBase();
  for (const path of ['/access-denied', '/profile', '/']) {
    await page.goto(`${portal}${path}`);
    const text = (await page.locator('body').innerText()).replace(/\s+/g, ' ');
    const who = /signed in as\s+(.+?)\s*\(([^()\s]+@[^()\s]+)\)/i.exec(text);
    if (!who) { continue; }
    const rolesText = /Your roles(?: on this site)?:\s*([^.]+)\./i.exec(text)?.[1]?.trim();
    const roles = rolesText === undefined
      ? null
      : rolesText.toLowerCase() === 'none' ? [] : rolesText.split(/\s*,\s*/).filter((r) => r !== '');
    return { name: who[1].trim(), email: who[2].trim(), roles, source: path };
  }
  throw new Error('could not read the signed-in email from the portal (none of /access-denied, /profile or / says "signed in as <name> (<email>)") - the run refuses to write without proving who it is');
}

/**
 * The same GUID in a different letter case for each attempt.
 *
 * The portal caches a Liquid fetch per query TEXT for up to ~15 minutes (AD-094), and every
 * page here feeds the id from the URL straight into its fetch. A new casing is a new query,
 * so it reads Dataverse rather than the cache - which is the only way to re-read a page
 * after a server-side change without waiting the cache out.
 */
export function guidVariant(id: string, attempt: number): string {
  const lower = id.toLowerCase();
  if (attempt === 0) { return lower; }
  if (attempt === 1) { return lower.toUpperCase(); }
  let letter = 0;
  return [...lower].map((ch) => {
    if (!/[a-f]/.test(ch)) { return ch; }
    const upper = ((attempt >> (letter % 16)) & 1) === 1;
    letter += 1;
    return upper ? ch.toUpperCase() : ch;
  }).join('');
}

/**
 * Loads `urlFor(attempt)` until `ready` says the page shows what is wanted, with a fresh
 * cache key each time. Backs off from 5s to 30s; the default ceiling covers the portal's
 * cache window, which is the slowest legitimate reason for the page to lag.
 */
export async function pollPortal(
  page: Page,
  label: string,
  urlFor: (attempt: number) => string,
  ready: () => Promise<boolean>,
  timeoutMs = 17 * 60_000,
): Promise<number> {
  const portal = portalBase();
  const deadline = Date.now() + timeoutMs;
  let attempt = 0;
  let delay = 5_000;
  for (;;) {
    await page.goto(urlFor(attempt));
    await expectSignedIn(page, portal);
    await expectNoLiquidError(page);
    if (await ready()) { return attempt; }
    if (Date.now() + delay > deadline) {
      throw new Error(`${label}: still not shown after ${Math.round(timeoutMs / 60_000)} minutes and ${attempt + 1} fresh reads`);
    }
    await pause(page, delay);
    delay = Math.min(delay * 2, 30_000);
    attempt += 1;
  }
}

export function reviewUrl(reviewId: string, attempt = 0): string {
  return `${portalBase()}/review?id=${guidVariant(reviewId, attempt)}`;
}

export function remediationCaseUrl(caseId: string, attempt = 0): string {
  return `${portalBase()}/remediation?case=${guidVariant(caseId, attempt)}`;
}

/**
 * The remediation list searched by a piece of the reference, with and without the "open"
 * stage filter: each pairing is a different fetch, so a different cache entry. Twelve
 * pairings, then they repeat.
 */
export function remediationListUrl(ref: string, attempt = 0): string {
  const variant = attempt % 12;
  const piece = ref.slice(Math.floor(variant / 2));
  const stage = variant % 2 === 1 ? 'stage=open&' : '';
  return `${portalBase()}/remediation?${stage}ref=${encodeURIComponent(piece)}`;
}

/**
 * Runs `action` on an autosaving control and waits for THIS save to land.
 *
 * The status line is blanked first, so the "Saved" awaited is this save's and not an earlier
 * one's. Anything it says other than Saving/Saved is the page reporting a refusal, and is
 * failed on at once with the page's own words.
 */
export async function withSave(page: Page, status: Locator, label: string, action: () => Promise<void>): Promise<void> {
  const hasStatus = (await status.count()) > 0;
  const write = page
    .waitForResponse((r) => r.url().includes('/_api/') && r.request().method() !== 'GET', { timeout: 60_000 })
    .catch(() => null);

  if (hasStatus) { await status.first().evaluate((el) => { el.textContent = ''; }); }
  await action();

  if (!hasStatus) {
    const response = await write;
    if (!response) { throw new Error(`${label}: no save was sent`); }
    if (!response.ok()) { throw new Error(`${label}: the save was refused (${response.status()})`); }
    return;
  }

  const deadline = Date.now() + 60_000;
  let said = '';
  while (Date.now() < deadline) {
    said = ((await status.first().textContent()) ?? '').replace(/\s+/g, ' ').trim();
    if (/^Saved/.test(said)) { return; }
    if (said !== '' && !/^Saving/.test(said)) {
      throw new Error(`${label}: the page said "${said}"`);
    }
    await pause(page, 250);
  }
  throw new Error(`${label}: never reported "Saved" (last said "${said}")`);
}

/* ------------------------------------------------------------ the review page */

/** Response types (al_responsetype), as OT Answer Options draws them. */
const RT = {
  passFail: '120910005',
  taxScale: '120910006',
  yesNo: '120910007',
  yesNoNa: '120910008',
  yesNoInsufficient: '120910009',
  grade: '120910010',
  suitability: '120910012',
  rootCauseSingle: '120910003',
  rootCauses: '120910013',
} as const;

/** Answer values (al_answerchoice). */
export const CHOICE = {
  pass: '120910300',
  fail: '120910301',
  insufficient: '120910302',
  passWithIssues: '120910303',
  potentialHarm: '120910304',
  yes: '120910305',
  no: '120910306',
  na: '120910307',
  rootCauseProcess: '120910325',
} as const;

/** A non-pass grade, mildest first; the first the page has not locked is chosen. */
const FAILING_GRADES = [CHOICE.passWithIssues, CHOICE.insufficient, CHOICE.potentialHarm];

/** The clean answer per question where the code decides it. */
const BY_CODE: Record<string, string> = {
  'Q-TAX-02': CHOICE.pass,
  'Q-FQTAX-01': CHOICE.pass,
  'Q-FQ-01': CHOICE.pass,
  // "Remedial action required?" follows the file quality outcome: a Pass takes No only.
  'Q-FQTAX-03': CHOICE.no,
  'Q-FQ-03': CHOICE.no,
};

/** Optional questions answered anyway, because the run is about them. */
const ALSO_ANSWER = new Set(['Q-TAX-03', 'Q-TAX-04', 'Q-GR-02', 'Q-GR-03', 'Q-GR-04']);

/** The clean answer for a response type: the pass end of its scale. */
function cleanFor(type: string): string[] {
  switch (type) {
    case RT.passFail:
    case RT.taxScale:
    case RT.suitability:
      return [CHOICE.pass];
    case RT.yesNo:
    case RT.yesNoNa:
    case RT.yesNoInsufficient:
      return [CHOICE.yes];
    default:
      return [];
  }
}

interface RowInfo {
  code: string;
  type: string;
  column: string;
  required: boolean;
  hidden: boolean;
  lens: boolean;
  missing: boolean;
  answered: boolean;
  editable: boolean;
}

async function rowInfo(row: Locator): Promise<RowInfo> {
  return row.evaluate((el) => {
    const element = el as HTMLElement;
    const inputs = [...element.querySelectorAll<HTMLInputElement>('[data-ot-input]')];
    const rich = element.querySelector<HTMLElement>('[data-ot-rich]');
    const answered = inputs.some((input) => {
      if (input.type === 'radio' || input.type === 'checkbox') { return input.checked; }
      if (input.tagName === 'TEXTAREA' || input.tagName === 'INPUT') { return (input.value ?? '').trim() !== ''; }
      return false;
    }) || (!!rich && (rich.textContent ?? '').trim() !== '');
    return {
      code: (element.getAttribute('data-question-code') ?? '').toUpperCase(),
      type: element.getAttribute('data-response-type') ?? '',
      column: element.getAttribute('data-column') ?? '',
      required: !!element.querySelector('.ot-required'),
      hidden: !!element.closest('[hidden]') || element.getClientRects().length === 0,
      lens: element.classList.contains('lens'),
      missing: element.classList.contains('ot-answer--missing'),
      answered,
      editable: inputs.some((input) => !input.disabled) || (!!rich && rich.isContentEditable),
    };
  });
}

/** The first of `values` this row offers and has not disabled. */
async function firstEnabled(row: Locator, values: readonly string[]): Promise<Locator | null> {
  for (const value of values) {
    const input = row.locator(`[data-ot-input][value="${value}"]`);
    if ((await input.count()) > 0 && (await input.first().isEnabled())) { return input.first(); }
  }
  return null;
}

/**
 * Answers one row the clean way, unless it is the grade, which takes the first non-pass the
 * page still offers. Returns what it chose, for the log.
 */
async function answerRow(page: Page, row: Locator, info: RowInfo, text: string): Promise<string> {
  const status = row.locator('[data-ot-status]');
  const label = `${info.code || 'a question'}`;

  if (info.column === 'richtext') {
    await withSave(page, status, label, () => row.locator('[data-ot-rich]').fill(text));
    return 'rich text';
  }

  if (info.column === 'text') {
    await withSave(page, status, label, () => row.locator('textarea[data-ot-input], input[type="text"][data-ot-input]').first().fill(text));
    return 'text';
  }

  if (info.column === 'date') {
    await withSave(page, status, label, () => row.locator('input[type="date"][data-ot-input]').first().fill(isoDay()));
    return 'date';
  }

  if (info.lens) {
    await withSave(page, status, label, () => row.locator('input[type="checkbox"][data-ot-input]').first().check());
    return 'yes (lens)';
  }

  if (info.type === RT.rootCauses || info.type === RT.rootCauseSingle) {
    const cause = await firstEnabled(row, [CHOICE.rootCauseProcess]);
    if (!cause) { throw new Error(`${label}: "Process / documentation" is not offered as a root cause`); }
    await withSave(page, status, label, () => cause.check());
    return 'Process / documentation';
  }

  if (info.column === 'choices') {
    // Tax check reason, a multi-select: the first reason offered.
    const box = row.locator('input[type="checkbox"][data-ot-input]:not([disabled])').first();
    if ((await box.count()) === 0) { throw new Error(`${label}: no reason can be ticked`); }
    await withSave(page, status, label, () => box.check());
    return 'first reason';
  }

  const wanted = info.type === RT.grade ? FAILING_GRADES : (BY_CODE[info.code] ? [BY_CODE[info.code]] : cleanFor(info.type));
  const input = await firstEnabled(row, wanted);
  if (!input) {
    throw new Error(`${label}: none of ${wanted.join(', ')} is offered (response type ${info.type}) - the checklist differs from the one this spec answers`);
  }
  if (await input.isChecked()) { return `${await input.getAttribute('value')} (already)`; }
  const value = (await input.getAttribute('value')) ?? '';
  await withSave(page, status, label, () => input.check());
  return value;
}

export interface AnswerRecord { code: string; answer: string }

/**
 * Answers every required question on the open review, plus the few optional ones this run is
 * about, on the clean side of each scale - so the only non-pass on the review is the grade,
 * and no gating rule is provoked. `onlyMissing` re-answers just the rows a refused submit
 * marked "Required - not answered".
 */
export async function answerReview(page: Page, text: string, onlyMissing = false): Promise<AnswerRecord[]> {
  const done: AnswerRecord[] = [];
  const rows = page.locator('[data-ot-answer]');
  const count = await rows.count();
  for (let i = 0; i < count; i += 1) {
    const row = rows.nth(i);
    const info = await rowInfo(row);
    const wanted = onlyMissing ? info.missing : (info.required || ALSO_ANSWER.has(info.code));
    if (!wanted || info.hidden || !info.editable || (info.answered && !info.missing)) { continue; }
    if (info.code === 'Q-TAX-04' && !onlyMissing) { continue; } // the Clear check writes this one
    done.push({ code: info.code, answer: await answerRow(page, row, info, text) });
  }
  return done;
}

/** Writes a remedial action against every row of the card, when the card is owed. */
export async function writeRemedialActions(page: Page, text: string): Promise<number> {
  const card = page.locator('[data-ot-remedial]');
  if ((await card.count()) === 0 || !(await card.isVisible())) { return 0; }
  const boxes = card.locator('[data-ot-remedial-rows] textarea');
  const count = await boxes.count();
  if (count === 0) { throw new Error('"Fail points and remedial actions" is shown with no row to write in'); }
  await withSave(page, card.locator('[data-ot-remedial-status]'), 'the remedial actions', async () => {
    for (let i = 0; i < count; i += 1) { await boxes.nth(i).fill(text); }
  });
  return count;
}

/**
 * Presses Submit and reads the outcome. A PRECONDITION refusal marks the unanswered rows on
 * the page; those are answered and the submit is tried again, twice at most, so a required
 * question this spec did not foresee costs a retry rather than the run.
 */
export async function submitReview(page: Page, text: string, log: (line: string) => void): Promise<void> {
  const button = page.locator('[data-ot-submit-button]');
  const status = page.locator('[data-ot-submit-status]');

  for (let attempt = 0; attempt < 3; attempt += 1) {
    await status.evaluate((el) => { el.textContent = ''; });
    await button.click();

    const deadline = Date.now() + 120_000;
    let said = '';
    while (Date.now() < deadline) {
      said = ((await status.textContent()) ?? '').replace(/\s+/g, ' ').trim();
      if (said.startsWith('Review submitted.')) { return; }
      if (said !== '' && !/^(Saving|Submitting)/.test(said)) { break; }
      await pause(page, 500);
    }

    const missing = await page.locator('.ot-answer--missing').count();
    if (missing === 0 || attempt === 2) {
      throw new Error(`the review was not submitted: "${said || 'no answer from the page'}"`);
    }
    log(`submit refused (${said}); answering the ${missing} row(s) it marked and retrying`);
    await answerReview(page, text, true);
    await writeRemedialActions(page, text);
  }
}

/** Opens a review the tester holds, proving it is the test case's and editable. */
export async function openEditableReview(page: Page, reviewId: string, ref: string): Promise<void> {
  await page.goto(reviewUrl(reviewId));
  await expectSignedIn(page, portalBase());
  await expectNoLiquidError(page);
  await expect(page.locator('[aria-labelledby="ot-review-summary"] dd a').first(), 'the review belongs to the test case').toHaveText(ref);
  await expect(
    page.locator('[data-ot-submit-button]'),
    'the review opens read-only - it must be assigned to the signed-in tester, who needs the reviewer web role for it',
  ).toBeVisible();
}
