# Code Quality & Assurance R3 — Living SCORECARD

> **Deliverable**: spec.md FR-03 / FR-04 (Phase-0 re-baseline). **Rubric**: [`docs/standards/CODE-QUALITY-RUBRIC.md`](../../../docs/standards/CODE-QUALITY-RUBRIC.md) (D1–D11 + A–F scale) — **PUBLISHED** by task 001 (2026-08-14).
> **Convention**: append ONE row per surface at assessment/wrap-up. The **Overall** column is the rubric §4.2 composition — the weighted-mean grade **capped by the gating dimensions D2 (Correctness) and D3 (Security)** (`min(mean, D2, D3)`); it is provisional until the surface's remediation completes (re-score at wrap-up). **No program aggregate grade is published until every surface is scored** (FR-04, task 016) — the March "A (95/100)" is superseded and treated as stale/unverified.
> **Status**: ✅ **COMPLETE (2026-08-14)** — all 8 surfaces scored (BFF + tasks 010–015, 017), each Fable-verified; **aggregate re-baseline published by task 016** (see "Task 016 — Aggregate Re-baseline" section below). Supersedes the March "A (95/100)".

---

## Rubric dimensions

D1 Architecture & boundaries · D2 Correctness & reliability · D3 Security · D4 Performance & scalability · D5 DRY / dead code · D6 Consistency & conventions · D7 Testability & test quality · D8 Dependency & supply-chain hygiene · D9 Observability · D10 ALM / build hygiene · D11 Knowledge/doc accuracy.

## Per-surface scores

