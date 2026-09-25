# Dataverse Write-Path Architecture — Invariants Live on the Server

> **Status**: Accepted — 2026-09-25
> **Decision**: [ADR-002](../adr/ADR-002-no-heavy-plugins.md) review of 2026-09-25 (root CLAUDE.md §6.5 Path C + clarification; owner-approved)
> **Concise rules**: [`.claude/adr/ADR-002-thin-plugins.md`](../../.claude/adr/ADR-002-thin-plugins.md) · [`.claude/constraints/plugins.md`](../../.claude/constraints/plugins.md)
> **Scope**: How Spaarke guarantees rules that records must satisfy when saved, without Dataverse plugins, across every write path (Code Pages, PCF, Office add-ins, import, integrations, and writes outside the product).

---

## 1. Decision synopsis (2026-09-25)

**Question reviewed.** Should Spaarke relax its "no Dataverse plugins" position, given cases where server-side code cannot enforce a rule efficiently or at all?

**Decision.**
1. **Keep the posture: Spaarke ships no plugins.** Plugins remain .NET Framework-only, add a second runtime and packaging model to every customer's managed solution, run per row on bulk imports (rejection storms, throughput collapse), and — once one exists — invite creep. Current Microsoft guidance and MVP practice ("thin plugins for in-transaction validation only; everything else in Azure") does not change that calculus for an ISV whose product surfaces all run through its own UI and BFF.
2. **Fix the actual defect: invariants were enforced in the client.** Because Spaarke's UX runs in React Code Pages, rules such as field-mapping defaults, core-ancestor stamping, and container/index default-fill were implemented in `Create*Wizard` `onFinish` and written via `Xrm.WebApi`. Every other write path — including the Office add-ins — skipped them. That, not the absence of plugins, produced the backfill scripts, reconciliation jobs, and runbook caveats found across ~15 projects.
3. **Adopt the Server-Side Write-Path rule (ADR-002 WP-1…WP-8).** One server owner per invariant; clients preview only; invariant-bearing tables written through the BFF; security + on-load UX applied inline; writes outside the product corrected asynchronously; security fails closed.

**Why this beats plugins for Spaarke specifically.**

| Concern | Plugin | Server-side write path |
|---|---|---|
| Code pages / wizards | Covered | Covered (once writes go through BFF) |
| Office add-ins (Word/Outlook) | Covered | Covered — they already write through BFF endpoints |
| Sanctioned bulk import | Per-row cost; per-row rejects | Batch-applied in one server pass |
| OOB forms / customer flows (not product surfaces) | Covered synchronously | Async fix-up; security fails closed |
| Runtime / ALM | .NET Framework 4.8 package + step registration in managed solution | .NET 10 BFF — existing CI/CD, tests, telemetry |
| Governance | Creep risk once the first plugin exists | Zero-plugin line is enforceable (arch test) |

The one thing only a plugin does — reject/mutate *any* client's write synchronously — is traded for eventual correction of non-product writes. That trade is accepted, on the condition that **security invariants fail closed** (a missed stamp hides a record; it never exposes one).

---

## 2. Definitions

**Write-path invariant** — a rule that must always hold for a record at the moment it is saved: a stamp (core ancestor), a default (field mapping, container, search index), an isolation setting (secure project), an owner, or a derived field. Not an invariant: a UX suggestion the user can edit before saving.

**Product write** — a write initiated by a Spaarke surface: Code Page, PCF, Office add-in, BFF job/worker, sanctioned import, Spaarke integration.

**Non-product write** — anything else: OOB model-driven forms / quick-create / grid edit, Excel import / Data Import wizard, customer Power Automate, third-party integrations, raw Web API scripts.

**Fail closed** — when an invariant has not (yet) been applied, the record grants *less* access, never more.

---

## 3. Architecture layers

