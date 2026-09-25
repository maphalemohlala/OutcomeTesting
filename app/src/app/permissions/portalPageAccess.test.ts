import { describe, expect, it } from 'vitest';
import pageRules from '../../../../powerpages/outcome-testing---outcometesting/webpagerule.yml?raw';
import header from '../../../../powerpages/outcome-testing---outcometesting/web-templates/header/Header.webtemplate.source.html?raw';
import myWork from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-my-work/OT-My-Work.webtemplate.source.html?raw';
import caseList from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-case-list/OT-Case-List.webtemplate.source.html?raw';

/**
 * Which pages each role is given (AD-218 follow-on). Every non-oversight role reads only its
 * own cases, so Cases is their one list: My Work and Tax reviews are oversight pages, AQS
 * reviews stays for the queue and Remediation for advisers and supervisors. Profile is no
 * one's. Pages and menu only - what anyone can read is still the table permissions.
 *
 * Oversight is the two roles with Global read on case data: Outcome Testing Manager and
 * Administrators. Portal Administrator reads no cases, so it is treated like every other
 * non-oversight role (owner, 2026-09-24): Cases only.
 *
 * An AQS checker works from ONE page (owner, 2026-09-24): AQS reviews holds the queue and
 * their own checks, so someone whose only role is AQS Reviewer is refused Cases and lands on
 * AQS reviews. The My cases / All cases toggle is an oversight tool and nobody else sees it.
 *
 * An adviser likewise works from ONE page (owner, 2026-09-25: "as an adviser I have cases and
 * remediation pages and they show the same data"): Remediation. Adviser Remediation no longer
 * admits Cases, and someone whose only list role is Adviser Remediation lands on Remediation.
 */
const ROLE = {
  tax: 'a1000000-0000-4000-8000-000000000090',
  aqs: 'a1000000-0000-4000-8000-000000000091',
  adviser: 'a1000000-0000-4000-8000-000000000092',
  supervisor: 'a1000000-0000-4000-8000-000000000093',
  manager: 'a1000000-0000-4000-8000-000000000095',
  portalAdmin: 'a1000000-0000-4000-8000-000000000096',
  administrators: 'c53b2908-1fc1-4470-89cd-6f5b95c17ffe',
};
const OVERSIGHT = [ROLE.manager, ROLE.administrators];
const OVERSIGHT_NAMES = ['AL Portal - Outcome Testing Manager', 'Administrators'];

const PAGE = {
  myWork: 'a1000000-0000-4000-8000-000000000030',
  cases: 'a1000000-0000-4000-8000-000000000031',
  taxReviews: 'a1000000-0000-4000-8000-000000000033',
  aqsReviews: 'a1000000-0000-4000-8000-000000000034',
  remediation: 'a1000000-0000-4000-8000-000000000035',
  review: 'a1000000-0000-4000-8000-000000000036',
  home: '52570e2a-4d91-41f8-95c9-d0017a937039',
  profile: '6b7c4888-877a-41ec-8a9c-2245601999f7',
};

interface Rule {
  name: string;
  right: string;
  page: string;
  roles: string[];
}

// \r?\n throughout: a Windows checkout may carry CRLF.
const rules: Rule[] = pageRules
  .split(/^- /m)
  .slice(1)
  .map((block) => {
    const field = (key: string) => new RegExp(`(?:^|\\s)${key}:[ \\t]*(.*)`).exec(block)?.[1].trim() ?? '';
    const list = /adx_webpageaccesscontrolrule_webrole:[ \t]*(\[\])?\r?\n((?:[ \t]*- .*(?:\r?\n|$))*)/.exec(block);
    return {
      name: field('adx_name'),
      right: field('adx_right'),
      page: field('adx_webpageid'),
      roles: list?.[2]
        ? list[2].split(/\r?\n/).filter((l) => l.trim()).map((l) => l.replace(/^\s*- /, '').trim())
        : [],
    };
  });

function restrictRead(page: string): Rule {
  const found = rules.filter((r) => r.page === page && r.right === '2');
  expect(found, `one Restrict read rule on page ${page}`).toHaveLength(1);
  return found[0];
}

