# current-task.md conversion — items to verify (2026-10-06)

`current-task.md` was converted from a 483 KB stacked journal (27 sessions) to a current-state file, per the repo procedure change in `.claude/skills/context-handoff/SKILL.md` "State, not history".

- **Nothing was deleted.** The original is at `current-task-archive-2026-10-06.md` in this folder, byte-identical.
- **Current state** was taken from checkpoint #18 (commit `657dbc9de`).
- **Standing items** were moved to the project `CLAUDE.md` "Standing directives & gotchas".

The items below could not be classified confidently as current or superseded. Line numbers refer to the ARCHIVE. Resolve them when convenient: move into `CLAUDE.md` / notes, or mark as no longer applicable.

## Stale content inside the #18 block (corrected in the new file — confirm)
- L75 "25 tasks are 🔄 [wip]" — TASK-INDEX shows 15; the new file uses the measured list.
- L79–82 integration contents / merging — integ consumed by #1312; dropped.
- L84–102 "Dev live steps — Remaining" — all shown done in lines 17–39; dropped.
- L101, L168 test user `uac.child.user@demo.spaarke.com` — superseded by testuser1 (no known password). Does the Session-A CIAM sign-in still need the owner?
- L102, L153 "tell word-add-in-r1 once 161 is deployed" — 161 is ✅; no record the note was relayed.
- L48–63 PR #1314 described as open — merged as `891cfd9a3`. The local branch `fix/uac-r2-deploy-script-fixes` still exists (housekeeping).
- L111 "114's licence proxy" owed — possibly answered by round 67.
- L106 #1293 draft — status not re-verified.

## Older blocks
- L343, L376 continuation-script rule — conflicts with memory `workflow-agent-messaging` (`resumeFromRunId`). Kept in CLAUDE.md marked *(verify)*; reconcile in one place.
- L1689, L1889, L2484 "schema work is CODE + DOCS ONLY; live creation is an operator step" (2026-09-04, BINDING) — since 2026-10-02 the main session applies live dev schema with per-round owner approval. Superseded, or still binding outside approved batches?
- L1739, L1932, L2065 "Dataverse MCP is DOWN" — MCP tools are available now; probably fixed.
- L1748, L1933, L2074 "Python is a Microsoft Store stub" — superseded in practice (excluded).
- L1685, L1941 `gh` token lacks `read:project` — possibly fixed by the 2026-10-04 portfolio work.
- L1126–1143 word-add-in coordination (`OfficeService.cs` synthetic-job deletion; `AssociationType` ordinal 3 burned) — possibly moot after #1315.
- L2043–2046 `Spaarke Demo` default team holds System Administrator; root default team must stay role-free — current state unknown.
- L1147, L1180–1183 open owner items from the D-12 era (Power BI F-SKU pool, M365 Copilot agent, Redis Standard bar, Trivy) — still this project's to track?
- L1714 / L1908 "B3: record the `section-break-flattened` acceptance in spaarkeai-compose-r8 — not yet done".
- L1680 #974 Dataverse-side expiry guard — open/closed not re-checked.
- Merge style: L604/L612/L660/L677 say "merge commit, keep the branch", but #1312/#1314 were squash-merged. Which is current?
- Housekeeping (unchanged by the audit): undeletable `wf_*` folders (L395, L623, L664); ~169 agent worktrees registered under `C:/code_files/spaarke/.claude/worktrees`; consumed worktrees `C:\wt4i`, `C:\wtD`, `C:\wtO`, `C:\wv*`/`C:\wvs*`.

---

# Independent audit (2026-10-06, read-only, after the conversion)

Verdict: the conversion is mostly safe to rely on. Nearly every standing rule, owner directive and trap is in the new files, memory, or a per-task note or POML. The gaps below are things that are still owed and are now only in the archive. Each was already buried deep in the old journal (lines 650–4,760), so recovery wasn't reaching it before either. Line numbers refer to the ARCHIVE.

