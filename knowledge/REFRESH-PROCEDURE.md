# Knowledge Base Refresh Procedure

> **Owner**: Ralph Schroeder (initial; rotate via PR amending this file)
> **Cadence**: Monthly on the first business day
> **Budget**: ~2–4 hours per refresh
> **Established**: 2026-05-14 (Phase 6 of `coding-knowledge-base-setup-r1`)

---

## Why this exists

Microsoft platform pieces in `knowledge/` move fast — repos get retired, Learn URLs rebrand, preview features ship, naming changes. Without a monthly ritual, the knowledge base drifts and the 6 knowledge-base skills start surfacing stale guidance to the agent. The initial curation found **substantial drift** in just 4 months of training-data lag (2 retired repos, 14+ moved Learn URLs, 1 product naming clarification). Without refresh, expect more.

---

## Cadence and triggers

| Trigger | Action |
|---|---|
| First business day of each month | Run the standard refresh below |
| Microsoft ships a notable platform change between refreshes (new product, major API change, repo migration) | Append an `Interim update` entry to `REFRESH-LOG.md` immediately; formalize at next monthly refresh |
| A 404 surfaces during normal development work | Log `GAP:` line in `REFRESH-LOG.md`; resolve at next refresh |
| A new topic warrants curation (e.g., new Microsoft AI service Spaarke depends on) | Schedule as a standalone project, not as part of monthly refresh |

---

## Monthly refresh — standard procedure

### Step 1: Open `REFRESH-LOG.md` and start the entry

```markdown
## YYYY-MM-DD — Monthly refresh

**Refresh duration**: HH:MM start → HH:MM end
**Refresher**: <name>
**Topics refreshed**: <list>
**Gaps logged**:
**Interim updates incorporated**:
```

### Step 2: For each topic — re-clone, diff, update

For each of the 11 topics in `knowledge/<topic>/`:

1. **Re-clone the source repos** listed in that topic's `SOURCE.md`. Use shallow clone: `git clone --depth 1 <url> c:/tmp/<repo-name>`.
2. **Capture the new commit SHA**: `cd c:/tmp/<repo-name> && git rev-parse HEAD`.
3. **Diff against curated copies**:
   ```bash
   # For each curated sample dir, compare with the fresh clone:
   diff -rq knowledge/<topic>/<sample-name>/ c:/tmp/<repo-name>/<original-path>/
   ```
4. **Inspect the diff**:
   - File added upstream that's worth curating? Copy it; update `SOURCE.md`.
   - File modified upstream? If the change reflects a meaningful pattern shift, update the curated copy. If it's cosmetic (whitespace, comments), skip.
   - File deleted upstream? Note in `SOURCE.md` GAPs; consider removing from curated set if the pattern is obsolete.
5. **Re-fetch reference docs** under `knowledge/<topic>/docs/`:
   - For each doc file, re-fetch its `source:` URL (from YAML frontmatter).
   - If the URL 404s, find the canonical replacement (`gh search`, web search). Update frontmatter; log the URL change.
   - Update `fetched:` date.
6. **Update `SOURCE.md`** with new commit SHAs, new fetch dates, any newly-included or removed files.
7. **Update `NOTES.md`** — usually no change unless a platform shift invalidates a Spaarke pattern. If so, flag for senior-engineer review (add a `> ⚠️ REVIEW: <reason>` callout under the affected section).
8. **Delete temp clone**: `rm -rf c:/tmp/<repo-name>`.

### Step 3: Look for new patterns and topics

Quick scan during refresh:

- Have any **new Microsoft platform pieces** emerged that Spaarke might depend on? (e.g., new Azure AI service, new Copilot extensibility surface, new MCP capability). If yes, scope a standalone project — not part of monthly refresh.
- Have any **existing topics** seen so much drift they need rewriting (not just patching)? Promote to a standalone refactor project.

### Step 4: Verify the 6 skills still work

Quick sanity check (don't repeat full Phase 5 verification every month):

- For each skill in `.claude/skills/{mcp-tool-handler, declarative-agent, foundry-agent, dataverse-mcp-usage, spe-integration, widget-design}/SKILL.md`:
  - Verify every file path the skill references still exists (`Glob` or `Test-Path` each path mentioned in Step 1 of the skill).
  - If a path moved during refresh, update the skill.

### Step 5: Close the entry in `REFRESH-LOG.md`

Fill in:
- Total topics refreshed (target: 11)
- Topics with substantive changes (rare; usually 1–3 per month)
- Topics with cosmetic changes only
- New GAPs logged
- Interim updates that were incorporated
- Skill paths that needed fixing

### Step 6: Commit

```bash
git add knowledge/ .claude/skills/
git commit -m "chore(knowledge): monthly refresh YYYY-MM-DD"
git push
```

---

## Sign-off

- **One engineer owns each refresh.** Default: Ralph Schroeder. Rotate by amending the Owner field at the top of this file via PR.
- **Budget**: ~2–4 hours for the standard refresh. If a refresh runs significantly over, log it in `REFRESH-LOG.md` and scope the overrun as a standalone project (don't compress).
- **No refresh in a month**: log "no refresh this month — reason: <X>" in `REFRESH-LOG.md` to keep the log continuous.

---

## When a topic needs rewriting (not patching)

Signals:
- The directive's primary source repo has been retired or renamed (multiple times now — see batch 1, 2, 3 gaps)
- Major Microsoft Learn URL tree rebrand affecting half or more of the topic's docs
- A new authoritative repo emerges that supersedes the current curated source
- The topic's `NOTES.md` is invalidated by a platform shift (e.g., a new approval-mode primitive replacing `set_approval_mode("prompt")`)

In those cases: open a standalone project (`projects/knowledge-refresh-<topic>-rN/`) following the same pattern as `coding-knowledge-base-setup-r1`. Don't pile rewrites into a monthly refresh.

---

## Pre-flight checklist for the refresher

Before starting:

- [ ] `gh` CLI authenticated (`gh auth status`)
- [ ] Network reachability to github.com, learn.microsoft.com, raw.githubusercontent.com
- [ ] `c:/tmp/` writable; >1 GB free
- [ ] Working tree clean (don't mix knowledge refresh with feature work)
- [ ] Last refresh entry in `REFRESH-LOG.md` reviewed (catch up on interim updates)
- [ ] Calendar block: ~3 hours uninterrupted (avoid context-switching mid-refresh)
