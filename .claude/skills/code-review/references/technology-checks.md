# Step 7 — Technology-Specific Checks

Read by `code-review` Step 7. Apply the section for each file type present in the scope; skip the others.

## .NET (.cs)

```
  Minimal API patterns
  - Endpoint groups properly organized
  - Result pattern for error handling
  - Dependency injection registration minimal (ADR-010)

  Nullable reference types
  - Proper null checks (and none on non-nullable types — Step 5.5 Smell 3)
  - No null-forgiving (!) without justification

  Modern C# patterns
  - Using file-scoped namespaces
  - Using records for DTOs where appropriate
```

## TypeScript / PCF (.ts, .tsx)

```
  React patterns
  - Proper hook usage (rules of hooks)
  - Memoization where appropriate
  - Key prop in lists

  TypeScript strictness
  - No "any" types without justification
  - Proper interface definitions
  - Null/undefined handling

  Fluent UI v9 Design System (ADR-021)
  - Using @fluentui/react-components (v9), NOT @fluentui/react (v8)
  - Icons from @fluentui/react-icons with currentColor
  - Semantic tokens (tokens.colorNeutralBackground1), no hard-coded colors
  - FluentProvider wrapper with theme
  - Dark mode compatibility (no hard-coded hex colors)
  - Accessibility: aria-labels on icon-only buttons
```

## Dataverse write path (ADR-002 WP-1…WP-8)

Applies to record create/update paths (wizard services, BFF writers) and to any plugin code. Rule text: `.claude/adr/ADR-002-thin-plugins.md`; registry and detail: `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`.

```
  - ANY plugin code / plugin-backed Custom API / low-code plugin → Critical (no plugins exist in Spaarke)
  - New rule a record must satisfy on save → has exactly ONE owner, listed in the invariant registry
    (DATAVERSE-WRITE-PATH-ARCHITECTURE.md §5): a BFF server-side write-path component, or a
    platform-native declarative mechanism only under WP-1 (a)–(c)                       [WP-1]
  - Wizard/PCF/add-in applies the invariant only as preview, not sole enforcement      [WP-2]
  - Invariant-bearing table created/updated via BFF, not Xrm.WebApi                    [WP-3]
  - Security/on-load invariants applied inline; multi-row = one Dataverse transaction  [WP-4]
  - Non-product writes have an idempotent fill-only fix-up or reconciliation          [WP-5]
  - Security invariants fail closed (NULL/absent ≠ "not secure")                       [WP-6]
  - Bulk import goes through a BFF batch path, or a raw load + server-side
    reconciliation pass; never per-row logic in Dataverse                              [WP-7]
  - Customer-built forms/flows are pointed at Spaarke BFF endpoints; nothing
    re-implements a Spaarke invariant in a customer plugin                             [WP-8]
```
