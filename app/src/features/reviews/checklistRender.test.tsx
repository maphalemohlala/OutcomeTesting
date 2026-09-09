import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import reference from '../../../../docs/reference/checker-checklist.html?raw';
import seedXml from '../../../../data/v8-seed/data.xml?raw';
import { failPoints, type FailReasonRef } from './checklistForm';
import { buildSections, type QuestionRef, type SectionRef, type VersionRef } from './reviewSections';
import type { ReviewDetailState, ReviewResponse } from './useReviewDetail';

/**
 * What the Code App's review page actually draws, as markup.
 *
 * `checklistDocument.test.ts` checks the model the page is built from; this renders the page
 * itself and reads the headings, tables and band rows back out of the HTML, so a heading that
 * the model carries but the component never emits is caught here rather than on someone's
 * screen. The page is rendered off the real seed, against the reference document, so both
 * ends of the comparison are the shipped artefacts.
 */

vi.mock('react-router-dom', () => ({
  useParams: () => ({ reviewId: 'rev-1' }),
  Link: ({ children }: { children?: unknown }) => children,
}));

const detailState = vi.hoisted(() => ({ current: null as ReviewDetailState | null }));
vi.mock('./useReviewDetail', () => ({ useReviewDetail: () => detailState.current }));

const { ReviewDetailPage } = await import('./ReviewDetailPage');

// --- the seed, read the way checklistDocument.test.ts reads it -------------------------

const decode = (value: string) =>
  value
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .trim();

function seedRecords(entity: string) {
  const block = new RegExp(`<entity name="${entity}"[\\s\\S]*?</entity>`).exec(seedXml);
  if (!block) throw new Error(`No ${entity} records in the seed`);
  return [...block[0].matchAll(/<record id="([^"]+)">([\s\S]*?)<\/record>/g)].map((record) => ({
    id: record[1],
    fields: Object.fromEntries(
      [...record[2].matchAll(/<field name="([^"]+)"[^>]*?value="([^"]*)"/g)].map((f) => [
        f[1],
        decode(f[2]),
      ]),
    ) as Record<string, string>,
  }));
}

const sectionRows = seedRecords('al_section');

function form(ownerRole: string) {
  const owned = new Set(
    sectionRows.filter((r) => r.fields.al_ownerrole === ownerRole).map((r) => r.id),
  );

  const sections: SectionRef[] = sectionRows
    .filter((r) => owned.has(r.id))
    .map((r) => ({
      id: r.id,
      code: r.fields.al_sectioncode,
      name: r.fields.al_name,
      order: Number(r.fields.al_displayorder),
      helpText: r.fields.al_helptext ?? null,
    }));

  const questions: QuestionRef[] = seedRecords('al_question')
    .filter((r) => owned.has(r.fields.al_sectionid))
    .map((r) => ({
      id: r.id,
      sectionId: r.fields.al_sectionid,
      order: Number(r.fields.al_displayorder),
      code: r.fields.al_questioncode,
    }));

  const versions: VersionRef[] = seedRecords('al_questionversion').map((r) => ({
    id: r.id,
    questionId: r.fields.al_questionid,
    order: Number(r.fields.al_displayorder),
    text: r.fields.al_questiontext,
    responseTypeValue: Number(r.fields.al_responsetype),
    responseType: '',
    mandatory: r.fields.al_ismandatory === 'True',
  }));

  const reasons: FailReasonRef[] = seedRecords('al_failreason').map((r) => ({
    id: r.id,
    name: r.fields.al_name,
    category: null,
    categoryValue: Number(r.fields.al_category),
    order: Number(r.fields.al_displayorder),
  }));

  return {
    sections: buildSections<ReviewResponse>(sections, questions, versions, []),
    failPoints: failPoints(reasons, new Set<string>()),
  };
}

