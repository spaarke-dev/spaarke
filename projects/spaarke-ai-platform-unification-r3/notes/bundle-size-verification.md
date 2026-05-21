# Bundle-size Verification — Task 061 (NFR-12)

> **Date**: 2026-05-20
> **Task**: `061-bundle-size-verification.poml` (Phase F — Integration & Verification)
> **Verdict**: **CONDITIONAL PASS** — NFR-12 budget exceeded; **Option 1 accepted by user** (accept overrun; empirical validation deferred to Phase G task 074).
> **Author**: task-execute / STANDARD rigor

---

## 1. Executive Summary

This memo is the formal task 061 record. It captures a fresh production-build measurement of the SpaarkeAi bundle, confirms source-level lazy-loading is implemented correctly, documents the `vite-plugin-singlefile` inlining caveat that nullifies the lazy-import bundle benefit, and records the user-accepted **Option 1** decision (ship R3 with the overrun; empirically validate via NFR-01 / NFR-03 timing in Phase G task 074).

The exhaustive root-cause analysis, options evaluation (Options 1–5), and cross-project recommendation already exist in two upstream documents:

- **Cross-project assessment** — [`docs/assessments/code-page-bundle-size-vs-singlefile-2026-05-20.md`](../../../docs/assessments/code-page-bundle-size-vs-singlefile-2026-05-20.md)
- **R3-local investigation** — [`projects/spaarke-ai-platform-unification-r3/notes/perf/bundle-size-investigation.md`](perf/bundle-size-investigation.md)

This memo references those documents rather than restating them.

---

## 2. Current Bundle Measurement (R3, post-Wave 3+)

Production build run on the current `work/spaarke-ai-platform-unification-r3` worktree branch:

```
cd src/solutions/SpaarkeAi
npm run build
```

Vite output (excerpt):

```
✓ 3157 modules transformed.
rendering chunks...
[plugin vite:singlefile]
[plugin vite:singlefile] Inlining: index-CAg33HKN.js
computing gzip size...
dist/index.html  2,959.42 kB │ gzip: 797.72 kB
✓ built in 11.85s
```

Post-build the file is renamed to `dist/spaarkeai.html` by the npm script; raw filesize confirmed as **2,963,475 bytes** on disk.

### Measurements summary

| Metric | Value |
|---|---|
| R3 production bundle (raw) | 2,959.42 KB (2,963,475 bytes on disk) |
| R3 production bundle (**gzip**) | **797.72 KB** |
| R2 baseline (gzip, pre-R3 master estimate from task 024 perf doc) | ~258 KB |
| NFR-12 budget allowance | +250 KB (gzip delta) |
| NFR-12 target | ~508 KB gzip |
| **Actual delta vs R2 baseline** | **+540 KB gzip** |
| **Overrun vs NFR-12 target** | **+290 KB gzip** |
| Build success | ✅ Zero errors, zero warnings; 3157 modules transformed in 11.85s |

The R3 number is stable across rebuilds; it matches the previously recorded post-Wave 3 figure of 798 KB gzip in the R3-local investigation memo. No new bloat surfaced between the investigation and this verification run.

---

## 3. Source-Level Lazy-Load Verification

NFR-12 (per spec) requires "file-extraction libs lazy-loaded on first `+` click". This is verified at the source level.

**File**: [`src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useChatFileAttachment.ts`](../../../src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useChatFileAttachment.ts)

The hook uses dynamic `await import()` for both heavy libraries:

| Library | Location in source | Pattern | Verdict |
|---|---|---|---|
| `pdfjs-dist` | Line 311 (`ensurePdfJs`) | `pdfJsRef.current = import('pdfjs-dist').then(...)` | ✅ Correct dynamic import |
| `mammoth` | Line 327 (`ensureMammoth`) | `mammothRef.current = import('mammoth').then(...)` | ✅ Correct dynamic import |

Both libraries:

- Are imported inside `useCallback` bodies that run only when `addFiles` is invoked (i.e., on first `+` click).
- Are memoized via `useRef` to ensure each lib loads at most once per hook lifetime.
- Are **NOT** referenced via static top-level `import` statements anywhere in the file.

