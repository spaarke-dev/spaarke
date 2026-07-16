# Task 015 — Confidence→Status Ladder + Auto-File: Behavior Contract & R-7 Evolution

> Companion to `011-resolver-behavior-contract.md`. Documents the FR-11 decision layer and the
> **intentional** status-semantics changes vs. the pre-015 binary Resolved/PendingReview engine.

## What 015 delivers

- **`AssociationStatusMapper`** (`Engine/AssociationStatusMapper.cs`) — the FR-11 confidence→status ladder:
  - deterministic (rungs 0–3) reinforced ≥ threshold + kill-switch ON → **Resolved** + auto-file
  - reinforced ∈ [0.50, threshold), OR any AI rung (4–5) involved, OR kill-switch OFF → **Suggested**
  - reinforced < 0.50 / no writable match → **Pending Review**
  - 2+ distinct targets on the SAME field each ≥ threshold → **Ambiguous** (no regarding written)
- **`AutoFileGate`** (`Engine/AutoFileGate.cs`) + **`AutoFileOptions`** (`Configuration/AutoFileOptions.cs`) —
  ADR-018 per-tenant kill-switch via `IOptionsMonitor` (runtime flip, no redeploy). Config:
  `Communication:AutoFile:{ Enabled(=true), Threshold(=0.85), Tenants{} }`.
- **Provenance JSON** → `sprk_associationprovenance` (rungs fired, per-field reinforced candidates +
  contributors, metadata-only signals, decision + reason). 10000-char cap w/ compact fallback.
- **Engine rework** (`IncomingAssociationResolver`): runs ALL deterministic rungs (no first-match
  short-circuit) → independent signals reinforce + the structural-detector pass always records
  category/obligations; AI rungs (4–5, W3) run only when the deterministic pass didn't auto-file.

## Owner priorities encoded (2026-07-15)

- **(a) Configurable per-tenant threshold** — `AutoFileOptions` (global + `Tenants` override), IOptionsMonitor.
- **(b) Signal reinforcement** — bounded **noisy-OR** `1 − Π(1 − cᵢ)` across DISTINCT rung kinds agreeing
  on the same (field, target). Two sub-threshold signals (0.70 + 0.65) reinforce to 0.895 ⇒ auto-file.
  Same-rung duplicates collapse to max first (a rung can't inflate itself).
- **(c) Conflict → Ambiguous** — never auto-file a wrong guess.
- **(d) Bounded downside** — auto-file ON by default (ADR Tension Path A); kill-switch is the escape hatch.
- **(e) Provenance-driven tuning** — full JSON trail for data-driven threshold tuning.
- **AI never auto-files** — the auto-file gate uses **deterministic-only** reinforced confidence. An AI rung
  can only ADD to the full confidence (Pending→Suggested); it can never push a target to Resolved, and never
  blocks a deterministic winner already over the bar. W3 (030/031) registers rungs 4/5 with **zero** ladder change.

## R-7 evolution (intentional status-semantics changes vs. pre-015 baseline)

The pre-015 engine marked ANY writable match as `Resolved`. FR-11 replaces that with the confidence ladder,
so two characterization tests' **status** assertions legitimately changed (the Dataverse WRITE contract —
which regarding field gets which target — is preserved):

| Test | Pre-015 | Post-015 (FR-11) | Why |
|---|---|---|---|
| `ResolveAsync_SenderMatch_LinksToContact` → `_AsSuggested` | Resolved (100000000) | **Suggested (100000003)** | A lone participant-contact match is 0.70 (< 0.85). Correctly surfaced for confirmation, not auto-filed. The `sprk_regardingperson` write is unchanged. |
| `ResolveAsync_PriorityCascade_ThreadWinsOverSender` → `_ThreadAndSenderBothMatch_...` | asserted sender query `Times.Never` (short-circuit) | assertion removed | Signal reinforcement requires ALL deterministic rungs to run. The thread's matter (1.0) still fills `sprk_regardingmatter` (Resolved); the sender-contact now contributes the complementary `sprk_regardingperson` instead of being suppressed. The `Times.Never` assertion tested the removed short-circuit (a B7 interaction-shape assertion). |

Tests preserved verbatim: `ThreadMatch_CopiesParentAssociations` (thread 1.0 → Resolved), `SubjectPattern_...`
(subject-token 0.90 ≥ 0.85 → Resolved), `NoMatch_SetsPendingReview`, `SkipsCommonProviders`,
`SenderDomainMatch_WritesOrganizationAndAccountToSeparateLookups` (asserts fields only, status-agnostic),
and the 014 engine-guard `Engine_MetadataOnlyDetection_StaysPendingReview`.

## Gate results

- 310 Communication tests green (275 → +35: mapper ladder/reinforcement/AI-never/conflict/provenance + gate + 2 engine tests).
- Build clean; publish **45.28 MB** compressed incl PDBs (+0.01 vs 014's 45.27; ceiling 60). CVE: no new HIGH (Kiota 1.21.2 pre-existing; 0 packages added).
- code-review: 0 Critical (1 pre-existing `_recordTypeRefCache` thread-safety follow-up, out of 015 scope; 1 intended-load Suggestion). adr-check: 0 violations; ADR-018/024/010/032(N-A)/045 compliant; ADR Tension Path A documented + approved.

## Follow-ups carried

- **Pre-existing (not 015):** `IncomingAssociationResolver._recordTypeRefCache` is a non-thread-safe `Dictionary`
  on a singleton — concurrent inbound messages could race. Candidate fix in 016/017 or a defer-issue.
- **Outbound wiring (017):** the mapper is direction-symmetric + tested both directions, but the engine is still
  invoked only inbound (per 011 note). 017 wires the outbound envelope through the same path.
