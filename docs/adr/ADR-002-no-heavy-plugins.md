# ADR-002: Dataverse Plugins Are Not an Execution Runtime

| Field | Value |
|-------|-------|
| Status | **Accepted** (reviewed + clarified 2026-09-25) |
| Date | 2025-09-27 |
| Updated | 2026-09-25 |
| Authors | Spaarke Engineering |

---

## 🟡 REVIEW 2026-09-25 — Read this BEFORE the body

**Outcome: posture reaffirmed; Server-Side Write-Path rule added.** A full review against current Microsoft guidance and Dataverse MVP practice (Sept 2026), from the perspective of Spaarke *as a product*, concluded:

1. **Spaarke ships no Dataverse plugin assemblies.** New plugin code is prohibited. The previous "Restricted (exception-only)" path is replaced by the §6.5 escalation with explicit **reopen criteria** (below).
2. **The real defect was not the plugin ban — it was where invariants lived.** Because much of Spaarke's UX runs in React Code Pages, record invariants (field-mapping defaults, core-ancestor stamping, secure-project isolation, container/index default-fill) ended up enforced **only in client wizards**. Any write that did not pass through the wizard — including the Office add-ins — silently skipped them. The fix is the **Server-Side Write-Path rule** (WP-1…WP-8), not plugins.
3. Classified under root CLAUDE.md §6.5 as **Path C (comply) + clarification** — not an amendment that re-admits plugins.

Architecture + component approach, invariant registry, and the evidence base: [`docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`](../architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md).

### Why not plugins (summary of the review)

| Consideration | Finding (Sept 2026) |
|---|---|
| Runtime | Plugins remain **.NET Framework only** (4.6.2–4.8; 4.8 recommended). No .NET Core/8+ support or roadmap — a second runtime and mental model beside the .NET 10 BFF. |
| Build | Plugin packages (NuGet dependent assemblies) replaced ILMerge (unsupported); no signing needed. Better than it was, but still a separate project, test harness (FakeXrmEasy v2 is now commercially licensed), and toolchain. |
| Registration / ALM | `pac plugin push` only updates an existing assembly; **steps are still registered via PRT / tooling** and added to the solution one by one. Assembly major.minor change orphans steps; patches cannot delete steps; execution order vs other ISVs' steps at the same rank is random. |
| Bulk data | Sync plugins run **per row** on import/integration loads — rejects become thousands of per-row failures and throughput collapses on large loads. This is exactly Spaarke's highest-risk data path. |
| Secrets | Plugin managed identity is GA (2025-08-31) but needs **one federated credential per customer environment** (capped ~20 per identity) — poor fit for a multi-tenant ISV. |
| Low-code | Automated low-code plugins and Dataverse Functions are **still preview**; instant low-code plugins deprioritized. Not product-grade. |
| Governance | The ALM cost of two plugins is modest; the **creep** cost ("just add a step") is not. A zero-plugin line is enforceable; a "few plugins" line is not. |

What *only* a plugin can do — reject or mutate a write **regardless of which client wrote it** — matters only for writes outside the product. For those, Spaarke accepts **async fix-up + fail-closed security** (WP-5, WP-6) instead.

---

## Context

Dataverse plugins execute **inside the database transaction boundary** and are therefore fundamentally ill-suited for:

- Long-running or asynchronous operations
- External service calls (Microsoft Graph, SharePoint Embedded, AI services, HTTP APIs)
- Complex orchestration or multi-step workflows
- Robust observability, retries, and fault isolation
- Scalable, testable, CI/CD-driven SaaS architectures

At product scale, plugins introduce:

- Transaction contention and service-protection throttling
- Opaque failures with limited diagnostics
- High operational risk and deployment friction
- Tight coupling between persistence and execution
- A second runtime (.NET Framework) and packaging model inside a managed solution shipped to many customer tenants

## Decision

