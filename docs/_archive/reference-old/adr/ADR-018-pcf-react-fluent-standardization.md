# ADR-018: PCF React and Fluent UI Version Standardization

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-12-18 |
| Authors | Spaarke Engineering |

## Context

PCF controls can either bundle UI libraries or use host-provided platform libraries. Inconsistent React and Fluent UI versions across controls causes:
- Multiple React copies at runtime leading to crashes and subtle bugs
- Larger bundle sizes
- Incompatible hook behavior between React versions
- Styling/theming inconsistencies when mixing Fluent v8 and v9

A code review identified that SpeFileViewer was using React 19.2.0 while all other controls use React 18.2.0, creating runtime risk.

## Decision

| Rule | Description |
|------|-------------|
| **React 18.2.x** | All PCF controls must use React ^18.2.0 (not React 19) |
| **Fluent UI v9 only** | Standardize on `@fluentui/react-components` (v9). Remove `@fluentui/react` (v8) dependencies |
| **Bundle strategy** | Bundle React and Fluent (do not rely on platform-library declarations for now) |
| **No platform-library mixing** | If using `<platform-library>` declarations, ensure code matches host versions exactly |

## Rationale

| Choice | Reason |
|--------|--------|
| React 18 over 19 | React 19 is too new for PCF runtime stability; 18.2.x is battle-tested |
| Bundle over platform-library | More predictable behavior; avoids version mismatch with host |
| Fluent v9 only | v9 is the current Microsoft standard; v8 is legacy and causes bundle bloat |

## Consequences

**Positive:**
- Consistent runtime behavior across all controls
- Smaller bundles (no duplicate React/Fluent)
- Predictable hook behavior and theming

**Negative:**
- Cannot leverage host-provided React (slightly larger initial bundle)
- Must manually keep versions aligned across controls

## Operationalization

### Standard package.json dependencies

```json
{
  "dependencies": {
    "react": "^18.2.0",
    "react-dom": "^18.2.0",
    "@fluentui/react-components": "^9.46.0",
    "@fluentui/react-icons": "^2.0.0"
  }
}
```

### Prohibited dependencies

| Package | Reason |
|---------|--------|
| `react@^19.x` | Too new, runtime instability |
| `@fluentui/react` | Legacy v8, causes bloat |
| Mixed platform-library + bundled React | Version conflicts |

### Current control compliance

| Control | React | Fluent | Status |
|---------|-------|--------|--------|
| AnalysisBuilder | 18.2.0 | v9 | Compliant |
| AnalysisWorkspace | 18.2.0 | v9 | Compliant |
| SpeDocumentViewer | 18.2.0 | v9 | Compliant |
| SpeFileViewer | 18.2.0 | v9 | Compliant (fixed 2025-12-18) |
| UniversalDatasetGrid | 18.2.0 | v9 | Compliant |
| UniversalQuickCreate | 18.2.0 | v9 | Compliant |

## Compliance

**Code review checklist:**
- [ ] React version is ^18.2.0 (not 19.x)
- [ ] No `@fluentui/react` (v8) in dependencies
- [ ] No mixed platform-library + bundled React approach
- [ ] Uses `createRoot` pattern (React 18+) not `ReactDOM.render`

## AI-Directed Coding Guidance

- When creating new PCF controls, use React 18.2.x and Fluent v9 only
- If you see React 19 or Fluent v8 in a package.json, flag it for remediation
- Do not add `<platform-library>` declarations unless the entire control is designed for host-provided libraries
- Use `createRoot` from `react-dom/client` (React 18 pattern)
