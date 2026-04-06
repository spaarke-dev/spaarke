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

## Critical Constraint (ADR-022)
PCF = React 16 (platform-provided). Code Pages = React 18 (bundled). Never mix.

## Related
- [PCF Constraints](../../constraints/pcf.md) — MUST/MUST NOT rules
- `src/client/pcf/CLAUDE.md` — Module-specific guidance
