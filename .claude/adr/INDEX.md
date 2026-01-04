# ADRs - Concise Versions (AI Context)

> **Purpose**: Concise versions of ADRs optimized for AI context loading
> **Target**: 100-150 lines per ADR
> **Full versions**: See `docs/adr/` for complete ADRs

## About This Directory

This directory will contain AI-optimized versions of Architecture Decision Records. Each file focuses on:
- **Decision**: What was decided
- **Constraints**: MUST/MUST NOT rules
- **Key patterns**: Code examples
- **Rationale**: Brief why (1-2 sentences)

**Omitted from concise versions**:
- Verbose context/background
- Historical discussion
- Detailed alternatives analysis
- Long examples

## Phase 3 TODO

- [ ] Create concise version of each ADR from `docs/adr/`
- [ ] Focus on actionable constraints and patterns
- [ ] Keep to 100-150 line target
- [ ] Cross-reference full ADR for deep dives

## ADR Index

| ADR | Title | Key Constraint |
|-----|-------|----------------|
| ADR-001 | Minimal API + BackgroundService | No Azure Functions |
| ADR-002 | Thin Dataverse plugins | No HTTP/Graph calls in plugins |
| ADR-006 | PCF over webresources | No new legacy JS webresources |
| ADR-007 | SpeFileStore facade | No Graph SDK types leak above facade |
| ADR-008 | Endpoint filters for auth | No global auth middleware |
| ADR-010 | DI minimalism | ≤15 non-framework DI registrations |
| ADR-012 | Shared component library | Reuse `@spaarke/ui-components` |
| ADR-013 | AI Architecture | AI Tool Framework; extend BFF |
| ADR-021 | Fluent UI v9 Design System | All UI uses Fluent v9; dark mode required |
| ADR-023 | Choice Dialog Pattern | Rich option buttons for 2-4 choices |

---

## Usage by AI Agents

Load concise ADRs proactively when creating new components:
- Creating API → Load ADR-001, ADR-008, ADR-010
- Creating PCF → Load ADR-006, ADR-012, ADR-022 (React 16 compatibility)
- Creating Plugin → Load ADR-002
- Working with auth → Load ADR-004, ADR-016
- Working with SPE → Load ADR-007, ADR-019
- Working with UI/UX → Load ADR-021, ADR-022
- Creating dialogs → Load ADR-021, ADR-023

Full ADRs in `docs/adr/` should be loaded only when:
- Need historical context
- Debugging architectural decisions
- Proposing changes to architecture
