# Note: ADR-025 numbering collision discovered during task 012 (A-2a)

> **Author**: Sub-agent (task 012 execution)
> **Date**: 2026-05-26
> **Severity**: Low (cleanup) — main session decision required
> **Status**: Resolved (PaneEventBus claims ADR-025 per project plan; orphan icon ADR-025 flagged for cleanup)

---

## What was found

During task 012 (A-2a, author ADR-025 PaneEventBus), an existing file was discovered:

- `.claude/adr/ADR-025-icon-library-and-deployment.md` — concise-only ADR titled "Icon Library and Deployment Strategy", dated 2026-02-17

Status of the existing entry:
- ✅ The concise file exists
- ❌ NO paired `docs/adr/ADR-025-*.md` full form (the file does not exist in `docs/adr/`)
- ❌ NOT registered in `.claude/adr/INDEX.md` — the INDEX table jumps from ADR-023 (Choice Dialog) to ADR-026 (Code Page Build Standard), skipping ADR-025 entirely
- The icon ADR contents reference `projects/spaarke-navigation-icons/GUIDE.md` (an existing operational guide)

## Project plan intent

`projects/spaarke-ai-platform-unification-r4/spec.md` (DR-04) and `plan.original.md` §4 Phase 1 A-2 explicitly assign **ADR-025 = PaneEventBus**. The plan was authored 2026-05-25 with awareness of the existing ADR file, indicating the operator's intent to repurpose the number.

This is reinforced by:
- `.claude/adr/INDEX.md` does NOT list ADR-025 (icon ADR is effectively unregistered)
- No `docs/adr/ADR-025-*.md` exists (icon ADR has no full form)
- Multiple R4 task POMLs reference ADR-025 = PaneEventBus (012, 042, 043)
- R4 `CLAUDE.md` "Other applicable ADRs" lists "ADR-025 (PaneEventBus) — NEW per A-2"

## Decision taken in task 012

I proceeded with the project plan's intent:
- Wrote `docs/adr/ADR-025-pane-event-bus.md` (full form, 245 lines) — the canonical full ADR
- Drafted the concise form for `.claude/adr/ADR-025-pane-event-bus.md` (returned in task report; main session applies)
- Drafted the `.claude/adr/INDEX.md` entry (returned in task report; main session applies)

## Action items for main session

When main session applies the concise form + INDEX entry, it should ALSO decide on the orphan icon ADR:

**Option A (recommended)**: Renumber the icon ADR
- The icon-library decision is real and valid; it just has the wrong number
- Move `.claude/adr/ADR-025-icon-library-and-deployment.md` → `.claude/adr/ADR-030-icon-library-and-deployment.md` (next free number)
- Register ADR-030 in INDEX.md
- Update any cross-references (grep `ADR-025` in repo — none expected to point to the icon ADR, since INDEX never registered it)

**Option B**: Archive the icon ADR
- If the icon-library decision is no longer in force (project archived, superseded, etc.), move the file to `.claude/archive/<date>/ADR-025-icon-library-and-deployment.md`
- Decision requires checking with project owner — the icon ADR references `projects/spaarke-navigation-icons/GUIDE.md` which DOES exist

**Option C**: Delete the icon ADR
- Only if option B's archive isn't desired and the operator confirms the decision is dead

## Recommendation

Option A. The icon ADR has substantive content and a paired operational guide. Renumbering to ADR-030 preserves the work and clears the number for PaneEventBus per project plan. INDEX update is one line.

## Verification after main session applies fixes

```bash
# 1. PaneEventBus concise form exists at correct path
ls .claude/adr/ADR-025-pane-event-bus.md

# 2. Full form exists (this task created it)
ls docs/adr/ADR-025-pane-event-bus.md

# 3. INDEX has ADR-025 entry
grep "ADR-025" .claude/adr/INDEX.md

# 4. No orphan ADR-025 icon file (if main session chose option A or C)
ls .claude/adr/ADR-025-icon-library-and-deployment.md
# Expected: file not found (or moved to ADR-030 / archive)

# 5. If option A taken: icon ADR is now ADR-030
ls .claude/adr/ADR-030-icon-library-and-deployment.md
grep "ADR-030" .claude/adr/INDEX.md
```

---

*This deviation is logged per task 012 step 8 ("Document deviations"). The deviation is from the implicit assumption in the project plan that ADR-025 was a free number — the existing orphan file was not anticipated. The deviation is harmless because the orphan was unregistered in INDEX and unpaired in docs/adr/, but cleanup is the responsible follow-up.*
