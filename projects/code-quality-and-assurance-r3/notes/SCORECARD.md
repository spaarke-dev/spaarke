# Code Quality & Assurance R3 — Living SCORECARD

> **Deliverable**: spec.md FR-03 / FR-04 (Phase-0 re-baseline). **Rubric**: `docs/standards/CODE-QUALITY-RUBRIC.md` (D1–D11, authored by task 001).
> **Convention**: append ONE row per surface at assessment/wrap-up. **No aggregate grade is published until every surface is scored** (FR-04) — the March "A (95/100)" is superseded and treated as stale/unverified.
> **Status**: seeded 2026-08-06 with the BFF row (workstream #1, Fable-verified). Remaining surfaces scored by tasks 010–015; aggregate by task 016.

---

## Rubric dimensions

D1 Architecture & boundaries · D2 Correctness & reliability · D3 Security · D4 Performance & scalability · D5 DRY / dead code · D6 Consistency & conventions · D7 Testability & test quality · D8 Dependency & supply-chain hygiene · D9 Observability · D10 ALM / build hygiene · D11 Knowledge/doc accuracy.

## Per-surface scores

| Surface | D1 | D2 | D3 | D4 | D5 | D6 | D7 | D8 | D9 | D10 | D11 | Assessed | Source |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFF (`Sprk.Bff.Api`)** | B | C+ | B– | A– | C | B– | B | A– | B | D+ | C+ | 2026-08-06 (Fable-verified) | [`workstreams/bff-api/design.md`](../workstreams/bff-api/design.md) + Verification Addendum |
| Shared client libs | — | — | — | — | — | — | — | — | — | — | — | pending (task 010) | — |
| Shared server libs | — | — | — | — | — | — | — | — | — | — | — | pending (task 011) | — |
| PCF controls | — | — | — | — | — | — | — | — | — | — | — | pending (task 012) | — |
| Dataverse model + ALM | — | — | — | — | — | — | — | — | — | — | — | pending (task 013) | — |
| Code pages + build sprawl | — | — | — | — | — | — | — | — | — | — | — | pending (task 014) | — |
| Plugins | — | — | — | — | — | — | — | — | — | — | — | pending (task 015) | — |
| Config-deployment (#1 KV federation) | — | — | — | — | — | — | — | — | — | — | — | pending (task 017) | — |
| **AGGREGATE** | — | — | — | — | — | — | — | — | — | — | — | pending (task 016 — after all surfaces) | — |

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
