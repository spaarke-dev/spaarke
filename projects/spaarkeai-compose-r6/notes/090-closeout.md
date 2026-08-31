# Task 090 — wrap-up close-out record

> 2026-08-13 · STANDARD rigor (TEST-MODIFYING override on the diet/review step) · prescriptive steps

## Step outcomes

| Step | Outcome |
|---|---|
| 1 ADR-049 amendment | ✅ R6 Path-B amendment verified on `origin/master` (merged with task 001's code) |
| 2 Deploy (anti-clobber) | ✅ resolved as **documented no-op** per escalation trigger 3: full surface deployed atomically 2026-08-07 17:25/17:26Z (12 min after PR #747); wrap-up verify confirmed today's live artifacts (r2 deploys: BFF 13:12Z, code page 18:04Z) are a **strict superset** carrying R6 markers + r2's newer work. Redeploying from this worktree would clobber r2's active session — the exact scenario the constraint forbids |
| 3 /test-diet (BINDING) | ✅ 37 files / 281 tests — **0 scaffolding, 0 ambiguous, 0 path-violations**; 1 MAINTAIN-conditional (stamper tests retire WITH the transitional path). Report: [`test-diet-report.md`](test-diet-report.md) |
| 4 code-review + adr-check | ✅ see below — 1 HIGH **fixed at wrap-up**, 4 LOW deferred, mechanical layer green (full suite 10,250/10,351 passed, 0 failed; includes arch guardrails + I-7 lexical audit) |
| 5 repo-cleanup | ✅ structure complete; Corteva docx deliberately untracked (pending sign-off) |
| 6 Success Criteria | ✅ all 6 verified with evidence (README) |
| 7 lessons-learned | ✅ [`lessons-learned.md`](lessons-learned.md) |
| 8 README/plan | ✅ Complete/100% + changelog |
| 9 devops-sync | ⚠️ degraded to warn — R6 has no portfolio Project Issue (never registered) |
| 10/11 ledger | ✅ TASK-INDEX 30/30 ✅; current-task reset to none; 090 POML completed |

## Step 4 — cross-slice close-out review (background agent on the final aggregate)

**Verdict: PASS-WITH-FINDINGS.** All server lenses verified clean: ADR-007 (no Graph above SpeFileStore),
ADR-013 (PublicContracts facades only), ADR-049 amendment invariants (surgical engine reachable ONLY from
the transitional `ContentModel`-null path; no content-locating text-search on the save path), §F.1 DI
symmetry (real+Null peers for both facades; engines unconditional), typed `ComposePdfIntakeException`
catch precedes `InvalidOperationException` in all four throwing handlers, and the FR-C3 ∪ B-MED-3
`PromoteIfEphemeralAsync` union composes correctly (ordering, disjoint attributes, independent
best-effort envelopes).

### HIGH — FIXED same-session (this is why the close-out review exists)

**Apply-template + remount ignored the document's own drive.** `triggerSave` carried the UAT-P2 fix
(`state.documentRef.driveId ?? effectiveDriveId`) but `handleApplyTemplate`, `canShowApplyTemplate`, and
the requestLoad load effect all used host-only `effectiveDriveId`. Consequences: bare-mount born-in-editor
docs never showed the Apply Template button; hosts where launch drive ≠ BU drive got a misleading 404 on
apply-template/remount for any create-on-save-minted doc (born-in-editor, forkNew, PDF-sourced post-save).
Cross-slice by construction — task 032's slice reviewed against ribbon-launched docs (host drive == doc
drive); the re-target lives in the create-on-save/P2 slice; only the union crosses them.

**Fix** (`ComposeWorkspace.tsx`, three sites, mirrors the shipped :1376 pattern): `loadDriveId` /
`applyDriveId` = `state.documentRef?.driveId ?? effectiveDriveId` in the load effect (guard + query),
`handleApplyTemplate` (guard + POST body), and the `canShowApplyTemplate` gate. Verified: tsc clean;
renderOnSave + reducer + banner + apply-template-dialog suites **74/74**.

### LOW — deferred (routed to the R7 defer register lineage; recorded here since the register moved to the r7 worktree)

| Finding | Detail | Routing |
|---|---|---|
| Create-on-save `VersionId` is the drive-ITEM id | The 041 PDF reducer re-baseline adopts it as a version id. LATENT (every post-PDF save still sends `content`; server prefers Content); becomes a 404 trap if a future path sends `baselineVersionId` without content | R7 candidate (small server fix: return null or real version id on create) |
| `NotifyLinkedCopyAsync` fires before `CreateAsync` | Spurious "linked copy" notification under the G7 two-key race / create failure. Best-effort, no data damage | FR-C3 owner (email-communication-intelligence-r2) or next `PromoteIfEphemeralAsync` toucher |
| Alt-key lookup conflates not-found with lookup-failed | Transient fault/schema skew routes an existing doc's save into the create branch; double failure = 500 after bytes landed | R7 candidate (distinguish absent vs failed) |
| PDF doorway asymmetry | Only the SPE Load door runs PDF intake; chat-upload + Browse-local mount the same PDF reference-only. Scope-accepted for FR-06 (stored PDFs) — now explicit | Fast-follow decision |

**Rebutted note**: the reviewer's "no client consumer of `/versions`" observation missed task 051's
`VersionHistoryModal` in `src/solutions/AllDocuments` (outside its `src/client/**` grep) — FR-07 UX shipped.

## Suite evidence (Step 10)

- Server: full `Sprk.Bff.Api.Tests` run **10,250 passed / 0 failed** (10,351 total, 101 skipped) —
  includes every seam/contract/regression/guardrail class. (One first-run flake did not reproduce —
  consistent with the documented `CreateOnSaveTests` FakeTimeProvider flake, already a defer-register item.)
- Client: `Spaarke.Compose.Components` tsc clean; R6 suites 74/74 post-HIGH-fix.
- No test-diet deletions → no re-baseline required.
