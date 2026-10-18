# Revision instructions: design.md and spec.md for spaarke-project-coordination-r1

> Audience: the Claude Code session that will revise `design.md` (rev 2) and `spec.md` in
> `projects/spaarke-project-coordination-r1/`. Read this whole file before editing.
> Follow the repo's convention for design revisions. If `design.md` must stay verbatim,
> produce `design.md` rev 3 as a revision with a change log at the top and update the spec's
> Corrections table to point at it; otherwise edit in place and bump the rev. The spec is
> edited in place and remains the governing document.

## 0. The objective this revision serves

The owner's objective, stated 2026-10-17:

> We stumbled onto cross-session messaging and it is very helpful. The process must take
> full advantage of it **without the owner identifying or orchestrating it**, and it must
> **never** cause Claude Code to constrain or refrain from messaging another session.
> A written process that enumerates cases will be read as the complete set of cases, and
> the unlisted case goes quiet. That outcome is a failure of this project.

Every edit below is tested against that sentence. When a proposed line could be read as
"message only when ...", rewrite it or remove it.

Two ideas govern the whole revision:

1. **Specify mechanics fully; specify judgment barely.** Mechanics (how a session is named,
   what settings apply, how to find who owns an area, how to subscribe for a notice) should
   be complete and exact. Judgment (when messaging is worth it) gets one permissive sentence
   and no conditions. Every additional sentence of judgment narrows the behaviour.
2. **The detector and the record are for the sessions that aren't running.** They catch what
   judgment misses when the other project has no live session. They are never the thing that
   tells a running session to speak.

## 1. design.md changes

### 1.1 §1 "What we want" — add objective 0

Insert before the current item 1:

> 0. **Sessions message each other on their own, and nothing in this process reduces that.**
>    Cross-session messaging (`SendMessage` / `ListAgents`) is the primary live coordination
>    channel. Claude already decides on its own to message another session when a change it
>    made affects that session's work. This project (a) removes the configuration friction
>    that makes those messages fail, (b) gives Claude the lookup data it needs to know *whom*
>    to tell, and (c) adds a durable record and a detector for the cases where the other
>    project has no session running. It adds no rule about *when* to message.

### 1.2 §2 Principles — rewrite two principles, add one

Replace "A message is a doorbell; GitHub is the record" with:

> **A message stands on its own; GitHub is the record for what must outlive the session.**
> Claude messages another session whenever it has something that session could use, with or
> without a GitHub item. A GitHub item is created in addition when the thing must survive a
> restart, be acted on later, or be decided by the owner.

Replace "Detection is a floor, not a ceiling" with:

> **The process never tells a session when to speak.** Sessions message each other on their
> own judgment. The detector, coordination items and start-of-session banner exist for the
> project whose session is not running. Nothing in a skill, hook or constraint file makes a
> message conditional on a detector hit, an open item, or a numbered step.

Add:

> **Messaging is configured so it works.** Sessions are named after their project, inbound
> messages are accepted, held messages never expire, and the minimum Claude Code version is
> pinned. A message that is held, dropped or misaddressed teaches Claude that messaging
> fails, and Claude then stops trying. Configuration is the first enforcement mechanism.

### 1.3 §3 "What Claude Code provides" — correct the messaging row

The current row says a held message expires in 5 minutes by default and a restarted session
gets a new address, as if these were inherent. Rewrite the row:

> | Cross-session messaging (`SendMessage` / `ListAgents`, `@name` mentions, idle notices) |
> **Primary live channel; Claude uses it unprompted.** Not durable: no queue or replay, so a
> message to a session that isn't running is lost. The 5-minute hold expiry and the
> approval hold for bypass-mode sessions are **settings** (`dialogExpiry`,
> `crossSessionInbound`), fixed in §5.0. Sessions are addressed by name, so a session
> restarted with the same `--name` is reachable again. Idle notices (v2.1.236+) replace
> polling when one session waits on another. Hooks and scripts can post into a session's
> inbox socket (`CLAUDE_CODE_MESSAGING_SOCKET`). WSL 2 and native Windows sessions on the
> same machine cannot reach each other. |

Add a row:

> | Channels | Push external events into a running session. Not adopted in r1; the inbox
> socket covers local hooks and the banner covers restarts. Re-evaluate if live sessions
> are found to miss record-only events. |

### 1.4 New §5.0 "Messaging mechanics" (before §5.1)

