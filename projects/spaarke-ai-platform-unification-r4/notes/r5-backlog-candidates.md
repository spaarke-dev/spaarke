# R5 Backlog Candidates — UPDATED 2026-05-27

> **Date**: 2026-05-26 (initial); updated 2026-05-27
> **Source**: R4 discoveries that operator initially deferred
> **Status**: BOTH ITEMS ABSORBED — neither remains as an R5 backlog item

---

## Resolution summary (2026-05-27)

Operator review on 2026-05-27 decided to NOT defer either item:

| Original item | New disposition | Where to find |
|---|---|---|
| 1. Iframe-wizards-as-mount-sources broader pattern | **Promoted to its own project** | [`projects/spaarke-iframe-wizard-pattern-enhancement/`](../../spaarke-iframe-wizard-pattern-enhancement/) |
| 2. WorkspaceRenderer type-narrowing wrapper | **Absorbed into R4 add-on scope** (task 072) — fix approach changed: tighten `WorkspaceRendererWebApi` to require methods (Path 2a), no wrapper | R4 task 072 |

---

## 1. Iframe-wizards-as-mount-sources — MOVED TO OWN PROJECT

**Project location**: [`projects/spaarke-iframe-wizard-pattern-enhancement/`](../../spaarke-iframe-wizard-pattern-enhancement/)

**Key constraint locked in** (operator 2026-05-27): NO Power Automate, NO Dataverse plugins may participate in the solution. Allowed mechanisms limited to web platform APIs (postMessage, BroadcastChannel), BFF API (polling/SSE), Dataverse Web API (client-side), React composition.

The project's [`design.md`](../../spaarke-iframe-wizard-pattern-enhancement/design.md) covers:
- 5 use case surfaces (iframe wizards, MDA forms, background jobs, external SPAs, Office Add-ins)
- Options evaluation (postMessage, BroadcastChannel, polling, SSE, in-process migration)
- Recommended layered architecture
- 7-phase implementation plan
- Security considerations

---

## 2. WorkspaceRenderer cast cleanup — ABSORBED INTO R4 (TASK 072)

**Decision (2026-05-27)**: the wrapper approach originally proposed was rejected as architectural debt. Instead, tighten `WorkspaceRendererWebApi` to require the methods that `IWebApi` requires — this eliminates the cast without adding a component layer.

**Rationale**: per operator direction, LegalWorkspace IS the dashboard renderer; new dashboard pieces are added INSIDE that library, not as separate renderers. The "many renderers, each with different method needs" use case that motivated the loose interface doesn't exist and won't exist. The wrapper would carry that fictional flexibility forward as debt.

**Implementation in task 072**:
```typescript
type WorkspaceRendererWebApi = Pick<IWebApi,
  'createRecord' | 'retrieveRecord' | 'retrieveMultipleRecords' | 'updateRecord' | 'deleteRecord'
>;
```

Drop the `LegalWorkspaceApp as unknown as WorkspaceRenderer` cast. TypeScript proves correctness statically.

---

## Notes

- This file no longer contains pending R5 backlog items. Both originally-deferred items have been re-scoped.
- R4's Phase 7 wrap-up (task 090) should reference this file in lessons-learned to document the 2026-05-27 decisions.
- Future R4 add-on tasks may add items here if any new R5-candidates are discovered during the remaining work.

---

*Maintainer note: this file historically captured the R4-to-R5 handoff. As of 2026-05-27 the handoff is empty — both items resolved.*
