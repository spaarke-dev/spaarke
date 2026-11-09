# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-07 ~20:50Z (main session checkpoint). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–018, 030a, 037–040, 042c, 043, 044, 046, 046b, 046c (repo), 048, 049, 049b, 049c, 070–075. Latest: #2619 (d53d67f604, 049b), #2624 (23567b2f93, 049c, merged 18:03Z).
> **Live on dev:** BFF 22487c6537 (= master d53d67f604 with 049b, plus the MCP spike), since 20:06:34Z; UAC-r2 gate PASS. The 049c deploy is in UAC-r2's next window, with the App Insights Debug setting for RecordCreation (`Logging__ApplicationInsights__LogLevel__Sprk.Bff.Api.Services.RecordCreation=Debug`, the owner's go in UAC-r2's session) so the 021 re-measure can time each step.
> **W3 direction:** named exceptions plus guards, no core redesign in R1 (decisions.md 2026-11-06).

## Quick Recovery

| Field | Value |
|---|---|
| **Work branch** | `work/field-mapping-server-write-path-r1`, pushed; no open PR on it. It holds the 049c snapshot already on master, plus 052a merged (6a07fee080), plus docs (decisions, defer-issues DI-020/021, live checks). PRs are opened from a snapshot branch (`work/field-mapping-server-write-path-r1-<tag>`), so the work branch stays free. The snapshot branch `…-049c` is still on origin; deleting it needs the user's OK. |
| **Next PR** | 030b + 034 (`origin/task/fm-030b`, a superset of `task/fm-034`) + 052a (already in the work branch). Steps: wait for fm-030b's cleanup round → merge `task/fm-030b` with `--no-ff` (merge-tree clean) → verify-suites.sh → publish-delta.ps1 (fresh master) → send the diff to UAC-r2 → PR from a snapshot branch, auto-merge. Cite ADR-008 path A in the PR description. |
| **Running agents** | fm-030b: cleanup round (§11 answers; delete the dead `EmailAttachmentProcessor.ProcessAttachmentsAsync` with its types and the 034 behaviour tests for it; drive the lockstep theory from `SupportedSpellings`; `Association` internal set; stager refuses columns outside the set; stale doc line). fm-032: design round 2 (allow-list table, gate = secure-if-any OR No Access entry OR `SourceMayBeCopiedAsync`, re-commit once, the other conditions; base-branch recommendation). |
| **Owner questions open** | (1) 045: approve the recommendations Q1–Q12 (summary: 18 pair tables; fill the id from the agreement lookups, never from stamps; a clear-only warning, logged on updates; the pair follows a moved lookup and the display columns are re-derived; one-line reply-type fix; invoice re-derive; direct writes via 052; split into 045a/b/c). (2) 052: exclude task/contact/account from the repair pass? (recommended yes). (3) 034: no field mapping on inbound system-created rows in R1, recorded as a deferred issue? (recommended yes). (4) 076 checks (a) and (c): the owner clicks through on Matter "FMSWP-075-push-src"; send the times to UAC-r2. (5) The 021 re-measure after the 049c deploy needs the owner's go in this session. (6) Sync-mode `-Apply` on dev (28 Assigned-To rules → One-time): owner review. |
| **Settled, don't re-ask** | 052's family cascade and Repair re-owning roots are binding (owner 2026-11-02; mechanism ours) → 052e is a design note plus UAC-r2 review, and the job stays off outside dev until then. SYSTEM and INTEGRATION users aren't product principals, so their rows are selected. 052a's two metadata reason codes are kept; the per-pass metadata cache goes in 052b. |
| **Gotchas this session** | Shell calls are slow while agents build: use timeouts ≥300 s; Grep and Glob are faster. `jq` isn't on the bash PATH (a watcher looped): use `gh --jq` instead. `python -I` blocks local imports: run the live helpers without `-I`. Live helpers are in the scratchpad `live/` (lc.py, run075.py); no token is ever printed. Pester: `-MaximumVersion 5.99`. `timeout 400 git commit`. App Insights drops whole requests (#2158). |

## Task state (detail in TASK-INDEX.md)

- **045:** Part A design on `task/fm-045` (f35b4d12b1). Part B waits for the owner. UAC-r2 ruled the playbook-update paths intended (#2257 "run as the user"); 045b adds no AppendTo there.
- **030b/034:** see Next PR. Reviews: no F. DI-020 #2625 (inbound dedupe) and DI-021 #2626 (rate card / rule set pointer by update) are filed.
- **032:** UAC-r2 and ebilling-r1 approved with conditions (decisions.md, three 2026-11-07 entries). Code waits on design round 2, the allow-list sign-off from both, and the base: `work/ebilling-r1` is 577 commits ahead of master, and the persister layout exists only there. 032 reuses 052a's `GetColumnMetadataAsync`.
- **052:** 052a done. 052b (scheduled job, `AddScheduledJob`, disabled by default, GoLiveUtc, principal-set selection, watermark/claim per `CoreAncestorStampReconciliationJob`, per-pass metadata cache, lift the `RecordRepairReachabilityTests` block), 052c (admin trigger), 052d (Signal/DR report, read-only) and 052e (cascade design) are not started.
- **076:** (b) and (d) PASS live (17:19–17:21Z, notes/task-040-notes.md); test rows: matter 33873a1a-dc61-481d-aae4-d736679305c6, project 8c788447-7ae7-4886-ba44-4a91d61db52b (cleanup list). (a) and (c) are owner checks.
- **Later:** 021 (re-measure after the 049c deploy), 022–029, 035, 036, 050, 051, 053, 060–063, 090.

## Standing rules (also in the project CLAUDE.md and memory)

- The repo is PUBLIC: access defects go to UAC-r2 by message only.
- Live Dataverse writes need the owner's go in THIS session.
- No deploys from here.
- Never run Sprk.Bff.Api.Tests unfiltered: use `scripts/verify-suites.sh`.
- Every Agent call names a model.
- Every defect is fixed in scope, or filed and reported.
