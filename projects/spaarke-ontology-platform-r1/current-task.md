# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-09-24 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. This is a **design/strategy project — no code has been written.**

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Phase** | Design — Phase 0 complete, MVP spec drafted, **not yet `/design-to-spec`** |
| **Branch** | `docs/ontology-platform-phase0` |
| **Status** | in-progress, no blockers |
| **Next Action** | **Spec the cross-source rule** (spend + communication) — see "Next Actions" below. It shapes `sprk_signal`, so it must precede building the first rule |

### Read in this order — do NOT read all five

| # | File | Read when |
|---|---|---|
| 1 | `notes/mvp-technical-spec.md` | **Always.** §0 (differentiation test) + §1 (end-to-end trace) = the current state of the work |
| 2 | `notes/mvp-synopsis.md` | Scope, in/out, success criteria |
| 3 | `notes/ontology-component-model.md` | **Vocabulary and definitions — authoritative.** Read §3 (terminology) before writing anything |
| 4 | `notes/phase0-codebase-inventory.md` | Verified codebase state as of 2026-09-19 |
| 5 | `notes/spaarke-ontology-strategy-synopsis-v2.md` | Strategy/market only (1,400 lines). **Component model wins on any vocabulary conflict** |

### Critical Context

The platform statement: **consolidate from third-party systems → resolve → match against the customer's rules → record what was decided.** The MVP is a spend-governance loop that exercises all seven pipeline stages. **Budget variance is the plumbing proof, NOT the pitch** — e-billing vendors already do it. The differentiated capability is the **cross-source** rule (spend + communication), which is the one thing Legal Tracker structurally cannot produce.

**Files modified this session** — all **UNCOMMITTED**:
- `notes/ontology-component-model.md` (new) · `notes/mvp-synopsis.md` (new) · `notes/mvp-technical-spec.md` (new) · `notes/spaarke-ontology-strategy-synopsis-v2.md` (edited → v2.2) · 4 researcher memory files (new)

---

## Full State

### Decisions made — do not re-litigate