Grep confirmation:

- Zero top-level `import .* from 'pdfjs-dist'` in any source file.
- Zero top-level `import .* from 'mammoth'` in any source file.

**Conclusion**: source-level lazy-loading is implemented to spec. **NFR-12's source-code intent is satisfied.**

---

## 4. Singlefile Inlining Caveat — Why the Source-Level Lazy Load Doesn't Reach the Deployable

The bundle-size overrun is **not** a code defect. It's a structural interaction between ADR-026 (singlefile Code Page deployment) and the standard Vite lazy-import pattern.

**Mechanism** (excerpt from the cross-project assessment §1.1):

```
Standard Vite + await import()  →  Rollup emits SEPARATE async JS chunks
                                    Initial JS bundle: SMALL ✅

Vite + viteSingleFile() plugin  →  Plugin INLINES all chunks (including
                                    async chunks) into ONE HTML file
                                    Initial deployable: BLOATED ❌
```

At runtime the `await import('pdfjs-dist')` call still returns a Promise, but it resolves instantly because the bytes are already in the inlined HTML. The dynamic-import semantic is preserved; only the bundle-size benefit is nullified.

**Evidence** (from `dist/spaarkeai.html` string-probe — see investigation memo §"Root cause"):

```
$ grep -oE "(pdfjsLib|GlobalWorkerOptions|getDocument|workerPort)" spaarkeai.html | sort -u
GlobalWorkerOptions
getDocument
pdfjsLib
workerPort
```

These are PDF.js core APIs. Their presence in the inlined HTML confirms `pdfjs-dist` is fully bundled despite the lazy import.

`pdfjs-dist` ≈ 250 KB gzip + `mammoth` ≈ 50 KB gzip = ~290 KB unavoidable overrun under singlefile deployment.

For the full options evaluation (Options 1–5) and cross-project recommendation (Option 2: separate Dataverse web resources), see [`docs/assessments/code-page-bundle-size-vs-singlefile-2026-05-20.md`](../../../docs/assessments/code-page-bundle-size-vs-singlefile-2026-05-20.md).

---

## 5. Decision: Option 1 — Accept Overrun for R3

Per the user's direction on 2026-05-20, R3 ships with **Option 1**:

| Aspect | Decision |
|---|---|
| Effort to fix in R3 | 0 (no code change) |
| Risk in R3 | Low — app functions correctly; only initial page-load latency affected |
| Spec compliance | NFR-12 overrun explicitly accepted; documented here + in plan.md Risk Register |
| R3 mitigation | Phase G task 074 will measure NFR-01 (pane render <500 ms) + NFR-03 (overlay <200 ms; populate <300 ms p95) against the real bundle. If both pass, the overrun is empirically acceptable. |
| Long-term path | **Option 2** (separate Dataverse web resources for PDF.js + mammoth + future heavy libs) per the cross-project assessment. Recommended ownership: scope as a new project, or include in the queued BFF remediation project. Not in R3 scope. |

**Why Option 1 is acceptable for R3**:

1. The application is functionally complete and correct — the lazy-load implementation is by-the-book.
2. The overrun's user impact is *initial page load latency*, not feature correctness. R3's NFR-01 / NFR-03 timings are the ground-truth user-facing metrics, and they will be empirically measured in Phase G.
3. Modern browser caching means after the first cold load, the bundle is served from cache. SpaarkeAi users typically hit this surface repeatedly within a session.
4. The cross-project fix (Option 2) is a discrete engineering effort that needs scoping, security review (CSP, auth, CORS), and a dedicated owner — not appropriate to retrofit into R3's Phase F.

---

## 6. Acceptance Criteria Reconciliation

The original POML acceptance criteria assumed a binary PASS / FAIL on the <250 KB delta. The user has revised the criteria to account for the Option 1 acceptance:

