# Defer / Issue Tracking — {project-name}

> **Source of truth** for deferred work + newly-discovered issues in this project.
> Each entry has a paired GitHub Issue. See `/project-defer-issue-tracking` skill for the protocol.
>
> **Rollup view**: `gh issue list --label {project-name}` (visible to whole team via portfolio board)
> **CLAUDE.md §11 rule**: every entry MUST name a concrete behavior or contract that fails without it. "For future flexibility" / "improve testability" / "separation of concerns" = NOT a valid deferral reason — refuse to file.

---

## Open (in priority order)

<!-- DEF-XXX entries (deferred scope) AND ISS-XXX entries (issues uncovered) interleave here, sorted by urgency: now > next-round > someday -->

### DEF-{NNN} — {Title}

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | {now / next-round / someday} |
| **Filed** | {YYYY-MM-DD} |
| **Source** | {What surfaced it — PR # / commit / spec section / telemetry query / etc.} |
| **GitHub Issue** | {URL} |

**Description**

{1-3 paragraphs. What it is. Why it matters. Concrete failure mode that proves it's real (NOT "for future flexibility").}

**Entry-points**

- {file path : line — paste-runnable}
- {OR command — paste-runnable}
- {OR KQL query — paste-runnable}

**Suggested fix** (if known)

{1-2 sentences. Hypothesis only. Reviewer should validate.}

**Estimated effort**: {hours / days, or "unknown — needs spike"}
**Blockers**: {bullet list or "none"}
**Related**: {ADR pointers, related issue numbers, sister project links}

---

### ISS-{NNN} — {Title}

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | {now / next-round / someday} |
| **Filed** | {YYYY-MM-DD} |
| **Source** | {What surfaced it} |
| **GitHub Issue** | {URL} |

**Description**

{Same structure as DEF entry above}

**Entry-points**

- {paste-runnable}

**Suggested fix** (if known)

{1-2 sentences or "none yet — needs investigation"}

**Estimated effort**: {hours / days, or "unknown"}
**Blockers**: {bullet list or "none"}
**Related**: {pointers}

---

## In Progress

<!-- Entries that someone is actively working on. Move here from Open when work starts. -->

*None.*

---

## Closed (Done / Won't Fix / Superseded)

<!-- Move closed entries here with a one-line outcome + PR/commit link. Keep the closing rationale visible. -->

*None.*

---

## Notes

- IDs are sequential per kind: DEF-001, DEF-002, ... ISS-001, ISS-002, ...
- ID never gets reused after closure — preserves traceability.
- When a Closed entry is reopened (rare), file a NEW entry referencing the old ID — don't reopen the original.
- Bulk operations: see `/project-defer-issue-tracking` skill, "Status lifecycle" section.
