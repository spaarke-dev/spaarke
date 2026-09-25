# Field Mapping → Server-Side Write Path — R1 — Design

> **Status**: DRAFT for owner review. Next step: `/design-to-spec` → `/project-pipeline`.
> **Date**: 2026-09-25
> **Origin**: the ADR-002 review of 2026-09-25 (owner-approved, root CLAUDE.md §6.5 Path C plus a clarification). Decision record: [`docs/adr/ADR-002-no-heavy-plugins.md`](../../docs/adr/ADR-002-no-heavy-plugins.md). Architecture: [`docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`](../../docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md).
> **Invariant-registry rows owned by this project**: **I-3** (creation-time field mapping) and **I-5** (search-index default). It also builds the shared pipeline that **I-1** (core-ancestor stamp, owned by UAC-r2) and **I-6** (record owner, owned by word-add-in-r1) plug into.

---

## 0. Thesis

Spaarke has **three field-mapping engines**, and the one that actually enforces mappings runs **in the browser**:

1. `FieldMappingService.applyFieldMappings` (TypeScript), called from eight wizard `onFinish` paths.
2. A Copy-only `ApplyMappingRule` inside the BFF `/push` endpoint.
3. A full C# port, `CreateTimeFieldMapping`, built on the Word add-in branch.

Mappings therefore apply only when a record is created through one of those wizards. Records created any other way get none: document save from the Office add-ins, quick-create, extraction-created invoices, the Daily Briefing inline To Do, native forms, imports and integrations. On master, even the add-ins get none.

This project applies ADR-002's **Server-Side Write-Path rule** to field mapping:
- **one engine**, running **server-side**, **inside the create request**, so mapped fields are present when the new record's page loads;
- called from **every** product create path;
- the client keeps a **preview** so the wizard experience is unchanged.

In doing so it builds the reusable **BFF record-creation pipeline** that the other write-path invariants (core-ancestor stamp, owner, container, search index) plug into, and it removes two of the three engines.

**This project is NOT:**
- a Dataverse plugin; no plugins, per ADR-002;
- a change to the field-mapping *configuration model* (`sprk_fieldmappingprofile` / `sprk_fieldmappingrule` and the four mapping types stay as they are);
- a change to *update-time* semantics (refreshing existing children stays the manual `/push`, now on the same engine);
- a redesign of the wizards' UI.

---

## 1. Background — the decision that frames this project

The owner reviewed ADR-002 on 2026-09-25 against current Microsoft and MVP guidance.

**Plugins were rejected for Spaarke.** A plugin was considered for field mapping specifically and rejected because:
- plugins still run on .NET Framework only;
- they cost per row on bulk imports and integrations, the highest-risk data path;
- they add ALM and package complexity to every customer's managed solution;
- they carry a creep risk;
- the Office add-ins already write through the BFF, so they don't need a plugin;
- OOB forms and customer Power Automate are not product surfaces.

**The defect the review actually found** is that invariants live in client wizards. The rule adopted:

| Rule | Meaning here |
|---|---|
| **WP-1** | One server-side owner per invariant: `CreateTimeFieldMapping` owns I-3. |
| **WP-2** | The client may preview; it is never the only enforcement. |
| **WP-3** | Tables that carry invariants are created **through the BFF**, not `Xrm.WebApi`. |
| **WP-4** | On-load UX invariants are applied **inline** in the create request (**field mapping is one**; the owner stated "users look for the field updates as soon as the page loads"). |
| **WP-5** | Writes from outside the product are corrected asynchronously, fill-only. |
| **WP-6** | Security fails closed. Not directly applicable to field mapping, but the pipeline must honour it for I-1/I-6. |

---

## 2. Problem — current state (verified 2026-09-25)

### 2.1 Three engines

| Engine | Location | Semantics | Used by |
|---|---|---|---|
| **A. Client TS** | `src/client/shared/Spaarke.UI.Components/src/services/FieldMappingService.ts` (621 lines) — `applyFieldMappings()` | Full: Copy (scalar + lookup), Default, Concat, Template; `ExecutionOrder`; failures are warnings; `sprk_expression` extension seam | **8 wizard services**: `matterService`, `projectService`, `eventService`, `todoService`, `invoiceService`, `workAssignmentService`, `reportCardService`, `CreateAnalysisWizardWidget` |
| **B. BFF push** | `Api/FieldMappings/FieldMappingEndpoints.cs` — `ApplyMappingRule` behind `POST /api/v1/field-mappings/push` | **Copy + basic coercion only** (no Default/Concat/Template) | `UpdateRelatedButton` PCF, `sprk_fieldmapping_push.js` ribbon (update-time refresh, ≤500 children) |
| **C. BFF create-time port** | `Services/Office/CreateTimeFieldMapping.cs` (363 lines, **branch `work/spaarkeai-word-add-in-r1` @ 949bfa123, not on master**), called by `RecordCreationService.CreateAsync` (646 lines) | Full, mirrors A. Server-side differences: typed Default conversion; Copy into text renders labels/Yes-No/ISO dates; null source skipped | Office add-in Quick Create (Matter, Project) |