```
 ┌──────────────────────────────────────────────────────────────────────────┐
 │ L0  Client surfaces (Code Pages, PCF, Office add-ins, wizards)           │
 │     MAY preview invariants (e.g. show mapped fields before save)         │
 │     MUST NOT be sole enforcement (WP-2)                                  │
 └───────────────┬──────────────────────────────────────────────────────────┘
                 │ create/update of an invariant-bearing table (WP-3)
                 ▼
 ┌──────────────────────────────────────────────────────────────────────────┐
 │ L1  BFF server-side write path                                           │
 │     RecordCreationService ─► invariant owners (one per rule, WP-1):       │
 │       • CreateTimeFieldMapping      • CoreAncestorResolver               │
 │       • RecordOwnershipResolver     • RecordContainerResolver            │
 │       • search-index default        • secure-project child isolation     │
 │     Inline, same request; multi-row = one Dataverse transaction (WP-4)   │
 └───────────────┬──────────────────────────────────────────────────────────┘
                 ▼
 ┌──────────────────────────────────────────────────────────────────────────┐
 │ L2  Dataverse (data platform — NO plugins)                               │
 │     Native security · alternate keys · formula columns · audit           │
 └───────────────┬───────────────────────────────▲──────────────────────────┘
                 │ change signal (no-code step)   │ non-product writes
                 ▼                                │ (OOB forms, flows, import)
 ┌──────────────────────────────────────────────────────────────────────────┐
 │ L3  Async fix-up (WP-5): service-endpoint step → Service Bus → BFF worker │
 │     Idempotent, fill-only; reuses the SAME L1 invariant owners           │
 ├──────────────────────────────────────────────────────────────────────────┤
 │ L4  Scheduled reconciliation (ADR-036 jobs): safety net + backfill       │
 ├──────────────────────────────────────────────────────────────────────────┤
 │ Read side: evaluators treat "invariant missing" as NO grant (WP-6)       │
 └──────────────────────────────────────────────────────────────────────────┘
```

**Key design property:** L1, L3 and L4 all call the **same invariant owner classes**. An invariant is implemented once; only the trigger differs (inline request / change event / schedule).

---

## 4. Component approach

### 4.1 Canonical components (one per concern)

| Concern | Canonical component | Location / status (2026-09-25) |
|---|---|---|
| Record create pipeline | `RecordCreationService` | Built on `work/spaarkeai-word-add-in-r1` (Matter, Project; tasks 030/031) — **not yet on master**. Becomes the canonical L1 entry point. |
| Field mapping engine | `CreateTimeFieldMapping` (pure, no I/O) | Same branch — C# port of the client engine (Copy/Default/Concat/Template). Becomes **the** engine. |
| Core-ancestor stamp | `CoreAncestorResolver` | Master — `Services/Dataverse/CoreAncestorResolver.cs`; 6 BFF writers use it (UAC-r2 Phase 3). |
| Record owner | `RecordOwnershipResolver` | Word-add-in-r1 branch (task 080) — owner = acting user's BU default team. |
| Container | `RecordContainerResolver` | Master — UAC-r2 tasks 075/076 (record-keyed, server-resolved). |
| Membership junction | BFF events + `MembershipReconciliationJob` | Master — ADR-034 (L1 + L4 reference implementation). |
| Change signal (L3) | Generic "Dataverse change → BFF worker" channel | **Does not exist.** Today the BFF only polls (`RecordSyncJob` `modifiedon` watermark, nightly sweeps). |

### 4.2 Field mapping — from three engines to one