## Missing: still live, only in the archive
1. **HIGH — 141 and 145 hand-offs to cpo-r1 never delivered** (L652, L616 "resend the peer message FIRST").
   - What's missing: INCOMING-141 and INCOMING-145 are still only in our `notes/handoffs/`, marked "To deliver". Nothing in cpo-r1 (worktree, origin/master or #1094) mentions the workforce tenant list or H7b.
   - Effect: every new customer environment gets an empty `WorkforceIdentity__CustomerTenantIds` (all workforce-customer identities denied) and no secure owner team or role (secure records refused).
   - Action: deliver them (owner relays, or a GitHub comment on #1094). Add to current-task open follow-ups and a CLAUDE.md coordination bullet.
2. **MEDIUM — `POST /api/v1/external-access/invite-and-grant` 500 never closed** (L930–943, L1187).
   - Two 500 paths: onboard failed, or onboarded but grant failed. Nothing records a fix or a diagnosis.
   - The owner's CIAM session goes through invites. Check status first.
   - When reproducing, use an already-invited email: a fresh address sends a real invitation.
3. **MEDIUM — live smoke checks promised and never run** (L1795–1797, L3090–3094).
   - **098:** call `set-record-share-expiry`, then read back `sprk_expiresdate` and confirm the exact date (TimeZoneIndependent via `BulkUpdateAsync`).
   - **R15:** create + read through `POST /api/v1/external/projects/{id}/documents`. The field names and the case-sensitive `sprk_Project@odata.bind` have never run against Dataverse. The owner said "before the external SPA deploys"; it deployed 2026-10-06, and gate 23 has since locked `sprk_graphdriveid`, which this route writes.
   - Add both to the 099 gate or the owner screen-session list.
4. **MEDIUM — regression test owed for the oid fix in the search authorization filters** (L3821–3835).
   - `SemanticSearchAuthorizationFilter` / `RecordSearchAuthorizationFilter` and their handlers need a test with a real-shape principal: long-form `oid` plus a `NameIdentifier` that differs from it.
   - A perturbation left 45 tests green. Add to `defer-issues.md` (090).
5. **LOW — no `sprk_noaccessentry` table means every read is denied** (L2490). Provisioning must create the table with or before the deploy. Add to the cpo-r1 INCOMING note (item 1).
6. **LOW — #969 residuals never disposed of** (L1712): `sdap-ci.yml:799` calls `listComments` unpaginated; the `createComment` branch may be broken; two redundant `continue-on-error` full-suite runs remain. Either a hand-off or an explicit "not ours".
7. **LOW — client build traps for batch-5 UI tasks** (L2732, L3000, L2849, L3062).
   - Build `@spaarke/sdap-client` (`dist`) before typechecking anything that depends on it.
   - Run ui-components jest from inside the package (`--rootDir` from the root gives 232 false failures).
   - `ApiError` has `statusCode` (not `status`) and no `detail`.
   - An injected `authenticatedFetch` has two production behaviours: one throws, the other returns the raw response.
   - Add to CLAUDE.md "Build, test and measurement".
8. **LOW — bare 401 from `TypedResults.Unauthorized()`** (L4761): no ProblemDetails (ADR-019). Wrap-up candidate for a 090 note.

Also: `notes/batch4-integration-steps.md` has unticked items that nothing points to: "One F3 check", the 6 external-SPA `tsc` errors, the 160 A5 note, and the CHANGELOG entries. Reconcile that file or link it from current-task.

## Wrongly carried: stale or incorrect in the new files
- **CLAUDE.md "Count TASK-INDEX by the ASCII `[open]`/`[done]` tokens":** now wrong. The 11 rows completed 2026-10-06 have ✅ but no token (003, 132, 133, 148, 149, 156, 158, 159, 160, 161, 167). Re-token those rows or qualify the rule.
- **current-task "Completed today"** omits 161 (its ✅ is in TASK-INDEX).
- **current-task "Work branch … Clean, 0 unpushed":** true but misleading. The branch is **309 commits behind origin/master** and lacks the batch-4 code (e.g. master removed `/healthz/dataverse/doc/{id}`). Merge master before building or doing task work here.
- **Older CLAUDE.md lines, not from the conversion:**
  - The NFR-05 gate names the `Secure Projects` BU, but task 121 renamed it `Secure Record`.
  - "~44.96 MB" baseline: root §10 says measure against a fresh master build.

## Cross-project item raised by the cpo-r1 audit (relevant here)
`notes/task-165-admin-surfaces.md` §13.9(b) plans `Repair-SpeConfigSecretName.ps1 -MintClientSecret` on `bfac7f6e` (Spaarke SPE Model 1 Owner). cpo-r1's owner decisions D16 and T250 say there must be NO secret-based Model 1 config, and the app uses MI-FIC. Reconcile with cpo-r1 before running it.

---

# Resolution (main session, 2026-10-06, from live knowledge of the project)

## Stale content inside the #18 block
- **"25 wip":** CORRECTED. 15 are wip (listed in `current-task.md`).
- **Integration / "Dev live steps — Remaining":** NOT APPLICABLE (consumed and done).
- **Test user:** CORRECTED. Gates run as testuser1. The CIAM session still needs the owner (CIAM sign-in), and a second non-admin login is still needed. Both are listed in `current-task.md`.
- **The word-add-in-r1 note on 161:** NOT APPLICABLE. The relay text was given to the owner on 2026-10-06 (peer messages go through the owner).
- **#1314 "open":** CORRECTED. Merged as `891cfd9a3`, and its branch was deleted on origin.
- **114's licence proxy:** RESOLVED by round 67 (the external flag on Restricted records; no licence check).
- **#1293:** VERIFIED. Still an open DRAFT; listed in `current-task.md`.

## Older blocks
- **Continuation-script vs `resumeFromRunId`:** RECONCILED. Both hold: use `resumeFromRunId` for the same script, and a continuation script when a pooled/DAG call order varies. The CLAUDE.md bullet is rewritten and the *(verify)* removed.
- **"Schema work is CODE + DOCS ONLY":** SUPERSEDED for dev. Since 2026-10-02 the main session applies live dev schema with per-round owner approval; production schema is provisioning's job (cpo-r1). Not restored.
- **"Dataverse MCP is DOWN":** NOT APPLICABLE (the tools work now).
- **"Python is a Store stub":** NOT APPLICABLE (python runs).
- **`gh` token lacks `read:project`:** NOT APPLICABLE. Project fields were updated on 2026-10-06.
- **Word-add-in coordination (synthetic job, AssociationType ordinal 3):** NOT APPLICABLE. On master the ordinal is pinned in code with an explicit "do not reclaim 3" comment, and the synthetic GUID is absent from `OfficeService`.
- **`Spaarke Demo` team holds System Administrator; the root default team stays role-free:** COVERED by round 66's peer note (a dev artifact under round 5; the census flags it). G1 re-observed it on 2026-10-06.
- **D-12-era items (Power BI F-SKU pool, M365 Copilot agent, Redis Standard, Trivy):** NOT THIS PROJECT'S. D-12/D-13 remediation is cpo-r1's (CLAUDE.md, 2026-09-28).
- **B3 `section-break-flattened` acceptance (compose-r8):** NOT THIS PROJECT'S. It is a compose-r8 owner accept/decline (root CLAUDE.md, ADR-049 row).
- **#974:** VERIFIED. Still open; listed in `current-task.md`.
- **Merge style:** CORRECTED. PRs squash-merge (new CLAUDE.md bullet).
- **Housekeeping:** listed in `current-task.md`. `C:\wtD` is kept on purpose (deploy backups).

## Independent audit
1. **HIGH, 141/145 hand-offs:** DELIVERED as a comment on #1094 (issuecomment-6028915048). Both notes are on master. The comment also adds the batch-4 schema check (audit item 5). Tracked in CLAUDE.md and `current-task.md` until cpo-r1 acknowledges.
2. **invite-and-grant 500:** RESTORED to `current-task.md` (check before the CIAM session; reproduce with an already-invited email). No live action taken.
3. **098 / R15 smoke checks:** RESTORED to `current-task.md` (owed API checks).
4. **Search-filter oid regression test:** RESTORED to `defer-issues.md` as ISS-032 (090).
5. **No `sprk_noaccessentry` table means every read is denied:** DELIVERED in the #1094 comment (the H6 solution-import check).
6. **#969 residuals:** MARKED not ours (the CI lane, #969 open); recorded as ISS-034.
7. **Client build traps:** RESTORED to CLAUDE.md "Build, test and measurement".
8. **Bare 401:** RESTORED as ISS-033 (090).
- **`batch4-integration-steps.md` unticked items:** linked from `current-task.md` (reconcile at 090).

**Wrongly carried:**
- **The `[open]`/`[done]` rule:** CORRECTED. The 11 rows from 2026-10-06 were re-tokened, and 19 older rows using `[completed]` were normalized to `[done]` (116 ✅ = 116 `[done]`; drift check clean). The CLAUDE.md rule now says every ✅ row carries `[done]`.
- **161 omitted from "Completed today":** CORRECTED.
- **The work branch is 314 behind master:** NOTED prominently in CLAUDE.md and `current-task.md`.
- **The `Secure Projects` BU name:** CORRECTED to `Secure Record`.
- **The "~44.96 MB" baseline:** CORRECTED to "measure against a fresh master build" (36.13 MB on 2026-10-06).

**Cross-project, 165 §13.9(b) `-MintClientSecret` on `bfac7f6e`:** RECONCILED. It must NOT be run: cpo-r1 D16 (the SPE owning app uses MI-FIC, with no certificate and no secret) and ADR-028 A4 (no new secrets). A CLAUDE.md 🔴 bullet was added, and a comment posted on #1313, which owns the secret-less config. The 165 note itself is on master only and gets annotated at the next master merge.
