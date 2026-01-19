# ENH-001 & ENH-002: Canvas Node Types

> **Project**: AI Playbook Node Builder R2
> **Status**: Pending
> **Priority**: High (ENH-001), Medium (ENH-002)
> **Related**: [design.md](../../projects/ai-playbook-node-builder-r2/design.md)

---

## Overview

This document combines two related enhancements that extend the playbook canvas node type system:

- **ENH-001**: Assemble Output Node Type (new node for output consolidation)
- **ENH-002**: Start Node (Implicit) (clarification of execution entry points)

---

## ENH-001: Assemble Output Node Type

**Priority**: High
**Status**: Pending

### Problem Statement

Currently, the `Deliver Output` node must handle both:
1. Consolidating outputs from previous nodes
2. Generating and delivering documents (Word, PDF, Email)

This conflates two distinct responsibilities and limits flexibility.

### Proposed Solution

Add a new **Assemble Output** node type that provides explicit control over output consolidation before delivery.

### Use Cases

| Use Case | Description |
|----------|-------------|
| **Selective inclusion** | Include only specific outputs in final report (not all) |
| **Transformation** | Restructure/format outputs before delivery |
| **Template mapping** | Explicitly map outputs to template sections |
| **Validation** | Check completeness/quality before document generation |
| **Aggregation** | Compute cross-output metrics (e.g., overall risk score from multiple analyses) |
| **Conditional assembly** | Include/exclude sections based on conditions |

### Node Configuration

**Properties Panel:**

```
┌─────────────────────────────────────────────────────────────┐
│              Assemble Output - Properties                   │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Label:           [Compile Lease Analysis      ]            │
│                                                             │
│  Output Variable: [compiledReport              ]            │
│                                                             │
│  ─────────────────────────────────────────────────────────  │
│  SECTION MAPPING                                            │
│  ─────────────────────────────────────────────────────────  │
│                                                             │
│  Template:        [TPL-LEASE-SUMMARY-001      ▼]            │
│                                                             │
│  Section Mappings:                                          │
│  ┌─────────────────┬──────────────────┬──────────────────┐ │
│  │ Template Section│ Source Variable  │ Transform        │ │
│  ├─────────────────┼──────────────────┼──────────────────┤ │
│  │ tldr            │ tldrSummary      │ none             │ │
│  │ compliance      │ complianceAnal...│ none             │ │
│  │ parties         │ partiesExtract...│ none             │ │
│  │ financial       │ financialTerms   │ formatCurrency   │ │
│  │ riskSummary     │ [computed]       │ aggregateRisk    │ │
│  └─────────────────┴──────────────────┴──────────────────┘ │
│  [+ Add Section Mapping]                                    │
│                                                             │
│  ─────────────────────────────────────────────────────────  │
│  VALIDATION                                                 │
│  ─────────────────────────────────────────────────────────  │
│                                                             │
│  ☑ Require all mapped sections                             │
│  ☑ Fail if any source is missing                           │
│  ☐ Allow partial output                                     │
│                                                             │
│  ─────────────────────────────────────────────────────────  │
│  COMPUTED FIELDS                                            │
│  ─────────────────────────────────────────────────────────  │
│                                                             │
│  ┌─────────────────┬────────────────────────────────────┐  │
│  │ Field Name      │ Expression                         │  │
│  ├─────────────────┼────────────────────────────────────┤  │
│  │ overallRiskScore│ max(compliance.provisions[].risk)  │  │
│  │ totalIssues     │ count(compliance.provisions)       │  │
│  └─────────────────┴────────────────────────────────────┘  │
│  [+ Add Computed Field]                                     │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Canvas JSON Schema

```json
{
  "id": "node_assemble_011",
  "type": "assembleOutput",
  "position": { "x": 400, "y": 1250 },
  "data": {
    "label": "Compile Lease Analysis",
    "type": "assembleOutput",
    "outputVariable": "compiledReport",
    "templateId": "TPL-LEASE-SUMMARY-001",
    "sectionMappings": [
      {
        "templateSection": "tldr",
        "sourceVariable": "tldrSummary",
        "transform": null
      },
      {
        "templateSection": "compliance",
        "sourceVariable": "complianceAnalysis",
        "transform": null
      },
      {
        "templateSection": "riskSummary",
        "sourceVariable": null,
        "computed": true,
        "expression": "aggregateRisk(complianceAnalysis, liabilityAnalysis)"
      }
    ],
    "computedFields": [
      {
        "name": "overallRiskScore",
        "expression": "max(complianceAnalysis.provisions[*].variancePercentage)"
      },
      {
        "name": "totalRedFlags",
        "expression": "count(complianceAnalysis.provisions[?riskCoefficient=='Red'])"
      }
    ],
    "validation": {
      "requireAllMappings": true,
      "failOnMissing": true,
      "allowPartial": false
    }
  }
}
```

### Orchestration Handler

```csharp
// AssembleOutputHandler.cs (new file)

