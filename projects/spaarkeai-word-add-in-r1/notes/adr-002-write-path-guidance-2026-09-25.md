# ADR-002 Review → Guidance for spaarkeai-word-add-in-r1

> **Date**: 2026-09-25
> **From**: ADR-002 plugin review (owner-approved; root CLAUDE.md §6.5 Path C + clarification)
> **Status**: Guidance. Items marked 🔔 need an owner call.
> **Source of truth**: `docs/adr/ADR-002-no-heavy-plugins.md` + `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`. These are on branch `work/adr-002-server-side-write-path` in the main checkout, **not yet on master**.

---

## 1. What was decided

Spaarke ships **no Dataverse plugins** (reaffirmed). The review found that the real defect was **record invariants enforced only in client wizards**, so every other write path skipped them. The new **Server-Side Write-Path rule** says:
- Each invariant has **one BFF owner** (WP-1).
- Clients may preview an invariant but never be its only enforcement (WP-2).
- Tables that carry invariants are written **through the BFF** (WP-3).
- Security and on-load UX invariants are applied **inline** (WP-4).
- Writes from outside the product get async fix-up plus reconciliation (WP-5).
- Security invariants **fail closed** (WP-6).

## 2. This project is already the reference implementation. Protect that.

The add-in already conforms: **every add-in write goes through BFF `/api/office/*` endpoints**, and nothing calls Dataverse directly. More importantly, this branch built what the architecture doc names as the **canonical server write-path seed**:

| Component (this branch) | Role in the write-path architecture |
|---|---|
| `Services/Office/RecordCreationService.cs` | The canonical create pipeline (L1) |
| `Services/Office/CreateTimeFieldMapping.cs` | **The** field-mapping engine. It replaces both the client `FieldMappingService.applyFieldMappings` (as the enforcer) and `/push`'s Copy-only `ApplyMappingRule` |
| `Services/Dataverse/RecordOwnershipResolver.cs` (task 080) | Owner of invariant I-6 (owner = acting user's business-unit default team) |

**Recommendations:**
1. 🔔 **Keep these generic, not Office-specific.** A follow-on project will point every `Create*Wizard` at `RecordCreationService` and `CreateTimeFieldMapping`. If it's cheap before merge, move them out of `Services/Office/` into a neutral namespace (for example `Services/RecordCreation/` and `Services/FieldMapping/`). Also keep their APIs free of Office types. If moving them now is too much, leave a note in the PR and the follow-on project will do the move. Either way, don't add Office-only assumptions.
2. **Land on master soon.** Until this branch merges, the canonical engine exists only here, and anyone who needs server-side field mapping would build a fourth one.
3. **BFF §10:** state the placement justification for these components in the PR, citing the write-path architecture doc as the reason they belong in the BFF.

## 3. Gaps on this branch, mapped to the rule

| # | Gap | Rule | Recommendation |
|---|---|---|---|
| G1 | **Document save** (`OfficeDocumentPersistence`) applies no field mapping, no core-ancestor stamp and no `sprk_searchindexname` default | WP-1/3 | Route document save through `RecordCreationService`, or have it call the same invariant owners (`CreateTimeFieldMapping`, `CoreAncestorResolver`, search-index default). 🔔 Decide whether this is in scope for r1 or goes to the follow-on project. |
| G2 | **Create and association are not atomic**: the create (`:271`) is followed by a separate update that sets regarding/association (`:317`) | WP-4 | Set the association lookups **in the create payload**, or use one `$batch` changeset. A failure between the two calls currently leaves an unassociated, unstamped document. |
| G3 | **QuickCreate Invoice** is not covered by `RecordCreationService` (only Matter and Project are) | WP-1 | Extend it, or record the gap as a deferred item. |
| G4 | The **Create To Do launcher** (`createTodoLauncher.ts`) opens the client `CreateTodoWizard`, which writes through `Xrm.WebApi` with client-only invariants | WP-2/3 | Known gap, not this project's to fix. The in-pane `CreateTodoView` → `/api/office/todo` path is correct (stamps server-side and fails closed). Prefer the in-pane path where possible. |
| G5 | Container fallback to the **acting user's** business-unit container when there is no target record (`OfficeService.cs:~270-300`) | INV-7 / I-4 | Check against UAC-r2 task 076 (record-keyed, server-resolved container). The user's-BU fallback is the pattern task 076 removed elsewhere. |

## 4. Housekeeping

- Register this project's invariants in the architecture doc's registry: I-3 (field mapping), I-5 (search-index default) and I-6 (owner). The rows already exist and name this branch as their owner. Update their **state** column when this branch merges.
- The ADR branch removed plugin code and phantom plugin CI jobs, rewrote `ADR002_PluginTests`, and deleted the `EmailProcessingMonitor` PCF and `scripts/Register-EmailWebhook.ps1`. None of these are referenced by this project. Merge master after the ADR branch lands.