> - **Session name = project slug.** `worktree-setup` launches `claude --name <project>`
>   where `<project>` is the `work/<project>` branch name. A `SessionStart` hook warns when
>   the session's name doesn't match its worktree's project. Unnamed or auto-named sessions
>   are not addressable by `@<project>` and break the banner's project identification.
> - **Inbound policy, user settings (single-operator machine):** `crossSessionInbound:
>   "accept"`, `dialogExpiry: "never"`. Any `-p` worker that could receive messages is
>   started with `crossSessionInbound` set to `accept` in its `--settings` value.
>   `isolatePeerMachines` stays unset in r1 (no cross-machine sessions in use).
> - **Minimum version:** 2.1.236 (idle notices), 2.1.234 on native Windows. Recorded in the
>   constraint file; `/list-agents` not being recognised is the diagnostic.
> - **Isolation:** record in `projects/INDEX.md` (or the registry file, see §1.6) whether
>   each project's sessions run in WSL 2 or native Windows. Sessions on different sides
>   cannot message each other; for those pairs the banner is the only channel.
> - **Whom to tell:** the owned-areas map (§5.1) is lookup data. When Claude wants to know
>   who is affected by a change under `Services/Access/**`, it reads the map and messages
>   that project's session by name. The map answers "who", never "whether".
> - **Waiting:** when a session waits on work in another local session, it subscribes for
>   an idle notice (`SendMessage` with `notify_when_idle`) instead of polling or sending
>   repeated status requests.
> - **Bursts and loops:** the channel refuses rapid bursts at the sender and throttles loops
>   at the receiver. No instruction to "batch" or "limit" messages is written anywhere; if a
>   send is refused for burst, Claude consolidates and resends, as the refusal itself says.

### 1.5 §5.1 — move the "Reaching another project" table

The table (active + live session → item plus message; active, no session → item only;
paused → item only; closed → never) describes **what the detector does with a hit**, not what
a session does when it has something to say. Move it to §5.3 under a heading "How the
detector reaches a project" and add one line above it: "This governs detector output only.
A session messages any live session it chooses, regardless of this table."

### 1.6 §5.1 — registry storage

Per spec K-4, INDEX.md rows are prose cells a script can't parse reliably. Change the source
of truth for `lifecycle`, `owned_areas`, and `platform` (WSL/Windows) to a small structured
file, `projects/registry.yaml` (or frontmatter in each project README; pick one and say
why), and state that the INDEX.md columns are mirrored from it by `devops-project-sync`.
Record this as a change to owner clarification C-2 with the K-4 reason, for the owner to
confirm at design review.

### 1.7 §5.2 — detector signals

