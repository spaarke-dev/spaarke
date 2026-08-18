# Smart To Do R5 — Lessons Learned

> Written at the (partial) wrap-up, 2026-08-17. The project is **substantially complete with documented deferrals** — NOT marked Complete (see README + TASK-INDEX). Ribbon expansion (051/052), header-hide (032), and the Playwright real-env run (041) are deferred/pending.

## Reusable patterns

### 1. Absorbing a stale/abandoned PR into a new project's prerequisite task (FR-01 / #508)
PR #508 (`fix/events-smarttodo-components-package-boundary`) had drifted and conflicted. Rather than reviving/merging it, R5 **re-applied its fix on current master as task 001** (the prerequisite of the Kanban hoist) and closed #508 as superseded at wrap-up. Pattern: when a stale PR's *intent* is still valid but the branch is unmergeable, fold the change into a fresh task against current master and close the original — don't rebase a rotten branch.

### 2. Real-Dataverse smoke gate as a cross-project process artifact (FR-20 / PROC-1, task 060)
The R4 UAT-5/6 regression (a mock harness invented `sprk_contact`, which doesn't exist — real is OOB `contact`; the mock hid the entity-name bug for multiple deploy rounds) became a **documented pre-merge gate** in `push-to-github` Step 1.7: any Dataverse-querying widget change must be exercised with ≥1 real create+read before merge. Advisory (ask-first), not a CI block — mirrors Steps 1.5/1.6. Lesson: a mock passing proves nothing about real Dataverse's schema; encode that as process, not tribal knowledge.

### 3. PCF barrel imports drag pdfjs — deep imports are the fix (RegardingResolver, this session)
RegardingResolver's webpack build failed because it imported the **root** `@spaarke/ui-components` barrel (→ SprkChat → `pdfjs-dist/pdf.mjs`, which the PCF webpack can't transform). Every other PCF uses per-module deep `dist/*` imports (ADR-012). The **true fix** was architectural conformance (deep imports), not a webpack shim — bundle dropped 2.2MB→60KB. Lesson: when a PCF build chokes on an unrelated dep, look for a root-barrel import pulling the whole lib; deep-import per ADR-012.

### 4. Feature-gated reroute for a silently-broken background rule (INBOUND / TodoGenerationService)
Two nightly generation rules queried events through the composite `IDataverseService` whose `QueryEventsAsync` is a silent-empty stub → zero To Dos. Fix = reroute to the real `IEventDataverseService`, but **gate creation behind a default-OFF options flag** (dry-run logs would-create counts) so an operator validates volume/dedupe/notifications before enabling. Lesson: when fixing a silently-broken rule that will suddenly *start* producing output, land the fix correctly-routed but dormant, with an observability dry-run, rather than flipping behavior blind. Options-flag gate ≠ conditional DI registration, so no ADR-032 null-object needed.

### 5. Live-solution drift: the deployed ribbon carrier ≠ the repo's copy
The repo authored the Matter "Create To Do" ribbon fix in `spaarke_insights/…/sprk_Matter/RibbonDiff.xml`, but the **live runtime carrier was the dedicated `MatterRibbons` solution** (ribbon diffs merge across solutions; last-import-wins per button ID). The operator's 404 was fixed by editing the *live* `MatterRibbons`, not the repo copy. Lesson: for ribbon fixes, export the live solution and confirm which solution actually carries the button before editing — repo authorship can point at the wrong carrier. **Tasks 051/052 must target the dedicated `*Ribbons` solutions.**

## Deploy mechanics (hard-won, reuse)
- **systemform edits are a silent no-op via direct Web API PATCH** → use `pac` solution export→edit→import roundtrip.
- **Large webresource (Code Page 2MB+): raw PATCH truncates (~2MB)** → temp-solution roundtrip; verify copied-file size == build BEFORE import and content markers AFTER (a stale build shipped once).
- **BFF publish size is measured COMPRESSED** (44.94 MB incl PDBs) — the raw `du` size (137MB) is not the baseline; the csproj's `linux-x64` RID also makes a bare `dotnet publish` self-contained.
- **Sandbox blocks `Remove-Item -Recurse -Force`** → use `[System.IO.Directory]::Delete($p,$true)` or fresh dir names.
- **.NET relative paths resolve against the process dir, not the PS location** → the pack.ps1 zip failure; use absolute paths.

## ADR tension in effect
ADR-050 Path A exception (FR-10..14): kept the OOB `navigateTo` main form for To Do create/open (owner requires native Save/business rules), not a proprietary FormModal. The **header-hide (032) was deferred** because hiding the OOB command bar also removes Save — a genuine constraint that killed the "hide header" sub-goal.

## Process note
`projects/INDEX.md` had a `smart-todo-r5` row from initialization (SpaarkeAi=Y correctly declared) — no Step 0.5 hot-path registration gap this time.
