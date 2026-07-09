# CLAUDE.md — spaarke-daily-update-service-r5 (project context)

> Loaded by `task-execute` for every task in this project. Repo-wide rules live in root `CLAUDE.md`.

## What this project delivers

Make the Daily Briefing **accurate by construction** and **appealing**, plus a bounded hardening sweep.

- **Accuracy (G-R5-A)**: item rows render deterministically from source fields; TL;DR stays LLM but on deterministically-computed facts + binary anchor resolution. **No groundedness threshold** — existence is never probabilistic (operator ruling 2026-07-08).
- **Appearance (G-R5-D)**: redesign via the `/prototype` harness → operator sign-off → port to existing shared-lib components (Fluent v9 + ADR-021 tokens, light + dark).
- **Hardening (G-R5-C)**: Choice-write coercion, collaborator-scope fix, collector de-dup, client-helper tests, `QueryHighPriority*` collapse, primary-contact cache, OData convention doc.

**Deferred (do NOT build)**: Monitored-For schema (D-3), EventDetailSidePane one-liner.

## Hot-path declaration

<hot-path-declaration> BFF=Y (Services/Ai/Narrators/DailyBriefing*, Nodes/UpdateRecordNodeExecutor) · SpaarkeAi=Y (Spaarke.DailyBriefing.Components) · ci-workflows=N · skill-directives=Y (jps-validate Step 7.7 — main-session-only edit) · root-CLAUDE.md=N </hot-path-declaration>

## Non-negotiable rules for this project

1. **Determinism is the contract.** Zero LLM in item-level rows. The TL;DR may only assert facts computed deterministically from source records; it never introduces its own fact. Non-resolving TL;DR anchors are dropped, not warned.
2. **No groundedness threshold / warn-withhold band.** `GroundednessCheckService` stays out of the briefing path (eval/telemetry signal only).
3. **Redesign extends existing components** — no parallel component tree, no new widget framework (CLAUDE.md §11). Fluent v9 + ADR-021 tokens; dark-mode verified.
4. **Frozen-engine exception (Path A)**: the `UpdateRecordNodeExecutor.CoerceFieldValue` change is defect-hardening (Choice writes 500), not new capability — state this in the PR description.
5. **BFF hygiene (root §10)**: state placement decision; verify publish size (≤60 MB; baseline ~49.63 MB incl PDBs) + no new HIGH CVE on BFF tasks; update tests.
6. **Gates are browser UAT on spaarkedev1** (G-R5-A/C) / **operator harness sign-off** (G-R5-D). curl/tests/logs never satisfy a gate.
7. **Coordinate**: run `/conflict-check` before each wave (r2-core `Services/Ai/` overlap); D-8 harness depends on the unmerged `fix/daily-briefing-components-standalone-build`.

## Key surfaces (from resource discovery)

See `plan.md` → Architecture Context for the full file map. Highlights:
- `DailyBriefingNarrator.cs:62` — `BRIEF-NARRATE-CHANNEL` (retire); per-channel LLM leg 197–231 (remove)
- `DailyBriefingCollector.cs` — deterministic `items[]` (`BuildNarrateRequest`), 7 `QueryHighPriority*` wrappers, resolver-bypass 216–248
- `UpdateRecordNodeExecutor.cs:417` — `CoerceFieldValue` (String→Choice defect)
- `Spaarke.DailyBriefing.Components/src/` — `TldrSection` (TL;DR) vs item renderers (`NarrativeBullet`, `HighPrioritySection`, `SubRow*`)
- `DailyBriefingCollectorTests.cs:238` — `..._ResolverBypassed` (re-flip in FR-C4)
- `tests/integration/contract/Eval/` — golden-utterance suite (mixed-item corpus likely needs new fixture)

## Rigor

Code tasks → FULL. Any `tests/**` touch → TEST-MODIFYING (code-review + adr-check unconditionally). `jps-validate` extension → main-session (skill-directive). Schema tasks → N/A (Monitored-For deferred).