| Engine | Where | Scope | Fate |
|---|---|---|---|
| `FieldMappingService.applyFieldMappings` (TS) | `@spaarke/ui-components`, called in every `Create*Wizard` | Full (Copy/Default/Concat/Template); **sole enforcement today** | Demote to **preview only** (WP-2), or remove once server returns mapped values |
| `ApplyMappingRule` | `Api/FieldMappings/FieldMappingEndpoints.cs` (`POST /push`) | Copy + basic coercion only | **Replace** with `CreateTimeFieldMapping` (update-time push uses the same engine) |
| `CreateTimeFieldMapping` (C#) | word-add-in-r1 branch | Full, typed | **Canonical** — promote to a shared BFF service (e.g. `Services/FieldMapping/`) used by create, push, and fix-up |

Result: one engine, three triggers (create inline · manual push · async fix-up), and field values present when the new record's page loads (the UX requirement), for every surface.

### 4.3 Moving wizards to the server write path (WP-3)

Today the wizards write through `IDataService` bound to the `Xrm.WebApi` adapter. The `bffDataServiceAdapter` already exists client-side, but the BFF has **no generic create endpoint** behind it (`/api/dataverse` exposes reads only). The migration is therefore: typed BFF create endpoints per invariant-bearing table (via `RecordCreationService`), then switch each wizard's create call to the BFF. The wizard keeps its UI, preview, and validation; the server applies and returns the authoritative record.

**Current client-only create paths that bypass server invariants** (inventory from UAC-r2, 2026-09-25): `CreateTodoWizard/todoService.ts`, `CreateEventWizard/eventService.ts`, `CreateInvoiceWizard/invoiceService.ts`, `CreateWorkAssignmentWizard/workAssignmentService.ts`, `CreateReportCardWizard/reportCardService.ts`, `CreateAnalysisWizardWidget.tsx`, `useInlineTodoCreate.ts` (Daily Briefing), `ConnectionsWriteHandler.ts` (Communication), `useSprkMemoRepository.ts` (Notepad), `EventDetailSidePane/App.tsx`, plus `matterService.ts` / `projectService.ts` (field mapping).

---

## 5. Invariant registry

Every write-path invariant has one row. A new invariant is not complete until it has a server owner here (WP-1).

| # | Invariant | Server owner (target) | State 2026-09-25 | Gap / fail mode | Owning project |
|---|---|---|---|---|---|
| I-1 | Core-ancestor stamp on child records | `CoreAncestorResolver` | 6 BFF writers stamp; RegardingResolver PCF stamps | 10 client create paths unstamped (fail closed — invisible); **native clear of regarding leaves stale stamp → over-grant (fail OPEN)**; no stamp reconciliation job; backfill script never run live | unified-access-control-r2 |
| I-2 | Secure-project child isolation | *(none)* | Not implemented | Children of secure projects **not isolated** at Dataverse level (fail OPEN for native reads); NULL `sprk_issecure` read as not-secure (fail OPEN) | **Unowned** — recommended: unified-access-control-r2 |
| I-3 | Creation-time field mapping | `CreateTimeFieldMapping` via `RecordCreationService` | Client engine only on master; server port on word-add-in-r1 branch (Matter/Project) | Records created outside wizards get no mapping; Office document save applies none | Server write-path consolidation project (proposed) |
| I-4 | SPE container id | `RecordContainerResolver` | Done for uploads (UAC-r2 task 076) | Office save falls back to acting user's BU container / default | unified-access-control-r2 (done) · word-add-in-r1 (Office path) |
| I-5 | Search index name default | `RecordCreationService` | Matter/Project on word-add-in-r1 branch | Office document save and other creates don't set it | Server write-path consolidation project |
| I-6 | Record owner = acting user's BU default team | `RecordOwnershipResolver` | word-add-in-r1 task 080 in progress | BFF-created records land in root BU on master | spaarkeai-word-add-in-r1 |
| I-7 | User-record membership junction | BFF events + `MembershipReconciliationJob` | Done (ADR-034) | ≤24h staleness for non-BFF changes (accepted; L4) | — |
| I-8 | External grant expiry | Read-time fail-closed (UAC-r2 task 107) + `ExternalAccessReconciliationJob` | Read side done; job registered disabled | Job not enabled | unified-access-control-r2 |
| I-9 | Single default (`sprk_isdefault`) | Read-side deterministic tie-break | Unenforced | Duplicate defaults possible | Backlog (read-side tie-break suffices) |

---

## 6. Writes outside the product (WP-5, WP-6)

- **Change signal.** Target: one generic channel — a no-code service-endpoint step (async) on invariant-bearing tables → Service Bus → a BFF worker that runs the L1 owners in fill-only mode. Auth per ADR-028 (HMAC/SAS; the managed-identity auth type on `serviceendpoint` exists in the schema but is undocumented — verify before relying on it). Not built yet; `scripts/Register-EmailWebhook.ps1` (retired target) and provisioning H14c (endpoint without step) are the only prior art.
- **Reconciliation.** ADR-036 scheduled jobs remain the safety net and the backfill mechanism.
- **Fail closed.** Evaluators and read paths treat an absent stamp / NULL flag as *no grant* / *restricted*. Where a missed write-side rule would *expose* data (I-1 native clear, I-2), the read side must compensate until the write side is fixed.
- **Customer-built forms/flows (WP-8).** Supported contract = Spaarke BFF endpoints. Customer plugins re-implementing Spaarke invariants are unsupported.

## 7. Bulk import and integration (WP-7)

Sanctioned imports go through a BFF batch path (invariants applied per batch, one server pass) or a raw load followed by a reconciliation run. This avoids the plugin failure modes the review specifically rejected: per-row synchronous cost on large loads and per-row rejection storms.

---

## 8. Current state of Dataverse plugins (research snapshot, Sept 2026)

| Area | State | Source |
|---|---|---|
| Runtime | .NET Framework 4.6.2–4.8 only (4.8 recommended, docs 2026-07); .NET Core/8+ unsupported | learn.microsoft.com — Build and package; Supported customizations |
| Packaging | Plugin packages (NuGet dependent assemblies); ILMerge unsupported; signing not required | Build and package |
| Registration | `pac plugin push` updates existing only; steps via PRT/tooling; steps added to solution individually | Register a plug-in |
| Limits | 2-min operation limit; ≤2 s guidance; HTTPS to DNS names only | Analyze performance; Access external web services |
| Managed identity | GA 2025-08-31; one federated credential per environment (~20 per identity cap) | Power Platform release plan 2025w1; Set up managed identity |
| Low-code | Automated low-code plugins + Dataverse Functions still preview; instant low-code deprioritized | Low-code plug-ins; Functions overview |
| ALM | major.minor change orphans steps; patches can't delete steps; same-rank order across ISVs random | Register a plug-in; Update solutions |
| Testing | No first-party framework; FakeXrmEasy v2 commercial licence; XrmMockup365 / DLaB.Xrm OSS | Testing tools |

Full notes with citations: `.claude/agent-memory/researcher/dataverse-plugins-state-alternatives-2026-09-25.md`.

## 9. Reopen criteria

See [ADR-002 § Reopen Criteria](../adr/ADR-002-no-heavy-plugins.md#reopen-criteria-replaces-the-former-exception-approval-path): a contractual requirement that fail-closed + fix-up cannot satisfy, or Microsoft shipping .NET 8+ plugins / GA Power Fx functions. Only via root CLAUDE.md §6.5.

## 10. Evidence base

Projects/docs that designed around the plugin ban and recorded the cost (2026-09-25 sweep): Field Mapping Framework (creation-time only in wizards), SpaarkeAi workspace §272 (wizards as cascade mechanism), multi-container-multi-index r1 (default-fill gaps; INV-7 "plugins + wizard"), Insights Engine D-19, ADR-034 (membership reconciliation), unified-access-control r2 (stamps, isolation), smart-todo r5 (cascade share), side-pane navigation (dedupe race), environment provisioning (`sprk_isdefault` unenforced), messaging r3 (BFF-only flag). The common root cause in every case is client-side enforcement, which WP-2/WP-3 address.
