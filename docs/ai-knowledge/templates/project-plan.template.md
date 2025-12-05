# Project Plan: {Project Name}

> **Last Updated**: {YYYY-MM-DD}
>
> **Status**: Draft | Approved | In Progress | Complete
>
> **Related**: [Project README](./README.md) | [Design Spec](./design.md)

---

## 1. Executive Summary

### 1.1 Purpose

{What is this project and why are we doing it? 2-3 sentences.}

### 1.2 Business Value

{Quantifiable value: time saved, revenue impact, risk reduction, user satisfaction improvement.}

### 1.3 Success Criteria

| Metric | Target | Measurement Method |
|--------|--------|-------------------|
| {Metric 1} | {Target value} | {How measured} |
| {Metric 2} | {Target value} | {How measured} |

---

## 2. Background & Context

### 2.1 Current State

{Describe the current situation. What exists today? What are the pain points?}

### 2.2 Desired State

{Describe the end state after project completion. What will be different?}

### 2.3 Gap Analysis

| Area | Current State | Desired State | Gap |
|------|--------------|---------------|-----|
| {Area 1} | {Description} | {Description} | {What's missing} |
| {Area 2} | {Description} | {Description} | {What's missing} |

---

## 3. Solution Overview

### 3.1 Approach

{High-level description of the technical approach. Reference ADRs where applicable.}

### 3.2 Architecture Impact

{How does this affect the existing architecture? New components? Modified components?}

```
{Optional: Simple architecture diagram or component list}
```

### 3.3 Key Technical Decisions

| Decision | Options Considered | Selected | Rationale |
|----------|-------------------|----------|-----------|
| {Decision 1} | A, B, C | A | {Why A was chosen} |
| {Decision 2} | X, Y | Y | {Why Y was chosen} |

---

## 4. Scope Definition

### 4.1 In Scope

| Item | Description | Priority |
|------|-------------|----------|
| {Feature 1} | {Description} | Must Have |
| {Feature 2} | {Description} | Must Have |
| {Feature 3} | {Description} | Should Have |
| {Feature 4} | {Description} | Nice to Have |

### 4.2 Out of Scope

| Item | Reason | Future Consideration |
|------|--------|---------------------|
| {Item 1} | {Why excluded} | {Yes/No - Phase 2?} |
| {Item 2} | {Why excluded} | {Yes/No} |

### 4.3 Assumptions

- {Assumption 1 - e.g., "API X will be available by start date"}
- {Assumption 2 - e.g., "Team has required skills"}
- {Assumption 3}

### 4.4 Constraints

- {Constraint 1 - e.g., "Must use existing authentication system"}
- {Constraint 2 - e.g., "Budget limited to X"}
- {Constraint 3 - e.g., "Must complete before Q2"}

---

## 5. Timeline & Milestones

### 5.1 Project Timeline

| Phase | Start Date | End Date | Duration |
|-------|------------|----------|----------|
| Assessment | {YYYY-MM-DD} | {YYYY-MM-DD} | {X days} |
| Design | {YYYY-MM-DD} | {YYYY-MM-DD} | {X days} |
| Development | {YYYY-MM-DD} | {YYYY-MM-DD} | {X days} |
| Testing | {YYYY-MM-DD} | {YYYY-MM-DD} | {X days} |
| Documentation | {YYYY-MM-DD} | {YYYY-MM-DD} | {X days} |
| Deployment | {YYYY-MM-DD} | {YYYY-MM-DD} | {X days} |

### 5.2 Key Milestones

| Milestone | Target Date | Criteria | Status |
|-----------|-------------|----------|--------|
| M1: Design Approved | {YYYY-MM-DD} | Design spec signed off | ⬜ Not Started |
| M2: Dev Complete | {YYYY-MM-DD} | All features implemented | ⬜ Not Started |
| M3: Testing Complete | {YYYY-MM-DD} | All tests passing | ⬜ Not Started |
| M4: Production Deploy | {YYYY-MM-DD} | Live in production | ⬜ Not Started |

### 5.3 Gantt View (Optional)

```
Phase          W1   W2   W3   W4   W5   W6   W7   W8
Assessment     ████
Design              ████ ████
Development                   ████ ████ ████
Testing                                 ████ ████
Documentation                           ████
Deployment                                        ████
```

---

## 6. Work Breakdown Structure

### 6.1 Phase 1: Assessment

| Task | Owner | Estimate | Dependencies |
|------|-------|----------|--------------|
| Review existing system | {Name} | {X days} | — |
| Stakeholder interviews | {Name} | {X days} | — |
| Document requirements | {Name} | {X days} | Interviews complete |

### 6.2 Phase 2: Design

| Task | Owner | Estimate | Dependencies |
|------|-------|----------|--------------|
| Data model design | {Name} | {X days} | Assessment complete |
| API design | {Name} | {X days} | Data model |
| UI wireframes | {Name} | {X days} | Requirements |
| Design review | {Team} | {X days} | All designs |

### 6.3 Phase 3: Development

| Task | Owner | Estimate | Dependencies |
|------|-------|----------|--------------|
| {Component 1} | {Name} | {X days} | Design approved |
| {Component 2} | {Name} | {X days} | Design approved |
| {Component 3} | {Name} | {X days} | Component 1 |
| Integration | {Name} | {X days} | All components |

### 6.4 Phase 4: Testing

| Task | Owner | Estimate | Dependencies |
|------|-------|----------|--------------|
| Unit tests | {Name} | {X days} | Development |
| Integration tests | {Name} | {X days} | Integration complete |
| UAT | {Name} | {X days} | Integration tests |

### 6.5 Phase 5: Documentation & Deployment

| Task | Owner | Estimate | Dependencies |
|------|-------|----------|--------------|
| User documentation | {Name} | {X days} | UAT complete |
| Admin guide | {Name} | {X days} | UAT complete |
| Production deployment | {Name} | {X days} | All tests pass |

---

## 7. Resources

### 7.1 Team

| Role | Name | Allocation | Period |
|------|------|------------|--------|
| Project Lead | {Name} | {X%} | Full project |
| Developer | {Name} | {X%} | Development phase |
| QA | {Name} | {X%} | Testing phase |

### 7.2 Tools & Infrastructure

| Resource | Purpose | Status |
|----------|---------|--------|
| {Tool 1} | {Purpose} | Available / Needed |
| {Environment 1} | {Purpose} | Available / Needed |

### 7.3 Budget (if applicable)

| Category | Estimated Cost | Actual Cost | Notes |
|----------|---------------|-------------|-------|
| Infrastructure | ${X} | — | {Notes} |
| Tools/Licenses | ${X} | — | {Notes} |
| External Services | ${X} | — | {Notes} |
| **Total** | **${X}** | — | |

---

## 8. Risk Management

### 8.1 Risk Register

| ID | Risk | Impact | Likelihood | Score | Mitigation | Owner | Status |
|----|------|--------|------------|-------|------------|-------|--------|
| R1 | {Risk description} | High | Medium | 6 | {Mitigation plan} | {Name} | Open |
| R2 | {Risk description} | Medium | Medium | 4 | {Mitigation plan} | {Name} | Open |
| R3 | {Risk description} | Low | High | 3 | {Mitigation plan} | {Name} | Open |

*Score: Impact (H=3, M=2, L=1) × Likelihood (H=3, M=2, L=1)*

### 8.2 Contingency Plans

| Trigger | Contingency Action |
|---------|-------------------|
| {If risk X occurs} | {Then do Y} |
| {If deadline slips by > X days} | {Then do Y} |

---

## 9. Communication Plan

### 9.1 Stakeholders

| Stakeholder | Interest | Communication Frequency | Method |
|-------------|----------|------------------------|--------|
| {Stakeholder 1} | {What they care about} | Weekly | Email/Meeting |
| {Stakeholder 2} | {What they care about} | Milestone | Report |

### 9.2 Status Reporting

| Report | Frequency | Audience | Owner |
|--------|-----------|----------|-------|
| Status Update | Weekly | Team | {Name} |
| Milestone Report | Per milestone | Stakeholders | {Name} |
| Final Report | Project end | All | {Name} |

---

## 10. Quality Assurance

### 10.1 Quality Gates

| Gate | Criteria | Approver |
|------|----------|----------|
| Design Review | Design spec approved, ADRs recorded | {Name} |
| Code Review | All PRs reviewed, no critical issues | {Name} |
| Test Complete | 80%+ coverage, all tests pass | {Name} |
| Release Ready | UAT signed off, docs complete | {Name} |

### 10.2 Definition of Done

A feature is **done** when:

- [ ] Code complete and merged to main branch
- [ ] Unit tests written and passing (≥80% coverage)
- [ ] Integration tests passing
- [ ] Code reviewed and approved
- [ ] Documentation updated
- [ ] No critical or high bugs open

---

## 11. Dependencies & Integration

### 11.1 Internal Dependencies

| Dependency | Team/System | Status | Impact if Delayed |
|------------|-------------|--------|-------------------|
| {Dependency 1} | {Team} | Ready | {Impact description} |
| {Dependency 2} | {System} | Pending | {Impact description} |

### 11.2 External Dependencies

| Dependency | Vendor/System | Status | Fallback Plan |
|------------|---------------|--------|---------------|
| {External dep 1} | {Vendor} | Confirmed | {Fallback} |

---

## 12. Acceptance Criteria

### 12.1 Functional Requirements

| ID | Requirement | Acceptance Test | Status |
|----|-------------|-----------------|--------|
| FR1 | {Requirement description} | {How to verify} | ⬜ |
| FR2 | {Requirement description} | {How to verify} | ⬜ |
| FR3 | {Requirement description} | {How to verify} | ⬜ |

### 12.2 Non-Functional Requirements

| ID | Requirement | Target | Measurement |
|----|-------------|--------|-------------|
| NFR1 | Performance | {e.g., <200ms response} | Load test |
| NFR2 | Availability | {e.g., 99.9% uptime} | Monitoring |
| NFR3 | Security | {e.g., No critical vulns} | Security scan |

---

## 13. Appendices

### A. Glossary

| Term | Definition |
|------|------------|
| {Term 1} | {Definition} |
| {Term 2} | {Definition} |

### B. References

- [Reference 1](link)
- [Reference 2](link)

### C. Related Documents

- [Design Specification](./design.md)
- [Task Breakdown](./tasks.md)
- [ADR-XXX](../../adr/ADR-XXX.md)

---

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| {YYYY-MM-DD} | 1.0 | Initial plan | {Name} |

---

*Template version: 1.0 | Based on Spaarke development lifecycle*
