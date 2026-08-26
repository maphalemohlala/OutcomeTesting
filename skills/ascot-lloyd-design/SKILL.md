---
name: ascot-lloyd-design
description: Enforces a distinctive, accessible and brand-faithful design system for the Ascot Lloyd Outcome Testing application. Use for every task that creates, changes or reviews UI, CSS, components, pages, screens, layouts, charts, icons or visual content.
---
# Ascot Lloyd Design

## Required context
Read `../../knowledge/project-context.md`, `../../knowledge/requirements-index.md` and `../../knowledge/decision-log.md` before acting. Use `code-app-development` for framework implementation, `power-platform-security` when a screen exposes permissioned data, and `test-quality-release` for verification.

## Purpose
Act as the design guardian for the Ascot Lloyd Outcome Testing application. Produce calm, precise, professional interfaces for operational case management. Do not invent a new brand, use generic AI styling or introduce visual choices that are unsupported by the project resources.

## Non-negotiable source of truth
Before designing or changing any interface, inspect the repository `brand/` directory. It is authoritative for:
- colour values and semantic colour roles;
- font families, weights and fallbacks;
- logos, marks, icons and approved imagery;
- existing design tokens, CSS variables and reusable style assets.

Never guess brand colours or fonts. Never substitute a fashionable font or palette merely because it looks polished.

If the directory or a required asset cannot be read, stop visual implementation and report exactly which path is unavailable. You may still propose structure and interaction, but label all visual tokens as unresolved. Do not create substitute branding.

## Required discovery workflow
Before writing UI code:
1. Recursively list `brand/`.
2. Identify images, SVGs, font files, stylesheets, token files and theme/configuration files.
3. Read text-based resources and extract existing colour and typography definitions.
4. Inspect visual assets directly rather than relying on filenames.
5. Search `app/src` for current token usage and reusable components.
6. Create or update a single token layer derived from the resources. Do not scatter literal colours and font names across components.
7. Record the verified assets used and any unresolved gaps.

## Brand-token rules
- Prefer existing CSS custom properties or theme tokens.
- If tokens do not exist, create a central token file derived only from verified resource values.
- Use semantic names such as `--colour-surface`, `--colour-text`, `--colour-action`, `--colour-danger`, `--font-body` and `--font-heading`.
- Do not duplicate equivalent tokens.
- Do not hard-code hex, rgb, hsl or font-family values inside page or component files.
- Do not edit, rename, recolour or distort brand assets without an explicit instruction.
- Preserve logo aspect ratios and safe spacing.
- Where a brand resource is internally inconsistent, do not silently choose. Record it in `knowledge/decision-log.md` and ask the brand owner.

## Product and audience
An operational Outcome Testing application supporting case intake, allocation, Tax and AQS checks, findings, remediation, reporting and controlled administration. Optimise for frequent professional use, accuracy, clear status, fast scanning and low cognitive load.

Use business language: case, check, finding, remediation, owner, due date. Do not expose Dataverse logical names, GUIDs, APIs or flow internals unless the screen is explicitly administrative.

## Anti-slop design rules
Never default to the visual clichés commonly produced by generative systems:
- no decorative gradients, glassmorphism, glowing blobs or aurora backgrounds;
- no excessive shadows, floating cards or rounded containers inside rounded containers;
- no oversized marketing hero section in an operational application;
- no arbitrary purple, cyan or neon accent colours;
- no emoji as functional icons;
- no fake testimonials, vanity metrics, marketing slogans or invented data;
- no generic three-card feature rows;
- no decorative charts or progress rings without a real user question;
- no needless animation, parallax or hover movement;
- no random pills for ordinary labels;
- no excessive whitespace that reduces information density;
- no vague copy such as "Unlock insights", "Seamless experience", "Welcome back" or "Empower your workflow".

The application is not a landing page. Start from the task, data hierarchy and business process.

## Design principles
1. **Task first.** Each screen has one clearly stated primary job. Establish user, task, key decision and required evidence before choosing a layout.
2. **Information hierarchy.** Use layout to express business meaning. Separate editable from read-only content. Make ownership, status, age and next action easy to identify.
3. **Restrained visual language.** One memorable, brand-supported design gesture at most. Decoration must carry meaning or be removed.
4. **Operational density.** Compact but readable. Prefer structured panels, tables, filters, timelines and forms. Do not convert every piece of information into a card.
5. **Honest content.** Realistic domain labels and explicit empty, loading, validation, access-denied and error states. Never fabricate client records, performance numbers or compliance outcomes. Clearly label test data.
6. **Consistency.** The same action keeps the same label, placement and treatment across screens. Reuse before creating variants.
7. **Accessibility.** WCAG 2.2 AA baseline (NFR-ACC-01). Keyboard access, visible focus, semantic structure, accessible names, sufficient contrast, clear errors, reduced-motion support. Never communicate status by colour alone.
8. **Responsiveness.** Desktop and tablet are the primary operational widths. Tables need a deliberate strategy: prioritised columns, horizontal scrolling or a structured detail view.

## Required design process
1. **Ground**: state the user, screen goal and source files inspected.
2. **Inventory**: identify reusable components, patterns and tokens already present.
3. **Plan**: compact layout and interaction plan.
4. **Differentiate**: identify one choice specific to Ascot Lloyd and the Outcome Testing workflow.
5. **Slop check**: name a generic pattern rejected and why.
6. **Build**: implement using verified tokens and existing conventions.
7. **Critique**: review hierarchy, density, copy, consistency, accessibility and responsive behaviour.
8. **Verify**: run available lint, type, build and UI checks.
9. **Remove**: delete at least one unnecessary visual or interaction element found during critique.

## Component guidance
- **Navigation**: reflect real user tasks and permissions, not table structure.
- **Tables**: support scanning, sorting and filtering only where required. Concise column labels. Predictable, accessible row actions.
- **Forms**: group by business decision. Mark required fields clearly. Preserve entered data after validation errors.
- **Statuses**: pair colour with text and, where helpful, an icon. Use a controlled set of semantic status styles.
- **Remediation**: make owner, requested action, evidence, due date, age and next step prominent.
- **Read-only records**: make the locked state unmistakable without making content look disabled or hard to read.
- **Destructive actions**: state consequences and require appropriate confirmation. Never style destructive as primary.
- **Empty states**: explain why the view is empty and offer the next valid action.
- **Errors**: say what happened, what remains safe and what to do next. Never expose sensitive technical detail.

## Security boundary
Visual affordances are presentation only. Hiding or disabling a control is never an access control. Enforcement belongs in Dataverse roles, table permissions and server-side commands.

## Definition of done
- colours and fonts trace back to `brand/`;
- no unjustified visual token introduced;
- the result does not resemble a generic AI-generated SaaS template;
- real task hierarchy is visible;
- responsive and keyboard behaviour considered;
- loading, empty, error, validation and permission states covered where relevant;
- reused and new components identified;
- the critique reports what was removed or simplified;
- checks performed and remaining limitations stated.

## Required output
End each design implementation with:

### Design verification
- Resources inspected:
- Tokens/assets used:
- Existing components reused:
- Generic patterns rejected:
- Accessibility checks:
- Responsive checks:
- Tests/build checks:
- Remaining gaps:
- Requirement IDs affected:
