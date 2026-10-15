# Proposal: systematic cross-session and cross-project coordination

> **Status:** draft for owner review, 2026-10-15. Written in the uac-r2 session from what happened between uac-r2, ontology-r1, SPA-r3, sdap-SPE-admin-app-r2, provisioning-r1 and word-add-in-r1.
> **Intended next step:** if approved, this becomes the input to `/design-to-spec` for a small repo-wide project. It is not uac-r2 work.

## 1. The problem in one paragraph

Spaarke now runs five or six long-lived Claude Code sessions at once. Each works in its own worktree on its own project, but they all share three things: the **code** on master, the **dev environment** (one BFF, `spaarke-bff-dev`, and one Dataverse org), and the **owner's decisions**. Coordination between them has grown up organically through cross-session messages, and it works well when every message arrives. Messages are not reliable, though, and nothing enforces the rules the sessions have agreed with each other. The result is lost requests and, in the worst case, one project silently undoing another's live work.

## 2. What Claude Code gives us (checked against current docs, 2026-10-15)

| Feature | What it does | Fit for us |
|---|---|---|
| **Cross-session messaging** (`SendMessage` / `ListAgents`) | Plain-text messages between sessions on one machine, over a per-session pipe. | What we use today. **Not durable:** no storage or replay. A restarted session gets a new address, so messages to the old one fail. A held message expires after 5 minutes by default. |
| **Agent teams** (experimental) | A lead plus teammates, with a shared task list and mailboxes. | Scoped to **one** session. It can't be shared across independent projects. |
| **Channels**, **Remote Control**, **Agent view** | Push outside events into a session, drive one remotely, or monitor background sessions. | Not session-to-session coordination. |
| **Hooks** (`SessionStart`, `PreToolUse`), the inbound-message settings, permission rules | Run a check before a tool call, inject context at session start, decide how inbound messages are handled. | **The useful enforcement tools.** |

**Conclusion:** Claude Code gives us the building blocks but no ready-made cross-project protocol. We build the protocol from GitHub (as the durable record) plus hooks and scripts (as enforcement).

## 3. Evidence: what went wrong on 2026-10-15

