# Current Task State — spaarke-ai-platform-unification-r7

> **Last Updated**: 2026-07-01 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Session** | R7 Wave 12 Daily Briefing continuation + UAT feedback |
| **Status** | in-progress — awaiting operator smoke of latest deploys |
| **Branch** | `work/spaarke-ai-platform-unification-r7` at HEAD `f6938617a` (pushed) |
| **Master** | `02962c268` (PR #527 merge; unchanged since ~3 hrs ago) |
| **Worktree** | `c:/code_files/spaarke-wt-spaarke-ai-platform-unification-r7/` |
| **Next Action** | Operator force-reloads SpaarkeAi widget + smokes the new HighPriority mini-report layout + verifies inline reference links + confirms LLM prompt tightening reduces hallucination. Then continue with wizard AC8-AC12 + Assistant↔Workspace AC13-AC15 UAT. |

### Files modified this session (all committed + pushed + deployed to spaarkedev1)

- `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs` — case fix (`sprk_HighPriority` → `sprk_highpriority`), new `Description` + `Action` + `Reason` + `ModifiedOn` outputs on HighPriorityItemDto, `ClassifyAction` helper, wiring for description column per entity
- `src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs` — HighPriorityItemDto extended with Description + Action + Reason + ModifiedOn fields
- `src/client/shared/Spaarke.DailyBriefing.Components/src/components/HighPrioritySection.tsx` — rewritten as mini-report layout: [Kind chip · Name link · Action badge · Reason chip] top row + truncated description below
- `src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx` — navigateTo binding fix (called as method, not destructured — resolves `_clientApiExecutor` platform error)
- `src/client/shared/Spaarke.DailyBriefing.Components/src/components/TldrSection.tsx` — rotating emoji next to "TL;DR" heading (16-emoji pool, deterministic per `generatedAt`)
- `src/client/shared/Spaarke.DailyBriefing.Components/src/services/briefingService.ts` — HighPriorityItemResult extended (description + action + reason + modifiedOn)

### Dataverse changes this session (via MCP, direct spaarkedev1 updates — NO git artifact)

- **BRIEF-NARRATE-CHANNEL Action** (`dc3533c0-fc70-f111-ab0e-7ced8ddc4cc6`) — sprk_systemprompt updated with: PAIRING RULE (title + regarding must come from same input item), GROUNDING CHECK (verify exact { title, regardingName } pair in items[]), AGGREGATION PREFERENCE (prefer aggregated over item-specific bullets). Metadata bumped to $version 2, lastModifiedBy=r7-w12-anti-hallucination-tightening.
- **BRIEF-NARRATE-TLDR Action** (`ce299eb4-fc70-f111-ab0e-7ced8ddc4cc6`) — sprk_systemprompt updated with: STRUCTURAL PREFERENCE (describes counts + themes, not item titles), same PAIRING RULE. Metadata bumped to $version 2, lastModifiedBy=r7-w12-structural-summary-tightening.

### Critical context

Tonight's session shipped a large operator-feedback batch on top of Wave 12's core widget cutover:

- **Waves 6/7/8/10 feedback** (vertical dots, primarycontact wiring, 15s toast + Open link, elevated channel headings) shipped earlier tonight in commit `5988966b8`.
- **Waves 2/3/4/5 feedback** (Perplexity-style inline hyperlinks + trailing `[N]` citations for narrative bullets) shipped in `ad903e01f`.
- **Item 9** (High Priority section) shipped in `9a683c2c5` with the initial "compact list with badges" layout.
- **Post-UAT continuation** (this session's commit `f6938617a`):
  1. Case-sensitivity fix (`sprk_HighPriority` → `sprk_highpriority`) — collector was filtering on the schema name; Dataverse needs the lowercase logical name, so HighPriority section was empty despite operator having 4 flagged matters.
  2. `navigateTo` binding fix — destructuring the method breaks its `this` context and throws `_clientApiExecutor undefined`. Fixed both call sites (handleOpenRecord + Open-To-Do toast link).
  3. Rotating TL;DR emoji.
  4. HighPriority section rewritten as mini-report cards with description + action badge + reason chip.
  5. LLM prompts tightened via MCP with PAIRING + GROUNDING + AGGREGATION rules.
- **DEF sub-agent fix** landed earlier: MembershipFieldDiscoveryService now synthesizes Owner + Customer targets from base AttributeMetadata (root-cause fix for the polymorphic-Owner bug that broke the resolver on `ownerid` fields).

**Compose-r1 status**: fully merged to master via PR #515 + PR #527. Compose-r1 auto-deployed BFF once at 04:07 UTC + widget once at 16:14 UTC — the widget deploy briefly overwrote my references + high-priority code, which was subsequently restored by rebuild + redeploy from this worktree.

### Deploy safety governance (added 2026-07-01 per operator concern)

Going forward, ALWAYS sync master into the worktree BEFORE building + deploying locally, so we never overwrite in-flight master changes from other teams. Standard sequence:

```
git fetch origin
git merge --ff-only origin/master  # or --rebase if conflicts
git log origin/master..HEAD         # sanity check: what am I about to deploy?
dotnet build src/server/api/Sprk.Bff.Api/
cd src/solutions/SpaarkeAi && npm run build
../../.. && ./scripts/Deploy-BffApi.ps1
./scripts/Deploy-SpaarkeAi.ps1
```

---

## Deferred / Follow-up items (log here for next session)

### Operator strategic asks (pending discussion)

- **"Monitored For" schema**: Choice option set on the 7 flagged entities (Matter, Project, Invoice, Document, Workassignment, Event, Todo) that captures WHY each record is being monitored (e.g., "Awaiting reply", "Budget review", "Regulatory deadline"). Replaces the binary Monitor flag with a semantic reason. → Future project (not R7 scope).
- **Fully-deterministic Activity Notes** (strategic option operator floated but deferred): kill LLM channel narration entirely; render structured item rows per channel. Preserves TL;DR as LLM-generated for the abstract summary. Zero hallucination risk. → Wave 12.5 or new project.

### Code-review follow-ups (from earlier scoped review; 5 medium/high items)

- **Revert collector membership-resolver bypass** now that root cause is fixed via `MembershipFieldDiscoveryService.ProjectLookupAttributeRows`. Owner-only queries silently lose collaborator scope (assigned attorneys, paralegals). Add smoke test that a `sprk_assignedattorney1` user sees their matter.
- **Author unit tests** for new client-side surfaces: `NarrativeCitedText.buildSegments` (segment splitter + overlap detection), `HighPrioritySection.classifyDueDate` + `actionToBadge`, `useBriefingRender.isEmptyResponse`, `useInlineTodoCreate` primary-contact wiring.
- **Metadata-drive the 7 QueryHighPriority\* helpers** — collapse into a single method + `record HighPriorityEntitySpec` array.
- **Fix `useInlineTodoCreate` primary-contact lookup race** — cache a `Promise<string | null>` in the ref instead of the resolved value so concurrent createTodo calls don't issue duplicate lookups.
- **Doc/code inconsistency**: `bulletToNotificationItem` truncates to 197 chars for "sprk_todo.subject" per comment but actual field is `sprk_name`. Fix comment + read maxLength from metadata.

### CI + governance items

- **CI env-var workarounds still in place**: `.github/workflows/ci-tier1-blocking.yml` has `APPLICATIONINSIGHTS_CONNECTION_STRING` + `Redis__AllowInMemoryFallback` patches (commits `fd657e0b2` + `37ef38c2f`). The underlying redis-r2 startup validations should be relaxed for Testing env so these workarounds can be removed. Not blocking anything.
- **F.2.1 restoration** (`fix/restore-bff-extensions-F.2.1` branch pushed earlier tonight): F.2.1 rule was restored to master via the compose-r1 PR #527 merge — my branch is not needed. Can be deleted.

### R7 UAT still pending (operator-driven)

- **5 wizards** (AC8-AC12): Matter, Project, Work Assignment, Document Summary, ... — operator to run in spaarkedev1 browser.
- **Assistant↔Workspace** (AC13-AC15): Scenario A ("what matter am I in?") flow.
- **Daily Briefing** operator smoke of tonight's changes (HighPriority mini-report + inline references + emoji).

---

## Rollback (if smoke fails)

**Tags for rollback**:
- `deploy/spaarkedev1/pre-widget-cutover` → commit `9bae5c306` (pre-tonight state)
- `deploy/spaarkedev1/pre-wave12-batch4` → commit `4fc73ae4a` (pre-batch4)

**Path A — bundle-only rollback**:
```powershell
git checkout deploy/spaarkedev1/pre-widget-cutover -- src/client/shared/Spaarke.DailyBriefing.Components/src/
cd src/solutions/SpaarkeAi && npm run build
../../.. && ./scripts/Deploy-SpaarkeAi.ps1
git restore src/client/shared/Spaarke.DailyBriefing.Components/src/
```

**Path B — branch revert**:
```powershell
git revert f6938617a --no-commit
git commit -m "revert(r7): roll back tonight's HighPriority + prompt changes (smoke failed)"
dotnet build src/server/api/Sprk.Bff.Api/
cd src/solutions/SpaarkeAi && npm run build
../../.. && ./scripts/Deploy-BffApi.ps1
./scripts/Deploy-SpaarkeAi.ps1
git push origin work/spaarke-ai-platform-unification-r7
```

---

## Reference (key docs)

- **This session's continuation commit**: `f6938617a`
- **PR #520** (R7 wave 12 merge): merged as `e106379462`
- **PR #524** (R7 wave 12 continuation merge): merged as `2de7509ee`
- **PR #527** (compose-r1 pull-forward): merged as `02962c268`
- **Restart doc**: [`notes/handoffs/daily-briefing-widget-cutover-restart.md`](notes/handoffs/daily-briefing-widget-cutover-restart.md)
- **Wave 12 plan**: [`notes/wave12-mvp-completion-plan.md`](notes/wave12-mvp-completion-plan.md)

---

*End of current-task.md. Ready for /compact or session pause. To resume: read this file's Quick Recovery, then continue with operator smoke of latest deploys.*
