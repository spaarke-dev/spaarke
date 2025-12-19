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

## Usage by AI Agents

Load concise ADRs proactively when creating new components:
- Creating API → Load ADR-001, ADR-008, ADR-010
- Creating PCF → Load ADR-006, ADR-012, ADR-014, ADR-015, ADR-018
- Creating Plugin → Load ADR-002
- Working with auth → Load ADR-004, ADR-016
- Working with SPE → Load ADR-007, ADR-019

Full ADRs in `docs/adr/` should be loaded only when:
- Need historical context
- Debugging architectural decisions
- Proposing changes to architecture
