# Current Task State - spaarkeai-mcp-bff-api-r1

> **Last Updated**: 2026-11-06 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Pre-pipeline: gather the full context so `/project-pipeline` can produce a **complete** plan |
| **Step** | 2 of 4: read the held ontology PRs #2580 / #2579 and every other open PR touching our files |
| **Status** | in-progress |
| **Next Action** | Finish the open-PR sweep (the last command timed out). Run `gh pr diff 2580` and `gh pr diff 2579`, and record what they change in our files in `notes/pre-pipeline-context.md`. Then propose to the owner how the plan covers B-1/B-2/B-5 without waiting (plan against #2580's code), and run `/project-pipeline` only after the owner agrees. |

### Files Modified This Session
- All committed and pushed. Branch `work/spaarkeai-mcp-bff-api-r1` = origin (merge `619712f997`, master merged 2026-11-06, clean). Draft PR #2587.

### Critical Context
- Owner directive (2026-11-06): "we do not want to put together a partial plan that awaits other project merges". The plan must cover every task with the code context in hand.
- Ontology asked that B-1, B-2 and B-5 build on PR #2580 (task/174-billing-refusal, 5 commits ahead, 708 behind master; it changes `DecisionCommitService.cs` +34, `SignalReadService.cs` +121, `EarlierInvoiceReadability.cs`, `DecisionCommitEndpoints.cs`, plus tests). PR #2579 (task/175-invoice-decisions-card, client: Worklist types, laneCountFilters, decisionDisplay, worklistWizardItem, worklistService) is held with it until ebilling task 112 puts Invoice Review on dev.
- Ontology said: "after #2580 is on master (**or branch from its head**)". So the plan can be written against #2580's code now.

---

## Active Task / Lanes
- Pre-pipeline context gathering (above). `spec.md` is final; `design.md` is approved (O-1 to O-26).

## Next Actions
1. Re-run the open-PR sweep, without the files filter if it is slow: `gh pr list --state open --limit 80 --json number,title,headRefName`, then `gh pr view N --json files` for the candidates touching `Services/Signals/`, `Api/Signals/`, `Components/Worklist/`, `DecisionWizard/`, `DataverseUserClient`, `CallerResolution`, `AuthorizationModule`, `EndpointMappingExtensions`, `RouteAuthorizationGuard` or `Sprk.Bff.Api.csproj`.
2. Read the #2580 and #2579 diffs. Record the post-merge shape of each file we touch (signatures, new members such as `invoiceReview`, the new 409 reason code) in `notes/pre-pipeline-context.md`.
3. Gather the other background the pipeline needs: `knowledge/mcp-apps`, the researcher notes (`.claude/agent-memory/researcher/*2026-11-05*`), ModelContextProtocol.AspNetCore 2.3.1 API surface (researcher if needed), and the widget samples.
4. Present the "complete plan" approach to the owner, then run `/project-pipeline projects/spaarkeai-mcp-bff-api-r1`.

## Open Owner Questions / Blockers
- **Billing review in Copilot** (next slice) vs EB-D-194. It does not block v1.
- The approach for B-1, B-2 and B-5 relative to the held #2580: plan against its code now, and decide how execution handles the merge order.

## Where the standing decisions live
- Owner decisions: `design.md` §0 (O-1 to O-26). Naming and cross-project agreements: `notes/coordination/2026-11-06-copilot-naming-and-ownership.md`.
- Issues: `notes/defer-issues.md` (ISS-001 → #2575).
- ADR-028 A10 is on this branch.
- Reviews: ontology reviews `Services/Signals/**`, `Api/Signals/**` and the worklist route; uac-r2 reviews auth diffs (4 checks, spec FR-02).
- Peer sessions: `spaarke-wt-spaarke-ontology-platform-r1-38`, `spaarke-wt-ebilling-r1-c7`, `spaarke-wt-spaarke-legal-ai-accelerator-80`, `spaarke-wt-unified-access-control-r2-20`.