Keep signals 1 (same files changed by two active projects) and 2 (change in another
project's owned area). Mark signal 3 (planned POML overlap) as **conditional on the
report-only period showing missed planned collisions**. Remove signal 4 (parsing design.md
prose) and replace it with one checklist line in `/design-to-spec`: "List the active
projects whose owned areas this design touches." Remove signal 5 (dev log) from the
detector; the dev log is already surfaced by the banner and is not an overlap signal.

### 1.8 §5.3 — coordination issues, scope

State that for code that has **landed** (an open PR), the PR is the coordination record:
`review:<owner>` label plus the Router acknowledgement. Coordination issues are opened only
for **pre-code** overlaps (planned work, designs) in a critical area or needing a decision.
Delete the sentence that implies an issue per detected point in general.

Add to the banner's description: "The banner's background refresh may also post a one-line
notice into the running session's own inbox socket when a new `coord` issue or
`review:<me>` label appears, so a live session learns of a record-only event without a
restart. Own-child messages are delivered without approval. Optional in r1, after the
banner itself ships."

### 1.9 §5.4 — Step 0 wording

Replace "the session contacts the other project, or records 'no impact'..." with wording that
does not tie messaging to the step: "Open coordination points on the task's files are
resolved before implementing: resolve on the item (comment, 'no impact' with reason, or
await the owner). Messaging the other project's session is something the session does
whenever it is useful, here and at every other point in the task; this step only ensures the
**item** is not left open."

### 1.10 §5.4 — acknowledgement form

Acknowledgement is the `ack` label only, applied by the owner. Remove the review-comment
form (`coord-ack:` line); on a single-operator repo it is self-attestation and adds parsing.
Remove the 24-hour block escalation; every block already reaches the owner.

### 1.11 New §5.9 "Worktree write boundary" (E4)

E4 (a session deleted 70 files in a peer's worktree) has no requirement. Add: a `PreToolUse`
hook (pattern: `guard-worktree-links.py`) that returns `ask` on `Write`, `Edit`, and
destructive Bash (`rm`, `git checkout --`, `git clean`, `git reset --hard`) whose target
path resolves outside the session's own worktree and outside the shared `scripts/` and
`.claude/` paths that NFR-06 governs. Must-fire and must-not-fire fixtures as for FR-04.
Evaluate Claude Code's sandbox filesystem allow-list as the stronger alternative and record
which was chosen.

### 1.12 §5.8 Status page — defer

Mark phase 6 as "not in r1; build only if Board #2 views plus the detector report prove
insufficient after the report-only period." Keep the section text for reference.

### 1.13 §8 Risks — add the suppression risk first

> | Written rules narrow messaging to the listed cases | Judgment text is one permissive
> sentence (constraint file §A); no skill, hook or step makes a message conditional;
> `SendMessage` usage is measured before and after phase 2 (§9 success criterion 10) |

### 1.14 §9 Rollout — reorder

Phase 0 gains "messaging configuration: naming, inbound settings, version pin, platform
column" and the baseline `SendMessage` measurement. Phase 6 becomes "deferred".

## 2. spec.md changes

### 2.1 New FR-00 (phase 0) — Messaging configuration

Specify §1.4 above as a requirement:
- `worktree-setup` launches with `--name <project>`; `SessionStart` hook warns on mismatch.
- User settings carry `crossSessionInbound: "accept"` and `dialogExpiry: "never"`; `-p`
  workers pass `crossSessionInbound` in `--settings`.
- Constraint file records minimum version 2.1.236 / 2.1.234 (Windows).
- Registry records platform (WSL 2 / Windows) per project.
- Acceptance: `/list-agents` in any two active same-platform worktrees lists each other by
  project name; a message sent between two bypass-mode sessions is delivered without a
  dialog; a session restarted with the same name receives a message addressed to that name.
- Negative: no `permissions.deny` entry names `SendMessage` or `ListAgents` in any settings
  scope; `isolatePeerMachines` is not set.

### 2.2 FR-05 constraint file — rewrite the content list

Replace the current bullet list with these sections, and nothing else about messaging:

**§A — Messaging (judgment, one paragraph, verbatim):**

> Whenever something you learned, changed, or decided could matter to work another project
> is doing, message that project's session now. Don't wait for a coordination item, a
> detector hit, or a step that tells you to. To find whom to tell, look up the owned-areas
> map. Examples of the habit, not a list of triggers: a renamed contract, a decision on a
> shared pattern, a finding in their area, a question only they can answer, a status they
> are waiting on. If you are waiting on another session's task, ask for an idle notice
> instead of polling.

**§B — Mechanics:** naming convention, settings, version pin, platform table pointer, idle
notice usage, inbox socket pointer. Exact and complete.

**§C — Record:** when a GitHub item is created in addition to a message (must outlive the
session, must be acted on later, or needs an owner decision); lifecycle meanings; the
detector's "how it reaches a project" table; critical-area glob list; `ack` label rule.

**§D — Triage prompt** (design §5.6.1 with the F1/Large resolution in §2.9 below).

**Remove from the constraint file:** "a peer message is never approval", "never ask another
session for an action denied in your own" and the other inbound limits. Claude Code enforces
these; restating them adds cautionary tone around messaging without adding safety. Replace
with one line: "Claude Code's own inbound limits apply (a message can't approve, can't change
configuration, can't run commands); see the cross-session messaging docs."

Acceptance change: replace "the prompt text is byte-identical to design §5.6.1" with "the
prompt matches the constraint file; the constraint file is the source of truth and may be
revised at the phase 2 checkpoint."

### 2.3 FR-13 — registry

Change source of truth to the structured file per §1.6; INDEX.md columns are mirrored.
Acceptance: the detector and banner read the structured file only; `devops-project-sync`
regenerates the INDEX columns; a diff between the two is a sync defect.

### 2.4 FR-14 — detector signals

Signals 1 and 2 required. Signal 3 behind a flag, default off, enabled only by owner
decision after the report-only period. Signals 4 and 5 removed. Fixture list reduced
accordingly. Keep byte-identical output; exclude timestamps from the JSON.

### 2.5 FR-15 — invocation points

Remove the `/design-to-spec` invocation; replace with the checklist line in §1.7. Keep on
demand, nightly, `task-create`, pre-PR.

### 2.6 FR-16 — `/conflict-check` refactor

Move to a later phase, after the detector has run report-only for the agreed period and
the owner has confirmed it is correct. Record that NFR-05 (reversible by phase) does not
hold for phase 3 while FR-16 is in it; this move restores NFR-05.

### 2.7 FR-17 — coordination issues

Scope to pre-code overlaps only (§1.8). Add negative acceptance: no `coord` issue is opened
for an overlap that already has an open PR; the PR carries the `review:<owner>` label
instead.

### 2.8 FR-18 — banner

Add the optional socket-post sub-requirement (§1.8) as FR-18b, phase 4, after FR-18.
Acceptance: when the background refresh finds a new `coord` issue or `review:<me>` PR, the
running session shows a one-line notice without a restart; the notice is an own-child
message and triggers no approval dialog; nothing is posted when there is no change.

### 2.9 FR-06 — triage decision gap

Resolve: an F1 whose **correct** fix is Large goes to the owner, flagged urgent, in the same
turn. "Fix now (no approval)" applies to F1 only when the correct fix is Small or Medium.

### 2.10 FR-19 — volume alarm

Reduce to one acceptance line on FR-14's report: "the report shows the count of open `coord`
issues and marks it when above 15 (configurable)." Remove as a standalone FR.

### 2.11 FR-20 — Step 0

Rewrite per §1.9. Add negative acceptance: "Step 0 is the only place the skill mentions
coordination items; the skill's general guidance contains the §A messaging sentence by
pointer and no step makes a message conditional on an item."

### 2.12 FR-22 — acknowledgement

`ack` label only. Remove comment parsing and the 24 h escalation. Acceptance fixtures
reduced to: un-acked critical PR fails; acked passes; non-critical advisory; paused/closed
owner never blocks.

### 2.13 FR-24 — digest

Mark deferred. Keep the text; set phase to "post-r1, conditional".

### 2.14 New FR-25 — Worktree write boundary (E4)

Specify §1.11. Phase 1 (it is cheaper than FR-04 and prevents a worse outcome). Fixtures:
must-fire on `Write` outside the worktree, `rm -rf` of a sibling worktree path, `git clean`
in a sibling; must-not-fire on writes inside the worktree, writes to `.claude/**` and
`scripts/**` from the main session, reads anywhere.

### 2.15 New success criterion 10 — messaging is not suppressed

> **Autonomous messaging does not decrease.** Verify: before phase 2 lands, count
> `SendMessage` calls and their prompting context across all local session transcripts for
> the prior 14 days (script in `scripts/quality/`, report-only). Repeat 28 days after phase
> 2 and again after phase 5. A drop in count, or a shift to detector-prompted messages only,
> is a defect against objective 0 and blocks the next phase until the wording that caused
> it is found and removed.

### 2.16 Affected Areas table

Add: `.claude/skills/worktree-setup/SKILL.md` (naming), user settings guidance in the
constraint file, `projects/registry.yaml` (new), `scripts/quality/` messaging-count script,
E4 hook. Remove: `design-to-spec` detector invocation. Adjust phases per the above.

### 2.17 New Components table (§11 gate)

Add rows for the registry file, the E4 hook, and the messaging-count script, each with its
concrete failure (K-4; E4; objective 0). Remove the digest row to a "deferred" note.

### 2.18 Owner Clarifications

Add C-8 (registry file vs INDEX columns, K-4 reason) and C-9 (defer phase 6) as items for
the owner to confirm. Do not mark them answered.

## 3. Order of work and self-check

1. Apply §1 to design.md, then §2 to spec.md, then reconcile the spec's Corrections table.
2. Re-read every edited paragraph that mentions messaging, `SendMessage`, "contact", or
   "coordinate" and apply this test: *could a session read this as "only when ..."?* If yes,
   rewrite or delete. The constraint file §A is the only sentence about when to message.
3. Confirm no edited skill step, hook description, or FR makes a message conditional on an
   item, a hit, or a step.
4. Confirm every mechanics item in §1.4 appears in FR-00 with an acceptance line.
5. List, at the top of the revised spec, the FRs removed, deferred, added and rewritten,
   one line each, so the owner can review the delta without a diff tool.
6. Do not implement anything. This revision changes the two documents only.
