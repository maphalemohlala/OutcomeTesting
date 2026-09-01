---
applyTo: "powerpages/**"
---
Use `skills/power-pages-development/SKILL.md` and `skills/power-platform-security/SKILL.md`. Enforce access in table permissions, not only UI visibility.

Power Pages is in scope and approved (AD-044, signed scope `OPP-41186` item 13). The portal serves Tax checkers, AQS checkers, advisers and T&C Managers; the Code App serves managers and administrators. Portal metadata lives in `powerpages/` at the repository root and never under `src/` or `app/` (AD-048).

Design and permission model: `docs/superpowers/specs/2026-08-29-power-pages-portal-design.md`. Portal access is Contact-scoped via Entra-synced Contacts joined on work email (AD-047); never grant Global access to operational tables.
