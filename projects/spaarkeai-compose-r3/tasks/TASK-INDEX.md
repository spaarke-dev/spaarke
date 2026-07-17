# Spaarke Compose R3 — Task Index

> **Created**: 2026-07-16 · **Source**: [plan.md](../plan.md) · **Spec**: [spec.md](../spec.md)
> **Legend**: Status 🔲 not-started · 🔄 in-progress · ✅ complete · ⛔ blocked
> **Gate**: 🟢 startable now · 🟡 splittable · 🔴 blocked on dependency
> **All 6 pre-spec spikes (S1/S1b/S2/S3/S4/S5) passed** — no Phase-0 spike phase; the only pre-build residual is the NFR-09 real-template hardening gate (task 003), which **gates the E1 delta-save cutover (task 022)**.

## Critical Path (read first)

**E2 (paraId substrate) → E1 (delta save) → Import.** paraId is the splice key, so Phase 1 blocks Phase 2; imported marks anchor by paraId and must survive the retained-original save, so Phase 2 blocks Phase 5. E3 + toolset parallelize alongside the fidelity core.

```
001 ─┬─ 003 (hardening gate) ───────────────────────┐
     │                                              ▼
002 ─┼──────────────────────────────► 022 (E1 cutover, keystone)
010 ─┴─ 011 ─ 012 ─ 020 ─ 021 ────────► 022 ─ 023 ─ 024 ─ 025
                     │                    │
                     ├─ 030 ─ 031/032 (E3)│
                     └─ 050 ─ 051 ─ 052 (import, also deps 022)
040/042/043/044 (toolset, independent) · 041 deps 011
```

**Coordination**: Confirm `spaarkeai-compose-r2` merged/frozen before the E1 cutover (022). Consume `spaarke-ai-architecture-redesign-r2` `PublicContracts` seams — no fork of `Services/Ai/`. `/conflict-check` before each BFF PR. (See [`../../INDEX.md`](../../INDEX.md).)

## Task Roster

| ID | Title | Phase | Gate | Deps | Status | Rigor | Model | Effort | Parallel-safe |
|----|-------|-------|------|------|--------|-------|-------|--------|---------------|
| 001 | Add `Docxodus` 7.1.0 (SkiaSharp excluded) + bump OpenXml 3.4.1→3.5.1; publish-size + CVE baseline | 0 Foundations | 🟢 | none | 🔲 | FULL | opus | high | false (csproj foundation) |
| 002 | `SpeFileStore` version-content fetch (FR-06) — fetch a driveItem version's content by `versionId` | 0 Foundations | 🟢 | none | 🔲 | FULL | sonnet | high | true |
| 003 | NFR-09 real-template hardening gate — re-run S1/S1b harness on 2–3 real firm templates | 0 Foundations | 🟢 | 001 | 🔲 | FULL | opus | xhigh | true |
| 010 | FR-08 server pre-parse + `w14:paraId` minting on Load (OOXML-valid, collision-checked) | 1 E2 | 🟢 | 001 | 🔲 | FULL | opus | xhigh | false (LoadAsync) |
| 011 | FR-09/FR-10 explicit load-time paraId carry (hidden node attr) + split-minting via `@tiptap/extension-unique-id` | 1 E2 | 🔴 | 010 | 🔲 | FULL | sonnet | high | true (client) |
| 012 | FR-11/FR-12 paraId-primary anchoring + fuzzy fallback (`AnnotationReanchorService`) + paraId as splice key | 1 E2 | 🔴 | 010,011 | 🔲 | FULL | opus | high | false (reanchor svc) |
| 020 | FR-02 edited-paragraph rebuild + paraId-keyed splice orchestration (server) | 2 E1 | 🔴 | 010,012 | 🔲 | FULL | opus | xhigh | false (Services/Compose) |
| 021 | FR-03/FR-05 Docxodus `WmlComparer` redline synthesis adapter (minimal ins/del + format-change) | 2 E1 | 🔴 | 001,020 | 🔲 | FULL | opus | xhigh | false (Services/Compose) |
| 022 | FR-01 baseline inversion in `SaveAsync` + drop `docx.js` export (**E1 cutover — keystone**) | 2 E1 | 🔴 | 002,003,020,021 | 🔲 | FULL | opus | xhigh | false (SaveAsync + docxBridge) |
| 023 | FR-04 AI redlines/comments reuse — apply via existing `DocxAnnotationWriter` onto retained-original baseline | 2 E1 | 🔴 | 022 | 🔲 | FULL | sonnet | high | false (Services/Compose) |
| 024 | FR-07/NFR-06 through-the-wire fidelity seam slice test (untouched OOXML preserved on dirty save) | 2 E1 | 🔴 | 022,023 | 🔲 | FULL | sonnet | xhigh | true (tests) |
| 025 | Deploy + smoke-verify E1 fidelity core (BFF + client) on spaarkedev1 | 2 E1 | 🔴 | 024 | 🔲 | STANDARD | sonnet | high | false (deploy) |
| 030 | FR-13/FR-16 server-derived `confidence_band` (additive `ComposeDraftPayload`) + paraId/offsets on anchor | 3 E3 | 🔴 | 012 | 🔲 | FULL | sonnet | high | false (contract mirror) |
| 031 | FR-14 rationale-first, anti-rubber-stamp accept/reject surface (no auto-accept low-band) | 3 E3 | 🔴 | 030 | 🔲 | FULL | sonnet | high | true (ComposeEditor UI) |
| 032 | FR-15 formatted AI insertions — enrich `new_text` to carry marks + `buildInsertionHtml` | 3 E3 | 🔴 | 030 | 🔲 | FULL | sonnet | high | true (insertion html) |
| 040 | FR-17 find/replace (case-sensitivity + replace-all; tracked-changes-mark-safe) | 4 Toolset | 🟢 | none | 🔲 | FULL | sonnet | high | true (client) |
| 041 | FR-18 basic tables (`@tiptap/extension-table`); table-cell paragraphs carry paraIds | 4 Toolset | 🔴 | 011 | 🔲 | FULL | sonnet | high | true (client) |
| 042 | FR-19/FR-20/FR-21 sticky toolbar + one-line bubble menu + dismissible simplification warning | 4 Toolset | 🟢 | none | 🔲 | FULL | sonnet | high | true (client) |
| 043 | FR-22 styles pane — apply existing document styles only (no create/rename/manage) | 4 Toolset | 🟢 | none | 🔲 | FULL | sonnet | high | true (client) |
| 044 | FR-23 richer comment-thread UI (author/timestamp/replies; view/create/reply/resolve) | 4 Toolset | 🟢 | none | 🔲 | FULL | sonnet | high | true (client) |
| 050 | FR-24 import existing revisions — project `DocxAnnotationReader` `RecoveredRevision` on Load + in-editor render | 5 Import | 🔴 | 010,012,022 | 🔲 | FULL | opus | high | false (LoadAsync) |
| 051 | FR-25 import existing comments — `RecoveredComment` threads via FR-23 | 5 Import | 🔴 | 044,050 | 🔲 | FULL | sonnet | high | false (LoadAsync) |
| 052 | FR-26 imported anchors survive save (paraId + retained-original) + seam slice test | 5 Import | 🔴 | 022,050,051 | 🔲 | FULL | sonnet | xhigh | true (tests) |
| 080 | NFR-01/02/05 publish-size ≤60 MB + CVE scan + ADR-013 NetArchTest facade verification | 6 Wrap | 🔴 | 021,022 | 🔲 | STANDARD | sonnet | high | true |
| 081 | Deploy full R3 (BFF + SpaarkeAi/shared-lib) to spaarkedev1 | 6 Wrap | 🔴 | 025,032,040,041,042,043,044,052 | 🔲 | STANDARD | sonnet | high | false (deploy) |
| 082 | Flagship gate G-R3 — browser-verified fidelity round-trip + toolset demo on spaarkedev1 | 6 Wrap | 🔴 | 081 | 🔲 | FULL | opus | high | false (UAT) |
| 090 | Project wrap-up (code-review, adr-check, repo-cleanup, /test-diet, lessons-learned) | 6 Wrap | 🔴 | all | 🔲 | FULL | opus | high | false |

