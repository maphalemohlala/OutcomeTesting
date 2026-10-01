/**
 * What this environment calls the product: OTIS in PROD, "Outcome Testing" everywhere else
 * (owner, 2026-10-01). Mirrors ProductName in the plug-in assembly; spec
 * docs/superpowers/specs/2026-10-01-otis-product-name-design.md.
 *
 * The name is a label in some places and a KEY in others - the manager web role is matched by
 * name - so the derived names live here and nowhere else. An environment that sets nothing
 * gets DEFAULT_PRODUCT, and every derived name is then exactly what was in use before.
 */
export const DEFAULT_PRODUCT = 'Outcome Testing';

/** The Dataverse environment variable holding the name. Its definition ships the default. */
export const PRODUCT_VARIABLE = 'al_ProductName';

/** A configured value, cleaned; blank means "not configured". */
export function cleanProductName(value: string | null | undefined): string {
  const trimmed = (value ?? '').trim();
  return trimmed === '' ? DEFAULT_PRODUCT : trimmed;
}

/** The portal web role that oversees and allocates both disciplines. */
export function managerWebRole(product: string): string {
  return `AL Portal - ${product} Manager`;
}

export function checklistTitle(product: string): string {
  return `${product} - Checker Checklist`;
}

export function checklistFooter(product: string): string {
  return `${product} Checker Checklist | V5 Draft`;
}

export function remediationFooter(product: string): string {
  return `${product} — Remediation and escalation | V8`;
}

/**
 * A role name written against the default product, as this environment names it. Only the
 * manager role carries the product name; every other role passes through unchanged.
 */
export function brandRole(role: string, product: string): string {
  return role === managerWebRole(DEFAULT_PRODUCT) ? managerWebRole(product) : role;
}

/** Rules written against the default product, with their role renamed for this environment. */
export function brandRules<T extends { role: string }>(rules: readonly T[], product: string): readonly T[] {
  if (product === DEFAULT_PRODUCT) return rules;
  return rules.map((rule) => ({ ...rule, role: brandRole(rule.role, product) }));
}
