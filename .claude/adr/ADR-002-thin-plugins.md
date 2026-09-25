# ADR-002: Dataverse Plugins Are Not an Execution Runtime (Concise)

> **Status**: Accepted (reviewed + clarified 2026-09-25)
> **Domain**: Dataverse Extensibility
> **Last Updated**: 2026-09-25

---

## Decision

Spaarke ships **no Dataverse plugins** (C#, plugin-backed Custom API, or low-code/Power Fx). Dataverse is a data platform, not an execution engine.

All business logic, orchestration, integrations, AI processing, **and record invariants** live in the BFF (`Sprk.Bff.Api`) and its workers.

**2026-09-25 review** (vs current Microsoft/MVP guidance, §6.5 Path C + clarification): posture reaffirmed. The real defect was invariants enforced **only in client wizards**, skipped by every other write path (incl. Office add-ins) → **Server-Side Write-Path rule** below. Architecture + invariant registry: [`docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`](../../docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md).

---

## Server-Side Write-Path Rule (WP-1…WP-8)

A **write-path invariant** = a rule that must always be true about a record when it is saved (stamp, default, isolation, derived field).

- **WP-1** — Each invariant has **exactly one owner**: a BFF server-side write-path component, listed in the invariant registry.
- **WP-2** — Client code **MAY preview**, **MUST NOT be the only enforcement**.
- **WP-3** — Tables with a **registered invariant** are created/updated **via BFF endpoints** (code pages, Office add-ins, sanctioned import, integrations). `Xrm.WebApi` direct writes stay OK for tables with no registered invariant.
- **WP-4** — Security and "user sees it on load" invariants apply **inline, same request** (not via a queue); multi-row effects in one Dataverse transaction (`$batch` changeset / `ExecuteTransaction`).
- **WP-5** — Writes **outside the product** (OOB forms, customer flows, raw imports, 3rd-party integrations) → **async fix-up** (change signal → BFF worker) and/or **reconciliation**; idempotent, **fill-only**.
- **WP-6** — **Security invariants fail closed**: an unstamped/unisolated record is *not visible* to inherited/broad principals until fixed.
- **WP-7** — **Bulk import** via BFF batch path or raw load + server-side reconciliation pass; never per-row logic in Dataverse.
- **WP-8** — Customer-built forms/flows use Spaarke's **BFF endpoints**; customer plugins re-implementing Spaarke invariants are unsupported.

---

## Constraints

### ❌ MUST NOT

- **MUST NOT** add plugin assemblies/packages, plugin-backed Custom APIs, or low-code plugins / Dataverse Functions
- **MUST NOT** enforce a write-path invariant only in client code
- **MUST NOT** write a registered-invariant table via `Xrm.WebApi` from product code
- **MUST NOT** design a security invariant that fails open
- **MUST NOT** run business logic, HTTP/Graph/AI/SPE calls, retries or orchestration inside the Dataverse pipeline

### ✅ Permitted (not plugins)

- Service endpoint / webhook **step registrations (no code)** as async change signals for WP-5 (HMAC/SAS per ADR-028; idempotent receiver)
- Plugin-less Custom APIs (business-event contracts only)
- Alternate keys (uniqueness), simple formula/rollup columns, entity-scope business rules
- Native Dataverse security (BU/teams/ownership/sharing/column security)

---

## Reopen Criteria (only via §6.5, human sign-off)

1. A customer **contractually** requires synchronous enforcement for writes outside the product **and** WP-5/WP-6 cannot satisfy it; or
2. Microsoft ships **.NET 8+ plugins** or **GA** Power Fx functions with ISV-grade ALM.

If reopened: one plugin **package**, `net48`, unsigned, in `Spaarke.sln` + CI; ~20-line adapters over a netstandard2.0 rule library shared with the BFF; registration-as-code; frozen `1.0.x`; PreValidation/PreOperation only; `CreateMultiple`/`UpdateMultiple`; **no outbound I/O, no secrets**; p95 < 50 ms. (The retired `Spaarke.CustomApiProxy` is the counter-example.)

---

## Preferred Patterns

| Concern | Mechanism |
|---------|-----------|
| Record invariants | BFF server-side write path (WP-1…WP-4) |
| Non-product writes | Async fix-up + reconciliation, fail-closed (WP-5/6) |
| Business logic / orchestration | BFF endpoints + async workers |
| Long-running work | Job contracts + queues (ADR-004), BackgroundService (ADR-001) |
| Authorization | Endpoint-level filters (ADR-008) |

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-001](ADR-001-minimal-api.md) | APIs + BackgroundService workers as primary runtime |
| [ADR-004](ADR-004-job-contract.md) | Async job contracts (fix-up/reconciliation) |
| [ADR-008](ADR-008-endpoint-filters.md) | Endpoint-level authorization |
| [ADR-028](ADR-028-spaarke-auth-architecture.md) | HMAC webhooks / managed identity for change signals |
| [ADR-036](ADR-036-background-job-infrastructure.md) | Scheduled reconciliation jobs |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-002-no-heavy-plugins.md](../../docs/adr/ADR-002-no-heavy-plugins.md)
**Architecture**: [docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md](../../docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md)
