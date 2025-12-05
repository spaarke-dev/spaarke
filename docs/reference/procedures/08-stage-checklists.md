# Stage Checklists

> **Audience**: All (quick reference)  
> **Part of**: [Spaarke Software Development Procedures](INDEX.md)

---

## Stage 0: Discovery & Research

- [ ] Problem hypothesis defined
- [ ] Key assumptions identified
- [ ] User interviews conducted (minimum 5 for new features)
- [ ] Jobs-to-be-done documented
- [ ] Journey map created (for UX-heavy features)
- [ ] Low-fidelity prototype created in Figma
- [ ] Prototype validated with 3-5 users
- [ ] Assumptions validated/invalidated documented
- [ ] Research summary written
- [ ] Decision: proceed / pivot / kill

---

## Stage 1: Feature Request

- [ ] Discovery artifacts linked (if Stage 0 was done)
- [ ] Business need documented
- [ ] User value articulated (linked to JTBD)
- [ ] User scenarios defined (3-5 primary)
- [ ] User roles identified
- [ ] UX expectations described (Figma links)
- [ ] Success metrics defined
- [ ] PM approval obtained

---

## Stage 2: Solution Assessment

- [ ] Technical team reviewed Feature Request
- [ ] 2-3 solution options evaluated
- [ ] Recommended approach selected with rationale
- [ ] Architecture impact analyzed
- [ ] Applicable ADRs identified
- [ ] RFC created (for significant changes)
- [ ] RFC reviewed and approved (if applicable)
- [ ] Technical risks identified
- [ ] Effort estimated
- [ ] PM + Dev approval obtained

---

## Stage 3: Design Specification

- [ ] Detailed technical design complete
- [ ] Architecture diagram created (if applicable)
- [ ] Data model defined
- [ ] API contracts specified
- [ ] UI/UX specifications documented (Figma high-fidelity)
- [ ] **BDD scenarios written in Gherkin format**
- [ ] Code recommendations provided
- [ ] Files to create/modify listed
- [ ] ADR compliance documented
- [ ] Acceptance criteria testable
- [ ] Testing approach defined
- [ ] Peer review complete
- [ ] PM + Dev approval obtained
- [ ] spec.md created in projects folder

---

## Stage 4: Project Initialization

- [ ] Project folder created at `projects/{project-name}/`
- [ ] spec.md in place
- [ ] README.md generated and reviewed
- [ ] plan.md generated and reviewed
- [ ] CLAUDE.md generated
- [ ] tasks/ directory created
- [ ] notes/ directory created with subdirectories
- [ ] Developer approval obtained

---

## Stage 5: Task Decomposition

- [ ] All plan.md phases have tasks
- [ ] Task numbering follows convention (001, 010, 020...)
- [ ] Tasks sized appropriately (2-4 hours)
- [ ] Dependencies form valid sequence
- [ ] First tasks have no unmet dependencies
- [ ] Each task has all required POML sections
- [ ] Acceptance criteria are testable
- [ ] TASK-INDEX.md updated
- [ ] Developer approval obtained

---

## Stage 6: Task Execution

Per task:
- [ ] Context check performed (< 70%)
- [ ] Progress reviewed
- [ ] Resources gathered
- [ ] Implementation planned
- [ ] Code implemented
- [ ] Tests written
- [ ] Tests pass
- [ ] Build succeeds
- [ ] TASK-INDEX.md updated (✅)
- [ ] Deviations documented

---

## Stage 7: Testing & Validation

- [ ] All unit tests pass
- [ ] All integration tests pass
- [ ] All E2E tests pass
- [ ] Build succeeds
- [ ] ADR validation run
- [ ] High-priority violations fixed
- [ ] Code review complete
- [ ] Security review complete
- [ ] Developer approval obtained

---

## Stage 8: Documentation & Completion

- [ ] Feature documentation created/updated
- [ ] Technical documentation updated
- [ ] In-code documentation verified
- [ ] All tasks marked complete in TASK-INDEX.md
- [ ] notes/ directory cleaned
- [ ] Pull request created
- [ ] PR approved
- [ ] CI/CD passes
- [ ] PR merged
- [ ] Graduation criteria met
- [ ] PM acceptance obtained

---

*Part of [Spaarke Software Development Procedures](INDEX.md) | v2.0 | December 2025*
