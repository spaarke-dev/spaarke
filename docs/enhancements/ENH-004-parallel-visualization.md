# ENH-004: Parallel Execution Visualization

> **Project**: AI Playbook Node Builder R2
> **Status**: Pending
> **Priority**: Low
> **Effort**: 2-3 days
> **Related**: [design.md](../../projects/ai-playbook-node-builder-r2/design.md)

---

## Description

Visual indication in the playbook builder when nodes will execute in parallel vs. sequential.

---

## Problem Statement

Currently, users cannot easily see which nodes in a playbook will execute in parallel vs. sequentially. Understanding execution order helps users:

1. Optimize playbook performance
2. Understand data flow and dependencies
3. Debug execution timing issues

---

## Proposed Solution

Nodes at the same "level" (no dependencies between them) should be visually highlighted as a parallel execution group.

---

## Visual Concept

```
       ┌──────────────┐
       │  Document    │   Level 0 (Start)
       │   Upload     │
       └──────┬───────┘
              │
    ┌─────────┴─────────┐
    │                   │
┌───┴───┐          ┌────┴────┐
│Extract│          │ Extract │   Level 1 (Parallel)
│Parties│          │Financial│   ← Highlighted as parallel group
└───┬───┘          └────┬────┘
    │                   │
    └─────────┬─────────┘
              │
       ┌──────┴──────┐
       │ Compliance  │   Level 2 (Sequential)
       │  Analysis   │
       └──────┬──────┘
              │
       ┌──────┴──────┐
       │  Generate   │   Level 3 (Sequential)
       │   Report    │
       └─────────────┘
```

---

## Implementation Options

### Option A: Level Bands (Background)

Draw subtle horizontal bands behind nodes at the same execution level:

```
┌─────────────────────────────────────────────────────────┐
│  Level 0: Sequential                                    │
│           ┌──────────┐                                  │
│           │ Document │                                  │
│           └──────────┘                                  │
├─────────────────────────────────────────────────────────┤
│  Level 1: Parallel (2 nodes)                 ⚡         │
│     ┌──────────┐        ┌──────────┐                   │
│     │ Extract  │        │ Extract  │                   │
│     │ Parties  │        │Financial │                   │
│     └──────────┘        └──────────┘                   │
├─────────────────────────────────────────────────────────┤
│  Level 2: Sequential                                    │
│           ┌──────────┐                                  │
│           │Compliance│                                  │
│           └──────────┘                                  │
└─────────────────────────────────────────────────────────┘
```

### Option B: Node Badges

Add a small "parallel" indicator badge to nodes that execute concurrently:

```
┌──────────────────┐     ┌──────────────────┐
│ ⚡ Extract       │     │ ⚡ Extract       │
│    Parties       │     │    Financial     │
└──────────────────┘     └──────────────────┘
   (parallel)               (parallel)
```

### Option C: Hover/Selection Mode

Only show parallel grouping when user hovers or selects "Show Execution Order" mode:

```
☑ Show parallel execution groups

[When enabled, parallel nodes get colored border or highlight]
```

---

## Recommendation

**Option C (Hover/Selection Mode)** is recommended:

- Non-intrusive by default
- User can enable when debugging
- Simpler implementation
- Doesn't clutter canvas permanently

---

## Implementation Tasks

- [ ] Calculate execution levels from graph topology
- [ ] Add "Show Execution Order" toggle to toolbar
- [ ] Implement level calculation algorithm (topological sort)
- [ ] Add visual indicators for parallel groups
- [ ] Add tooltip showing level info on hover

---

## Technical Notes

### Level Calculation Algorithm

```typescript
function calculateExecutionLevels(nodes: Node[], edges: Edge[]): Map<string, number> {
  const levels = new Map<string, number>();
  const inDegree = new Map<string, number>();

  // Initialize
  nodes.forEach(n => {
    inDegree.set(n.id, 0);
    levels.set(n.id, 0);
  });

  // Count incoming edges
  edges.forEach(e => {
    inDegree.set(e.target, (inDegree.get(e.target) || 0) + 1);
  });

  // BFS from root nodes
  const queue = nodes.filter(n => inDegree.get(n.id) === 0);

  while (queue.length > 0) {
    const node = queue.shift()!;
    const nodeLevel = levels.get(node.id) || 0;

    // Find outgoing edges
    const outgoing = edges.filter(e => e.source === node.id);
    outgoing.forEach(e => {
      levels.set(e.target, Math.max(levels.get(e.target) || 0, nodeLevel + 1));
      inDegree.set(e.target, (inDegree.get(e.target) || 0) - 1);
      if (inDegree.get(e.target) === 0) {
        queue.push(nodes.find(n => n.id === e.target)!);
      }
    });
  }

  return levels;
}
```

---

## Effort Estimate

| Task | Effort |
|------|--------|
| Level calculation | 0.5 days |
| UI toggle | 0.5 days |
| Visual indicators | 1 day |
| Testing | 0.5 days |
| **Total** | **2-3 days** |

---

## Dependencies

- None (standalone enhancement)

---

## Revision History

| Date | Changes |
|------|---------|
| 2026-01-16 | Initial design (extracted from design.md) |