Dataverse plugins (C# or low-code) are **not used** in Spaarke. Spaarke treats Dataverse as a **data platform, not an execution engine**.

All orchestration, business logic, integrations, AI-driven processing, **and record invariants** MUST be implemented in explicit, testable, observable services:

- BFF endpoints (`Sprk.Bff.Api`) — including the **server-side write path** for records that carry invariants
- Asynchronous workers and job contracts (ADR-004) inside the BFF host (ADR-001)
- Explicit service orchestration outside the Dataverse transaction pipeline

## Server-Side Write-Path Rule (added 2026-09-25)

A **write-path invariant** is a rule that must always be true about a record at the moment it is saved (a stamp, a default, an isolation setting, a derived field) — not "usually", not "when the user used our wizard".

| ID | Rule |
|----|------|
| **WP-1** | Every write-path invariant has **exactly one owner**: a server-side component in the BFF write path. It is listed in the invariant registry in `DATAVERSE-WRITE-PATH-ARCHITECTURE.md`. |
| **WP-2** | Client code (wizards, code pages, PCF, add-ins) **MAY pre-compute or preview** an invariant for UX, but **MUST NOT be its only enforcement**. The server result is authoritative. |
| **WP-3** | Creates/updates of a table that carries a **registered invariant** MUST go through a BFF endpoint — from code pages, Office add-ins, sanctioned import, and integrations alike. `Xrm.WebApi` direct writes remain acceptable for tables with **no** registered invariant (per `DATA-ACCESS-DECISION-CRITERIA.md`). |
| **WP-4** | Invariants that affect **what the user sees on load** or **security** are applied **inline** — synchronously, in the same request that creates the record — not via a queue. Multi-row effects use one Dataverse transaction (`$batch` changeset / `ExecuteTransaction`). |
| **WP-5** | Writes **outside the product** (OOB forms, customer Power Automate, raw imports, third-party integrations) are corrected by **async fix-up** (Dataverse change signal → BFF worker) and/or **scheduled reconciliation**. Fix-ups MUST be idempotent and **fill-only** (never overwrite a value the caller set). |
| **WP-6** | **Security invariants MUST fail closed.** A record missing its security stamp or isolation must be *not visible* to inherited/broad principals until fixed — a missed stamp may cause delay, never a leak. |
| **WP-7** | **Bulk import** goes through a BFF batch path, or a raw load followed by a server-side reconciliation pass. Never per-row synchronous logic inside Dataverse. |
| **WP-8** | Customers may build their own forms, quick-creates, and flows. Spaarke's supported contract for them is the **BFF endpoints** (plus the WP-5 safety net). Customer-authored plugins that re-implement Spaarke invariants are **unsupported**. |

## Policy

### ❌ Prohibited

- Plugin assemblies or plugin packages in any Spaarke solution
- Custom APIs backed by a plugin (a plugin-backed Custom API *is* a plugin)
- Low-code plugins / Dataverse Functions (Power Fx) — preview, and treated the same as C# plugins
- Enforcing a write-path invariant **only** in client code (WP-2)
- Business logic, HTTP/Graph/AI/SPE calls, retries, polling, or orchestration inside the Dataverse pipeline

### ✅ Permitted (not plugins)

| Mechanism | Use | Notes |
|---|---|---|
| **Service endpoint / webhook step registrations** (no code) | Change signal for WP-5 async fix-up (Dataverse → Service Bus / BFF) | Async steps only; HMAC/SAS per ADR-028; receiver idempotent |
| **Plugin-less Custom APIs** | Business-event contracts only | No logic |
| **Alternate keys** | Uniqueness for any client | Preferred over any code for uniqueness |
| **Formula / rollup columns, business rules (entity scope)** | Trivial derived values / defaults | Keep simple; not a substitute for WP-1 owners |
| **Native Dataverse security** (BU, teams, ownership, sharing, column security) | Real access control | Query-interception is never a security boundary |

### Preferred Patterns

| Concern | Required Mechanism |
|---------|-------------------|
| Record invariants (stamp / default / isolate / derive) | BFF server-side write path (WP-1…WP-4) |
| Writes outside the product | Async fix-up worker + reconciliation, fail-closed (WP-5, WP-6) |
| Business logic | BFF endpoints |
| Orchestration | API + async workers |
| External services | BackgroundService (ADR-001) |
| Long-running work | Job contracts + queues (ADR-004) |
| Observability | Application Insights |
| Retries & idempotency | Worker infrastructure |
| Authorization | Endpoint-level filters (ADR-008) |

## Reopen Criteria (replaces the former "exception approval" path)

Plugins may be reconsidered **only** through root CLAUDE.md §6.5 (Path A or B, human sign-off), and only when at least one of these is demonstrated:

1. A customer **contractually** requires a Spaarke invariant to be enforced synchronously for writes *outside* the product, **and** a fail-closed + fix-up design (WP-5/WP-6) cannot satisfy it.
2. Microsoft ships **.NET 8+ plugin support** or **GA** Power Fx functions/low-code plugins with ISV-grade ALM — which would remove the runtime/packaging objections.

### Dormant build standard (applies only if reopened)

So a future exception does not repeat the retired `Spaarke.CustomApiProxy` mistakes: one plugin **package** (not ILMerge/ILRepack), SDK-style, `net48`, unsigned, in `Spaarke.sln` and CI; plugin classes are ~20-line adapters over a netstandard2.0 rule library shared with the BFF; registration-as-code (no hand clicks in PRT); assembly version frozen at `1.0.x` (version the solution); PreValidation/PreOperation only; **no outbound I/O, no secrets**; register on `CreateMultiple`/`UpdateMultiple` for bulk-safe execution; p95 < 50 ms.

## Consequences

### Positive

- Clear execution boundaries; one runtime (.NET 10) for all server logic
- Invariants are enforced once, server-side, for every product surface (code pages, add-ins, import, integrations)
- Predictable bulk-import behavior (no per-row plugin cost or reject storms)
- No plugin ALM in the managed solution; no plugin creep
- Full observability, CI/CD, and test coverage in the BFF

### Trade-offs (accepted)

- Writes outside the product are **eventually** consistent for invariants (seconds to hours, depending on WP-5 mechanism)
- Security invariants must be designed **fail-closed** (WP-6), which constrains the access model
- Tables with invariants cannot be written directly via `Xrm.WebApi` from product code (WP-3) — requires BFF create/update endpoints
- The BFF gains write-path responsibility (subject to root CLAUDE.md §10 BFF Hygiene)

## Success Metrics

| Metric | Target |
|--------|--------|
| Plugin assemblies/packages in Spaarke solutions | Zero |
| Invariants enforced only client-side | Zero (every registry row has a server owner) |
| Security invariants that fail open | Zero |
| Invariant gaps on product surfaces (incl. Office add-ins) | Zero |

## Compliance

**Architecture tests:** `tests/Spaarke.ArchTests/ADR002_PluginTests.cs` (rewritten 2026-09-25) is a repo-wide zero-plugin guard over `src/**`: **R1** no `IPlugin` / `IPluginExecutionContext` / `CodeActivity` types; **R2** no `Microsoft.CrmSdk.*` package references or version pins, no `net4x` projects, no `.snk` files; **R3** no ILMerge/ILRepack; **R4** no plugin assemblies, plugin steps, or plugin-backed Custom APIs in solution XML (empty `<SolutionPluginAssemblies />` allowed); **R5** no `src/dataverse/plugins/` directory — plus negative, positive, and scanner-sees-tree controls, and the BFF facts `BffShouldNotContainPluginOrchestration` / `EndpointsShouldNotReferencePluginInterfaces`. (`Microsoft.Xrm.Sdk` itself is allowed — the Dataverse ServiceClient uses it.)

**Code review checklist:**
- [ ] No plugin code, plugin-backed Custom API, or low-code plugin
- [ ] Any new record invariant is registered with a single server-side owner (WP-1)
- [ ] Client wizard logic for an invariant is preview-only (WP-2)
- [ ] Tables with invariants are written via BFF, not `Xrm.WebApi` (WP-3)
- [ ] Security invariants fail closed (WP-6)
- [ ] Non-product writes have a fix-up or reconciliation path (WP-5)

## AI-Directed Coding Guidance

- **Never create a Dataverse plugin**, plugin-backed Custom API, or low-code plugin. If you think you need one, you need a server-side write path instead; if that is genuinely insufficient, escalate via §6.5 against the reopen criteria.
- When a feature adds a rule a record must satisfy (default, stamp, isolation, derived field): implement it in the BFF write path, register it in the invariant registry, and have the client call the BFF — the client may preview, not enforce.
- When an existing wizard applies such a rule client-side, treat it as a WP-2 gap to migrate, not a pattern to copy.
- Non-product writes: add an idempotent, fill-only fix-up/reconciliation; make security fail closed.

---

## Related ADRs

| ADR | Relationship |
|-----|-------------|
| ADR-001 | APIs and BackgroundService workers as primary runtime |
| ADR-004 | Uniform async job contracts (fix-up / reconciliation jobs) |
| ADR-008 | Endpoint-level authorization |
| ADR-028 | Webhook/HMAC + managed identity auth for change signals |
| ADR-034 | User-record membership (reconciliation pattern precedent) |
| ADR-036 | Background-job infrastructure (scheduled reconciliation) |

---

## Summary

Dataverse plugins are not used in Spaarke. Spaarke's execution model is **API-first, async-by-default, and AI-forward** — and, since 2026-09-25, **invariants live on the server**: one owner per rule in the BFF write path, inline for security and on-load UX, async fix-up + fail-closed for everything written outside the product.

---

## Amendment History

| Date | Change | §6.5 path |
|------|--------|-----------|
| 2025-09-27 | Accepted — plugins not an execution runtime; thin validation/projection plugins exception-only | — |
| 2026-01-05 | Low-code plugins treated same as C# plugins | — |
| 2026-09-25 | Review vs current Microsoft/MVP guidance. Posture reaffirmed (**no plugins**; exception path replaced by reopen criteria). **Server-Side Write-Path rule WP-1…WP-8 added.** Retired `Spaarke.CustomApiProxy` plugin, dead `EmailProcessingMonitor` PCF, `Register-EmailWebhook.ps1`, and CrmSdk pins removed from source; arch test rewritten as a repo-wide zero-plugin guard and **armed in Tier-1 blocking CI** (verdict-neutral, mid-shadow-window). Dead plugin-size CI jobs deferred to post-cutover (`projects/ci-cd-unit-test-remediation-r1/notes/post-cutover-adr002-ci-cleanup.md`). **Dev (`spaarkedev1`) cleaned 2026-09-25**: proxy assembly, `sprk_GetFilePreviewUrl` and `sprk_proxyauditlog` were already absent; deleted the still-**active** async "Email-to-Document: Email Create" step + "Email-to-Document Webhook" service endpoint (was calling the deleted `/api/v1/emails/webhook-trigger` on every email create), `EmailProcessingMonitorSolution` + its custom control, and the single `sprk_externalserviceconfig` row (a plaintext BFF-app client secret — verified by credential hint to be stale, not a live credential). Remaining: the now-empty `sprk_externalserviceconfig` table — deletion blocked by the `sprk_SpaarkePlatform` app module + 10 Dataverse-search attribute settings. | C (comply) + clarification |

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-002 Concise](../../.claude/adr/ADR-002-thin-plugins.md)
- [Plugins Constraints](../../.claude/constraints/plugins.md) - MUST/MUST NOT rules
- [Dataverse Write-Path Architecture](../architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md) - component approach + invariant registry

**When to load this full ADR**: Historical context, reopen criteria, compliance checklist details.
