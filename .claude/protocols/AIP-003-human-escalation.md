# AIP-003: Human Escalation Protocol

> **Status**: Active  
> **Created**: December 4, 2025  
> **Applies To**: All AI agents working in Spaarke projects

---

## Summary

This protocol defines when AI agents must request human input before proceeding. These escalation triggers prevent incorrect implementations, wasted effort, and ensure humans remain in control of critical decisions.

---

## Escalation Triggers

### Rule 1: MUST Escalate Immediately

Stop and request human input for these situations:

| Trigger | Why | Example |
|---------|-----|---------|
| **Ambiguous requirement** | Multiple valid interpretations | "Make it faster" - how much faster? |
| **Conflicting requirements** | Spec contradicts itself | Section 2 says X, section 4 says Y |
| **Missing information** | Cannot proceed without data | API endpoint not specified |
| **Security-sensitive code** | Risk of vulnerabilities | Auth, encryption, secrets handling |
| **Breaking changes** | Could affect other systems | Changing API contracts, DB schema |
| **ADR conflict** | Implementation would violate ADR | Pattern required but ADR forbids it |

### Rule 2: SHOULD Escalate Before Proceeding

Request input when possible, but can proceed if urgent:

| Trigger | Why | Proceed If |
|---------|-----|------------|
| **Multiple valid approaches** | Human preference matters | One approach clearly better |
| **Scope expansion** | Might exceed task boundaries | Small, obvious extension |
| **Performance tradeoffs** | Speed vs. memory vs. simplicity | Clear best practice exists |
| **External dependencies** | Third-party services/packages | Well-known, stable dependency |

### Rule 3: INFORM But Don't Block

Mention but continue working:

| Trigger | Why | Action |
|---------|-----|--------|
| **Minor deviation from spec** | Spec was slightly off | Note in completion report |
| **Better approach found** | Improvement opportunity | Implement, explain why |
| **Deprecated pattern used in existing code** | Following existing patterns | Use existing pattern, note for future |

---

## Escalation Format

When escalating, use this format:

```markdown
🔔 **Human Input Required**

**Situation**: {brief description of what triggered escalation}

**Context**: {relevant background}

**Question**: {specific question needing answer}

**Options** (if applicable):
1. {Option A}
   - Pros: {benefits}
   - Cons: {drawbacks}
   
2. {Option B}
   - Pros: {benefits}
   - Cons: {drawbacks}

**My recommendation**: {if you have one and why}

**Impact of delay**: {what happens if we wait for answer}

Please advise before I proceed.
```

---

## Escalation Examples

### Example 1: Ambiguous Requirement

```markdown
🔔 **Human Input Required**

**Situation**: The spec says "support large files" but doesn't define "large."

**Context**: Implementing file upload endpoint. Current limit is 10MB.

**Question**: What file size should be considered "large"? What's the maximum?

**Options**:
1. **50MB limit** - Covers most business documents
2. **100MB limit** - Handles larger presentations  
3. **500MB limit** - Supports video files

**My recommendation**: 100MB - balances user needs with server resources.

**Impact of delay**: Can implement basic upload now, add size validation later.

Please advise before I proceed.
```

### Example 2: ADR Conflict

```markdown
🔔 **Human Input Required**

**Situation**: Task requires Graph SDK types in controller, but ADR-007 forbids this.

**Context**: Task 015 step 3 says "return GraphServiceClient response directly."
ADR-007 states: "No Graph SDK types leak above SpeFileStore facade."

**Question**: Should I:
1. Follow the task spec (violate ADR-007)
2. Follow ADR-007 (deviate from spec)
3. Propose alternative approach

**My recommendation**: Option 2 - Create DTO and map inside SpeFileStore.

**Impact of delay**: Blocking - cannot proceed without clarity.

Please advise before I proceed.
```

### Example 3: Security-Sensitive Code

```markdown
🔔 **Human Input Required**

**Situation**: Task requires storing API keys in configuration.

**Context**: Implementing third-party integration. Need to store credentials.

**Question**: Where should these credentials be stored?

**Options**:
1. **Azure Key Vault** - Most secure, requires setup
2. **User Secrets** - Good for dev, not for prod
3. **appsettings.json** - Simple but NOT recommended

**My recommendation**: Azure Key Vault for production, User Secrets for development.

**Impact of delay**: Can stub the integration, add real credentials later.

Please advise before I proceed.
```

### Example 4: Multiple Valid Approaches

```markdown
🔔 **Human Input Required**

**Situation**: Two valid patterns for implementing this feature.

**Context**: Adding caching to the file metadata endpoint.

**Options**:
1. **Redis distributed cache** (per ADR-009)
   - Pros: Consistent with other caching, works in multi-instance
   - Cons: Network latency, Redis dependency
   
2. **In-memory cache with Redis fallback**
   - Pros: Faster for hot data
   - Cons: ADR-009 says "no hybrid L1 unless profiling proves need"

**My recommendation**: Option 1 - Follow ADR-009 unless you have profiling data.

**Impact of delay**: Low - can proceed with Option 1, refactor if needed.

Please advise before I proceed.
```

---

## After Human Response

When human provides direction:

1. **Acknowledge** the decision
2. **Document** in task notes or code comments
3. **Proceed** with implementation
4. **Reference** the decision if similar situation arises

Example code comment:
```csharp
// Per human decision (2025-12-04): Using 100MB file limit
// See task notes for rationale
private const int MaxFileSizeBytes = 100 * 1024 * 1024;
```

---

## Non-Escalation Situations

Do NOT escalate for:

| Situation | Why | Action Instead |
|-----------|-----|----------------|
| Implementation details | AI expertise | Make reasonable choice |
| Coding style | Follow existing patterns | Match codebase style |
| Variable naming | Follow conventions | Use clear, consistent names |
| Test coverage | Best practices apply | Aim for high coverage |
| Documentation | Standard practice | Document as you go |

---

## Rationale

### Why mandatory escalation triggers?
- **Security**: Humans must approve security-sensitive changes
- **Architecture**: ADR violations require explicit approval
- **Scope**: Prevents AI from expanding beyond task boundaries
- **Quality**: Ambiguous requirements lead to rework

### Why structured format?
- **Clarity**: Human can quickly understand the situation
- **Options**: Provides actionable choices
- **Context**: Preserves reasoning for future reference

---

## Related Protocols

- [AIP-001: Task Execution](AIP-001-task-execution.md) - Overall execution protocol
- [AIP-002: POML Format](AIP-002-poml-format.md) - Task file structure

---

*Part of [AI Protocols](INDEX.md) | Spaarke AI Knowledge Base*