/** The page's markup for one discipline, rendered off the seed. */
function render(reviewType: 'Tax' | 'AQS', ownerRole: string): string {
  const built = form(ownerRole);
  detailState.current = {
    status: 'ready',
    detail: {
      header: {
        id: 'rev-1',
        reference: 'REV-1',
        type: reviewType,
        status: 'In progress',
        sequence: 1,
        checklistVersion: 'V8',
        checklistVersionId: 'cv-1',
        caseId: 'case-1',
        caseName: 'Case 1',
        owner: null,
        startedOn: null,
        submittedOn: null,
        isSubmitted: false,
        typeMismatch: false,
      },
      caseHeader: [],
      sections: built.sections,
      failPoints: built.failPoints,
    },
  };
  return renderToStaticMarkup(<ReviewDetailPage reviewType={reviewType} />);
}

const AQS = '120910101';
const TAX = '120910100';

/** The headings and band rows the page emits, in document order. */
function outline(html: string): string[] {
  const doc = html.slice(html.indexOf('checklist-doc'));
  return [...doc.matchAll(/<h2[^>]*>([\s\S]*?)<\/h2>|<tr class="section"><td[^>]*>([\s\S]*?)<\/td>/g)]
    .map((m) => (m[1] !== undefined ? `H2  ${decode(m[1])}` : `BAND  ${decode(m[2])}`));
}

const documentHeadings = [...reference.matchAll(/<h2>([\s\S]*?)<\/h2>/g)]
  .map((m) => decode(m[1]).replace(/&ndash;/g, '–'))
  .filter((h) => h !== 'Remediation and escalation');

const documentBands = [...reference.matchAll(/type: "section", label: "((?:[^"\\]|\\.)*)"/g)].map(
  (m) => decode(m[1]),
);

describe('the Code App review page draws the document’s headings', () => {
  it('heads every block on an AQS review, with the E1-E5 subsection bands', () => {
    const got = outline(render('AQS', AQS));

    // The document's own order, with each E section's band inside Suitability core checks.
    const expected: string[] = [];
    for (const heading of documentHeadings) {
      if (heading === 'File Quality - Tax check section') continue;
      expected.push(`H2  ${heading}`);
      if (heading === 'Suitability core checks') {
        for (const band of documentBands) expected.push(`BAND  ${band}`);
      }
    }

    expect(got).toEqual(expected);
  });

  /**
   * The same guarantee as the test above, written out by name.
   *
   * That one derives its expectation from the reference document, which is what keeps the two
   * in step; this one says out loud which headings a reviewer must see, so a regression names
   * the heading that went missing instead of reporting that two arrays differ. It is here
   * because exactly this failed on the portal: one heading rendered for the whole AQS form and
   * Consumer Duty overlay, Checker judgement and grading, Centralised Retirement Proposition
   * and Suitability core checks were all absent (AD-105).
   */
  it('shows Consumer Duty overlay, Checker judgement and grading and every other block heading', () => {
    expect(outline(render('AQS', AQS)).filter((line) => line.startsWith('H2'))).toEqual([
      'H2  File Quality - AML and CRA checking points',
      'H2  File Quality – Fail points',
      'H2  File Quality Outcome',
      'H2  Suitability core checks',
      'H2  Centralised Retirement Proposition',
      'H2  Consumer Duty overlay',
      'H2  Checker judgement and grading',
    ]);
  });

  it('heads every block on a Tax review', () => {
    expect(outline(render('Tax', TAX))).toEqual([
      'H2  File Quality - Tax check section',
      'H2  File Quality – Fail points',
      'H2  File Quality Outcome',
    ]);
  });

  it('gives each grid the document’s column heading row', () => {
    const html = render('AQS', AQS);
    const heads = [...html.matchAll(/<table class="grid">[\s\S]*?<\/thead>/g)].map((t) =>
      [...t[0].matchAll(/<th class="(?:label|opt)"[^>]*>([\s\S]*?)<\/th>/g)].map((m) => decode(m[1])),
    );

    expect(heads).toEqual([
      ['Check', 'Yes', 'No', 'N/A'],
      ['Suitability test point', 'Pass', 'Fail', 'Insufficient evidence'],
      ['Centralised Retirement Proposition test point', 'Pass', 'Fail', 'Insufficient evidence'],
      ['Outcome', 'Yes', 'No', 'Insufficient evidence'],
    ]);
  });
});
