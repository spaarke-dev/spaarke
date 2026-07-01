# R6 → R7 Backlog

> **Project**: spaarke-ai-platform-unification-r6 (closed 2026-06-29)
> **Purpose**: Explicit deferred-work list with GitHub Issue pointers so R7+ planners can prioritize without re-deriving context.
> **Read alongside**: [`lessons-learned.md`](lessons-learned.md) for the why.

---

## How this list was built

R6 closeout audited every `notes/defer-issues.md` entry, every "deferred to R7" hint in `r6-deliverables-audit.md`, and every CRITICAL/NON-CRITICAL finding from the wrap-up code-review. Each item below has a GitHub Issue (visibility for the team) so future planners can groom in the portfolio board ([project #2](https://github.com/users/spaarke-dev/projects/2)) without reading R6's notes folder.

Items are grouped by **owning lane**. Owning lane = the project (current or future) that should naturally take it.

---

## Items deployment-ready in R6 master but waiting for ops

These are CODE-COMPLETE on master `ecb650e44` and waiting for Azure deploy + UAT lift:

| Item | What | Where | Activation |
|---|---|---|---|
| DEF-001 (#471) | BFF `context_event` SSE emission → frontend ExecutionTraceWidget | `Services/Ai/Telemetry/IContextSseRelay.cs` + `ChatEndpoints.SendMessageAsync` finally hook | Run `/bff-deploy` against spaarke-dev; open chat; invoke a chat tool; verify ExecutionTraceWidget shows live frames |
| DEF-002 (#473) | Playbook-attached persona FK wiring | `AnalysisPersonaService.GetEffectivePersonaAsync` reads `sprk_playbookpersona` FK | After BFF deploy: in spaarke-dev, attach a persona to a playbook via maker portal main form; start a chat session bound to that playbook; verify system prompt uses the attached persona's `sprk_systemprompt` |
| DEF-003 (#474) | Builder UI `destination` + `widgetType` fields | `DeliverOutputForm.tsx` + `playbookNodeSync.ts` | Build is at `src/client/code-pages/PlaybookBuilder/out/sprk_playbookbuilder.html` (2,977 KB); upload to make.powerapps.com → Web resources → `sprk_playbookbuilder` → Save → Publish |

---

## Items owned by sister project — chat-routing-redesign-r1

These are surfaces the chat-routing-redesign-r1 project is the natural owner of — R6 deployed prerequisites; the upstream behavior is theirs.

| Issue | Title | Why theirs |
|---|---|---|
| [#470](https://github.com/spaarke-dev/spaarke/issues/470) | `SYS-Recall_Session_File` repeatedly fails ("not present in this session") in chat | `RecallSessionFileHandler` was added by their task 085. R6 deployed the row; upstream tool-selection behavior is their codepath. Diagnostic at `notes/tier-c-b-recall-session-file-diagnostic.md` enumerates 3 candidate root causes. |
| [#475](https://github.com/spaarke-dev/spaarke/issues/475) | Chat ↔ Workspace write-side unification | Their codebase has the area in flux (Phase 5R + Phase 7 / PR #509 CapabilityRouter retirement). Full handoff doc already filed in their wt: `chat-workspace-write-side-unification-r6-handoff.md`. ADR-030 doesn't forbid additive events on existing channels; the current "chat handlers commented as ADR-030 compliance therefore no SSE" is over-restrictive. |

---

## Items owned by sister project — spaarke-daily-update-service

| Issue | Title | Why theirs |
|---|---|---|
| [#510](https://github.com/spaarke-dev/spaarke/issues/510) | `daily-briefing-narrate.json` writes to vestigial `sprk_capabilities` field | Surfaced by DEF-004 verification — they write `"sprk_capabilities": 100000006` (Title Case enum value) instead of `"sprk_playbookcapabilities": 100000006`. After DEF-004 schema removal their next `Deploy-Playbook.ps1` run will fail with "attribute not found" — bumped `urgency: now`. |

---

## Items for future focused projects

These don't fit any existing project; each is a candidate "mini-project" scope.

| Issue | Title | Suggested project scope |
|---|---|---|
| [#472](https://github.com/spaarke-dev/spaarke/issues/472) | Slash commands focused project (Tier A/B slashes non-functional) | A `/slash-completion-r1` mini-project: review each of the 10 slash commands end-to-end against chat-routing-redesign-r1's new dispatch path; fix per-slash gaps. Per user direction: "focused project (mini-spaarke project)". |
| [#511](https://github.com/spaarke-dev/spaarke/issues/511) | BFF Sprk.Bff.Api nullable-reference warning cleanup (6 sites) | ~1-2h focused cleanup of CS8604/CS8601/CS8766 family. Filed 2026-06-29 by `/devops-idea-create` from R6 build verification. Not bundled into R6 to keep scope clean. |

---

## Items deliberately deferred from R6 spec

From spec.md §Out of Scope + Q-decisions resolved 2026-06-07:

| Item | R6 disposition | R7 handle |
|---|---|---|
| **Scope admin UI** (Q3) | R6 used Power Apps Dataverse forms; no custom admin UI built | If maker UX feedback shows the Power Apps form is friction, design a dedicated `sprk_aipersona` + `sprk_analysisaction` + `sprk_analysisplaybook` editor. Scope: ~2-3 weeks. |
| **Full eval harness** (Q10) | R6 shipped lightweight Q10 markdown transcripts at `notes/eval-baseline/` (4 transcripts) | R7 should add metric extraction + CI integration. Sketch: transcripts → eval runner → expected-shape assertions → CI gate. ~1-2 weeks. |
| **Additional `ActionType` node executors** | Per NFR-08, R6 did NOT modify the 11 production executors. Spec §Out of Scope lists: RuleEngine, Calculation, DataTransform, CallWebhook, SendTeamsMessage, Parallel, Wait | These are per-executor projects (each ~1-2 weeks). Prioritize by playbook author demand. |
| **R6 Pillar 6b "Pin to Matter" UI affordance** | Backend ships (`PinnedContextRepository` etc.); chat tool exists; in-chat UI button not built | ~3-5 days of UI work in `ConversationPane` + chat suggestions chip path. |
| **R6 Pillar 7 sliding-window summarization compression** | Pattern documented; compression algorithm not implemented (sliding window today is hard-truncated at NFR-10's 8K budget) | ~1-2 weeks: implement LLM-based summarization of oldest M turns when budget exceeded. |
| **R6 Pillar 9 widget visibility per-widget rollout** | 4 of 11 widgets shipped `getAgentVisibleState`; 7 default to opaque | Roll out case-by-case as widgets need it. Track per widget. |

---

## Items deferred from R6 closeout audit

From `r6-deliverables-audit.md` + UAT findings 2026-06-25:

| Item | Why deferred | Suggested handler |
|---|---|---|
| R6 task 091 Builder UI (persona dropdown) | DEF-002 BFF wiring lands the resolution path; maker portal main form exposes the field; in-Builder UI is nice-to-have | Combine with Q3 scope admin UI if pursued; or descope permanently |
| R6 task 092 (deeper cleanup audit of `sprk_capabilities` references in tests + docs) | DEF-004 closed the production-code surface; tests still reference the field in fixtures | Light cleanup pass; ~2 hours; bundle with #511 nullable warning cleanup |

---

## Items NOT going to R7 (closed, won't fix, or absorbed)

| Item | Disposition |
|---|---|
| DEF-004 (#476) | **Closed** 2026-06-29 — user removed schema column |
| R6 task 094 | **Withdrawn** — audit found already wired |
| Successor PR #401 stale-info confusion (chat-routing-redesign-r1 claimed PR #401 was open and blocking their Phase 7) | Resolved — direct verification showed PR #401 was MERGED 2026-06-24T18:43:20Z (commit `8579d6536` on master). No action needed. |

---

## Suggested R7 first moves (opinionated)

1. **Run the 3 deployment-ready items above** (DEF-001 BFF, DEF-002 BFF — same deploy, DEF-003 PlaybookBuilder upload). ~2h elapsed including UAT.
2. **Coordinate with `spaarke-daily-update-service` to ship ISS-003 fix** before their next scheduled deploy fails. ~1h elapsed by the other team.
3. **Schedule `/slash-completion-r1` mini-project** (issue #472) — biggest user-visible gap.
4. **Schedule warning-cleanup idea** (issue #511) when next BFF-touching project starts — bundle 1-2h.
5. **Capture lessons from R6 deploy + UAT in successor projects' notes** — R6's `lessons-learned.md` is the carry-forward for R7+.

---

*Last updated 2026-06-29 by R6 task 090 project wrap-up.*
