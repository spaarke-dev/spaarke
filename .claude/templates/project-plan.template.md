# Project Plan: {Project Name}

> **Last Updated**: {YYYY-MM-DD}  
> **Status**: Draft | Ready for Tasks | In Progress | Complete  
> **Spec**: [SPEC.md](SPEC.md)

---

## 1. Executive Summary

**Purpose**: {1-2 sentences - what and why}

**Scope**: {Key deliverables - bullets}
- {Deliverable 1}
- {Deliverable 2}
- {Deliverable 3}

**Timeline**: {X weeks/days} | **Estimated Effort**: {Y hours}

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-{NNN}**: {Constraint description}
- **ADR-{NNN}**: {Constraint description}

**From Spec**:
- {Key constraint from spec}
- {Key constraint from spec}

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| {Decision 1} | {Why chosen} | {What changes} |
| {Decision 2} | {Why chosen} | {What changes} |

### Discovered Resources

**Applicable Skills** (auto-discovered):
- `.claude/skills/{skill-name}/` - {purpose}
- `.claude/skills/{skill-name}/` - {purpose}

**Knowledge Articles**:
- `docs/ai-knowledge/{path}` - {purpose}
- `docs/adr/ADR-{NNN}` - {purpose}

**Reusable Code**:
- `{path/to/pattern.ts}` - {what to reuse}

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: {Phase Name} (Week X-Y)
└─ {Key objective}
└─ {Key deliverable}

Phase 2: {Phase Name} (Week X-Y)
└─ {Key objective}
└─ {Key deliverable}
```

### Critical Path

**Blocking Dependencies:**
- Phase {N} BLOCKED BY Phase {M}
- Task {NNN} BLOCKS {other tasks}

**High-Risk Items:**
- {Risk item} - Mitigation: {approach}

---

## 4. Phase Breakdown

### Phase 1: {Phase Name} (Week X-Y)

**Objectives:**
1. {Objective 1}
2. {Objective 2}

**Deliverables:**
- [ ] {Deliverable 1}
- [ ] {Deliverable 2}
- [ ] {Deliverable 3}

**Critical Tasks:**
- {Task description} - MUST BE FIRST / BLOCKS others

**Inputs**: {Files/resources needed}

**Outputs**: {Files/artifacts created}

### Phase 2: {Phase Name} (Week X-Y)

{Repeat structure}

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| {Azure service} | GA | Low | {Plan if unavailable} |
| {External API} | Beta | Medium | {Fallback} |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| {Existing service} | `src/path/` | Production |
| {ADR} | `docs/adr/` | Current |

---

## 6. Testing Strategy

**Unit Tests** ({X}% coverage target):
- {What to test}

**Integration Tests**:
- {Critical path 1}
- {Critical path 2}

**E2E Tests**:
- {User scenario 1}
- {User scenario 2}

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1:**
- [ ] {Criterion with measurable outcome}
- [ ] {Criterion with measurable outcome}

**Phase 2:**
- [ ] {Criterion with measurable outcome}

### Business Acceptance

- [ ] {Business metric} achieves {target}
- [ ] {Quality metric} meets {threshold}

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | {Risk} | High/Med/Low | High/Med/Low | {Plan} |
| R2 | {Risk} | High/Med/Low | High/Med/Low | {Plan} |

---

## 9. Next Steps

1. **Review this PLAN.md** with team
2. **Run** `/task-create {project-name}` to generate task files
3. **Begin** Phase 1 implementation

---

**Status**: {Current status}  
**Next Action**: {What happens next}

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
