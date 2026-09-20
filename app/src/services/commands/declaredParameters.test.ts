import { describe, expect, it } from 'vitest';

/**
 * That every parameter a command actually sends is one the code app is allowed to send.
 *
 * The Power Apps client does not post the body it is given. It filters it against the
 * parameter list the generator wrote into `dataSourcesInfo.ts`, drops anything not on that
 * list, and posts the rest - and the command still succeeds, because from Dataverse's side
 * an optional parameter that never arrived is simply absent. Nothing throws, nothing warns,
 * and the feature is dead.
 *
 * That has now cost three findings: F14 and F15 (`al_SetFailAccountability` gaining
 * `FqContactId` and `AqContactId`) and F31 (`al_RetireAndSucceedQuestion` gaining
 * `EffectiveFrom`). In each case the contract was right, the plug-in was right, the app
 * looked right, and the parameter went nowhere.
 *
 * So the rule is checked over every command rather than over the three that bit: a name a
 * wrapper puts in a body must appear in the generated declaration for that operation. It
 * fails when somebody adds a parameter and forgets to run
 * `pa app add dataverse-api --api-name <name>` - and it fails when a regeneration DROPS one,
 * which is the way round that has no other warning at all, because the CSDL the generator
 * reads can lag the Custom API by hours.
 */
const wrappers = import.meta.glob('./*.ts', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

const declarations = import.meta.glob('../../../.power/schemas/appschemas/dataSourcesInfo.ts', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

/** The declared parameter names for one operation, from the generated data source info. */
function declaredFor(source: string, operation: string): Set<string> {
  const at = source.indexOf(`"${operation}": {`);
  if (at === -1) return new Set();

  // The operation's own block, up to the next operation key at the same depth. The generator
  // writes one "parameters" array per operation, so the first one after the key is ours.
  const paramsAt = source.indexOf('"parameters": [', at);
  if (paramsAt === -1) return new Set();
  const end = source.indexOf('\n        ],', paramsAt);
  const block = source.slice(paramsAt, end === -1 ? undefined : end);

  return new Set([...block.matchAll(/"name":\s*"(\w+)"/g)].map((m) => m[1]));
}

/** The parameter names one wrapper function puts in its body. */
function sentBy(fn: string): string[] {
  const names = new Set<string>();

  // `Foo: input.bar,` inside the body literal, and `body.Foo = ...` after it. Both forms are
  // used; the second is how an optional parameter is added only when it was supplied.
  for (const m of fn.matchAll(/^\s{4}([A-Z]\w*):/gm)) names.add(m[1]);
  for (const m of fn.matchAll(/\bbody\.([A-Z]\w*)\s*=/g)) names.add(m[1]);

  return [...names];
}

describe('the parameters a command sends', () => {
  const info = Object.values(declarations)[0];

  it('finds the generated declaration and the wrappers', () => {
    // Guards the test itself: a moved file would otherwise make every case below pass by
    // finding nothing to check.
    expect(info).toBeTypeOf('string');
    expect(info).toContain('"al_retireandsucceedquestion"');
    expect(Object.keys(wrappers).length).toBeGreaterThan(10);
  });

  it('are all declared, or the client drops them in silence', () => {
    const missing: string[] = [];
    let checked = 0;

    for (const [path, source] of Object.entries(wrappers)) {
      if (path.endsWith('.test.ts')) continue;

      // One wrapper function at a time, so a file holding four commands is not read as one.
      for (const fn of source.split(/\nexport function |\nexport async function /)) {
        const call = fn.match(/executeCommand<[^>]*>\(\s*'(al_\w+)'/);
        if (!call) continue;

        const operation = call[1];
        const declared = declaredFor(info, operation.toLowerCase());
        checked++;

        for (const name of sentBy(fn)) {
          if (!declared.has(name)) missing.push(`${operation}.${name}`);
        }
      }
    }

    expect(checked).toBeGreaterThan(20);
    expect(
      missing.sort(),
      'These parameters are sent by a command wrapper and are not in the generated data ' +
        'source declaration, so the Power Apps client discards them before the request is ' +
        'made and the command still reports success. Run ' +
        '`npx pa app add dataverse-api --api-name <name>` - and if that regenerates without ' +
        'the parameter, the CSDL has not caught up yet and the app must not ship the field ' +
        '(F14, F15, F31).',
    ).toEqual([]);
  });
});
