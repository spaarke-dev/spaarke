# Code Quality & Assurance R3 — Living SCORECARD

> **Deliverable**: spec.md FR-03 / FR-04 (Phase-0 re-baseline). **Rubric**: [`docs/standards/CODE-QUALITY-RUBRIC.md`](../../../docs/standards/CODE-QUALITY-RUBRIC.md) (D1–D11 + A–F scale) — **PUBLISHED** by task 001 (2026-08-14).
> **Convention**: append ONE row per surface at assessment/wrap-up. The **Overall** column is the rubric §4.2 composition — the weighted-mean grade **capped by the gating dimensions D2 (Correctness) and D3 (Security)** (`min(mean, D2, D3)`); it is provisional until the surface's remediation completes (re-score at wrap-up). **No program aggregate grade is published until every surface is scored** (FR-04, task 016) — the March "A (95/100)" is superseded and treated as stale/unverified.
> **Status**: seeded 2026-08-06 with the BFF row (workstream #1, Fable-verified). Remaining surfaces scored by tasks 010–015; aggregate by task 016.

---

## Rubric dimensions

D1 Architecture & boundaries · D2 Correctness & reliability · D3 Security · D4 Performance & scalability · D5 DRY / dead code · D6 Consistency & conventions · D7 Testability & test quality · D8 Dependency & supply-chain hygiene · D9 Observability · D10 ALM / build hygiene · D11 Knowledge/doc accuracy.

## Per-surface scores

| Surface | D1 | D2 | D3 | D4 | D5 | D6 | D7 | D8 | D9 | D10 | D11 | **Overall** | Assessed | Source |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFF (`Sprk.Bff.Api`)** | B | C+ | B– | A– | C | B– | B | A– | B | D+ | C+ | **C+** ⚠︎ prov. | 2026-08-06 (Fable-verified) | [`workstreams/bff-api/design.md`](../workstreams/bff-api/design.md) + Verification Addendum |
| **Shared client libs (Spaarke.*)** | B– | A– | B+ | A– | B– | B– | C+ | B– | C | D+ | C+ | **B–** | 2026-08-14 (Fable-verified) | [`workstreams/shared-client-libs/design.md`](../workstreams/shared-client-libs/design.md) |
| **Shared server libs (Core/Dataverse/Scheduling)** | D+ | C+ | B– | B– | C+ | B– | C+ | B+ | B– | B– | D+ | **C+** | 2026-08-14 (Fable-verified) | [`workstreams/shared-server-libs/design.md`](../workstreams/shared-server-libs/design.md) |
| PCF controls | — | — | — | — | — | — | — | — | — | — | — | — | pending (task 012) | — |
| **Dataverse model + ALM** | B– | B– | B– | B– | B | B– | C+ | B+ | C+ | C+ | C+ | **B–** | 2026-08-14 (Fable-verified) | [`workstreams/dataverse-model-alm/design.md`](../workstreams/dataverse-model-alm/design.md) |
| **Code pages + build sprawl** | C+ | B+ | A– | C+ | D+ | C+ | D+ | D | D+ | D+ | C– | **C** | 2026-08-14 (Fable-verified) | [`workstreams/code-pages-build/design.md`](../workstreams/code-pages-build/design.md) |
| Plugins | — | — | — | — | — | — | — | — | — | — | — | — | pending (task 015) | — |
| **Config-deployment (#1 KV federation)** | B– | D+ | **F** | A– | C | C+ | C+ | A | B+ | D+ | C | **F** | 2026-08-14 (Fable-verified) | [`workstreams/config-deployment/design.md`](../workstreams/config-deployment/design.md) |
| **AGGREGATE** | — | — | — | — | — | — | — | — | — | — | — | — | pending (task 016 — after all surfaces) | — |

> **BFF Overall = C+ (provisional).** Per rubric §4.2: the equal-weighted mean of the 11 dimension points is ≈ 2.70 (**B–**), but the gating cap `min(B–, D2=C+, D3=B–)` = **C+** — the correctness dimension (D2, the broken invoice-totals path) gates the surface below its mean, exactly as the rubric intends. This is a **re-baseline input, not a final grade**; it improves as tasks 020–029 land (Bug-1 fix lifts D2, dead-code deletion lifts D5, tarball removal lifts D10). Re-score at wrap-up (task 090).

> **⚠️ Cross-surface reconciliation for task 016 (aggregate re-baseline).** The config-deployment row scores the live anonymous Finance Dataverse-write endpoint as **D3 = F** (rubric §3's named F exemplar). The **BFF row scores the *same* defect as D3 B–** — because it was assessed 2026-08-06, *before* the rubric was published (task 001, 2026-08-14). Under the standing ruler this defect is an **F wherever scored**. Task 016 MUST reconcile: re-score BFF D3 → F (dropping BFF Overall from C+ to **F** until BFF task **023** lands the `@spaarke/auth` closure), OR document the exception. Fastest grade-recovery for BOTH surfaces = land task 023.

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
