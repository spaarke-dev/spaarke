# Lessons Learned — messaging-communication-app-r1

**Project**: Add ACS Chat as the second communication channel (server plumbing + thread model + polling MDA UX).
**Window**: 2026-07-16 → 2026-07-18. 27 work tasks + wrap-up. Delivered via subagent-per-task orchestration.

---

## What went well

- **Seam-first reuse paid off.** R1 rode the ADR-045 channel seams shipped by email-r4 (sender/archiver) and added exactly one net-new seam (`ICommunicationChannelIngestor`). Dispatch-by-`sprk_communicationtype` meant the email path was byte-unchanged (414→421 characterization, 0 regressions).
- **Impersonation over hand-computed unions.** Task 042 was reworked (research-driven) from a hand-computed access union to Dataverse **impersonation** (`MSCRMCallerID` = systemuserid, fail-closed, no app-only fallback). This deleted code and aligned reads with native Dataverse RLS. Reads = impersonation + a pure 2-rule app filter (internal-only + privilege). The membership-union-on-reads was explicitly **retired** (`notes/access-model-decision.md`, 2026-07-16) — don't reintroduce it.
- **Idempotency + DLQ from day one.** Echo-dedup keyed on ACS message id (`acs-msg:{id}`), persist-before-mark, DLQ on poison. Task 031 also fixed a latent bug: `CommunicationJobProcessor` was email-only and would have DLQ'd every messaging job.
- **Honesty-over-volume on tests.** Task 080 (tests-only) did a reuse-first pass and added only 3 genuinely-additive tests rather than cloning existing MAINTAIN-class coverage — and surfaced a real product gap (Finding 1) instead of fabricating a test for behavior that doesn't exist. `/test-diet` at close found **0 scaffolding** across 136 tests.

## What bit us (and the durable fixes)

- **PCF invisibility in the form component library** — a code component only appears in the library if it declares a **bound** property. Both PCFs originally had none. Fix: added a bound `anchorField` property. *Durable lesson:* any new form-hostable PCF needs at least one `usage="bound"` property, even a nominal anchor. (Cost ~an hour of "why isn't it listed" before the root cause.)
- **BFF "deploy succeeded" but routes 404'd → actually a boot crash.** The messaging routes stayed 404 through 3 "successful" deploys. Root cause was **not** a stale/partial package (byte-scan confirmed the DLL had the routes) — it was a **SIGABRT/exit-134 startup crash-loop**: the ACS client factory threw `InvalidOperationException` when `Communication__Acs__Endpoint` was unset, and it was resolved **eagerly** via `MembershipReconcileSweepService`. *Durable fixes:* (1) `AcsIdentityService`/`AcsThreadService` now inject `Lazy<client>` per **ADR-032 boot-safety** — missing config no longer crashes boot, it fails only at the ACS operation with a clear message; (2) regression test `AcsBootSafetyTests`; (3) set the endpoint app-setting on dev. *Durable lesson:* when a deploy "succeeds" but routes 404, check `StartupLogs`/`failure.log` for a boot crash **before** re-chasing the package — hash-verify passing means the files ARE replaced.
- **CI `CI / Router` spurious red.** Tier2 advisory concurrency was keyed on `github.ref`, so superseded master runs got **cancelled**, and the alls-green aggregator treated a cancelled tier2 as failure. Fix: per-SHA concurrency group (`…-${{ github.sha }}`). *Durable lesson:* concurrency groups on `github.ref` + an alls-green gate = false reds on rapid pushes.

## Coordination / architecture notes for the next project

- **Reads access model is settled**: impersonation + 2-rule filter. R2's new `by-regarding`/`query` endpoints MUST extend `CommunicationThreadReadService` + `IImpersonatedCommunicationQuery` + `ICommunicationAccessFilter` — **do not** add a second access mechanism or reintroduce membership-union on reads.
- **Participants are not queryable** — `sprk_from/to/cc` are `;`-joined text. R2's person filter needs the new `sprk_communicationparticipant` junction (owner locked this in 2026-07-18).
- **Thread regarding discriminator differs from communication** — `sprk_communicationthread.sprk_regardingrecordtype` is **Text** (communication's is a **Lookup**). RegardingResolver needs a Lookup binding → R2 adds a new Lookup discriminator field (non-breaking), not a retype.
- **Notification-spine contract** — R1 polls; the `threadId` + `kind` taxonomy alignment with `spaarke-notification-spine-r1` binds messaging **R2**, not R1.

## Metrics

- BFF publish size: **~46.99 MB** peak (task 043), ceiling 60 MB — comfortable.
- Tests: 136 R1-authored methods, all MAINTAIN (0 scaffolding at close).
- No new HIGH CVE introduced (pre-existing Kiota HIGH noted across tasks, not a regression).
- Merged to master: PRs #655 (messaging), #658 (ACS boot-safety + format), #659 (CI concurrency fix), + wrap-up PR (this close-out).
