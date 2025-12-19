# PCF Patterns Index

> **Domain**: PCF / Power Platform Component Framework
> **Last Updated**: 2025-12-19

---

## Available Patterns

| Pattern | Purpose | Lines |
|---------|---------|-------|
| [control-initialization.md](control-initialization.md) | PCF lifecycle and React root | ~130 |
| [theme-management.md](theme-management.md) | Theme resolution and dark mode | ~120 |
| [dataverse-queries.md](dataverse-queries.md) | WebAPI and environment variables | ~125 |
| [error-handling.md](error-handling.md) | Error boundaries and user-friendly errors | ~115 |
| [dialog-patterns.md](dialog-patterns.md) | Dialog close and navigation | ~100 |

---

## When to Load

| Task | Load These Patterns |
|------|---------------------|
| Create new PCF control | `control-initialization.md`, `theme-management.md` |
| Add Dataverse queries | `dataverse-queries.md` |
| Add error handling | `error-handling.md` |
| Build dialog/modal | `dialog-patterns.md` |

---

## Canonical Source Files

All patterns reference actual implementations in:
```
src/client/pcf/
├── UniversalDatasetGrid/control/     # Grid with file operations
│   ├── index.ts                      # PCF lifecycle pattern
│   ├── components/ErrorBoundary.tsx  # Error boundary pattern
│   └── providers/ThemeProvider.ts    # Theme management
├── UniversalQuickCreate/control/     # Multi-file upload dialog
│   ├── index.ts                      # Dialog pattern
│   └── services/                     # API clients, hooks
├── AnalysisWorkspace/control/        # AI analysis workspace
│   └── index.ts                      # Two-panel layout
├── SpeFileViewer/control/            # SPE file preview
│   ├── index.ts                      # State machine pattern
│   └── AuthService.ts                # MSAL auth
└── shared/utils/
    └── environmentVariables.ts       # Environment variable access
```

---

## Related Resources

- [PCF Constraints](../../constraints/pcf.md) - MUST/MUST NOT rules
- [src/client/pcf/CLAUDE.md](../../../src/client/pcf/CLAUDE.md) - PCF-specific AI instructions

