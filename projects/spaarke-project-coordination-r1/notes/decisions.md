# Decisions — spaarke-project-coordination-r1

Full rationale for the binding one-liners in `CLAUDE.md` §3. Newest first.

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
