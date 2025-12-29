# Stage 0: Discovery & Research

> **Audience**: Product Managers, UX Designers  
> **Part of**: [Spaarke Software Development Procedures](INDEX.md)

---

## Purpose

Validate that we're solving the right problem before investing in solution design. Discovery reduces the risk of building features users don't need or won't use.

---

## When to Use Stage 0

| Situation | Stage 0 Required? |
|-----------|-------------------|
| New feature or module | ✅ Yes - full discovery |
| Enhancement to existing feature | ⚡ Light discovery - user feedback review |
| Bug fix | ❌ No - skip to Stage 1 |
| Technical debt / refactoring | ❌ No - skip to Stage 2 |
| Regulatory / compliance requirement | ⚡ Light discovery - stakeholder validation |

---

## Inputs

- Business hypothesis or customer feedback
- Market research or competitive analysis
- Support tickets or user complaints
- Strategic roadmap

---

## Process

### 1. Problem Framing (1-2 days)

1. **Define the hypothesis**: "We believe {user segment} has {problem} because {evidence}"
2. **Identify assumptions**: What must be true for this to be worth building?
3. **Define success metrics**: How will we know if this feature succeeds?

### 2. User Research (3-5 days)

| Method | When to Use | Output |
|--------|-------------|--------|
| **User interviews** | Understanding motivations, pain points | Interview notes, quotes |
| **Contextual inquiry** | Observing actual workflows | Journey maps, pain points |
| **Survey** | Validating at scale | Quantitative data |
| **Support ticket analysis** | Understanding common issues | Frequency analysis |

**Minimum**: 5 user interviews for new features

### 3. Jobs-to-be-Done Analysis

Frame the problem as user jobs:

```
When [situation], I want to [motivation], so I can [expected outcome].
```

**Example**:
```
When I receive a document for review, 
I want to open it in my desktop Word application, 
so I can use track changes and familiar editing tools.
```

### 4. Concept Validation (2-3 days)

1. **Create low-fidelity prototype** in Figma (wireframes, not polished)
2. **Test with 3-5 users**: "Would this solve your problem?"
3. **Iterate based on feedback**
4. **Document validated/invalidated assumptions**

---

## Outputs

| Artifact | Purpose | Tool |
|----------|---------|------|
| **Research Summary** | Key findings, user quotes, patterns | Notion/Confluence |
| **Journey Map** | User's current experience with pain points | Miro/FigJam |
| **Jobs-to-be-Done** | Framed user needs | Notion |
| **Validated Prototype** | Concept that resonates with users | Figma |
| **Assumption Log** | What was validated/invalidated | Notion |

---

## Approval Gate

| Approver | Criteria |
|----------|----------|
| Product Manager | Problem is validated, solution direction is clear |
| (Optional) Stakeholder | Strategic alignment confirmed |

**Exit Criteria**: 
- Problem validated with user evidence
- At least one solution concept tested with users
- Clear job-to-be-done documented
- Decision to proceed, pivot, or kill

---

## Checklist

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

## Next Step

Proceed to [03-stages-1-3-planning.md](03-stages-1-3-planning.md) (Stage 1: Feature Request)

---

*Part of [Spaarke Software Development Procedures](INDEX.md) | v2.0 | December 2025*
