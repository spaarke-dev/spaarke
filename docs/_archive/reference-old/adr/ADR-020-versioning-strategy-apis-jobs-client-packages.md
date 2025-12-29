# ADR-020: Versioning Strategy (APIs, Jobs, and Client Packages)

| Field | Value |
|-------|-------|
| Status | **Proposed** |
| Date | 2025-12-12 |
| Updated | 2025-12-12 |
| Authors | Spaarke Engineering |
| Sprint | TBD |

---

## Context

Spaarke spans multiple deployable surfaces:
- BFF Minimal API endpoints
- Background jobs (`JobContract`) and handlers
- PCF controls
- Shared UI component library packages

Without a versioning strategy, changes can create brittle deployments and compatibility breaks, especially as async work and shared packages grow.

## Decision

### Decision Rules

| Rule | Requirement |
|------|-------------|
| **SemVer for packages** | Client packages (e.g., shared UI components) use SemVer. Breaking API changes require a major bump. |
| **Tolerant readers** | Job handlers and clients must be tolerant readers: accept older payloads when possible; default missing fields safely. |
| **Schema versioning** | Any evolving contract (job payloads, AI prompt formats, cached artifacts) must have an explicit version input that can be used for compatibility and cache invalidation. |
| **No silent breaking changes** | Breaking changes require an ADR update and a migration plan.
| **Deprecation window** | Deprecate before removal for external-facing APIs; removal must be planned and communicated.

## Scope

Applies to:
- API route and request/response contracts
- Job payload shapes and handler expectations
- Shared packages (PCF shared code, UI components)

## Non-goals

- Defining the release train mechanics for all environments

## Operationalization

### APIs

- Prefer additive changes:
  - Add new fields
  - Add new endpoints
- Avoid breaking changes:
  - Renaming fields
  - Changing semantics of existing fields

### Jobs

- Keep `JobContract` stable (ADR-004).
- Treat `Payload` as versioned:
  - Payload producers and consumers must agree on a `payloadVersion` or equivalent version input.
  - If a breaking payload change is needed, introduce a new `JobType` or new payload version with tolerant parsing.

### Client packages

- Shared UI components should:
  - Have a clear public API surface (barrel exports)
  - Version breaking changes with a major bump
  - Document migrations

## Failure modes

- **Breaking deployments** (client/server mismatch)
- **Jobs poison** due to incompatible payload changes
- **Cache poisoning** if versions aren’t included in keying

## AI-Directed Coding Guidance

When introducing a contract change:
- Identify whether it is additive vs breaking.
- Add/maintain a version input for the artifact.
- Prefer tolerant parsing to keep old messages working.

## Compliance checklist

- [ ] Contract changes are additive or explicitly versioned.
- [ ] Job payload parsing is tolerant.
- [ ] Cache keys include a version input (ADR-014).
- [ ] Package/API breaking changes are versioned and documented.

## Related ADRs

- [ADR-004: Async job contract](./ADR-004-async-job-contract.md)
- [ADR-012: Shared component library](./ADR-012-shared-component-library.md)
- [ADR-014: AI caching and reuse policy](./ADR-014-ai-caching-and-reuse-policy.md)

---

## Revision History

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2025-12-12 | 0.1 | Initial proposed ADR | Spaarke Engineering |
