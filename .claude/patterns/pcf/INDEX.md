# PCF Patterns Index

> Pointer-based pattern files for PCF control development and Code Page dialogs.
> Each file points to canonical source code — read the code, not descriptions.

| Pattern | When to Load |
|---------|-------------|
| [control-initialization.md](control-initialization.md) | Creating/modifying PCF control lifecycle |
| [error-handling.md](error-handling.md) | Adding error boundaries and error states |
| [theme-management.md](theme-management.md) | Dark mode support and theme-aware styling |
| [dataverse-queries.md](dataverse-queries.md) | Querying Dataverse from PCF controls |
| [dialog-patterns.md](dialog-patterns.md) | Opening dialogs, side panels, Code Pages |

## Critical Constraint (ADR-022)
PCF = React 16 (platform-provided). Code Pages = React 18 (bundled). Never mix.

## Related
- [PCF Constraints](../../constraints/pcf.md) — MUST/MUST NOT rules
- `src/client/pcf/CLAUDE.md` — Module-specific guidance
