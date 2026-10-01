import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('../../generated', () => ({
  EnvironmentvariabledefinitionsService: { getAll: vi.fn() },
  EnvironmentvariablevaluesService: { getAll: vi.fn() },
}));

import { EnvironmentvariabledefinitionsService, EnvironmentvariablevaluesService } from '../../generated';
import {
  DEFAULT_PRODUCT,
  brandRole,
  brandRules,
  checklistFooter,
  checklistTitle,
  cleanProductName,
  managerWebRole,
  remediationFooter,
} from './productName';
import { loadProductName, readProductName, resetProductNameForTests } from './loadProductName';
import { productName } from './useProductName';

const asMock = (fn: unknown) => fn as ReturnType<typeof vi.fn>;
const definitions = asMock(EnvironmentvariabledefinitionsService.getAll);
const values = asMock(EnvironmentvariablevaluesService.getAll);

/**
 * PROD calls the product OTIS; DEV and TEST keep "Outcome Testing" (owner, 2026-10-01). The
 * manager web role is matched by name, so it is derived, and under the default every derived
 * name has to be exactly the literal that was in use before - or DEV and TEST lose access.
 */
describe('product name', () => {
  beforeEach(() => {
    resetProductNameForTests();
    definitions.mockReset();
    values.mockReset();
  });

  it('derives today’s names under the default', () => {
    expect(managerWebRole(DEFAULT_PRODUCT)).toBe('AL Portal - Outcome Testing Manager');
    expect(checklistTitle(DEFAULT_PRODUCT)).toBe('Outcome Testing - Checker Checklist');
    expect(checklistFooter(DEFAULT_PRODUCT)).toBe('Outcome Testing Checker Checklist | V5 Draft');
    expect(remediationFooter(DEFAULT_PRODUCT)).toBe('Outcome Testing — Remediation and escalation | V8');
  });

  it('derives the OTIS names', () => {
    expect(managerWebRole('OTIS')).toBe('AL Portal - OTIS Manager');
    expect(checklistTitle('OTIS')).toBe('OTIS - Checker Checklist');
    expect(checklistFooter('OTIS')).toBe('OTIS Checker Checklist | V5 Draft');
    expect(remediationFooter('OTIS')).toBe('OTIS — Remediation and escalation | V8');
  });

  it('renames only the manager role in the built-in rules', () => {
    const rules = [
      { role: 'AL Portal - Outcome Testing Manager', resource: 'page.cases' },
      { role: 'AL Portal - Tax Reviewer', resource: 'page.cases' },
    ];
    expect(brandRules(rules, 'OTIS').map((r) => r.role)).toEqual(['AL Portal - OTIS Manager', 'AL Portal - Tax Reviewer']);
    expect(brandRules(rules, DEFAULT_PRODUCT)).toBe(rules);
    expect(brandRole('Administrators', 'OTIS')).toBe('Administrators');
  });

  it('treats a blank value as not configured', () => {
    expect(cleanProductName('   ')).toBe(DEFAULT_PRODUCT);
    expect(cleanProductName(undefined)).toBe(DEFAULT_PRODUCT);
    expect(cleanProductName(' OTIS ')).toBe('OTIS');
  });

  it('reads the value set on the environment', async () => {
    definitions.mockResolvedValue({ success: true, data: [{ environmentvariabledefinitionid: 'd1', defaultvalue: 'Outcome Testing' }] });
    values.mockResolvedValue({ success: true, data: [{ value: 'OTIS' }] });
    await expect(readProductName()).resolves.toBe('OTIS');
  });

  it('reads the shipped default when no value is set', async () => {
    definitions.mockResolvedValue({ success: true, data: [{ environmentvariabledefinitionid: 'd1', defaultvalue: 'Outcome Testing' }] });
    values.mockResolvedValue({ success: true, data: [] });
    await expect(readProductName()).resolves.toBe('Outcome Testing');
  });

  it('keeps the default where the variable does not exist or cannot be read', async () => {
    definitions.mockResolvedValue({ success: true, data: [] });
    await expect(readProductName()).resolves.toBe(DEFAULT_PRODUCT);

    definitions.mockResolvedValue({ success: false, error: new Error('no privilege') });
    await expect(readProductName()).resolves.toBe(DEFAULT_PRODUCT);
  });

  it('never rejects, and publishes what it read', async () => {
    definitions.mockRejectedValue(new Error('offline'));
    await expect(loadProductName()).resolves.toBe(DEFAULT_PRODUCT);

    resetProductNameForTests();
    definitions.mockResolvedValue({ success: true, data: [{ environmentvariabledefinitionid: 'd1', defaultvalue: null }] });
    values.mockResolvedValue({ success: true, data: [{ value: 'OTIS' }] });
    await loadProductName();
    expect(productName()).toBe('OTIS');
  });
});
