# Task 012 — Retire `BRIEF-NARRATE-CHANNEL` Action

> **Date**: 2026-07-09 · **FR-A2 / NFR-05** · depends on 010 (call path removed)

## What changed

| Surface | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingNarrator.cs` | Deleted the dead `ChannelActionCode` const + its XML-doc; reworded the file-header amendment comment to remove the literal `BRIEF-NARRATE-CHANNEL` / `ChannelActionCode` strings. `TldrActionCode` (`BRIEF-NARRATE-TLDR`) and the TL;DR resolution/call are **unchanged**. |
| `.claude/catalogs/scope-model-index.json` (catalog mirror, main-session edit) | Removed the `BRIEF-NARRATE-CHANNEL` scope entry (id `dc3533c0-fc70-f111-ab0e-7ced8ddc4cc6`); corrected the `DAILY-BRIEFING@v1` composite description that still claimed a live per-channel Action leg (now: deterministic per-channel bullets). |

## Verification

- **Grep-zero under `src/`**: `grep -r "BRIEF-NARRATE-CHANNEL|ChannelActionCode" src/` → **0 hits**. ✅
  - Non-code provenance survivors intentionally retained (constraint-permitted): `projects/**` notes/specs/plans (R4/R5/R7 history), `projects/spaarke-daily-update-service/notes/playbooks/**` action-JSON mirror, `scripts/Deploy-R4-Playbook-Nodes.ps1` + `scripts/dataverse/Sync-BriefNarrateOutputSchemas.ps1` (R4 deploy scripts). These are catalog-export/migration artifacts, not code paths.
- **Eval cases**: no `tests/**` case references `BRIEF-NARRATE-CHANNEL` (the `ChannelNarration*` symbols are the *deterministic* response-shape DTOs, which stay). No eval edit required — verified by grep, not assumed. ✅
- **Build**: `dotnet build -c Release` → **0 errors** (18 pre-existing warnings). ✅
- **Tests**: `DailyBriefing* + CodedWorkflow*` → **61/61**; `GoldenUtterance/Eval*` suite → **223/223**. ✅
- **Publish size (root §10)**: **45.13 MB compressed incl PDBs** — below the 49.63 MB baseline (this task is deletion-only) and well under the 60 MB ceiling. Δ vs baseline ≈ −4.5 MB (attributable to prior 010 fan-out removal on this branch, not this task; 012's own delta ≈ 0). ✅
- **CVE**: no `<PackageReference>` added or changed → no new CVE surface. ✅

## Placement decision (BFF §10)

No new endpoint / service / DI registration / package. This task **removes** a dead constant and a retired catalog entry within the existing `Narrators/` surface + the code-side catalog mirror. Placement is unchanged; no facade or new dependency introduced.

## ⚠️ DEFERRED — live Dataverse Action-row retirement (operator / deploy step)

The **catalog Action row itself** (`sprk_analysisaction` `sprk_actioncode = 'BRIEF-NARRATE-CHANNEL'`, id `dc3533c0-fc70-f111-ab0e-7ced8ddc4cc6`, in **spaarkedev1**) must be retired (statecode → Inactive, or row delete) via **Dataverse MCP / BA editor**. The Dataverse MCP connector is **not authorized in this session**, so the live-data retirement could not be executed here.

- **Code + mirror are already consistent with retirement** (mirror-first per ADR-040): the repo no longer references the Action; the scope index no longer lists it.
- **Execute the live retirement at Phase A deploy (task 017)** against spaarkedev1, alongside the `BRIEF-NARRATE-TLDR` prompt PATCH bundled there. Verify: `read_query sprk_analysisaction WHERE sprk_actioncode='BRIEF-NARRATE-CHANNEL'` returns 0 Active rows post-retirement.
- No runtime consumer remains (the coded composite `DailyBriefingNarrator` reads only `BRIEF-NARRATE-TLDR`; the R4 `DAILY-BRIEFING-NARRATE` playbook that referenced the channel Action is superseded by the coded composite per ADR-039/043), so the row is inert until formally retired — leaving it Active briefly is non-breaking.

## Escalation check

The task `<escalation>` trigger (retiring the row would break a non-briefing consumer) did **not** fire: the only consumer was the retired per-channel leg + the superseded R4 playbook. No dangling reference created.
