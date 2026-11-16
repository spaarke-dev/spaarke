# Current Task State

> **Last Updated**: 2026-11-16 (context checkpoint at 85%)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **014** IN PROGRESS: BFF Event forms (FR-35–FR-37), `tasks/014-bff-event-forms.poml`. FULL, opus high, two adversarial passes (auth). Split into **014a** (contact-access service, its own PR, uac-r2 gate review) and **014b** (everything else) |
| **Step** | 014a **PR #2852 open** (head `work/r4-014a-record-contact-access` @ `09ce996d15`). Pass 1 + fix round + scoped re-check done. Sent to uac-r2 for gate review 2026-11-16 (with K1 question and merge-order question vs `perf/2845-round2`). CI was running at PR open |
| **Status** | uac-r2 review recorded verbatim in `notes/task-014-notes.md` (Q1–Q6). Owner approved creator name (Q5) and was told Q2.9 |
| **Next Action** | **014b PR #2869 open** (head `9719d2f594`; pass 2 + fixes + scoped re-check clean; +25 KB; sent to uac-r2 for review). Waiting: uac-r2 OK + CI → owner go to merge → SHA to uac-r2 for dev deploy → task 015 (SPA; must hide Add event when `creatableEventTypes` is empty). **Owner decision open:** PATCH assignee semantics (a) match internal reassign [recommended, uac-r2 no objection] / (b) plain update / (c) leave out — PATCH assignee returns 400 until decided. (History:) **014a MERGED** (#2852 squash `16771d1051`, 2026-11-16 13:24Z, owner go "Yes proceed"; uac-r2 told the SHA, it rebases #2867 round 2 onto it). **014b STARTED**: branch `work/r4-014b-event-forms` from `16771d1051` in worktree `agent-a9853fb71e8bd4430`; Opus implementer running (commit only, no push). Conflict check: only #2867 (uac-r2) overlaps — `RequestAccessMemo.cs` (014b must not edit) and `ExternalAccessModule.cs` (ReadableColumns entries only). After implementer: publish size, CVE, adversarial pass 2 on full 014 diff, fix F-class, PR, uac-r2 review → SHA for dev deploy. `C:\wt14m` / `C:\wt14a` kept for the 014b size check (move wt14m to master `16771d1051` and wt14a to the 014b head, delete their `pub` folders, rerun scratchpad `pubsize.ps1`); remove both via Remove-WorktreeSafe.ps1 after. (History:) Gate fixes **pushed at `a866dad1be`** (strict `QueryStrictAsync` grant read per owner option A, cancel, firm cache, doc, deterministic #2851 test). Scoped re-check clean; publish +10,741 B; PR body updated. **Waiting on uac-r2's re-check of `09ce996d15..a866dad1be` + CI** → merge 014a FIRST → start 014b. Issues: D-19 #2854, D-20 #2860 (uac-r2). Results recorded in `notes/task-014-notes.md` "014a result". (History:) Fix round 1 DONE at `09ce996d15` (tests 32/32; ExternalAccess/AccessControl 11464; Arch 2635). K2 not done (needs an internal type on the public interface) → known limit. New defect in uac-r2's code → D-18 #2851 (RequestAccessMemo.For race), reported to uac-r2. Scoped Sonnet re-check of `ac28d45699..09ce996d15` RUNNING → then open the 014a PR. (History:) Pass 1 DONE: F1 (inactive firm, ISS-026), F2 (memo path untested), F2 (duplicated memo wrappers), plus K2 (ancestry re-read) and K3 (tests) sent to the 014a implementer as fix round 1. On its report: scoped re-check of the fix diff (Sonnet) → open PR (base master) with Placement Justification, size delta, CVE result, NFR-05 deviation note, and K1 question for uac-r2 (which "org-expansion term": conferring membership vs workforce standing-grant/derived members) → tell uac-r2 the PR number |

## 014a facts
- **Files:**
  - `Infrastructure/ExternalAccess/RecordContactAccessService.cs` (new, 559 lines):
    - `GetContactsWithAccessAsync(rootType, rootId, ct)` :198;
    - `EvaluateCandidateAsync` :391;
    - result `RecordContactAccess.Known/Unknown` :80–90.
  - DI `AddScoped<RecordContactAccessService>()` `ExternalAccessModule.cs:694`.
  - Tests: `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/RecordContactAccessServiceTests.cs` (24).
- Implementer results: build 0 errors; ExternalAccess unit tests 2041/2041; ArchTests 2635/2635; no vulnerable packages.
- **Publish size** (Compress-Archive, short paths `C:\wt14m` / `C:\wt14a`, 191 files each, MATCH):
  - master `ce6c241964` 42,336,904 B (40.38 MB);
  - 014a `ac28d45699` 42,346,488 B;
  - **delta +9,584 B**.
  - Remove the two worktrees with `scripts/worktree/Remove-WorktreeSafe.ps1` when done.
- **Known F1 (fix in 014a):** a direct grant naming an inactive firm is still listed. The evaluator refuses it (ISS-026). Fix: confirm each candidate via the existing `ExternalGrantLifecycle.ReadContactHeldGrantsAsync` (`ExternalGrantLifecycle.cs:~1203`; applies ISS-026, Secure/Limited/Restricted, conferring orgs, `mayConfer` = UTC-today rule) or `ExternalParticipationService.GrantOrganizationConfers` (:267).
- **Other implementer notes:**
  - existing `CheckGranteeNoAccessAsync` logs contact/org ids (pre-existing; mention in PR, not an NFR-04 listed item);
  - **NFR-05 says "no new DI registration"** but uac-r2 Q2.8 asked for this service. Record it as a justified deviation in the PR and tell the owner;
  - caps: more than 200 contacts or more than 1000 rows gives Unknown;
  - org expansion = conferring-membership rule; Limited is treated like Secure.

## 014b (after uac-r2 OKs 014a)
- Per POML + uac-r2 conditions:
  - types: memo key includes principal kind + id + sorted ids;
  - create fields;
  - list fields incl. creator name;
  - `GET /events/{id}` (uniform 404);
  - `GET /{root}/{id}/assignable-people` (Known → 200, Unknown → typed 503, isMe);
  - `PATCH /events/{id}` (contact: RightsForRootWriteAsync on every parent ≥ Collaborate; systemuser: + row Write via `GetPrincipalRightsAppOnlyOrUnknownAsync`; unknown → 503; can't read → 404; read-not-edit → 403 `event_update`).
- The assignee write reuses the service path behind `EventEndpoints.cs:~193` (verify and cite). No memo clear needed.
- Pins: uac-r2 Q6 list.
- Second adversarial pass on the full 014 diff.

## Live state
- **Dev BFF** `f451e0874d` (master `ce6c241964` + spike), live since about 00:45Z 2026-11-16. Rollback BFF `aa7d6345f3` alone.
- **Dev SPA** master `c1eb6e4a8e`. Rollback `04dbd8df05` only with BFF `5a9b687a0b`.
- uac-r2's 30-minute gate timings for #2845: not yet received.

## Open items
- D-13 #2800, D-14 #2809, D-15 #2810, D-16 #2823, D-17 #2845 (fixed, timings pending).
- Older owner questions: D-1 #2686, D-2 #2687, D-4 #2693, #2786 backfill.
- Phase 2 (020/022/023/024/028) still open; preview not loading is by design until 022.
