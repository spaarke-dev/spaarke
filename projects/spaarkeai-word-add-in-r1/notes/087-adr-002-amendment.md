# Task 087: ADR-002 amendment (path B). A platform-native declarative mechanism may own a write-path invariant

> **Task**: `tasks/087-adr-002-platform-native-invariant-owner.poml` · STANDARD · opus @ high · main session
> **Date**: 2026-10-03

## 1. Why

Task 076 gave Matters and Projects an interim number from **Dataverse's platform autonumber**. That rule is
write-path invariant I-11: the number is the record's primary name. But ADR-002 WP-1 said every invariant's owner
is "a server-side component in the BFF write path".

Both Step 9.5 reviewers flagged the conflict. The owner chose, on 2026-10-03, ***"A now, B as its own task"***:
- task 076 recorded a project exception (path A);
- this task amends ADR-002 (path B).

## 2. Owner sign-off on the wording (CLAUDE.md §6.5)

The proposed wording was shown to the owner in full (WP-1, the new Permitted row, the formula/rollup
reconciliation, and the root CLAUDE.md line). The owner replied on **2026-10-03**:

> *"approved--but let's not add business rules in this revision."*

So business rules are **excluded**. They stay Permitted for defaults and UX, and they are explicitly **never** an
invariant owner. The wording offered business rules as an option, recommending against it; the owner declined.

## 3. The rule, as amended

> **WP-1** — Every write-path invariant has exactly one owner, listed in the invariant registry: **a server-side
> component in the BFF write path**, or **a platform-native declarative mechanism** that meets ALL three conditions:
> **(a)** it is Dataverse metadata, so no Spaarke code runs (e.g. an autonumber column format, an alternate key);
> **(b)** the platform applies it on **every** create/update, whoever writes (so it needs no WP-5 fix-up);
> **(c)** a scripted per-environment check (`-Verify`) proves it is configured, and the registry row names that check.
> Plugins, low-code plugins, Power Automate flows, webhooks, business rules and client code **never** own an invariant.

**Permitted (not plugins)**:
- **Autonumber columns** are added. They may own an invariant under (a)–(c). The `-Verify` is mandatory, because
  the seed is per environment and not carried by a solution import.
- **Alternate keys** may own a uniqueness invariant.
- **Formula/rollup columns** compute on read, so they are not a stored invariant and need no owner.
- **Business rules** are never an owner.

**Also resolved**: an existing contradiction. `plugins.md` said a formula column needs "no owner", while the full
ADR said formula columns are "not a substitute for WP-1 owners". Both now give the same answer.

## 4. What changed

| File | Change |
|---|---|
| `docs/adr/ADR-002-no-heavy-plugins.md` | Status/Updated; WP-1; the Permitted table (autonumber row; alternate keys; formula/rollup and business rules split); Preferred Patterns; Summary; Amendment History row (path B) |
| `.claude/adr/ADR-002-thin-plugins.md` | Status/Last Updated; WP-1; Permitted list; Preferred Patterns |
| `.claude/constraints/plugins.md` | Core principle; the MUST rule for owners; decision table (adds a "generated sequential identifier" row; the formula row reconciled); Permitted table |
| Root `CLAUDE.md` | The "Dataverse write path" pointer row: "ONE BFF server-side owner" → "ONE server-side owner: BFF code, or a platform-native declarative mechanism such as autonumber … (ADR-002 amended 2026-10-03)" |
| `.claude/CHANGELOG.md` | Entry (CLAUDE.md §18) |
| `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` | L2 box names autonumber; the §5 intro names platform-native owners; the I-11 row's exception note becomes "owner per ADR-002 WP-1 as amended", with the check named (`Set-RecordNumberingSchema.ps1 -Verify`) |
| `projects/spaarkeai-word-add-in-r1/spec.md` | ADR Tensions row: path B done, exception closed |
| `projects/INDEX.md` | This project's Skill Directives flag → Y |

**Escalation trigger 2 (concurrent edits)** did not fire. No open PR touches ADR-002, `plugins.md`, root `CLAUDE.md`
or the changelog, apart from this project's own PR #1110 (the write-path doc).

## 5. Acceptance criteria

| # | Criterion | Status |
|---|---|---|
| 1 | Full and concise ADR-002: the WP-1 allowance with three conditions, autonumber Permitted, date and origin | ✅ |
| 2 | The `plugins.md` contradiction is gone | ✅ |
| 3 | Root `CLAUDE.md` row and `.claude/CHANGELOG.md` | ✅ |
| 4 | Write-path doc: §5 intro, L2 description, I-11 row | ✅ |
| 5 | Spec ADR Tensions: path B done | ✅ |
| 6 | Owner sign-off recorded | ✅ §2 |
| 7 | ArchTests green, `WorkloadPlacementDocDriftTests` included | ✅ **345 / 345** |
