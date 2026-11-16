# Current Task State

> **Last Updated**: 2026-11-16 (context checkpoint at 85%)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **014** IN PROGRESS: BFF Event forms (FR-35–FR-37), `tasks/014-bff-event-forms.poml`. FULL, opus high, two adversarial passes (auth). Split into **014a** (contact-access service, its own PR, uac-r2 gate review) and **014b** (everything else) |
| **Step** | 014a built on branch `work/r4-014a-record-contact-access` @ `ac28d45699` (pushed, no PR yet). Adversarial review **pass 1 running** (background agent) on that diff. Publish-size delta measured. One F1 already known (below) |
| **Status** | uac-r2 review recorded verbatim in `notes/task-014-notes.md` (Q1–Q6). Owner approved creator name (Q5) and was told Q2.9 |
| **Next Action** | When pass 1 returns: send ALL F-class findings + the known F1 below to the 014a implementer agent (SendMessage to the original 014a implementer; if gone, start a new `implementer` opus agent in an isolated worktree on that branch) → re-check the fix diff only → open PR (base master) with Placement Justification, size delta, CVE result, NFR-05 deviation note → tell uac-r2 the PR number for its gate review |

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