| Original criterion | Revised criterion (Option 1) | Status |
|---|---|---|
| Verification memo exists at `notes/bundle-size-verification.md` with baseline, R3 size, delta, verdict | Same | ✅ This document |
| R2 baseline recorded as specific number | Same — ~258 KB gzip recorded | ✅ |
| R3 production build succeeded with zero errors | Same | ✅ |
| Delta <250 KB gzip (PASS) OR follow-up task opens + Phase G blocked | **Delta documented; Option 1 accepted; Phase G task 074 will empirically validate via NFR-01/NFR-03** | ✅ — Option 1 path |
| PDF.js + mammoth in separate async chunks | **Source-level lazy-load verified; singlefile inlining caveat documented** | ✅ — source-correct; deployable-inlined per ADR-026 |
| PASS or FAIL verdict; if PASS, Phase G unblocked from bundle-size standpoint | **CONDITIONAL PASS — Phase G unblocked; final validation occurs in task 074** | ✅ |

---

## 7. Action Items

### In R3 scope (this project)

| # | Action | Owner | Status | Pointer |
|---|---|---|---|---|
| 1 | This verification memo | task 061 | ✅ Completed | This document |
| 2 | Add NFR-12 overrun entry to plan.md Risk Register | task 061 | ✅ Completed (R-6 row revised; see §8 below) | plan.md §8 |
| 3 | Empirical validation via NFR-01 / NFR-03 timings | Phase G task 074 | 🔲 Pending Phase G | tasks/074-*.poml |
| 4 | R3 lessons-learned (Phase H wrap-up) records this decision + the singlefile caveat for future Code Page projects | Phase H task 090 | 🔲 Pending Phase H | tasks/090-*.poml |

### Deferred to follow-on projects

| # | Action | Owner | Pointer |
|---|---|---|---|
| 5 | Evaluate Option 2 (separate Dataverse web resources for heavy libs) as a discrete project — likely 1–2 weeks scoped | TBD (recommended: BFF remediation project or dedicated new project) | Cross-project assessment §3 Option 2 |
| 6 | Evaluate Option 3 (server-side extraction via BFF endpoint) — viable if scoped into BFF remediation | BFF remediation team | Cross-project assessment §3 Option 3 |
| 7 | ADR-026 amendment to add "Heavy library handling" subsection (singlefile/lazy-import incompatibility note + Option 2 pattern reference) | Architecture / docs owner | Cross-project assessment §4 |

---

## 8. Phase G Gate Status

**Phase G is unblocked from a bundle-size standpoint** per Option 1 acceptance. Final empirical validation occurs in Phase G task 074 (NFR-01 + NFR-03 timings against the 798 KB bundle on real Dataverse network).

- If task 074 NFR-01 + NFR-03 pass → Option 1 stands as the permanent R3 answer; long-term Option 2 work moves to a follow-on project.
- If task 074 NFR-01 + NFR-03 fail → escalate; consider hotfix via Option 2 or Option 3 scoping.

---

## 9. References

- **Cross-project assessment** (load-bearing context): [`docs/assessments/code-page-bundle-size-vs-singlefile-2026-05-20.md`](../../../docs/assessments/code-page-bundle-size-vs-singlefile-2026-05-20.md)
- **R3-local investigation** (root-cause + initial options): [`notes/perf/bundle-size-investigation.md`](perf/bundle-size-investigation.md)
- **R3 spec NFR-12**: [`spec.md`](../spec.md) line 157
- **R3 plan Risk Register**: [`plan.md`](../plan.md) §8
- **ADR-026** (full-page Code Page standard / singlefile): [`.claude/adr/ADR-026-full-page-custom-page-standard.md`](../../../.claude/adr/ADR-026-full-page-custom-page-standard.md)
- **Hook implementation**: [`src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useChatFileAttachment.ts`](../../../src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useChatFileAttachment.ts) lines 311 + 327
- **Phase G task 074** (downstream empirical validation): `tasks/074-*.poml`

---

*Task 061 status: completed (Option 1 accepted). Phase G unblocked from a bundle-size standpoint. Final validation in task 074.*
