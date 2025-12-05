# ADR-006: Prefer PCF controls over legacy JavaScript webresources

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-09-27 |
| Updated | 2025-12-04 |
| Authors | Spaarke Engineering |

## Context

Legacy webresources (JS/HTML) are harder to package, test, and lifecycle with modern Power Platform. PCF controls provide typed, testable components and better performance characteristics.

## Decision

| Rule | Description |
|------|-------------|
| **PCF for Model-Driven** | Build custom UI using PCF controls (TypeScript) for model-driven apps and custom pages |
| **No new webresources** | Do not create new legacy JavaScript webresources |
| **React for SPAs** | Use React/SPA for external surfaces (Power Pages, add-ins) |

## Consequences

**Positive:**
- Better lifecycle management, packaging, and testability
- Access to modern UI patterns and performance improvements

**Negative:**
- Learning curve for PCF; initial scaffolding effort

## Alternatives Considered

Continue with webresources. **Rejected** due to maintainability and lifecycle limitations.

## Operationalization

| UI Surface | Technology | Location |
|------------|------------|----------|
| Model-driven forms | PCF (TypeScript) | `src/client/pcf/` |
| Subgrid replacement | Dataset PCF | `src/client/pcf/UniversalDatasetGrid/` |
| Quick create dialogs | PCF + FluentUI | `src/client/pcf/UniversalQuickCreate/` |
| External SPAs | React | `src/client/apps/` |
| Office add-ins | React | `src/office-addins/` |

## Exceptions

### Allowed Webresources

| File | Purpose | Status |
|------|---------|--------|
| `sprk_subgrid_commands.js` | Ribbon/command bar button invocation | ✅ Allowed (minimal, invocation only) |
| `DocumentOperations.js` | Legacy SPE utility (859 LoC) | ⚠️ Legacy - no new features; refactor to PCF planned |

### Exception Rules

1. **Ribbon/Command Bar Scripts** - Required for Model-Driven App command bar buttons. Must be minimal (invocation only, no business logic).

2. **Pre-existing Legacy Files** - May remain if low-risk and read-only. No new features to be added.

3. **New webresources** - Require explicit approval with documented justification.

## Success Metrics

| Metric | Target |
|--------|--------|
| UI regressions | Reduced |
| Embedded UI delivery | Faster |
| Performance | Improved (vs webresource baseline) |
| New webresources created | Zero (without approval) |

## Compliance

**Code review checklist:**
- [ ] No new `.js` files in `webresources/` without approval
- [ ] Command bar scripts contain invocation only
- [ ] PCF used for all new interactive UI
