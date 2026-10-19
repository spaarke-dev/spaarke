# Decisions — spaarke-project-coordination-r1

Full rationale for the binding one-liners in `CLAUDE.md` §3. Newest first.

## 2026-10-18 — Owner answers before start {#pre-start}

1. **Phase 6 deferred** until after this project (C-9 confirmed). FR-24 has no task.
2. **Critical-area globs.** The owner asked how to judge them. Answer: by evidence, not assumption. The globs are derived from the documents that define each area, checked for coverage, and replayed against recent merged PRs. The owner approves the result (task 020). Checked 2026-10-18: every A-2 glob matches files. But A-2 misses at least `Services/Dataverse/RecordOwnershipResolver.cs`, `Api/Filters/RecordRouteAccessAuthorizationFilter.cs`, `config/secure-record-owner-role.json`, `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs`, and the write-path services named in `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` (`RecordCreationService`, `RegardingResolver`, `RecordContainerResolver`).
3. **PR #2043** merged 2026-10-18 and is merged into this branch (`f9a8f9fa4`). Every settings edit backs up first and diffs to additions only. User settings backed up to `~/.claude/settings.json.bak-2026-10-18-pre-coordination-r1` (197,590 bytes; 1,328 allow rules, 365 additional directories). Repo `.claude/settings.json` is in git.
4. Task 002: the owner will do the rename.
5. **Task 004: the session is authorized** to edit `~/.claude/settings.json` and update or remove the npm `claude`.
6. **Baseline excludes this project's own spike sessions** (2026-10-17).
7. **Task 016:** the owner agrees to a deploy. It redeploys the commit already live, so no product code changes (see task 016).
8. **Task 024** uses a uac-r2 sample (up to 30 issues).
9. **`ci-cd-unit-test-remediation-r1` is no longer active.** Don't rely on it. D-1 becomes owner approval of the Router change (task 051).
10. Checkpoint decisions are made when they arrive.
11. Target Date stays blank.
12. Board Status stays Active (already set).

## 2026-10-18 — Parent Epic {#epic}

#429 DevOps & Code Quality (owner, at `/project-pipeline` Step 4.2).

## 2026-10-17 — No messaging guidance text {#no-guidance}

The owner observed that Claude Code's out-of-the-box cross-session messaging already finds the right projects, and was concerned that this project would constrain it. The decision: the project adds no text on when, whether, how often or in what form to message. That includes the review's proposed permissive paragraph, because any guidance is a nudge.

What remains is mechanics only: settings, addresses, version floor, owned-area lookup. Supersedes the rev-1 spec bullets ("a message is a doorbell", "detection is a floor") and the review's §A paragraph.

## 2026-10-17 — Phase 0 measured checkpoint {#phase0-gate}

The sequence is: baseline → apply FR-00 only → re-measure after 14 days. The checkpoint passes when:
- cross-session sends don't drop;
- unprompted sends don't shift toward item-prompted ones;
- held, expired and misaddressed notices are gone.

Phases 3–5 wait for it. Phases 1–2 don't touch messaging and may proceed.

## 2026-10-17 — Registry in README frontmatter {#registry}

Supersedes C-2 (INDEX.md columns). Per-project frontmatter is parseable, owned by each project, and has no shared-file contention. `projects/INDEX.md` isn't edited for this.

## 2026-10-17 — Autonomous acknowledgement {#ack}

The owning project's session applies `ack` after reviewing (owner: "we want autonomous"). A later critical-area push removes the label (assumption A-4), so an autonomous ack always covers the code that merges.

## 2026-10-17 — Naming spike

See `notes/naming-spike-2026-10-17.md`:
- `--name` gives a stable address across restarts;
- the default suffix changes every start;
- a hook's `sessionTitle` doesn't set the address (headless).

The interactive VS Code check is task 002.