| # | Decision |
|---|---|
| 1 | **Naming**: Spaarke Console (3-pane app) · Spaarke Matter Management (MDA) · Spaarke External Access (SPA) · Connection Engine (component) / Spaarke Connect (SKU) |
| 2 | **Three engines**: Connection · Insights · Action — decoupled *by* the ontology, not chained. Agents are a **surface**, not an engine |
| 3 | **CM-1**: Policy lives in the Insights Engine. **Extended 2026-09-24**: signals are Insights outputs too; `SignalEvaluationService` becomes one producer among several |
| 4 | **CM-5**: Inquiry is a record (distinct from Action and Policy) → covered by `sprk_servicerequest` with a direction discriminator. Likely absorbs Front Door's proposed `sprk_legalrequest` |
| 5 | **CM-3 closed**: policy rule body = a Dataverse filter; materialize facts as rollup + calculated columns (zero C#) |
| 6 | **Ingestion = Option A** — our own worker on `UpsertMultiple`. Rejected: dataflows (**service principals cannot deploy them**), Fabric Copy job (F capacity per customer tenant + parallel ALM track). Both are deployment-model constraints, not capability ones |
| 7 | **Console hosting resolved**: web resource + `appid` + **`navbar=off`** = full-width chrome-free, verified working. Rejected MDA-subarea / custom-page+PCF / standalone SPA — each costs the global `Xrm.WebApi` that LegalWorkspace's 48 files depend on |
| 8 | **Authority is post-MVP** — while every action needs human confirmation, the human *is* the authority |
| 9 | **Action Engine is NOT in MVP** — the MVP needs an *Action* (data), not the engine |
| 10 | **Terminology**: use Spaarke Connect's existing entities (`sprk_externalconnection` / `externalref` / `externalfieldmapping` / `externalevent`). "Binding registry" and "connector manifest" are **dropped**. "Ledger" → **Decision Record** customer-facing |
| 11 | **Connection Engine stays in this project** — harvest `spaarke-connect-integration-module-r1`'s design, don't fork it. Its README still needs a status note |
| 12 | **§0 differentiation test is binding**: name the data a capability needs that the incumbent doesn't hold, or it isn't differentiated |

### Next Actions — in order

1. **Spec the cross-source rule.** A concrete predicate over communication + spend — e.g. *"over budget AND an inbound communication in the last 30 days classified scope/fee-related with no disposition."* **Do this before building the first rule**, so `sprk_signal` is shaped by two producers rather than one. (spec §9 item 7)
2. **Read `sprk_matter`'s current schema** before adding any field in spec §5.1 — several may already exist under other names.
3. **Probe `sdkmessagefilters`** on `sprk_matter` / `sprk_billingevent` — is `UpsertMultiple` supported? Plugin registrations can disable bulk messages. (spec §9 item 2)
4. **Determine what populates `sprk_spendsnapshot` today** — if it assumes Spaarke-originated invoices, the connector work is larger than spec §8 implies. (spec §9 item 3)
5. **Run the §0 differentiation test retroactively** across strategy-synopsis §8 Wave 2 / Wave 3 modules before any gets specced.
6. **Add a status note to `projects/spaarke-connect-integration-module-r1/README.md`** — design harvested, first execution slice moved here, reference-mode/iManage remains the next slice.
7. **Tell Front Door** (`spaarke-legal-front-door-r1`, pre-spec) that CM-5 likely absorbs `sprk_legalrequest`. Its own roadmap says that decision "gates everything."

### Owner decisions still open

| ID | Decision |
|---|---|
| — | **Is the MM connector in MVP?** Largest single item. Export-only (Tier 2) is ~2 months; API mirror adds ~2 |
| — | **Which platform is first?** Determines the `RestConnector` and Tier-1-vs-Tier-2 path |
| — | **Is the first customer a department or a firm?** Departments → build; firms → bind to their existing consolidation layer (Entegrata/Intapp/Iridium) |
| CM-2 | Object-definition registry — needed only if the MCP server must describe itself |
| CM-4 | Reuse "disposition" for an Inquiry's typed outcome? |

### Verified this session (don't re-verify)

- `SignalEvaluationService` + `sprk_spendsignal` + `sprk_spendsnapshot` **exist and work** — deterministic, no AI, idempotent upsert, `ISignalRule` strategy pattern. **`sprk_spendsignal` has ZERO client-side references** — computed and invisible.
- `sprk_budgetamount` already exists on `sprk_spendsnapshot`.
- LegalWorkspace sections **self-fetch** via `Xrm.WebApi` / `authenticatedFetch` (130 occurrences, 48 files) → **a worklist widget needs no workspace changes**.
- Front Door is **pre-spec**, and its `sprk_legalrequest` decision is explicitly open.
- `sprk_policy`, `sprk_obligation`, `sprk_engagement`, `sprk_externalconnection`, `sprk_sourcebinding`, `sprk_actionledger` — **zero hits across `src/`**.
- No `AppModule` / `SiteMap` XML in the repo — model-driven apps live in the environment, not source control.

### Research completed (in `.claude/agent-memory/researcher/`)

Fabric IQ (read-only, Dataverse path strips row security → L5 not L1) · Foundry IQ + Work IQ (GA scope is extractive-only; no `sharePointEmbedded` kind; Work IQ app-only unsupported) · Entra Agent ID / Agent 365 / Dataverse audit limits · Dataverse ingestion options. **Do not re-research these.**

---

## Session Log

| Date | Work |
|---|---|
| 2026-09-19 | Phase 0 codebase inventory (pre-existing) |
| 2026-09-21 → 24 | Strategy synopsis → v2.2 · component model created · MVP synopsis · MVP technical spec · 4 research passes · Console hosting resolved · differentiation test added |
