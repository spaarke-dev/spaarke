---
name: lavern-precedent-board
description: Lavern's Precedent Board (Claw Mode institutional memory) — fully implemented, not aspirational. Tentative→confirmed promotion, recurrence reinforcement, time-based decay, drift detection. Concrete code paths + data model.
metadata:
  type: reference
---

# Lavern Precedent Board — verified real

**Verdict: REAL.** The third-party claim about persistent cross-engagement memory with tentative→confirmed promotion, reinforcement on recurrence, and decay of stale entries is fully implemented in code. Tested. Not marketing. Supplements [[lavern-multi-agent-legal-system]] and [[lavern-followup-2026-05-20]] — prior passes missed this because Precedent Board lives in `src/claw/` (Clawern daemon) not in the main orchestrator surface.

## Where it lives

- **`src/claw/precedent-board.ts`** (478 lines) — the class itself.
- **`src/claw/curator.ts`** — consolidation pass (~line 380-422): promotion + drift decision.
- **`src/claw/index.ts`** ~line 660-690 — heartbeat tick consumes `decision.promoteToConfirmed`, calls `markConfirmed`.
- **`src/mcp/tools/memory-system.ts`** ~line 58-110 — `PrecedentEntry` type + `PrecedentStatus = 'tentative' | 'confirmed' | 'deprecated'` + `migratePrecedent()`.
- **`src/claw/types.ts`** ~line 128-144 — `CuratorDecision` shape with `promoteToConfirmed[]` + `driftDetected[]`.
- **`src/config.ts`** ~line 286-291 — env-tunable thresholds.
- Tests: `tests/unit/precedent-board.test.ts` (455 lines), `claw-precedent-lifecycle.test.ts` (154), `claw-reader-precedent.test.ts`, `claw-curator.test.ts`.
- Docs: `README.md` line 80 (verbatim third-party-quote source), `CHANGELOG.md` v0.13.0, `QUICKSTART.md` line 119, `CONNECTORS.md` (feedback-loop).
- UI: `viz/src/claw/components/PrecedentsTab.tsx` — dashboard surface.

## Data model (concrete)

`PrecedentEntry` fields actually persisted:
- `id` (`PREC-{hex}`), `patternName`, `description`, `beforeSnippet`, `afterSnippet`
- `documentType`, `jurisdiction`, `tags: { engagementType, custom[findingType] }`
- `qualityScore`, `effectivenessScore` (0-1, clamped), `timesUsed`, `timesQueried`
- `outcomes[]` (capped 50): `{ sessionId, timestamp, applied, scoreDelta, verificationPassed }`
- `status: 'tentative' | 'confirmed' | 'deprecated'` (Phase 5 addition), `statusUpdatedAt`
- `deprecated: boolean`, `deprecationReason`
- `addedAt` (ISO)

Storage: `~/.lavern/precedents.json` (atomic writes, per-client isolated). **Not SQLite** — plain JSON via `writeJsonFileAtomic`. Compacted/archived to `precedents-archive.json` after 90 days (env-tunable).

## Promotion / decay / drift logic — concrete

1. **Index** (`indexFindings`): only RED/YELLOW findings with confidence ≥ 0.7 and non-empty evidence enter. O(1) dedup via SHA-256 of `findingType:evidence[0]`.
2. **Reinforce** (`reinforce`): dedup hit on indexing → `timesUsed++`, `effectivenessScore += scoreDelta * 0.2` (clamped), outcome appended.
3. **Promote tentative → confirmed** (`curator.ts` consolidationPass + `markConfirmed`):
   - `timesUsed ≥ CONFIRM_THRESHOLD` (env `LAVERN_CLAW_PRECEDENT_CONFIRM_THRESHOLD`, default **5**)
   - AND `entry.outcomes.every(o => o.applied && o.verificationPassed)` — "consistent verdicts"
   - Curator pushes IDs into `decision.promoteToConfirmed`; heartbeat calls `markConfirmed` per ID.
4. **Drift detection**: same loop — if `negativeOutcomes >= 2` (outcomes where `applied=false || verificationPassed=false`), the precedent enters `driftDetected[]` and surfaces as a Telegram/email notification. Operator decides; not auto-deprecated.
5. **Decay** (`decay`, runs ≤ 1×/day from heartbeat): `daysInactive > decayDays` → `effectivenessScore *= 0.95`. `daysInactive > decayDays * 6` → `deprecated = true` with reason.
6. **Search ranking**: `usageScore*0.3 + effectivenessScore*0.4 + recencyScore*0.3` where `recencyScore = 1 / (1 + daysSinceLastActivity/90)`.
7. **Compaction**: deprecated + entries older than `precedentArchiveDays` (default 90) moved to archive JSON.

Confirmed precedents are weighted higher when surfaced into Reader prompts ("the firm has confirmed this position across N matters" vs "tentatively flagged"). This is a string-level prompt enrichment, not a separate retrieval path.

## What's NOT in the implementation

- **No semantic similarity** for matching across documents — dedup is `SHA-256(findingType + first 200 chars of first evidence)`. Two findings worded differently won't dedupe; they'll create two precedents that both individually accumulate occurrences. The "matching pattern" claim is shallow string-match, not embedding-based.
- **No cross-tenant pooling** — per-client `dir` isolation; one firm's precedents don't seed another's.
- **No causal reasoning** about WHY a verdict was inconsistent — drift is a boolean flag from raw outcome counts, not a model judgement.
- **No persistent representation outside one JSON file** — no SQLite schema, no versioned history beyond `outcomes[]` and archive.

## For Spaarke — design implication

The Precedent Board pattern is **directly worth designing for** in the Insight Engine. Lavern's Fact/Observation/Inference layering (per prior research) lacks anything above Observations — and that's exactly where Precedent Board sits: cross-engagement persistent patterns that get reinforced or decayed based on subsequent observations. The promotion gate (N recurrences + consistent verdicts) is a cheap, defensible alternative to ML-based pattern learning, and the drift-detection branch (negativeOutcomes ≥ 2) gives an honest "this pattern is breaking down" signal that's auditable.

The reusable design moves:
1. Three-state lifecycle (`tentative` | `confirmed` | `deprecated`) with explicit promotion gate.
2. Outcomes array as evidence-cited audit trail per precedent.
3. Heartbeat-paced consolidation (decay/compaction outside the request path).
4. Drift detection as a surfaced operator decision, not silent deprecation.

What Spaarke should do *better* than lavern: use Azure AI Search vector + BM25 hybrid for dedup/matching (lavern's SHA-256 dedup is brittle), and persist to Dataverse/Cosmos with proper schema instead of a single JSON file.

## Open questions

- Lavern's `markConfirmed` doesn't change `effectivenessScore` — confirmation just toggles status. Does that miss an opportunity? If status weights into Reader prompt enrichment, score should arguably bump on confirm. Worth verifying behavior in eval runs (`evals/jv/runs-v34/`).
- The `outcomes[]` cap of 50 is silently FIFO-dropped (`shift()`). Long-lived precedents lose early evidence; the "consistent verdicts" check only sees the last 50. Acceptable in practice but worth knowing.
