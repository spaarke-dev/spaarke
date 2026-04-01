# Webresource & UI Patterns Index

> Pointer-based patterns for Code Pages, custom dialogs, and legacy webresources.

## Webresource Patterns
| Pattern | When to Load |
|---------|-------------|
| [full-page-custom-page.md](full-page-custom-page.md) | Building full-page React SPAs (Vite + React 18) |
| [code-page-wizard-wrapper.md](code-page-wizard-wrapper.md) | Building wizard dialogs as Code Pages |
| [custom-dialogs-in-dataverse.md](custom-dialogs-in-dataverse.md) | Opening dialogs from form scripts or ribbons |
| [subgrid-parent-rollup.md](subgrid-parent-rollup.md) | Legacy JS for KPI refresh on subgrid changes |

## UI Patterns
| Pattern | When to Load |
|---------|-------------|
| [../ui/choice-dialog-pattern.md](../ui/choice-dialog-pattern.md) | Reusable choice/confirmation dialogs |

## Key Constraint (ADR-006)
Standalone dialogs → Code Page (React 18). NOT PCF wrapper + custom page.

## Related
- [PCF Patterns](../pcf/INDEX.md) — PCF control development
- [Dialog Patterns](../pcf/dialog-patterns.md) — Opening Code Pages from PCF
