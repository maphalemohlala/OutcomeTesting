import { describe, expect, it } from 'vitest';
import pageRules from '../../../../powerpages/outcome-testing---outcometesting/webpagerule.yml?raw';
import header from '../../../../powerpages/outcome-testing---outcometesting/web-templates/header/Header.webtemplate.source.html?raw';
import myWork from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-my-work/OT-My-Work.webtemplate.source.html?raw';

/**
 * Which pages each role is given (AD-218 follow-on). Every non-oversight role reads only its
 * own cases, so Cases is their one list: My Work and Tax reviews are oversight pages, AQS
 * reviews stays for the queue and Remediation for advisers and supervisors. Profile is no
 * one's. Pages and menu only - what anyone can read is still the table permissions.
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
const OVERSIGHT = [ROLE.manager, ROLE.portalAdmin, ROLE.administrators];
const OVERSIGHT_NAMES = ['AL Portal - Outcome Testing Manager', 'AL Portal - Portal Administrator', 'Administrators'];

const PAGE = {
  myWork: 'a1000000-0000-4000-8000-000000000030',
  taxReviews: 'a1000000-0000-4000-8000-000000000033',
  aqsReviews: 'a1000000-0000-4000-8000-000000000034',
  remediation: 'a1000000-0000-4000-8000-000000000035',
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
    const redirect = /\{% if user and ot_oversight == false %\}([\s\S]*?)\{% else %\}/.exec(myWork)?.[1] ?? '';
    expect(redirect).toContain("window.location.replace('/cases')");
    expect(redirect).not.toContain('fetchxml');
  });
});
