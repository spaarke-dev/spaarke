# Current Task — `ai-advanced-capabilities-nda-r1`

**Mode**: autonomous wave execution (owner-authorized 2026-07-25). Waves in parallel via subagents; gates after each wave; branch-only (no master merge); env-coupled steps flagged, never faked.

**Active wave**: Wave 1 — tasks **010** (model-tier last-mile) + **012** (RAG seed + tenant pin), in parallel.
**Status**: in-progress

## Completed
- [x] 001 — ADR-039 amendment (Output Determinism Modes; grounding mode-independent; strengthened per owner). ✅ SIGNED OFF. On PR #689.

## Wave plan (deps-driven)
- Wave 1 (parallel): 010, 012 — deps: 001 ✅
- Wave 2 (parallel): 011, 013 — deps: 010
- Wave 3: 020 (opus) — deps: 001,010,012 → then 022, 023 (opus), 021
- Wave 4 (parallel): 030, 031 — deps: 023; then 032 (after 031), 033 (after 022)
- Wave 5: 040 (after 031), 041 (after 023), 042 (after 023)
- Wave 6 (parallel): 050, 051, 052 — evals
- Wave 7: 060 deploy, 061 UI, 090 wrap-up (env-coupled / final review)

## Env-coupled (implement code/config; flag live steps)
- 012 live ingest + tenant-pin empirical check (Azure AI Search creds)
- 013 Reasoning deployment provisioning (Azure)
- 020 live model calls · 060 deploy · 061 UI tests on live org
- Live verification acceptance criteria across the above → reported as blocked-pending-environment.

## Gates (mandatory, per wave)
dotnet build (BFF) / npm build (client) → code-review + adr-check (Step 9.5) → commit to branch → next wave.

## Next action
Awaiting Wave 1 subagents (010, 012). On completion: build + gates + commit, then launch Wave 2 (011, 013).
