import { describe, expect, it } from 'vitest';
// The router's own text, not its behaviour. Vite's ?raw import keeps this inside the app's
// own module graph, so it needs no Node types and moves with the file if it is ever renamed.
import source from './router.tsx?raw';

/**
 * Every screen route is behind a permission gate.
 *
 * F43, found in DEV on 2026-09-21 working APP-002, the first time this app was ever driven
 * by somebody who was not a System Administrator. The dashboard route rendered
 * `<DashboardPage />` directly, with no `RequirePermission` around it, while every other
 * route had one. A user holding no application role at all signed in and got the whole
 * thing: 12 open cases, the outcome distribution, the remediation backlog, case ageing.
 *
 * What makes it worth a guard rather than a one-line fix is how completely it was hidden.
 * `page.dashboard` is a declared ResourceKey; `al_pagepermission` grants it to eight roles;
 * `navigation.ts` filters the menu item on it, so the menu DID disappear; and
 * `pageResourceForPath('/')` returns it, with a test asserting so. Every mirror of the rule
 * was present and correct. The authoritative gate was the only missing piece, and no test
 * touched it because `pageResourceForPath` is called by tests and by nothing else.
 *
 * So this checks the router's own text. A route is exempt only if it navigates elsewhere -
 * a redirect lands on a route that IS gated - or if it is the catch-all, which deliberately
 * answers any unknown address.
 */
describe('every route is permission-gated', () => {
  /** Each `<Route …/>` element in the router, as written. */
  const routes = source.match(/<Route\b[\s\S]*?\/>/g) ?? [];

  it('is looking at something', () => {
    // Without this the assertion below passes by finding no routes at all, which is the
    // same shape of blind spot it exists to close.
    expect(routes.length).toBeGreaterThan(10);
    expect(routes.some((r: string) => r.includes('path="/"'))).toBe(true);
  });

  it('wraps every screen route in RequirePermission', () => {
    const ungated = routes
      .filter((route: string) => !route.includes('<Navigate'))
      .filter((route: string) => !/path="\*"/.test(route))
      .filter((route: string) => !route.includes('RequirePermission'))
      .map((route: string) => (route.match(/path="([^"]*)"/) ?? [, '(unknown)'])[1]);

    expect(
      ungated,
      'These routes render a screen with no permission gate, so anyone who can open the '
        + 'app reaches them whatever their role - the menu hiding the link is not a gate '
        + '(F43). Wrap them in <RequirePermission resource="…">.',
    ).toEqual([]);
  });

  it('gates the dashboard on page.dashboard specifically', () => {
    // Named rather than left to the sweep above, because the dashboard is the landing
    // screen: it is what a role-less sign-in sees before it sees anything else.
    const dashboard = routes.find((route: string) => /path="\/"/.test(route)) ?? '';
    expect(dashboard).toContain('resource="page.dashboard"');
  });
});
