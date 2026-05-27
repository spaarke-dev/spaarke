# PCF Patterns Index

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

> Pointer-based pattern files for PCF control development and Code Page dialogs.
> Each file points to canonical source code — read the code, not descriptions.

| Pattern | When to Load | Last Reviewed | Status |
|---------|-------------|---------------|--------|
| [control-initialization.md](control-initialization.md) | Creating/modifying PCF control lifecycle | 2026-04-05 | Verified |
| [error-handling.md](error-handling.md) | Adding error boundaries and error states | 2026-04-05 | Verified |
| [theme-management.md](theme-management.md) | Dark mode support and theme-aware styling | 2026-04-05 | Verified |
| [dataverse-queries.md](dataverse-queries.md) | Querying Dataverse from PCF controls | 2026-04-05 | Verified |
| [dialog-patterns.md](dialog-patterns.md) | Opening dialogs, side panels, Code Pages | 2026-04-05 | Verified |
| [fluent-v9-modern-theming.md](fluent-v9-modern-theming.md) | New PCF setup, `<platform-library>` manifest, theme-source decision | 2026-05-26 | Current |
| [fluent-v9-canvas-vs-mda-disabled.md](fluent-v9-canvas-vs-mda-disabled.md) | PCF ships to both Canvas + MDA; disabled state needs different handling | 2026-05-26 | Current |

## Critical Constraint (ADR-022)
PCF = React 16 (platform-provided). Code Pages = React 18 (bundled). Never mix.

## Related
- [PCF Constraints](../../constraints/pcf.md) — MUST/MUST NOT rules
- `src/client/pcf/CLAUDE.md` — Module-specific guidance
- [`../ui/INDEX.md`](../ui/INDEX.md) — cross-surface UI patterns (Fluent v9 component authoring + theming + portal-gotcha + React version boundaries)
