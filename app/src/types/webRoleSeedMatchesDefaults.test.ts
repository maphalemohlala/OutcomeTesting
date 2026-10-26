import { describe, expect, it } from 'vitest';
import seedSource from '../../../plugins/OutcomeTesting.Registration/WebRoleSeed.cs?raw';
import { DEFAULT_PERMISSIONS } from './permissions';

/**
 * The registration tool's seedwebroles verb writes the rules a new environment starts with,
 * and the app falls back to DEFAULT_PERMISSIONS while none are stored. The two are one
 * decision written twice (C# and TypeScript) and drifted twice, so this reads the C# matrix
 * and compares it rule for rule.
 */
const MANAGER = 'AL Portal - Outcome Testing Manager';

function seedRules(source: string): string[] {
  const names = new Map<string, string>([['OutcomeTestingManager', MANAGER], ['outcomeTestingManager', MANAGER]]);
  for (const [, name, value] of source.matchAll(/private const string (\w+) = "([^"]+)";/g)) names.set(name, value);
  const role = (name: string) => {
    const value = names.get(name.trim());
    if (!value) throw new Error(`unknown role constant ${name}`);
    return value;
  };

  const allRoles = /AllRoles\(string outcomeTestingManager\) => new\[\]\s*\{([^}]*)\}/.exec(source);
  if (!allRoles) throw new Error('AllRoles not found');
  const listOf = (body: string) => body.split(',').map((s) => s.trim()).filter(Boolean).map(role);

  const start = source.indexOf('Matrix(string OutcomeTestingManager)');
  const end = source.indexOf('public static int Run');
  if (start < 0 || end < start) throw new Error('Matrix not found');
  let body = source.slice(start, end);

  const rules: string[] = [];
  const tuple = /yield return \((\w+), "([\w.]+)", Access(\w+)\);/g;
  const loop = /foreach \(var (\w+) in (AllRoles\(OutcomeTestingManager\)|new\[\] \{([^}]*)\})\)\s*\{([\s\S]*?)\n {8}\}/g;
  for (const [whole, variable, , inline, inner] of body.matchAll(loop)) {
    const roles = inline === undefined ? listOf(allRoles[1]) : listOf(inline);
    for (const r of roles) {
      for (const [, who, resource, level] of inner.matchAll(tuple)) {
        if (who !== variable) throw new Error(`loop over ${variable} yields ${who}`);
        rules.push(`${r} | ${resource} | ${level}`);
      }
    }
    body = body.replace(whole, '');
  }
  for (const [, who, resource, level] of body.matchAll(tuple)) rules.push(`${role(who)} | ${resource} | ${level}`);
  return rules.sort();
}

describe('the seedwebroles matrix', () => {
  it('grants exactly the default permissions', () => {
    const app = DEFAULT_PERMISSIONS.map((r) => `${r.role} | ${r.resource} | ${r.level}`).sort();
    expect(seedRules(seedSource)).toEqual(app);
  });
});
