# Current Task — Spaarke Insights Engine Phase 1.5 (r2)

> **Purpose**: Active task state tracker. Managed by `task-execute` skill.
> **Lifecycle**: Wave F implementation complete (tasks 050, 051, 052, 053 all ✅). Pending main-session batch commit + PR open + auto-merge.

---

## 🎯 Active task — none

Wave F (contract v1.1) is **implementation-complete**. All 4 Wave F tasks shipped — no active sub-agent work remaining.

**Next action (owner / main session)**: batch-commit Wave F changes (sources + tests + docs) on branch `work/ai-spaarke-insights-engine-r2-wave-f`, push, open PR with auto-merge enabled (consistent with Wave D + E precedent), monitor CI, redeploy BFF to `spaarke-bff-dev` post-merge.

---

## Wave F status (post-task-053 ✅)

| Task | Wave-item | Title | Status | Effort | Output |
|---|---|---|---|---|---|
| 050 | F1 | Streaming + citation ID flow spike | ✅ | 0.5d | `notes/spikes/wave-f-streaming-citation-spike.md` (6 sections A–F; binding scope decision: **SHIP FULL** both observation + document citation href; R5 escape hatch NOT triggered — plumbing cost Small) |
| 051 | F2 | SSE streaming on `POST /api/insights/assistant/query` | ✅ | 3d | `IInsightsAi.AssistantQueryStreamAsync` + `AssistantQueryChunk` Zone-B DTO + endpoint `Accept`-header negotiation + 8 new tests |
| 052 | F3 | `citations[].href` projection + URL resolution | ✅ | 1d | `AssistantQueryCitation.Href` optional field (JSON key lowercase `href`) + `AssistantCitationHrefOptions` config (section `Insights:CitationHref`, key `BffBaseUrl`) + URL pattern `{bffBaseUrl}/api/documents/{sprk_document-guid}/preview` + 17 new tests |
| **053** | **F4** | **Contract v1.1 docs + R5 coordination update** | **✅** | **0.5d** | **`design-e3-tool-call-contract.md` bumped v1.0 → v1.1 (+ §3.5 SSE schema + §4.6 href schema + §12 changelog + §11 Phase 2 deferrals updates); `notes/insights-engine-assistant-integration-brief.md` v1.1 amendment (§3.4 Accept negotiation + §4.7 SSE event schema + §4.8 citations.href + §5.3 mid-stream errors + 4 new §B sanity checks); R5 coord doc `insights-r2-coordination.md` §8 changelog new entry (`2026-06-04 (late) — Wave F v1.1 shipped`) resolving §4.4 + §4.6 touchpoints; TASK-INDEX + this file updated** |

**Effort total**: ~5d (matches mini-plan estimate). **Wall clock**: ~4 days end-to-end (051 + 052 parallel sub-agent dispatch per mini-plan §4.1).

## Quality-gate verification @ Wave F close

- Build: 0 errors, 15 pre-existing warnings (no new warnings introduced)
- Test suite: all green; 25 new tests added (8 SSE + 17 citation-href)
- Publish size: 44.13 MB compressed (vs Wave E baseline 44.10 MB; +0.03 MB delta, well under +5 MB per-task escalation threshold; well under 60 MB hard ceiling)
- §3.5 facade-boundary grep: clean (no Zone A leaks into Zone B endpoints/models)
- `dotnet format whitespace Spaarke.sln --verify-no-changes`: clean

---

## Project status (post-Wave F)

**Phase 1.5 acceptance bar**: hit. Wave F is a strictly-additive minor-version bump (v1.0 → v1.1) on top of Phase 1.5; the FR-04 / FR-05 / SC-04 / SC-05 acceptance criteria from spec.md were already met by Wave E.

**Pending operator-mediated follow-ups** (unchanged from Wave E close):
- Assistant team review of `design-e3-tool-call-contract.md` (now v1.1 — sub-task A.5 of task 042 still owner-mediated)
- Assistant-side implementation of the v1.1 contract additions

**Wave 090 (wrap-up)**: still 🔲 not-started (per TASK-INDEX). Owner directs whether to execute now (post-Wave-F lessons-learned) or batch later. The Wave F lessons (parallel dispatch held; 0 stuck-agent incidents; spike binding decision drove F2/F3 scope clarity) should be captured in the 090 lessons-learned when it runs.

---

## Previous active task — 052 (F3) — citations[].href projection + URL resolution (✅ COMPLETE 2026-06-03)

See Wave F task 052 POML output section + commits on `work/ai-spaarke-insights-engine-r2-wave-f` for the source diff. Tests: 17/17 passing. Quality gates clean.

---

## Wave F closure note

Per mini-plan §4.1 dispatch plan:
- Round 1 (done): 050 serial — spike ✅
- Round 2 (done): 051 + 052 parallel — both ✅
- Round 3 (done): 053 serial — docs ✅

**No further Wave F sub-agent work remains.** Wave F is implementation-complete; awaiting main-session batch commit + PR open + auto-merge per owner direction.
