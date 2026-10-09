# Task 003 — ADR-040 amendment (path B) + ADR-039 exception (path A)

> **Date**: 2026-10-03 · **Status**: complete · **Rigor**: FULL · **Tier**: opus @ xhigh
> **Owner decision**: D-9 (2026-10-03), resolving the two tensions `design.md` section 6 left
> path-deferred on 2026-09-30.

## What changed

| File | Change |
|---|---|
| `.claude/adr/ADR-040-session-ledger.md` | New `Amendment 2026-10-03` section: sibling table, the named link, 2 new MUSTs, 2 new MUST NOTs, an explicit does-NOT-do list. Header carries an amendment banner |
| `docs/adr/ADR-040-session-ledger.md` | Parallel prose section with the rationale, the alternatives considered for the amendment itself, and the blast-radius argument. `Amended` line added to the header |
| `.claude/CHANGELOG.md` | Entry dated 2026-10-03 (root CLAUDE.md section 18 requires one for any `.claude/` procedure-surface change) |
| `projects/.../spec.md`, `design.md` | Precedent citation path corrected (see Deviations) |

**ADR-039 was NOT amended** — its resolution is a project-scoped exception recorded in `spec.md` section 6,
to be cited in the PR description. That is the negative acceptance criterion and it holds: `git status`
shows no modification to any ADR-039 file.

## The escalation trigger was evaluated and did not fire

The POML said to STOP if the amendment could not be kept narrow, specifically *"if it turns out other
consumers depend on SessionGate being the only gate ledger."* Checked rather than assumed:

- `SessionGate` (`Models/Ai/Chat/SessionLedgerEntries.cs:298`) carries `GateId`, `Kind`, `Status`, `Turn`,
  `BindingId`, `SideEffectClass`, `MissingFields`, `OutputKey`, `CreatedAt`, `ResolvedAt` — and **no
  authority, no policy version, no evidence, no confirmer identity and no record scope**. There is no
  field the amendment competes for.
- **`sprk_emailreviewlog` already ships as a durable, append-only, per-decision authority record**, written
  by the email proposal-apply path. So a durable sibling of the gate ledger exists in production *today*.

That second point is what makes the amendment narrow: it **names an existing pattern** rather than
introducing one, so **no consumer depended on `SessionGate` being the only record of a decision — it
already was not.** Blast radius is documentation, not migration.

## Deviations from the POML

1. **Step 5 found a defect, not just a wording check.** The POML asked to verify the ADR-039 exception
   wording was concrete rather than boilerplate. The wording *is* concrete — it cites a specific prior
   owner decision — but the citation pointed at **`notes/041-rule-store-decision.md`, which does not
   exist in this project**. The real record is
   `projects/spaarke-notification-spine-r1/notes/041-rule-store-decision.md`. Corrected in both `spec.md`
   and `design.md`.

   This mattered more than a broken link: `project-pipeline` Step 1.7 and `code-review` Step 6.6 both
   check that a path-A rationale is concrete, and a citation to a nonexistent file reads as fabricated
   support. The real note is in fact **stronger** than the spec claimed — it records an explicitly
   *accepted* ADR-039 exception with three stated reasons, dated 2026-07-22, for `sprk_communicationrule`.

2. No other deviation. Steps 1-4, 6, 7 executed as written.

## Sequencing obligation (acceptance criterion 6)

The amendment **must merge before or alongside task 031** (the nightly re-evaluating `IScheduledJob`).
TASK-INDEX.md records this on both rows: 003 carries *"Must merge before/alongside 031"* and 031 lists
`003` in its deps. Both still read that way after this task.