| Surface | D1 | D2 | D3 | D4 | D5 | D6 | D7 | D8 | D9 | D10 | D11 | **Overall** | Assessed | Source |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFF (`Sprk.Bff.Api`)** | B | C+ | **F**¹ | A– | C | B– | B | A– | B | D+ | C+ | **F**¹ | 2026-08-06 (Fable); D3 re-scored 2026-08-14 (task 016) | [`workstreams/bff-api/design.md`](../workstreams/bff-api/design.md) + Verification Addendum |
| **Shared client libs (Spaarke.*)** | B– | A– | B+ | A– | B– | B– | C+ | B– | C | D+ | C+ | **B–** | 2026-08-14 (Fable-verified) | [`workstreams/shared-client-libs/design.md`](../workstreams/shared-client-libs/design.md) |
| **Shared server libs (Core/Dataverse/Scheduling)** | D+ | C+ | B– | B– | C+ | B– | C+ | B+ | B– | B– | D+ | **C+** | 2026-08-14 (Fable-verified) | [`workstreams/shared-server-libs/design.md`](../workstreams/shared-server-libs/design.md) |
| **PCF controls (19 live)** | C+ | C+ | B+ | B+ | C+ | C+ | B– | C+ | D+ | C– | C+ | **C+** | 2026-08-14 (Fable-verified) | [`workstreams/pcf-controls/design.md`](../workstreams/pcf-controls/design.md) |
| **Dataverse model + ALM** | B– | B– | B– | B– | B | B– | C+ | B+ | C+ | C+ | C+ | **B–** | 2026-08-14 (Fable-verified) | [`workstreams/dataverse-model-alm/design.md`](../workstreams/dataverse-model-alm/design.md) |
| **Code pages + build sprawl** | C+ | B+ | A– | C+ | D+ | C+ | D+ | D | D+ | D+ | C– | **C** | 2026-08-14 (Fable-verified) | [`workstreams/code-pages-build/design.md`](../workstreams/code-pages-build/design.md) |
| **Plugins (Spaarke.CustomApiProxy)** | D+ | C+ | D | C+ | C | C+ | B– | D | B– | D+ | D+ | **D** | 2026-08-14 (Fable-verified) | [`workstreams/plugins/design.md`](../workstreams/plugins/design.md) |
| **Config-deployment (#1 KV federation)** | B– | D+ | **F** | A– | C | C+ | C+ | A | B+ | D+ | C | **F** | 2026-08-14 (Fable-verified) | [`workstreams/config-deployment/design.md`](../workstreams/config-deployment/design.md) |
| **AGGREGATE (8 surfaces)** | — | — | **D**³ | — | — | — | — | — | — | — | — | **D**³ (was F at task 016) · maintainability mean **C+** | 2026-08-14 (task 090 final) | supersedes March "A (95/100)"; see "Task 090 — Final Wrap-up Re-score" |

> **▶ Remediation progress (2026-08-14).** **BFF task 023 has LANDED** — the anonymous Finance Dataverse-write endpoint (the program's D3=F root cause) now requires `@spaarke/auth` authorization; the 3 healthz `ex.Message` leaks are scrubbed; OBO(7)+User(2) require auth. The **gating F is closed server-side** (web-resource token flow pending live-Dataverse validation + app-reg prereqs — see `notes/task-023-notes.md`). Under the rubric, BFF D3 and config-deployment D3 both lift out of F (→ residual ~B–/C+); the **program aggregate un-gates from F toward the C+ maintainability mean.** Also landed: 020 (dead code −1,639 LOC), 021 (Bug-1 invoice cast), 022 (Bug-2 .eml), 027 (tarballs −31.8 MB), 060 (#3a app-reg refs). Formal re-score at wrap-up (task 090).

## Task 016 — Aggregate Re-baseline (2026-08-14)

**This supersedes the March "A (95/100)".** Eight surfaces scored against the standing rubric (D1–D11), each Fable-verified (NFR-05). The honest picture has two numbers, and both are true:

- **Maintainability mean ≈ C+** (equal-weight mean of the 8 surface *means*: BFF 2.70, client-libs 2.67, server-libs 2.39, PCF 2.37, dataverse 2.64, code-pages 1.98, plugins 1.84, config 2.26 → **2.36 → C+**). The codebase is **structurally solid with real, schedulable debt**: runtime bones (correctness, security-at-the-seam, `@spaarke/auth` routing, ADR-013/022/028 adherence) are largely healthy; the debt is concentrated in **unenforced hygiene** — no PR-blocking CI gates on client/PCF/plugin surfaces, God components, build sprawl, observability gaps, and doc drift that accumulated below the enforcement threshold.
- **Gating aggregate = F** (rubric §4.3 rule 2: the aggregate cannot exceed the weakest surface's gating dimension). The program carries a **live D3 = F** — a single unauthenticated Finance Dataverse-write endpoint (`FinanceRollupEndpoints.cs`), the rubric §3 named F exemplar. It surfaces on **two** rows (BFF, where it lives; config-deployment, which inherits it) but is **ONE root cause → ONE fix**.

**The single highest-leverage action for the entire program grade is landing BFF task 023** (`@spaarke/auth` Finance closure, already owner-decided 2026-08-06). It clears both F's; the program then re-gates on the next-weakest gating dim (**plugins D3 = D** — the plain-text-secret proxy plugin, remediable by decommission per task 015's recommendation) and trends toward the C+ maintainability mean as the horizontals (CI gates 040–042, dead-code, doc-drift 034, CVE 032 incl. the new `pdfjs-dist`/`System.Text.Json` HIGH CVEs) land.

**No A+ is published** (rubric §4.3 rule 3 requires every surface ≥ A– with no gating dim below A–). The aggregate re-scores at project wrap-up (task 090) as remediation lands.

**¹ BFF D3 re-scored B– → F (task 016).** The BFF row was seeded 2026-08-06, *before* the rubric existed (task 001, 2026-08-14); its D3 B– scored the anonymous Finance Dataverse-write endpoint (finding B-1) on the old informal scale. Under the standing ruler that live unauthenticated data-mutation endpoint is the §3 **F** exemplar — the same defect config-deployment independently scored F. Re-scoring caps BFF Overall at F (`min(mean 2.70, D2 C+, D3 F) = F`). Recovers to the C-band once task 023 lands (D3 → ~B–) plus tasks 020–029 (D2/D5/D10 lifts).

**² Aggregate F is one root cause, not a rotten codebase.** The two F surfaces share the single Finance endpoint. Absent that one live defect, the gating aggregate would sit at plugins D3 = D (also remediable — decommission), and the headline would track the **C+** maintainability mean. The F is a *gate*, not a verdict on eight surfaces' worth of code — exactly the honest signal the rubric's gating design is meant to produce.

_The BFF row remains a re-baseline input; re-score at wrap-up (task 090)._

## BFF row evidence (verified 2026-08-06)

- **D1 B** — governance strong (ADR-032 uniform, PublicContracts facade exists) but 4+1 live facade violations; 6 files >2.4k LOC (`SpeAdminGraphService.cs` 4,910).
- **D2 C+** — live broken invoice-totals cast (Bug-1); 3 KPI web resources silently 401-broken (MF-1); dead `.eml` half with conflicting registrations (Bug-2).
- **D3 B–** — anonymous Dataverse-write (B-1); unguarded live-Dataverse health probes echoing exception detail (B-2 + doc/{id}); 9 anonymous-by-omission endpoints; no fallback policy.
- **D4 A–** — 44.96 MB incl PDBs (net10 baseline; net8 was 46.90) compressed vs 60 MB ceiling; rate-limiting on anonymous surfaces.
- **D5 C** — 2,701 dead prod LOC + 1,149 test LOC (verified exact); 13 downcast copies; triple `.eml` builder.
- **D6 B–** — false "follows ScorecardCalculatorEndpoints exactly" comment; exact-name class collision; 3 legacy namespaces mid-migration.
- **D7 B** — 831 test LOC exercising unwired code; archived test file in tree; live invoice path untested (bug shipped).
- **D8 A–** — no HIGH CVE per design's audit; no new packages since (not independently re-verified — confirm in task 032).
- **D9 B** — structured logging + telemetry present; exception-detail echo in 3 health endpoints is the blemish.
- **D10 D+** — 2 tarballs (31.8 MB) tracked in git; `.gitignore` gap for `*.tar.gz`; CS0618 warnings in Release build.
- **D11 C+** — `.claude/patterns/webresource/subgrid-parent-rollup.md` actively mandates the AllowAnonymous anti-pattern (MF-2); the assessment docs themselves verified accurate against code.

**net10 HEAD refresh (2026-08-14, Fable):** BFF row still valid post-merge (532 commits). Publish **43.67 MB compressed incl PDBs** at HEAD (≈ the 44.96 net10 rebaseline; D4 stays A–). Oversized-file census drift (D1/D5 evidence): `Api/Ai/ChatEndpoints.cs` 4,066 (was 3,587), `ComposeService.cs` 3,573, `CommunicationService.cs` 2,676, `OfficeService.cs` 2,038; `CommunicationModule.cs` 490→557 lines. Resolved-by-master: MF-4 captive-dep + the `56ae2188` stale doc refs (D11 nudges up slightly). #3b still required. Full detail: `workstreams/bff-api/design.md` §net10 HEAD Reconciliation.

_The BFF row is a re-baseline input, not a final grade — it improves as tasks 020–029 land. Re-score at wrap-up (task 090)._

---

## Task 090 — Final Wrap-up Re-score (2026-08-14, program close)

**The gating F is LIFTED.** Task 023 closed the program's D3=F root cause server-side — the anonymous
Finance Dataverse-write endpoint now requires `@spaarke/auth` authorization (`.RequireAuthorization()`),
the healthz exception-detail leaks are scrubbed, and OBO(7)+User(2) endpoints require auth. The single
live unauthenticated write that capped the aggregate at F no longer exists in the server code.

### Final aggregate

| Measure | March (superseded) | Task 016 re-baseline | **Task 090 final** |
|---|---|---|---|
| Headline | A (95/100) — unverified | **F** (gated) | **D** (gated) · maintainability mean **C+** |
| Gating cap source | — | live anonymous Finance write (D3=F) | plugins **D3=D** (BaseProxyPlugin decommission, deferred live-env) |

**Honest reading**: the program un-gated from **F → D**. The residual gating cap is **plugins D3=D** — the
retired-but-present `BaseProxyPlugin` (ADR-002 violation), whose disposition is **decommission** (task 015)
— a live-environment/ALM action deferred to the deployment owner, not r3 code. A secondary residual is
**BFF D3**'s web-resource token flow, which needs LIVE-Dataverse validation + app-reg prereqs before it is
fully closed client-side (`notes/task-023-notes.md`); the server side is closed. Absent these two deferred
live-env items, the aggregate tracks the **C+ maintainability mean**.

### A+ target — NOT reached (honest close)

The chartered aspiration "aggregate reaches A+ (senior-panel standard)" is **NOT met** and is not claimed.
The rubric (§4.3) requires every surface ≥ A– with no gating dim below A–; the program instead delivered:
(1) an honest re-baseline replacing the stale "A 95/100"; (2) a large net-positive remediation (Finance F
closed, −1,639 dead LOC, 2 prod bugs fixed, 13→1 downcast, facade compliance, −31.8 MB tarballs, namespace
migration, DI decompose); (3) **live forcing-functions** (4 ArchTest fitness functions, C# analyzers-as-
errors repo-wide, config-validation fail-fast, naming-conformance gate) that prevent re-drift. Reaching A+
is a **multi-cycle** goal gated on the deferred live-env items (plugins decommission, web-resource live
validation) + the per-surface TS mechanical-baseline activation — now de-risked because the guardrails are
in place. This is the honest signal the rubric's gating design is meant to produce: a **gate**, not a
verdict on eight surfaces' worth of largely-C+ code.