describe('page rules', () => {
  it('Tax reviews is an oversight page', () => {
    expect(restrictRead(PAGE.taxReviews).roles.sort()).toEqual([...OVERSIGHT].sort());
  });

  it('My Work is an oversight page', () => {
    expect(restrictRead(PAGE.myWork).roles.sort()).toEqual([...OVERSIGHT].sort());
  });

  it('AQS reviews stays with the AQS reviewer, for the queue', () => {
    expect(restrictRead(PAGE.aqsReviews).roles.sort()).toEqual([ROLE.aqs, ...OVERSIGHT].sort());
  });

  it('Remediation stays with advisers and supervisors', () => {
    expect(restrictRead(PAGE.remediation).roles.sort()).toEqual([ROLE.adviser, ROLE.supervisor, ...OVERSIGHT].sort());
  });

  it('the Review page stays with the two reviewer roles', () => {
    expect(restrictRead(PAGE.review).roles.sort()).toEqual([ROLE.tax, ROLE.aqs, ...OVERSIGHT].sort());
  });

  it('Portal Administrator is on no page rule but Home and Cases, so it has Cases and nothing else', () => {
    for (const rule of rules.filter((r) => r.page !== PAGE.home && r.page !== PAGE.cases)) {
      expect(rule.roles, rule.name).not.toContain(ROLE.portalAdmin);
    }
    expect(restrictRead(PAGE.home).roles).toContain(ROLE.portalAdmin);
    expect(restrictRead(PAGE.cases).roles).toContain(ROLE.portalAdmin);
  });

  it('Cases is every portal role but the AQS Reviewer and the adviser, who each work from one page', () => {
    expect(restrictRead(PAGE.cases).roles.sort()).toEqual(
      [ROLE.tax, ROLE.supervisor, ROLE.portalAdmin, ...OVERSIGHT].sort(),
    );
  });

  it('Profile is readable by no role', () => {
    const rule = restrictRead(PAGE.profile);
    expect(rule.roles).toEqual([]);
    expect(pageRules).toMatch(/adx_webpageaccesscontrolrule_webrole: \[\]/);
  });
});

describe('the header', () => {
  it('offers no Profile link, only Sign out', () => {
    expect(header).not.toContain("weblinks['Profile Navigation']");
    expect(header).not.toContain("sitemarkers['Profile']");
    expect(header).toContain('website.sign_out_url_substitution');
  });

  it('shows the My Work link to oversight roles only', () => {
    for (const name of OVERSIGHT_NAMES) {
      expect(header).toContain(`user.roles contains '${name}'`);
    }
    expect(header).toMatch(/link\.url == '\/' and ot_oversight == false/);
    expect(header).not.toContain("user.roles contains 'AL Portal - Portal Administrator'");
  });

  it('counts shown links for the dividers, so a hidden first link leaves no stray divider', () => {
    expect(header).not.toMatch(/\{% unless forloop\.first %\}\s*<li class=' nav-item divider-vertical'/);
  });
});

describe('the landing page', () => {
  it('sends a non-oversight user from / to Cases and renders nothing else for them', () => {
    for (const name of OVERSIGHT_NAMES) {
      expect(myWork).toContain(`user.roles contains '${name}'`);
    }
    // Portal Administrator is named in the landing choice (it brings Cases), never as oversight.
    const oversightLine = myWork.split(/\r?\n/).find((l) => l.includes('assign ot_oversight = true')) ?? '';
    expect(oversightLine).not.toBe('');
    expect(oversightLine).not.toContain('Portal Administrator');
    const redirect = /\{% if user and ot_oversight == false %\}([\s\S]*?)\{% else %\}/.exec(myWork)?.[1] ?? '';
    expect(redirect).toContain("window.location.replace('{{ ot_landing }}')");
    expect(redirect).not.toContain('fetchxml');
  });
});

describe('someone Cases does not admit lands on their one page', () => {
  const landing = /\{% assign ot_landing = '\/cases' %\}([\s\S]*?)\{% if user and ot_oversight == false %\}/.exec(myWork)?.[1] ?? '';

  it('keeps Cases for anyone holding a role that Cases admits', () => {
    const guard = /\{% unless ([^%]*) %\}/.exec(landing)?.[1] ?? '';
    for (const role of ['AL Portal - Tax Reviewer', 'AL Portal - T&C Supervisor', 'AL Portal - Portal Administrator']) {
      expect(guard).toContain(`user.roles contains '${role}'`);
    }
    expect(guard).not.toContain('Adviser Remediation');
  });

  it('sends an AQS checker to /aqs-reviews', () => {
    expect(landing).toMatch(/user\.roles contains 'AL Portal - AQS Reviewer' %\}\{% assign ot_landing = '\/aqs-reviews' %\}/);
  });

  it('sends an adviser to /remediation', () => {
    expect(landing).toMatch(/user\.roles contains 'AL Portal - Adviser Remediation' %\}\{% assign ot_landing = '\/remediation' %\}/);
  });
});

describe('the Cases page scope toggle', () => {
  it('is offered to oversight only', () => {
    expect(caseList).toMatch(/\{% if user and ot_oversight %\}\s*<nav class="ot-scope"/);
    for (const name of OVERSIGHT_NAMES) {
      expect(caseList).toContain(`user.roles contains '${name}'`);
    }
  });

  it('ignores ?mine=1 for everyone else, so an old link cannot empty their list', () => {
    expect(caseList).toContain("{% unless ot_oversight %}{% assign f_mine = '' %}{% endunless %}");
  });

  it('calls the list "Your cases" for everyone else, not "All cases"', () => {
    expect(caseList).toMatch(/\{% elsif ot_oversight == false %\}Your cases/);
  });
});