Engines A and C already produce different results in edge cases (text rendering of option sets, typed defaults, null handling). A and B **definitely** differ (B has no Default/Concat/Template). The same profile gives three answers depending on which surface created the record. This is the §11 "five that partially overlap" failure mode in miniature.

### 2.2 Create paths that get no mappings today

| Path | Why no mapping |
|---|---|
| Office add-in **document save** (`OfficeDocumentPersistence`) | Server path; not wired to any engine (branch and master) |
| Office add-in **Quick Create** on master | Name + description only (fixed for Matter/Project on the word branch; Invoice still minimal) |
| Invoice created by **extraction** | No client hook |
| Daily Briefing **inline To Do** (`useInlineTodoCreate.ts`) | Not a wizard |
| Communication `ConnectionsWriteHandler`, Notepad memo, EventDetailSidePane To Do | Not wizards |
| OOB forms / quick-create, Excel/Data Import, customer Power Automate, integrations | Not product surfaces (WP-5 territory) |

`docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md` documents this as a deliberate boundary ("anything created outside a wizard falls back to the manual push"). That boundary existed only because of the plugin ban. ADR-002 WP-1/WP-2 retires it.

### 2.3 Why the wizards can't simply "call the server engine"

Wizards write through `IDataService` bound to the **`Xrm.WebApi` adapter**. A client BFF adapter exists (`bffDataServiceAdapter.ts`, which POSTs to `/api/dataverse/{entity}`), but **the BFF has no generic create endpoint behind it**: `Api/Dataverse/RecordEndpoints.cs` maps only `GET /record/{entity}/{id}`. Moving creation server-side therefore means **building BFF create endpoints**, not relocating a function call.

---

## 3. Options considered

