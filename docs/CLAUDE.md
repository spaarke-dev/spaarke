<!--
Maintainer notes (stripped before Claude reads this file): loads when Claude reads a file under docs/. Keep it a
router; the catalogue is docs/INDEX.md and the load order is root CLAUDE.md §14 — do not copy either here.
Previous version: .claude/archive/2026-10-07/modules/docs.CLAUDE.md
-->
# docs/ — how to use this folder

**Code is the source of truth; docs lag.** When a doc disagrees with `src/`, the code is right and the doc gets fixed (root §2). Read code and `.claude/patterns/` (index: `.claude/patterns/INDEX.md`) / `.claude/adr/` first; come here for the *why* and for procedures.

- **Find a doc:** [`docs/INDEX.md`](INDEX.md) — the full catalogue. Load order across layers: root `CLAUDE.md` §14.
- **Folders:** `architecture/` decisions and rationale · `standards/` cross-cutting coding standards · `guides/` operations (deploy, configure) · `procedures/` development workflow · `data-model/` Dataverse schemas · `adr/` full ADR history (the concise rules are in `.claude/adr/`) · `deployment/`, `assessments/`, `enhancements/`, `notes/`, `product-documentation/`, `screenshots/`.

| Task | Primary source | Then |
|---|---|---|
| Implement a feature | Code + `.claude/patterns/` + `.claude/adr/` | `docs/architecture/` for the why |
| Deploy | The deploy skill (`.claude/skills/*-deploy/`) | `docs/guides/` |
| Understand an ADR | `.claude/adr/ADR-XXX-*.md` | `docs/adr/ADR-XXX-*.md` for full context |
| Debug | Code first | `docs/guides/` |
| Change architecture | `docs/adr/` (full versions) | `docs/architecture/` for current decisions |

When you change behaviour, update the doc that describes it in the same PR.
