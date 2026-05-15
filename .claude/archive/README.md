# `.claude/archive/` — Reversibility Convention

> **Purpose**: Preserve any procedure-surface artifact (skill, settings block, workflow, CLAUDE.md section) that is removed or replaced. Every removal goes here BEFORE it is removed from its original location. `rm` from git history is never the right answer.

---

## Convention

```
.claude/archive/
  YYYY-MM-DD/          ← date the removal/replacement happened
    <original-path>    ← path mirror, e.g. .claude/archive/2026-05-14/skills/old-skill-name/SKILL.md
```

Examples:

- Removing `.claude/skills/foo/SKILL.md` on 2026-05-14 → `.claude/archive/2026-05-14/skills/foo/SKILL.md`
- Retiring a workflow `.github/workflows/old.yml` on 2026-07-01 → `.claude/archive/2026-07-01/github-workflows/old.yml`
- Replacing a CLAUDE.md section: the OLD section text goes to `.claude/archive/2026-05-14/CLAUDE.md-section-<slug>.md` with a short note at the top describing what replaced it.

---

## Retention

Indefinite. Git is the actual archive. This directory is just an obvious-to-find pointer so a future operator looking for "what happened to skill X?" can grep here instead of bisecting commits.

---

## How to restore

```bash
git mv .claude/archive/2026-05-14/skills/foo .claude/skills/foo
git commit -m "restore: foo skill — context note"
```

The original commit that did the archive is also in `git log -- .claude/archive/<date>/<path>` so you can read the reasoning if needed.

---

## When to use

- **Removing a skill** (Phase 2b destructive actions): archive the entire folder
- **Merging skills** (Phase 2b): archive the source skill's folder; the target skill keeps its history
- **Replacing the root CLAUDE.md** (Phase 3b): archive the old CLAUDE.md verbatim
- **Retiring a GitHub workflow** (Phase 4b): archive the .yml file
- **Anything else that feels destructive**: when in doubt, archive

---

## When NOT to use

- Trivial edits (typo fixes, link updates) — git history is enough
- Adding new content — archive only captures removals/replacements
- `.claude/settings.local.json` — that's user-local and gitignored

---

*Established 2026-05-14 by project `ai-procedure-quality-r1` (task 014). See [.claude/CHANGELOG.md](../CHANGELOG.md) for ongoing entries.*
