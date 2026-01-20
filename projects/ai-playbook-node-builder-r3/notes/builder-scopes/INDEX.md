# Builder Scope Records Index

> **Created**: 2026-01-19
> **Total Scopes**: 23
> **Task**: 020-create-builder-scope-records.poml

---

## Summary

This directory contains the 23 builder-specific scope records that power the AI Playbook Builder itself. All scopes are:
- System-owned (`ownerType: 1`)
- Immutable (`isImmutable: true`)
- Prefixed with `SYS-`

---

## Action Scopes (ACT-BUILDER-*) - 5 Total

| ID | File | Name | Purpose |
|----|------|------|---------|
| ACT-BUILDER-001 | [ACT-BUILDER-001-intent-classification.json](ACT-BUILDER-001-intent-classification.json) | Intent Classification | Parse user message into operation intent |
| ACT-BUILDER-002 | [ACT-BUILDER-002-node-configuration.json](ACT-BUILDER-002-node-configuration.json) | Node Configuration | Generate node config from requirements |
| ACT-BUILDER-003 | [ACT-BUILDER-003-scope-selection.json](ACT-BUILDER-003-scope-selection.json) | Scope Selection | Select appropriate existing scope |
| ACT-BUILDER-004 | [ACT-BUILDER-004-scope-creation.json](ACT-BUILDER-004-scope-creation.json) | Scope Creation | Generate new scope definition |
| ACT-BUILDER-005 | [ACT-BUILDER-005-build-plan-generation.json](ACT-BUILDER-005-build-plan-generation.json) | Build Plan Generation | Create structured plan from requirements |

---

## Skill Scopes (SKL-BUILDER-*) - 5 Total

| ID | File | Name | Purpose |
|----|------|------|---------|
| SKL-BUILDER-001 | [SKL-BUILDER-001-lease-analysis-pattern.json](SKL-BUILDER-001-lease-analysis-pattern.json) | Lease Analysis Pattern | How to build lease playbooks |
| SKL-BUILDER-002 | [SKL-BUILDER-002-contract-review-pattern.json](SKL-BUILDER-002-contract-review-pattern.json) | Contract Review Pattern | Contract playbook patterns |
| SKL-BUILDER-003 | [SKL-BUILDER-003-risk-assessment-pattern.json](SKL-BUILDER-003-risk-assessment-pattern.json) | Risk Assessment Pattern | Risk workflow patterns |
| SKL-BUILDER-004 | [SKL-BUILDER-004-node-type-guide.json](SKL-BUILDER-004-node-type-guide.json) | Node Type Guide | When to use each node type |
| SKL-BUILDER-005 | [SKL-BUILDER-005-scope-matching.json](SKL-BUILDER-005-scope-matching.json) | Scope Matching | Find/create appropriate scopes |

---

## Tool Scopes (TL-BUILDER-*) - 9 Total

| ID | File | Name | Operation |
|----|------|------|-----------|
| TL-BUILDER-001 | [TL-BUILDER-001-addNode.json](TL-BUILDER-001-addNode.json) | Add Node | Add node to canvas |
| TL-BUILDER-002 | [TL-BUILDER-002-removeNode.json](TL-BUILDER-002-removeNode.json) | Remove Node | Remove node from canvas |
| TL-BUILDER-003 | [TL-BUILDER-003-createEdge.json](TL-BUILDER-003-createEdge.json) | Create Edge | Connect two nodes |
| TL-BUILDER-004 | [TL-BUILDER-004-updateNodeConfig.json](TL-BUILDER-004-updateNodeConfig.json) | Update Node Config | Modify node configuration |
| TL-BUILDER-005 | [TL-BUILDER-005-linkScope.json](TL-BUILDER-005-linkScope.json) | Link Scope | Wire scope to node |
| TL-BUILDER-006 | [TL-BUILDER-006-createScope.json](TL-BUILDER-006-createScope.json) | Create Scope | Create new scope in Dataverse |
| TL-BUILDER-007 | [TL-BUILDER-007-searchScopes.json](TL-BUILDER-007-searchScopes.json) | Search Scopes | Find existing scopes |
| TL-BUILDER-008 | [TL-BUILDER-008-autoLayout.json](TL-BUILDER-008-autoLayout.json) | Auto Layout | Arrange canvas nodes |
| TL-BUILDER-009 | [TL-BUILDER-009-validateCanvas.json](TL-BUILDER-009-validateCanvas.json) | Validate Canvas | Validate playbook structure |

---

## Knowledge Scopes (KNW-BUILDER-*) - 4 Total

| ID | File | Name | Content |
|----|------|------|---------|
| KNW-BUILDER-001 | [KNW-BUILDER-001-scope-catalog.json](KNW-BUILDER-001-scope-catalog.json) | Scope Catalog | Available system scopes |
| KNW-BUILDER-002 | [KNW-BUILDER-002-reference-playbooks.json](KNW-BUILDER-002-reference-playbooks.json) | Reference Playbooks | Example patterns |
| KNW-BUILDER-003 | [KNW-BUILDER-003-node-schema.json](KNW-BUILDER-003-node-schema.json) | Node Schema | Valid configurations |
| KNW-BUILDER-004 | [KNW-BUILDER-004-best-practices.json](KNW-BUILDER-004-best-practices.json) | Best Practices | Design guidelines |

---

## Common Fields

All scope records share these common fields:

```json
{
  "id": "GUID",
  "name": "SYS-{TYPE}-BUILDER-{NNN}-{purpose}",
  "displayName": "Human readable name",
  "description": "Purpose description",
  "scopeType": "Action|Skill|Knowledge|Tool",
  "ownerType": 1,
  "isImmutable": true,
  "metadata": {
    "tags": ["builder", ...],
    "version": "1.0.0",
    "category": "builder-*"
  }
}
```

---

## Type-Specific Content

| Scope Type | Content Field | Description |
|------------|---------------|-------------|
| Action | `systemPrompt` | Full system prompt for AI classification/generation |
| Skill | `promptFragment` | Domain expertise prompt fragment |
| Tool | `configuration` | Handler type, input/output schemas, validation rules |
| Knowledge | `content` | Inline content or reference to external source |

---

## GUID Patterns

GUIDs follow a systematic pattern for easy identification:

| Type | GUID Pattern |
|------|--------------|
| Actions | `a1b2c3d4-e5f6-4a5b-8c9d-00{N}00{N}00{N}00{N}` |
| Skills | `b1c2d3e4-f5a6-4b5c-9d8e-00{N}00{N}00{N}00{N}` |
| Tools | `c1d2e3f4-a5b6-4c5d-8e9f-00{N}00{N}00{N}00{N}` |
| Knowledge | `d1e2f3a4-b5c6-4d5e-9f0a-00{N}00{N}00{N}00{N}` |

---

## Acceptance Criteria Verification

- [x] All 23 scopes defined with complete content
- [x] All scopes have SYS- ownership markers (ownerType: 1)
- [x] Action scopes have valid system prompts (systemPrompt field)
- [x] Skill scopes have valid prompt fragments (promptFragment field)
- [x] Tool scopes have valid configurations (configuration field with handlerType, inputSchema, outputSchema)
- [x] Knowledge scopes have valid content (content field with sourceType)
- [x] All scopes marked as immutable (isImmutable: true)

---

*Generated by task 020*
