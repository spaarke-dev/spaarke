# Current Task — `ai-advanced-capabilities-nda-r1`

**Mode**: autonomous wave execution (owner-authorized 2026-07-25). Waves in parallel via subagents; gates after each wave; branch-only (no master merge); env-coupled steps flagged, never faked.

**Active**: Wave 3 — 023 (opus orchestration) + 021 (summary) + gate011, parallel. 022 HELD until 023 commits (both touch BFF dispatch).
**Status**: in-progress

## Committed so far (PR #689)
001✅ · 010✅(gated) · 012🔄 · 013⛔ext · 020🔄(jps PASS) · 011🔄(dispatch-spine, seam tests pass)
Latest HEAD: 06317bbbc. Publish 47.49 MB.

## Follow-ons surfaced (backlog)
- sprk_outputdeterminism Dataverse column + BFF read-path (make ADR-039 mode=data; today prompt-enforced). 
- ReasoningModel token-leak guard (resolver fallback if #{...}# unresolved) — deploy gate 060.
- 010 low code-review items (resolver doc overclaim; Binding.cs:120 stale comment; Fast-tier test; config validation).
- Tenant-pin OR-clause fix — AWAITING OWNER.

## Completed / committed
- [x] 001 — ADR-039 amendment (Output Determinism Modes; grounding mode-independent; strengthened). ✅ SIGNED OFF.
- [x] 010 — model-tier last-mile. ✅ Gated CLEAN (commit 9c9696ab3). Publish 47.49 MB. 4 Low follow-ups deferred to 090.
- [~] 012 — KNW-011 source authored + tenant-pin analysis (commit 9c9696ab3). 🔄 live ingest env-blocked; tenant-pin ESCALATION pending owner.

## Running (subagents)
- 011 runtime picker (client) · 013 Reasoning provisioning (config+runbook; Azure env-blocked) · 020 NDA-REVIEW Action (opus, critical path)

## 🔔 Pending owner decision
- NFR-06 tenant-pin fix: recommended OR-clause `tenantId eq '{t}' or eq 'system'` (idiom already in repo). Security-adjacent. Gates live grounding + task 052. Does NOT block 020 authoring / Compose UX.

## 010 code-review follow-ups (Low, deferred to wrap-up 090)
- ModelTierDeploymentResolver doc overclaims "ONLY tier→deployment" (ModelSelector coexists for OperationType) — narrow wording.
- Binding.cs:120 EffectiveModelTier comment stale (says ModelSelector decides).
- Add Fast-tier symmetry seam test.
- Optional: startup validation that Fast/StandardModel non-empty when DI enabled.

## Next
On 011/013/020 completion: client build verify, gate, commit each; then Wave 4 (030+031 after 020→022→023). 023 (opus) + 022 come after 020.

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
