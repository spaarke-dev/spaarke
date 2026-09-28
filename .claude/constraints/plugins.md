# Dataverse Plugin + Write-Path Constraints

> **Domain**: Dataverse Extensibility / Record Invariants
> **Source ADRs**: ADR-002 (reviewed + clarified 2026-09-25)
> **Last Updated**: 2026-09-25
> **Last Reviewed**: 2026-09-25
> **Reviewed By**: ADR-002 plugin review (main session, owner-approved)
> **Status**: Verified

---

## When to Load This File

Load when:
- Anyone proposes a Dataverse plugin, plugin-backed Custom API, low-code plugin, or Dataverse Function
- A feature adds a rule a record must satisfy on save (default, stamp, isolation, derived field, cascade)
- Writing a `Create*Wizard` / create or update path for a Spaarke table
- Designing import, integration, or Office add-in write paths
- Deciding `Xrm.WebApi` vs BFF for a **write**

---

## Core Principle

**Spaarke ships no Dataverse plugins. Invariants live on the server.**

Every rule a record must satisfy when saved has **one owner in the BFF write path**. Clients may preview it; they never solely enforce it. Writes outside the product are corrected asynchronously, and security rules fail closed.

Full rationale + registry: [`docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`](../../docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md).

---

## MUST NOT Rules

- ❌ **MUST NOT** add plugin assemblies/packages, plugin-backed Custom APIs, or low-code plugins / Dataverse Functions (ADR-002)
- ❌ **MUST NOT** enforce a write-path invariant only in client code — wizard `onFinish`, PCF, code page, add-in (WP-2)
- ❌ **MUST NOT** write a table that carries a registered invariant via `Xrm.WebApi` from product code (WP-3)
- ❌ **MUST NOT** design a security invariant that fails open — e.g. treating NULL as "not secure", or leaving a stale stamp that over-grants (WP-6)
- ❌ **MUST NOT** run per-row synchronous logic for bulk import inside Dataverse (WP-7)
- ❌ **MUST NOT** write code comments or docs that claim a plugin performs work (no Spaarke plugin exists — see FAILURE-MODES AP-12)

---

## MUST Rules

- ✅ **MUST** give each new invariant exactly one server-side owner and add it to the invariant registry (WP-1)
- ✅ **MUST** apply security and on-load-UX invariants **inline, same request**; multi-row effects in one Dataverse transaction (WP-4)
- ✅ **MUST** provide an idempotent, **fill-only** async fix-up and/or reconciliation for writes outside the product (WP-5)
- ✅ **MUST** make security invariants fail closed (WP-6)
- ✅ **MUST** escalate via root CLAUDE.md §6.5 (against the ADR-002 reopen criteria) before proposing any plugin

---

## Decision Table — "Where does this rule go?"

| Question | Answer |
|---|---|
| Is it a rule the saved record must satisfy? | Yes → **write-path invariant** → BFF write path owner (WP-1) |
| Does it affect security, or what the user sees when the record opens? | Inline in the BFF create/update request (WP-4) |
| Can the record also be written outside the product? | Add async fix-up (change signal → BFF worker) and/or reconciliation (WP-5) |
| Is it a security rule? | Must fail closed (WP-6) |
| Is it only uniqueness? | Alternate key |
| Is it a trivial derived value? | Formula column may suffice (no owner needed if no code) |
| Is it a UX suggestion the user can change before saving? | Client-only is fine — it is not an invariant |

---

## Permitted Non-Plugin Mechanisms

| Mechanism | Use |
|---|---|
| Service endpoint / webhook **step registration (no code)** | Async change signal to Service Bus / BFF for WP-5 |
| Plugin-less Custom API | Business-event contract only |
| Alternate keys | Uniqueness |
| Formula / rollup columns, entity-scope business rules | Trivial derived values/defaults |
| Native Dataverse security | Real access control |

---

## Source ADRs (Full Context)

| ADR | Focus | When to Load |
|-----|-------|--------------|
| [ADR-002](../adr/ADR-002-thin-plugins.md) | No plugins + Server-Side Write-Path rule | Always for this domain |
| [ADR-001](../adr/ADR-001-minimal-api.md) | APIs + BackgroundService workers | Placing fix-up workers |
| [ADR-004](../adr/ADR-004-job-contract.md) | Async job contracts | Fix-up / reconciliation jobs |
| [ADR-036](../adr/ADR-036-background-job-infrastructure.md) | Scheduled jobs | Reconciliation scheduling |

Companion: [`docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`](../../docs/standards/DATA-ACCESS-DECISION-CRITERIA.md) (`Xrm.WebApi` vs BFF — WP-3 row).
