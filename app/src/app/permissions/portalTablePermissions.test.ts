import { describe, expect, it } from 'vitest';

/**
 * The portal's case boundary as the table permissions draw it (AD-218, AR-02 to AR-04).
 * Read as YAML text, because nothing else checks these files before they are written to
 * the environment - and a wrong scope here is a leak, not a bug.
 */
const files = import.meta.glob(
  '../../../../powerpages/outcome-testing---outcometesting/table-permissions/*.tablepermission.yml',
  { query: '?raw', import: 'default', eager: true },
) as Record<string, string>;

function field(yaml: string, key: string): string | null {
  const match = new RegExp(`^${key}:\\s*(.*)$`, 'm').exec(yaml);
  return match ? match[1].trim() : null;
}

// \r?\n throughout: a Windows checkout may carry CRLF, and a regex written for \n alone
// would read every role list as empty - which passes a "no Planner" test for the wrong reason.
function roles(yaml: string): string[] {
  const block = /adx_entitypermission_webrole:\r?\n((?:- .*(?:\r?\n|$))*)/.exec(yaml);
  return block
    ? block[1].split(/\r?\n/).filter((line) => line.trim()).map((line) => line.replace(/^- /, '').trim())
    : [];
}

const byName = new Map(Object.values(files).map((yaml) => [field(yaml, 'adx_entityname'), yaml]));

const ROLE = {
  tax: 'a1000000-0000-4000-8000-000000000090',
  aqs: 'a1000000-0000-4000-8000-000000000091',
  adviser: 'a1000000-0000-4000-8000-000000000092',
  supervisor: 'a1000000-0000-4000-8000-000000000093',
};

const PARENTS: [string, string, string, string][] = [
  ['Case - my Tax checks', 'contact_al_taxcheckercontactid_al_outcomecase', '756150001', ROLE.tax],
  ['Case - my AQS checks', 'contact_al_aqscheckercontactid_al_outcomecase', '756150001', ROLE.aqs],
  ['Case - my clients', 'contact_al_advisercontactid_al_outcomecase', '756150001', ROLE.adviser],
  ['Case - advisers I supervise', 'contact_al_tcsupervisorcontactid_al_outcomecase', '756150001', ROLE.supervisor],
];

describe('each role reads cases through its own column', () => {
  it.each(PARENTS)('%s is Contact-scoped, read-only, on its relationship', (name, relationship, scope, role) => {
    const yaml = byName.get(name);
    expect(yaml, name).toBeDefined();
    expect(field(yaml!, 'adx_entitylogicalname')).toBe('al_outcomecase');
    expect(field(yaml!, 'adx_scope')).toBe(scope);
    expect(field(yaml!, 'adx_contactrelationship')).toBe(relationship);
    expect(field(yaml!, 'adx_read')).toBe('true');
    expect(field(yaml!, 'adx_write')).toBe('false');
    expect(roles(yaml!)).toEqual([role]);
  });

  it('the AQS queue is Account-scoped and has no children', () => {
    const yaml = byName.get('Case - AQS queue');
    expect(yaml).toBeDefined();
    expect(field(yaml!, 'adx_scope')).toBe('756150002');
    expect(field(yaml!, 'adx_accountrelationship')).toBe('account_al_aqsqueueaccountid_al_outcomecase');
    expect(roles(yaml!)).toEqual([ROLE.aqs]);

    const id = field(yaml!, 'adx_entitypermissionid');
    const children = Object.values(files).filter((other) => field(other, 'adx_parententitypermission') === id);
    expect(children).toEqual([]);
  });

  it.each(PARENTS)('%s passes read down to the case detail, and nothing more', (name) => {
    const parentId = field(byName.get(name)!, 'adx_entitypermissionid');
    const children = Object.values(files).filter((yaml) => field(yaml, 'adx_parententitypermission') === parentId);
    const tables = children.map((yaml) => field(yaml, 'adx_entitylogicalname')).sort();
    expect(tables).toEqual(['al_outcome', 'al_remediationaction', 'al_reviewinstance', 'al_signoff']);
    for (const child of children) {
      expect(field(child, 'adx_scope')).toBe('756150003');
      expect(field(child, 'adx_write')).toBe('false');
      expect(field(child, 'adx_create')).toBe('false');
    }

    const review = children.find((yaml) => field(yaml, 'adx_entitylogicalname') === 'al_reviewinstance')!;
    const reviewId = field(review, 'adx_entitypermissionid');
    const answers = Object.values(files).filter((yaml) => field(yaml, 'adx_parententitypermission') === reviewId);
    expect(answers.map((yaml) => field(yaml, 'adx_entitylogicalname'))).toEqual(['al_response']);
  });

  it('every new id is in the AD-218 band', () => {
    for (const yaml of Object.values(files)) {
      const name = field(yaml, 'adx_entityname') ?? '';
      if (!name.startsWith('Case - ')) continue;
      expect(field(yaml, 'adx_entitypermissionid')).toMatch(/^a1000000-0000-4000-8000-0000000000[cd][0-9a-f]$/);
    }
  });
});