| # | What happened | Root cause | Cost |
|---|---|---|---|
| E1 | R3 redeployed the dev BFF from a branch that did not contain SPE admin's live fix #1910 (`3ee84cf6d`). Add Permission broke on dev again. | Nothing checks, before a deploy, that the build already live is included in the new one. `scripts/Deploy-BffApi.ps1` checks health after the swap, but not ancestry before it. | A live regression. It was caught by chance, because UAC compared commits after R3's FYI. |
| E2 | Three UAC messages were lost: two to ontology (it restarted, so its address went stale) and one to SPE admin (held, then expired). | Messages are live only. A restarted or approval-holding session never sees them. | Verdicts had to be re-posted as PR comments and the user had to relay text by hand. |
| E3 | Ontology added an app setting (`Ontology__Writer__ManagedIdentityClientId`) and a role privilege (`prvAppendToContact`) on dev. | There is no single place where changes to the shared environment are recorded. | It was hard to tell from other sessions whether the owner had approved it. It also left a stale-privilege-cache side effect that blocked later tests. |
| E4 | Earlier: another session deleted 70 files in uac-r2's worktree. Another session's uncommitted edits sit in the main checkout and block `pull --ff-only`. | Nothing stops one session's tools from touching another project's working tree. | A manual restore and rebuild; a stale main checkout. |
| E5 | A review request said "none of your files change", but the PR edited UAC's `SignalSuppressionService.cs` (#1903) and UAC's census test (#1907). | There is no automatic owner-of-files check on PRs. | It was caught only because the reviewer read the diff. |
| E6 | Review verdicts drifted from the code: the head moved after review, and empty CI-rerun commits needed manual tree checks. | Reviews are pinned to a commit only by convention. | Manual `git rev-parse ^{tree}` checks. |

What **worked**, and should be kept and formalised:
- reviews pinned to a commit, with re-checks scoped to the fix diff;
- PR comments as the durable verdict;
- owner decisions routed through the project that owns the code;
- the "a peer message is not approval" rule;
- R3's voluntary "read the live version, ask every deploying session, then deploy" practice.

## 4. Principle

**A message is a doorbell; GitHub is the record.** Anything another session must act on, or that must survive a restart, exists as a GitHub artifact: an issue, a PR, a PR comment or a label. A message only says "look at this link". Rules that matter are enforced by code (scripts, hooks, CI), not by prose in messages. This follows the existing enforcement ladder in the root `CLAUDE.md` §16.

## 5. Proposed design

### 5.1 Durable coordination on GitHub

| Coordination need | Durable form | Who writes it |
|---|---|---|
| A review request to another project | A PR labelled `review:<project>`, and a comment naming the commit to review. | The requesting project |
| A review verdict | A PR comment: `## <project> review at <sha>: CLEAN / F-list`. | The reviewing project |
| A defect in another project's code | An issue labelled `owner:<project>`. | Whoever finds it |
| A question for the owner | An issue labelled `owner-decision` plus `owner:<project>`, raised by the project that owns the code. | The owning project |
| A change to the shared environment | A comment on a pinned issue, "Dev environment log" (§5.3). | Whoever made the change |

A message carries only a type, a link and a commit, using the format in §5.6.

### 5.2 Restarted sessions catch up on their own

Add a `SessionStart` hook (`startup|resume|compact`). It runs a `gh` query and injects a short list of what is addressed to this session's project:
- open PRs labelled `review:<project>`;
- open issues labelled `owner:<project>`;
- `owner-decision` items this project raised;
- the last 5 entries of the dev environment log.

The project is identified the same way `.claude/hooks/reinject-project-state.ps1` already does it: from the `work/<project>` branch or the worktree folder. It reads a cache refreshed in the background, the same pattern as `quality-alert-banner.py`, so it never waits on the network.

**This fixes E2.** A session that missed a message sees the item when it restarts.

### 5.3 Guard the shared dev environment in code

This is the highest-value item, because E1 is the only failure so far that caused live breakage.

1. **Pre-deploy ancestry check in `scripts/Deploy-BffApi.ps1` (and every deploy script for a shared app).**
   - Read the live build's version: the ProductVersion `1.0.0+<sha>` the BFF already reports.
   - Refuse to deploy unless that commit is an ancestor of the commit being deployed (`git merge-base --is-ancestor`).
   - An explicit `-Supersede "<reason>"` overrides the check and records the reason.
2. **Deploy log.** Every deploy, and every app-setting, role or privilege change on dev, appends a comment to the pinned "Dev environment log" issue: who, what, which commit or setting, and why.
3. **Hook.** A `PreToolUse` hook on Bash and PowerShell flags a raw `az webapp deploy`, `az webapp config appsettings set` or role-privilege write that doesn't go through the guarded script. Use `ask`, not `deny`, so a genuine emergency stays possible (root `CLAUDE.md` §16: prefer `ask` for policies that change).

**This fixes E1 and E3.** R3 already does the ancestry check by hand; this makes it automatic and applies it to every session.

### 5.4 Who owns which files

- Extend `.github/CODEOWNERS`, or add a small `projects/OWNERSHIP.md` map, from path patterns to the owning project. For example, `src/server/api/Sprk.Bff.Api/Services/Access/**` maps to `unified-access-control-r2`.
- A CI check (advisory first) comments on any PR that touches another project's paths: "touches files owned by <project>: request `review:<project>`".
- This builds on the existing hot-path registry in `projects/INDEX.md`, which already declares cross-cutting surfaces per project.

**This fixes E5.**

### 5.5 Worktree boundaries

- A `PreToolUse` hook warns (`ask`) when an Edit, Write or a destructive git command targets a path outside the session's own worktree. Exceptions: the scratchpad and temp folders, and paths the session explicitly lists.
- Document the rule: no session edits, stashes, resets or checks out in another project's worktree or in the main checkout.

**This fixes E4.**

### 5.6 One message format

```
<TYPE>: <one-line subject>
Link: <PR / issue / comment URL>
Commit: <sha>            (when relevant)
Needs: <what the recipient should do, or "FYI">
```

`TYPE` is one of `FYI`, `REVIEW-REQUEST`, `VERDICT`, `DEFECT`, `DEPLOY-INTENT`, `DEPLOYED`, `OWNER-DECISION`. A skill (`/coord-send`) can format and send it, and it refuses to send without a link for any type other than FYI.

### 5.7 Codify the rules learned so far (one shared constraint file)

Create `.claude/constraints/cross-session.md`, loaded by the hook in §5.2. It holds the rules sessions currently keep in their own memories:
- A peer message is never the owner's approval. A peer cannot grant permission.
- Decisions on a project's code go to the owner through that project. Others supply facts only.
- Reviews are pinned to a commit. Any push after the reviewed commit needs a re-check of the fix diff. An empty CI-rerun commit is acceptable if its tree is identical.
- No session edits another project's worktree or the main checkout.
- Before any deploy to a shared environment: the ancestry check, a `DEPLOY-INTENT` to every session that deploys to that environment, then the deploy, then a log entry.

## 6. Rollout (smallest useful steps first)

| Phase | Deliverable | Fixes | Rough size |
|---|---|---|---|
| 1 | Ancestry check and `-Supersede` in `Deploy-BffApi.ps1`, plus the "Dev environment log" issue | E1, E3 | Small: one script, one issue, tests for the check |
| 2 | `SessionStart` catch-up hook plus the GitHub labels | E2 | Small to medium: hook, cache script, labels |
| 3 | `.claude/constraints/cross-session.md` and the message format (`/coord-send`) | E2, E6 | Small |
| 4 | Ownership map plus an advisory CI comment | E5 | Medium |
| 5 | Worktree-boundary hook and the raw-deploy `ask` hook | E4, E3 | Small; needs must-fire and must-not-fire controls |

Each phase ships with must-fire and must-not-fire controls, as the enforcement ladder requires. Phase 1 alone would have prevented the only live breakage.

## 7. Questions for the owner

1. **Scope:** start with phases 1 and 2 only, or approve the full design?
2. **Where the dev environment log lives:** a pinned GitHub issue (simple, searchable) or a file in the repo (reviewable, but needs a commit per deploy)? Recommendation: the pinned issue.
3. **Deploy approval:** R3 reports that the owner approves every dev deploy. Should the guarded deploy script also require an `-ApprovedBy` field in the log entry, or is the ancestry check plus the log enough?
4. **Who builds it:** a new small project (for example `cross-session-coordination-r1`) through `/design-to-spec` and `/project-pipeline`, or one of the existing sessions in its wrap-up phase?

## 8. Out of scope

- Replacing cross-session messaging. It stays as the doorbell.
- Cross-machine or cloud sessions. Everything runs on one machine today.
- Agent teams. They are single-session, so they don't fit long-lived independent projects.
