# Task 023 — CreateInvoiceWizard verification handoff

**Date**: 2026-06-07
**Task**: 023 — CreateInvoiceWizard FR-WIZ-03 (verify + fix `sprk_containerid` + add `sprk_searchindexname` from BU)
**Status**: BLOCKED — wizard does not exist in repository
**Reporter**: task-execute (FULL rigor, parallel-group A1)

---

## TL;DR

**`CreateInvoiceWizard` does not exist in the codebase.** Neither the shared-lib component nor a consuming code-page is present. FR-WIZ-03 cannot be implemented as a "verify-and-extend" task — there is no wizard to extend. Spec assumption A1 is incorrect for the Invoice case (the assumption claims "follow the same pattern as `CreateMatterWizard`"; the wizard simply doesn't exist).

---

## Evidence gathered

### Search 1 — Component directory under shared lib

Glob: `src/client/shared/Spaarke.UI.Components/src/components/Create*Wizard/**`

Existing wizards found:
- `CreateEventWizard/` (eventService.ts present)
- `CreateMatterWizard/` (matterService.ts present — canonical line 216 cascade)
- `CreateProjectWizard/` (projectService.ts present)
- `CreateRecordWizard/` (generic wrapper)
- `CreateTodoWizard/` (todoService.ts present)
- `CreateWorkAssignmentWizard/` (workAssignmentService.ts present)

**Not present**: `CreateInvoiceWizard/`

### Search 2 — Code-page directory

Glob: `src/client/code-pages/Create*Wizard/**`

Result: **No files found.** None of the 5 wizards have a code-page sibling at that path — the wizards are all consumed via solution-side hosts (e.g. `src/solutions/LegalWorkspace/`). The task definition references `src/client/code-pages/CreateInvoiceWizard/` as a relevant-file, but the directory layout convention is `src/solutions/`, not `src/client/code-pages/`. Either way, no Invoice host exists.

### Search 3 — Repo-wide grep for `InvoiceWizard|invoiceService|CreateInvoice`

Hits are only in project documentation (spec.md, design.md, task-index.md, this project's 4 other task .poml files) and unrelated BFF finance code (`InvoiceReviewService.cs`). **Zero production source files reference the wizard.**

### Search 4 — Repo-wide grep for `sprk_invoice`

31 hits across the repo — all are entity-type strings in semantic-search components, PCF controls, and OfficeAddins (where `sprk_invoice` appears as one of N entity tokens in dropdown lists or routing). **No wizard implementation files.**

---

## Why this is task-blocking, not task-doable

The task definition specifies a 2-3 hour effort with tags `react, typescript, frontend, shared-library, fluent-ui` and Step 1 is "Read `invoiceService.ts` to determine whether the field is currently being set." That premise — that a baseline wizard exists and just needs a cascade extension — is the entire shape of the task.

To implement an actual `CreateInvoiceWizard` from scratch would require:
- New form-state types (`formTypes.ts`)
- A multi-step wizard component (`CreateInvoiceWizard.tsx`)
- A service layer (`invoiceService.ts`) modeled on `matterService.ts`
- A barrel (`index.ts`)
- A consuming host wiring (solution-side, e.g. `src/solutions/LegalWorkspace/` — outside this task's permitted path scope)
- Form fields covering `sprk_invoice` entity (invoice number, type, customer, line items, dates, etc.) — requires schema interrogation, lookup wiring, business rules, and reasonable UX
- Unit tests
- Code-page deployment integration (task 029)

That work is a multi-day greenfield component, not a "verify-and-fix any gap" extension.

The constraint "DO NOT touch files outside `src/client/shared/Spaarke.UI.Components/src/components/CreateInvoiceWizard/` and `src/client/code-pages/CreateInvoiceWizard/`" reinforces this — neither directory exists, so there is nothing inside-scope to modify.

---

## Container-id gap analysis (per FR-WIZ-03 verification step)

**Result**: **N/A** — verification not applicable. There is no existing `invoiceService.ts` whose `sprk_containerid` assignment can be examined. FR-WIZ-03 verification step ("verify whether `sprk_containerid` was already being set") returns "no, because there is no wizard at all".

If/when a CreateInvoiceWizard is later created, the implementer should follow the now-canonical pattern (post-task 020):

```ts
// In invoiceService.ts (when it exists), immediately after building the entity payload:
const userId = (window.parent as any).Xrm?.Utility?.getUserId()?.replace(/^\{|\}$/g, '');
const defaults = await EntityCreationService.resolveUserBuDefaults(this._dataService, userId);
EntityCreationService.applyUserBuDefaults(entity, defaults);
// entity now has BOTH sprk_containerid AND sprk_searchindexname populated from BU, INV-5-safe.
```

This satisfies FR-WIZ-03 at the moment of wizard creation, with no follow-up patching needed.

---

## Recommendations for project owner

Three options, in order of decreasing project disruption:

1. **Defer FR-WIZ-03 and unblock task 028 (INV-5 unit tests)**: mark FR-WIZ-03 as "deferred — wizard not yet implemented; cascade pattern documented for future implementer in `notes/handoffs/023-invoice-wizard-verification.md`". Backfill (Phase F) will eventually populate `sprk_searchindexname` on any Invoice records created via other paths (direct Dataverse API, integrations). Task 028 unit tests proceed without Invoice coverage.

2. **Scope-expand task 023 to "create the wizard"**: requires (a) reopening the task estimate (multi-day vs 2-3h), (b) loosening the path constraint to permit code-page / solution-host wiring, (c) authoring form types + schema-aware fields for `sprk_invoice` (needs Dataverse MCP schema interrogation), (d) extending the dependency chain (task 029 deploy currently assumes all 5 wizards are extensions, not net-new). **Not recommended within current task framing.**

3. **Remove FR-WIZ-03 from scope and update spec/design**: the project's primary objective (multi-container multi-index routing) is satisfied for the 4 wizards that DO exist (Matter, Project, Event, WorkAssignment) plus DocumentUploadWizard. Document the Invoice gap in `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` under "known coverage gaps" and let Phase F backfill handle Invoice records (whatever path creates them) the same way it handles other non-wizard create paths (G3 in design.md §2). **Recommended** — aligns with G3's existing acknowledgment that "Wizards are the only paths that get it right; other creates produce empty-field records. Backfill catches them."

---

## Sibling task status notes

This finding may apply to other A1 parallel-group tasks. The implementer of task 023 has NOT checked tasks 024 (WorkAssignment) or 025 (Event) since `workAssignmentService.ts` and `eventService.ts` both exist (confirmed via Glob above). Those tasks should be doable as the spec assumed. Task 022 (CreateProjectWizard) has `projectService.ts` present (G2 fix path is intact). Only task 023 (Invoice) is blocked.

---

## Compliance with task constraints

| Constraint | Status |
|---|---|
| Do not modify TASK-INDEX.md, current-task.md, commit | ✅ Honored |
| Do not touch `.claude/` paths | ✅ Honored |
| Do not touch files outside `CreateInvoiceWizard/` paths | ✅ Honored (no files modified) |
| Do not modify `EntityCreationService.ts` | ✅ Honored |
| Do not touch other wizards or DocUploadWizard | ✅ Honored |
| Produce verification handoff note | ✅ This file |

No source files modified. Shared-lib build state is unchanged from task 020's green baseline.

---

## ADR / invariant compliance for the hypothetical implementation

When the wizard is created, the implementer must apply:

- **ADR-012**: cascade logic lives in shared lib (`EntityCreationService.applyUserBuDefaults`), not duplicated in code-page consumer
- **ADR-021**: Fluent v9 + `tokens.*` only; no v8 imports, no hex / rgb / `var(--…)` literals
- **ADR-022**: invoiceService.ts must remain React-version-portable (no React-18-only APIs)
- **ADR-028**: any BFF calls (none expected for cascade) use `authenticatedFetch` from `@spaarke/auth`
- **INV-5**: explicit overrides on payload are sacred; cascade only fills unset fields (the `applyUserBuDefaults` helper already enforces this)
- **INV-6**: container reference + index travel together (the convenience helper sets both in one call)