| # | Option | Verdict | Why |
|---|---|---|---|
| A | **Dataverse plugin** applying mappings on Create | ❌ Rejected (ADR-002) | Per-row cost on imports; .NET Framework runtime; ALM; creep. Owner decision 2026-09-25. |
| B | **Async fix-up only** (change signal → BFF worker applies mappings after create) | ❌ Rejected as the primary mechanism | Fields arrive seconds later, so the page loads empty (fails the owner's UX requirement), and a worker can race a user edit. **Kept** as the WP-5 safety net for non-product writes (W5). |
| C | **Server "compute" endpoint**: the client sends a draft, the BFF returns the mapped payload, the client still writes via `Xrm.WebApi` | ⚠️ Rejected as the end state; possible **interim** step | One engine, but the client is still the writer, so enforcement stays client-side (WP-2 violation) and non-wizard paths remain uncovered. Could be a stepping stone if W3 must be staged. |
| D | **Status quo + fix engine drift** (make A and B agree) | ❌ Rejected | Keeps client-only enforcement and three implementations. |
| **E** | **Server-side write path**: BFF create endpoints run `RecordCreationService`, which applies `CreateTimeFieldMapping` (and the other invariant owners) inline and returns the created record. Wizards call it; the client engine becomes preview-only. `/push` uses the same engine. | ✅ **Chosen** | Satisfies WP-1…WP-4, covers every product surface including add-ins, fields present on page load, one engine, and it is the pipeline the other invariants need anyway. |

---

## 4. Solution — Option E

### 4.1 Target architecture

```
Wizard / add-in / import / integration
   │  POST typed create (draft values + parent context)
   ▼
BFF create endpoint  ──►  RecordCreationService.CreateAsync
                            ├─ authorize caller (can create this entity? can read the source parent?)
                            ├─ read profile + source record (1 profile read, 1 source read)
                            ├─ CreateTimeFieldMapping.Apply        (I-3)
                            ├─ search-index default                 (I-5)
                            ├─ CoreAncestorResolver                 (I-1, UAC-r2)
                            ├─ RecordOwnershipResolver              (I-6, word-add-in-r1)
                            ├─ RecordContainerResolver (if needed)  (I-4)
                            └─ single Dataverse create (or one $batch changeset)
   ◄── created record id + applied values + warnings
Wizard: navigates to the record → page loads WITH the mapped fields
```

### 4.2 Workstreams

| WS | Scope | Notes |
|---|---|---|
| **W1 — Canonical engine** | Promote `CreateTimeFieldMapping` + `RecordCreationService` out of `Services/Office/` into a neutral namespace (e.g. `Services/RecordCreation/`, `Services/FieldMapping/`). Remove Office-specific assumptions. Close the A↔C semantic gaps by **written decision** (typed defaults, text rendering, null handling), not by accident. Include the `sprk_expression` seam. | Starts from word-add-in-r1 code once merged. If that merge slips, coordinate the move with that project rather than forking. |
| **W2 — One engine for `/push`** | Replace `ApplyMappingRule` (Copy-only) with the canonical engine in update mode. | Update-time push gains Default/Concat/Template, a behaviour change to review with the owner (§7 Q3). |
| **W3 — BFF create endpoints + wizard migration** | Typed create endpoints for the invariant-bearing tables the 8 wizards create (`sprk_matter`, `sprk_project`, `sprk_event`, `sprk_todo`, `sprk_invoice`, `sprk_workassignment`, report card, analysis). Switch each wizard's create call to the BFF. Keep `applyFieldMappings` **only as preview**, or drop it if the server response is enough. | Migrate one wizard at a time behind the shared `IDataService` seam. Matter first (reference), then the rest. |
| **W4 — Other product create paths** | Office **document save** through the pipeline (field mapping + ancestor stamp + search index), with the association set **in the create**, not a follow-up update. Invoice Quick Create, the Daily Briefing inline To Do, and the other non-wizard product creates listed in §2.2. | Coordinate document save with word-add-in-r1 (its guidance note, gaps G1–G3). |
| **W5 — Non-product safety net** | Change signal: a no-code service-endpoint step on invariant-bearing tables → Service Bus → BFF worker that runs the **same** pipeline in fill-only mode. Plus an ADR-036 reconciliation job for backfill. | May be split into its own project if W1–W4 are large. The BFF currently learns about Dataverse changes only by polling, so this channel is new and benefits every invariant. |
| **W6 — Registry, docs, retirement** | Update invariant-registry rows I-3/I-5 to done. Rewrite `SPAARKE-FIELD-MAPPING-FRAMEWORK.md` for the server engine. Remove client-enforcement code paths. Add a guard so a new wizard can't reintroduce client-only enforcement (arch/lint rule). | Include the §11 duplicate-engine removal evidence. |

### 4.3 Authorization and identity — the main design question

Today wizard creates run **as the user** through `Xrm.WebApi`, so Dataverse enforces the user's create privileges, sets `createdby` to the user, and applies the user's business unit. A BFF create running **app-only** would:
- bypass Dataverse's per-user privilege checks;
- stamp `createdby` as the BFF app user;
- land records in the root business unit. word-add-in-r1 task 080 already hit this: every BFF-created document was owned by the app user in the root BU.

The spec must choose, per ADR-028/ADR-008:
- **(a) Impersonated create** (`CallerId` / `MSCRMCallerID` = the caller's `systemuserid`). Dataverse enforces the user's privileges and audit shows the user; `RecordEndpoints.cs` already uses an impersonated-`CallerId` read path. **Recommended default.**
- **(b) App-only create plus explicit endpoint authorization** (UAC evaluator) plus `RecordOwnershipResolver` for the owner. This is what word-add-in-r1 did.

Either way, the endpoint must verify the caller can read the **source parent** used for mapping, so mapping is not a read-escalation channel.

---

## 5. ADR tensions (per CLAUDE.md §6.5)

| ADR | Tension | Resolution |
|---|---|---|
| ADR-002 | None. This project *is* the ADR-002 WP-1…WP-4 implementation for I-3/I-5. | Comply |
| `DATA-ACCESS-DECISION-CRITERIA.md` | Previously allowed single-record creates via `Xrm.WebApi`. The 2026-09-25 WP-3 row now routes invariant-bearing tables to the BFF. | Comply (already amended) |
| ADR-010 (DI minimalism) | New registrations (pipeline, endpoints). | Engine stays `internal static` (no DI). Count registrations in the spec. |
| ADR-028 / ADR-008 | Impersonation vs app-only (§4.3). | Spec decision. Default (a). |
| ADR-012 (shared lib) | Client `FieldMappingService` shrinks to preview. | Comply. The package boundary is unchanged. |
| ADR-001 | W5 worker runs as a BFF BackgroundService, not an Azure Function. | Comply |

---

## 6. Placement Justification (BFF §10) and component justification (§11)

**Placement.** In the BFF. Creation must apply several server-owned invariants (I-1, I-3, I-4, I-5, I-6) in one request under Spaarke auth, and it is called by code pages, add-ins and workers alike. There is no other server runtime (no plugins, per ADR-002; Functions are out per ADR-001). This is CRUD code with no AI dependency, so the ADR-013 facade rule is not triggered.

| New surface | Existing (grep evidence) | Extend instead? | Cost of doing nothing |
|---|---|---|---|
| Shared `RecordCreationService` (promoted) | `Services/Office/RecordCreationService.cs` (word branch) | **Yes — extend/promote, don't rebuild** | Each surface keeps its own create path, so invariants drift per surface (today: 3 engines) |
| Canonical field-mapping engine | `CreateTimeFieldMapping.cs` (branch), `ApplyMappingRule`, `FieldMappingService.ts` | **Yes — promote C, delete B's logic, demote A** | Same profile gives different field values depending on the creating surface |
| Typed BFF create endpoints | `/api/office/quickcreate/{entityType}`, `/api/office/todo`; no generic `/api/dataverse` create | Extend the Office endpoints' pattern. Decide in the spec whether to generalise `quickcreate` or add per-entity endpoints | Wizards can't reach the server engine, so client-only enforcement persists (WP-2) |
| Change-signal channel (W5) | None; BFF polls (`RecordSyncJob`) | No existing push channel | Non-product writes never get mappings; every future invariant builds its own poller |

**Publish-size and CVE**: W1–W4 add no packages. W5 uses the existing Service Bus SDK. Measure per task against a fresh master build (root CLAUDE.md §10.4).

---

## 7. Open questions for the owner

1. **Q1 — Sequencing with word-add-in-r1.** Wait for its merge and promote its code (recommended), or co-develop on a shared branch?
2. **Q2 — Identity model** (§4.3): impersonated create (recommended) vs app-only plus explicit authorization?
3. **Q3 — `/push` behaviour change**: once on the canonical engine, update-time push also applies Default/Concat/Template, not just Copy. Accept, or restrict push to Copy?
4. **Q4 — Client preview**: keep a live preview in the wizard (needs a BFF "preview" call or retains the TS engine as preview-only), or rely on the post-create page showing the values?
5. **Q5 — W5 in or out**: include the non-product change-signal channel here, or split it into its own project (it serves all invariants, not just field mapping)?

---

## 8. Success criteria (measurable, for the spec)

- **One** field-mapping engine in the codebase. `ApplyMappingRule` logic is removed, and `FieldMappingService.ts` has no enforcement role; enforced by a guard/test.
- 100% of the §2.2 **product** create paths apply mappings server-side, verified by an integration test per path using the same profile fixture.
- For a record created from any wizard, mapped fields are present in the **first** retrieve after create (no async gap).
- A parity test: the same profile + source gives identical output from create-time and push-time.
- Non-product create (W5, if in scope): fields filled within N seconds, fill-only (a caller-set value is never overwritten).
- Invariant registry rows I-3 and I-5 marked done. `SPAARKE-FIELD-MAPPING-FRAMEWORK.md` rewritten.

---

## 9. Dependencies and coordination

| Project | Relationship |
|---|---|
| **spaarkeai-word-add-in-r1** | **Upstream.** Source of `RecordCreationService`, `CreateTimeFieldMapping`, `RecordOwnershipResolver`; document-save gaps G1–G3 (`projects/spaarkeai-word-add-in-r1/notes/adr-002-write-path-guidance-2026-09-25.md` in that worktree). |
| **unified-access-control-r2** | **Peer.** Owns `CoreAncestorResolver` (I-1) and the proposed secure-child isolation (I-2). W3 fixes UAC's 10 unstamped client create paths as a side effect. Guidance: `projects/unified-access-control-r2/notes/adr-002-write-path-guidance-2026-09-25.md` (in that worktree). |
| **set-regarding-and-field-mapping-resolver-r2** | **Predecessor.** Built the client engine and the creation-time amendment; its "no client hook, so defer" boundary is superseded. |
| **ci-cd-unit-test-remediation-r1** | Tier-1 now carries the ADR-002 zero-plugin guard. No workflow edits are expected from this project. |

## 10. Next steps

1. Owner answers Q1–Q5.
2. `/design-to-spec` for this folder.
3. `/project-pipeline`: register in `projects/INDEX.md` with the hot-path declaration below.

```xml
<hot-path-declaration>
  <bff>Y</bff>                   <!-- create endpoints, RecordCreationService, engine, worker -->
  <spaarkeai>N</spaarkeai>       <!-- CreateAnalysisWizardWidget lives in Spaarke.AI.Widgets, not src/solutions/SpaarkeAi — confirm in spec -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
