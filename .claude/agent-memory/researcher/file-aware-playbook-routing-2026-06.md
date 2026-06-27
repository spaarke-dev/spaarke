---
name: file-aware-playbook-routing-2026-06
description: Research on adding file-content awareness to PlaybookDispatcher. Doc Intelligence prebuilt contract model exists for extraction (not classification); custom classifier requires PDF/image binary not raw text; filename-as-signal is a real 442x speedup with ~96% accuracy on well-named docs (Koay et al. 2024); voyage-law-2 beats text-embedding-3-large by 6-10% on legal but is non-Azure; gpt-4o-mini TTFT 200-400ms in Azure-native deployments.
metadata:
  type: project
---

# File-aware classification for PlaybookDispatcher (Spaarke R6 design research, 2026-06-19)

**Why**: PlaybookDispatcher Stage 1 today uses only the user's chat message text. User explicitly flagged this as inadequate — same message ("summarize") + different file = different playbook required (NDA Summary vs Patent Summary). Multi-file case (patent + invoice → user wants invoice playbook) needs reconciliation.

**How to apply**: When R6 task generation reaches PlaybookDispatcher file-awareness work, this memory captures the verified facts from external research. Specifics in [[insights-engine-pre-design-2026-05]] don't cover this.

## Key verified facts

1. **Azure Document Intelligence has a prebuilt "contract" model** — but it's an EXTRACTION model (parties, jurisdiction, contract ID, title), not a classifier that distinguishes NDA vs MSA vs Employment. Custom classifier exists but needs ≥5 labeled samples per class and accepts only PDF/image/Office binaries — **not raw text strings**. Spaarke's `TextContent` is already extracted client-side, so Doc Intelligence custom classifier requires re-sending the binary, which the BFF doesn't have.
2. **Custom classifier pricing**: $3/1000 pages (some sources say $50/1000 for true classification — pricing page is canonical).
3. **Filename-as-signal is high-value**: Koay et al. 2024 (arxiv 2410.01166) — TF-IDF over filename alone achieves 96-99% accuracy on 90%+ of in-scope documents and runs 442× faster than DiT.
4. **gpt-4o-mini TTFT**: 200-400ms baseline in OpenAI/Azure regional deployments under normal load.
5. **text-embedding-3-large vs -small**: large is ~80.5% acc vs ~75.8% for small on classification benchmarks. Latency is dominated by provider batching, not input length (flat curve up to ~8k tokens).
6. **voyage-law-2**: leads MTEB Legal by 6% over OpenAI v3 large, 23% relative on long-context legal. NOT available in Azure first-party — would need to call Voyage API (data-residency tradeoff) or migrate to a marketplace offer. Newer MLEB benchmark (2025) ranks it 8th — still ahead of OpenAI but no longer dominant.
7. **Semantic router thresholds**: production semantic routers (e.g., aurelio-labs/semantic-router) typically use 0.70-0.75 cosine threshold, achieve ~95% accuracy at 10-20ms latency for routing.
8. **LegalBench**: GPT-4o-class on contract clause classification (CUAD) hits ≥88% balanced accuracy; on 1-2 page disclosure classification, drops to 74-75%. Document-TYPE classification (NDA vs Patent vs Invoice) is an EASIER task than clause classification — top-line accuracy should be higher.

## Spaarke-specific constraints captured

- TextContent is client-extracted (PDF.js, mammoth.js). For one user turn only. Doc Intelligence custom classifier path requires sending the binary too — not the current architecture.
- Stage 1 budget 1.5s, Stage 2 budget 0.5s, total 2s.
- text-embedding-3-large 3072-dim is already deployed for `playbook-embeddings` index.
- Multi-file is common in legal workflows (matter packets).

## What the design doc should recommend (synthesized in findings)

- **Phase A (per-file fingerprint, parallel)**: filename TF-IDF/keyword pass + first 4000 chars of text concatenated as a single string per file. Hash + cache per-(filename, sha256) for the session.
- **Phase B (per-file vector match)**: embed `userMessage + " | DocType: " + filenameTokens + " | " + first2000Chars` against same `playbook-embeddings` index. One embed call per file (parallelize). Top-3 per file.
- **Phase C (reconciliation)**: if all files agree on same top playbook → run it. If different → use gpt-4o-mini with the user message + per-file top-3 list as structured-output decider (single LLM call, 300-500ms).
- **No new ADR needed** if implementation stays inside `PlaybookDispatcher` and uses existing `text-embedding-3-large`. **Would need an ADR if** we add Doc Intelligence as a runtime dep (new resource, new cost, new data-residency) OR if we switch embedding model to voyage-law-2.

## Open questions for main session

- Does the BFF have access to the original binary, or only the client-extracted TextContent? (Code in `ChatEndpoints.cs:2655` says only TextContent.) This determines whether Doc Intelligence is even a viable option without architecture changes.
- Is the "no new ADR in R6" rule strict for this scope? Per [[feedback_adrs-are-defaults-not-laws]], surface the trade-off — file-awareness is functional and could land without ADR, but voyage-law-2 swap or Doc Intelligence addition both warrant ADRs.