**Total**: 27 tasks.

## Parallel Execution Groups

| Wave | Tasks | Prerequisite | Goal-eligible | Notes |
|------|-------|--------------|---------------|-------|
| W0 | 001, 002 | none | no (001 = packaging foundation) | Different files (csproj vs SpeFileStore) — parallel |
| W0.5 | 003 | 001 | no (hardening gate) | Gates the E1 cutover (022); may block on owner supplying real firm templates |
| W1 (E2) | 010 → 011 → 012 | 001 | no | Serial: server pre-parse → client carry → anchoring (shared LoadAsync/reanchor seams) |
| W2 (E1) | 020 → 021 → 022 → 023 → 024 → 025 | W1, 002, 003 | no (irreversible save cutover) | Serial — all touch `ComposeService`/`docxBridge`; 022 is the keystone cutover |
| W3 (E3) | 030 → {031, 032} | 012 | partial | 031/032 parallel after 030 (distinct UI files) |
| W4 (Toolset) | {040, 042, 043, 044}; 041 after 011 | none / 011 | **yes** | Independent client components — strong parallel candidate; goal-eligible wave |
| W5 (Import) | 050 → 051 → 052 | 022 (+ 044 for 051) | no | Serial (LoadAsync); depends on E1/E2 |
| W6 (Wrap) | 080 → 081 → 082 → 090 | all impl | no (deploy/UAT/irreversible) | Verification → deploy → flagship → wrap-up |

**Concurrency note**: W4 (toolset) can run alongside W1–W3. Max 6 agents/wave. BFF tasks touching `ComposeService.cs` / `ComposeEndpoints.cs` serialize (parallel-safe=false). Client tasks touching distinct components parallelize. Build-verify between waves (dotnet build BFF if `.cs` changed; `npm run build` shared lib if `.ts`/`.tsx` changed).

## Rigor / Model Assignment Rationale

- **opus @ xhigh**: the OOXML fidelity engine + irreversible cutover (010 paraId minting, 020 splice, 021 Docxodus adapter, 022 cutover) + 003 hardening gate — high blast radius, brownfield root-cause reasoning.
- **opus @ high**: keystone-adjacent + flagship/wrap (012 anchoring, 050 import projection, 082 flagship, 090 wrap, 001 packaging).
- **sonnet @ high**: well-specified client/UI + additive-contract + deploy tasks (toolset, E3 UI, deploys).
- **sonnet @ xhigh**: seam slice tests (024, 052) — fully-specified but demand careful through-the-wire assertion authoring.
- **TEST-MODIFYING override**: 024, 052 run code-review + adr-check at Step 9.5 unconditionally (modify `tests/**`).

---

*Maintained by task-execute (status flips 🔲→🔄→✅). Every BFF task runs `.claude/constraints/bff-extensions.md` + reports publish-size delta vs ~49.63 MB baseline.*
