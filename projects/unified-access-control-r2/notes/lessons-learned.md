# uac-r2 — lessons learned (task 090, 2026-11-11)

Short list for the next access-control project. Sources: the project `CLAUDE.md` §6 gotchas, `tasks/TASK-INDEX.md`, the
batch live-gate notes and the wrap-up decision table.

## Product and design

1. **Enumeration undercounts; a forcing function finds the rest.** The ungated-route count was wrong every time it was
   taken by hand (~15 estimated → 22 → +2 → +5). Task 074's ArchTest found three more holes (077, 078, 081) on its first
   run. Build the guard first, then fix what it finds.
2. **A tool finds what it was built to look for.** Task 091: a write-sink census saw 3 of 9 ungated SPE admin routes
   because the 6 read routes were invisible to it. State what an instrument cannot see next to its count.
3. **Verify the mechanism a comment names.** Several "enforcement happens elsewhere" comments were false (task 022), and a
   deleted file's defence recorded an accident as design (task 083). This wrap-up still found four comments naming a
   deleted filter (H-8a).
4. **Two keys for one decision diverge.** Authorizing against one client field and writing to another (085, 076) is the
   recurring shape. Make the authorization key and the write target the same value, derived on the server.
5. **Core vs child must stay explicit.** A matter does not inherit from its project; only child tables inherit, through the
   one-hop core-ancestor stamp. Inverting it hands every project holder every matter beneath it
   (`docs/architecture/uac-access-control.md`, "Parents and Children").
6. **Reuse the platform's own record before building a log.** The access-event table (087) was cut because Dataverse
   auditing already records grants, shares and flag changes; the open requirement is org-level auditing and retention in
   every customer environment (#2027). FR-32 replay stays a known gap (#1303).
7. **Environment work is UAT, not code.** The business-unit restructure and the browser checks were moved out of the
   tasks (round 90): a task closes on its merge and server gate, and its browser check goes on one owner table. A failed
   check is a new defect, not a re-opened task.

## Process

8. **Bound review ceremony, never fixing.** Unbounded full re-reviews produced fix rounds up to `-r6` and 50+ owner
   decision rounds. The owner's rule is now repo-wide (`task-execute` Step 9.5): re-check the fix diff and its direct
   callers, one full adversarial pass per task (two for security), and keep fixing until no F-class finding remains.
9. **Close tasks as they land.** Twelve tasks sat `[wip]`/`[open]` for weeks after merge and gate, waiting on browser
   checks, and had to be closed in one batch at wrap-up (D-1). The drift check catches POML/index disagreement, but not a
   row with no ASCII token (186, 191, 193, 194 still use emoji only).
10. **Task numbers collide across projects.** About 24 "task 093" comments existed repo-wide, only three of them ours.
    Never sweep by task number; cite the project with the number.
11. **A live gate must exercise the user's path.** Form scripts failed silently on dev for days because the gates called
    BFF routes directly while the scripts built `/api/api/...` URLs (#1488). Gate the form path, or at least the script's
    URL resolver against the real environment value.
12. **Shared machine, shared stash.** Never kill processes by name, never `git stash`, run one polling or merge job at a
    time, and re-check a PR with `git merge-tree` before merging: master moves fast and a conflicting PR gets no CI.
13. **Rebuild after every master merge.** A clean textual merge still broke the build (114 × 171); build the merged
    result before trusting it, and never run `--no-build` after an unguarded build.
14. **Keep `current-task.md` as state.** A 2026-10-06 measurement found 150, 445 and 483 KB files in three active projects, read at every task start. Rewrite it at
    each checkpoint and move standing rules to `CLAUDE.md`.
15. **The test diet needs a budget.** This project touched 935 test files (15,266 methods); a wrap-up pass can apply only
    the mechanical heuristics. Run the diet per batch, not once at the end (`notes/test-diet-report.md`).