private async Task<NodeResult> ExecuteAssembleNodeAsync(
    PlaybookNode node,
    Dictionary<string, JsonElement> allOutputs)
{
    var config = node.Data;
    var assembledSections = new Dictionary<string, object>();

    // 1. Validate required inputs exist
    if (config.Validation.RequireAllMappings)
    {
        foreach (var mapping in config.SectionMappings.Where(m => !m.Computed))
        {
            if (!allOutputs.ContainsKey(mapping.SourceVariable))
            {
                if (config.Validation.FailOnMissing)
                    throw new PlaybookExecutionException($"Missing required output: {mapping.SourceVariable}");
            }
        }
    }

    // 2. Map sections from source variables
    foreach (var mapping in config.SectionMappings)
    {
        if (mapping.Computed)
        {
            // Evaluate computed expression
            var value = EvaluateExpression(mapping.Expression, allOutputs);
            assembledSections[mapping.TemplateSection] = value;
        }
        else
        {
            var sourceData = allOutputs[mapping.SourceVariable];

            // Apply transform if specified
            if (!string.IsNullOrEmpty(mapping.Transform))
            {
                sourceData = ApplyTransform(sourceData, mapping.Transform);
            }

            assembledSections[mapping.TemplateSection] = sourceData;
        }
    }

    // 3. Calculate computed fields
    var computedFields = new Dictionary<string, object>();
    foreach (var field in config.ComputedFields)
    {
        computedFields[field.Name] = EvaluateExpression(field.Expression, allOutputs);
    }

    // 4. Build final compiled report
    var compiledReport = new
    {
        metadata = new
        {
            reportId = Guid.NewGuid(),
            generatedAt = DateTime.UtcNow,
            templateId = config.TemplateId,
            playbookId = _currentPlaybookId
        },
        sections = assembledSections,
        computed = computedFields
    };

    return new NodeResult
    {
        NodeId = node.Id,
        OutputVariable = config.OutputVariable,
        Data = JsonSerializer.SerializeToElement(compiledReport)
    };
}
```

### UI Component (PCF)

**New files needed:**

```
src/client/pcf/PlaybookBuilderHost/control/
├── components/
│   ├── Nodes/
│   │   └── AssembleOutputNode.tsx    ← New node visual
│   └── Properties/
│       └── AssembleOutputProperties.tsx  ← New properties panel
```

### ENH-001 Implementation Tasks

- [ ] Add `assembleOutput` to node type enum
- [ ] Create `AssembleOutputNode.tsx` component
- [ ] Create `AssembleOutputProperties.tsx` panel
- [ ] Add node to palette with icon
- [ ] Update canvas store to handle new node type
- [ ] Add orchestration handler in C#
- [ ] Add expression evaluator for computed fields
- [ ] Add transform functions (formatCurrency, aggregateRisk, etc.)
- [ ] Update canvas JSON schema validation
- [ ] Write unit tests
- [ ] Update documentation

---

## ENH-002: Start Node (Implicit)

**Priority**: Medium
**Status**: Pending

### Description

Consider whether playbooks need an explicit "Start" node or if execution begins implicitly from nodes with no incoming edges.

**Current behavior**: Unclear - needs investigation.

### Options

| Option | Description | Pros | Cons |
|--------|-------------|------|------|
| **A) Explicit Start node** | User drags a Start node onto canvas | Clear visual entry point | Extra step for users |
| **B) Implicit start** | Nodes with no incoming edges execute first | Simpler, more intuitive | Less explicit |
| **C) Document trigger** | Start defined by trigger (e.g., "on document upload") | Event-driven | More complex |

### Recommendation

**Option B (Implicit start)** is recommended for simplicity. The playbook execution engine should automatically identify entry nodes as those with no incoming edges.

### Implementation Notes

If implementing Option B:
- The orchestration service already identifies root nodes (no dependencies)
- UI could highlight implicit start nodes with a subtle indicator
- No new node type needed

If implementing Option A:
- Add `start` to node type enum
- Create minimal `StartNode.tsx` component
- Enforce exactly one Start node per playbook

---

## Combined Effort Estimate

| Task | Effort |
|------|--------|
| Assemble Output node (PCF) | 3-4 days |
| Assemble Output handler (C#) | 2-3 days |
| Expression evaluator | 2-3 days |
| Transform functions | 1-2 days |
| Start node decision & implementation | 1 day |
| Testing | 2 days |
| **Total** | **11-15 days (~2-3 weeks)** |

---

## Open Questions

1. **Expression language**: What syntax for computed fields? JSONPath? Custom DSL? JavaScript subset?
2. **Transform library**: Pre-built transforms or custom code?
3. **Start node**: Should we formalize Option B or implement explicit Start node?

---

## Revision History

| Date | Changes |
|------|---------|
| 2026-01-16 | Initial design (extracted from design.md) |
