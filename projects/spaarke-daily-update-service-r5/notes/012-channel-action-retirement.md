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

## ✅ DONE — live Dataverse Action-row retirement (completed 2026-07-09 via MCP)

The catalog Action row (`sprk_analysisaction` `sprk_actioncode = 'BRIEF-NARRATE-CHANNEL'`, id `dc3533c0-fc70-f111-ab0e-7ced8ddc4cc6`, in **spaarkedev1**) has been **retired** (deactivated — reversible, non-destructive) via `mcp__dataverse__update_record` (`statecode → 1`, `statuscode → 2`). Read-back confirms `statecodename = "Inactive"`, `statuscodename = "Inactive"`.

> **Note**: the Dataverse MCP connector turned out to BE authorized in this session (the earlier "MCP unavailable" assumption was wrong — the auth gate only covered Gmail/Calendar/Drive), so the live retirement was completed here rather than deferred to task 017.

- **Code + mirror + live data are all now consistent** (mirror-first per ADR-040 satisfied end-to-end): repo has no reference, scope index no longer lists it, and the Dataverse row is Inactive.
- `BRIEF-NARRATE-TLDR` (id `ce299eb4-…`) confirmed still **Active** — untouched, as required.
- Reversible: reactivate via `update_record statecode=0, statuscode=1` if ever needed.

## Escalation check

The task `<escalation>` trigger (retiring the row would break a non-briefing consumer) did **not** fire: the only consumer was the retired per-channel leg + the superseded R4 playbook. No dangling reference created.
