# Task 129 — An email filed to a record shows on that record's Messages panel

> **Date**: 2026-10-11 · **Status**: design for owner review (no BFF code yet) · **Found by**: owner UAT, communication `b22bb67e…` filed to PAT-444023

## 1. What happens today

The record Messages panel (CommunicationConversationPanel) calls `GET /api/communications/by-regarding/{type}/{id}`.
`CommunicationThreadReadService.ReadByRegardingAsync` (`:252-315`) does two impersonated queries:

1. **threads** whose own typed regarding lookup is the record;
2. the visible **messages** of those threads.

A message reaches the panel only through a thread anchored to the record. A thread gets its anchor and owner once, at creation (`ThreadResolver.CreateThreadAsync` `:146-182`):

- **anchored** to the message's regarding at that moment, owned by the record's team (UAC-r2 task 146, decision S6);
- **unanchored** (Direct) when the message was unfiled, keeping its creator as owner.

An inbound email is ingested unfiled, so its thread is Direct and owned by the BFF app user in the root business unit. Filing the email later goes through one of three paths:
- `PATCH /api/communications/{id}/filing` (`OwnedChildWrite.RefileAsync`);
- the association engine (`IncomingAssociationResolver.ApplyDecisionAsync`);
- the add-in's file-if-unfiled, which uses the association engine.

Each path updates the **email** only, and nothing ever revisits the thread. `ThreadResolver.cs:355-366` and `email-communication-solution-r5/design.md:125` record this as a deferred gap.

**Dev today (80 active threads):**

| Finding | Count | Effect |
|---|---|---|
| Unanchored threads holding emails filed to a record | 9 | Those emails never show on their record |
| Threads holding emails filed to more than one record | 5 | Each email shows only where its thread is anchored |
| Anchored threads owned by a BFF app user in root (pre-S6) | 27 of 35 | A child-BU user (e.g. Test User 1) cannot read the thread, so the panel is empty even though the thread is anchored |

## 2. Invariant

> An email a user can read that is filed to record R is reachable from R's Messages panel. Its thread is anchored to R and owned by R's team. The only exception is an email the caller may not see, such as a private thread without a grant or an internal-only message for an external user.

The visibility key stays **the thread's anchor**. Private-thread isolation depends on it: a private thread the caller has no grant for is missing from the impersonated thread query, and nothing else enforces it. Reading by the message's own regarding would bypass that and is rejected (§4).

## 3. Proposed fix

### 3.1 The thread follows the filing (write side, one owner)

**New step.** Add one method to the thread owner, `IThreadResolver.ReconcileThreadForFilingAsync(communicationId)`:
- no new service;
- it reuses `ResolveRecordThreadOwnerAsync`, `FindOrCreateDefaultThreadAsync`, `AssignToThreadReconcilingOwnerAsync` and `ReDeriveThreadNameAsync`.

**Where it runs.** After a successful filing write in the two filing cores:
- `ChildRecordEndpoints.UpdateAsync` when the table is `sprk_communication` and `outcome.Written`, beside `children.AfterRefileAsync`;
- `IncomingAssociationResolver.ApplyDecisionAsync` when the parent lookups changed (this also covers the add-in's file-if-unfiled).

It is best-effort, like every thread step (NFR-02): a failure is logged and never fails the filing.

**Rules.** Let R be the email's primary regarding (the ADR-024 pair) and T its thread:

| Case | Action |
|---|---|
| Email unfiled, or T already anchored to R | Nothing |
| T unanchored (Direct conversation thread, not the per-user master), and no other email in T is filed to a different record | **Anchor T to R.** Set the typed regarding and its display fields, set thread type Record-Anchored, set the owner to R's team via the S6 resolver (the Secure team for a secure record; a refusal leaves T untouched and logs), and re-derive the name |
| T is the per-user master, T is anchored to another record, or T holds emails filed to other records | **Move the email** to R's per-record default thread (the existing Tier 2). This uses the existing join step, so later replies follow the email (email threads key on the parent email's thread) |

What this changes for users:
- **Most filings:** a fresh inbound email filed once is the 9-thread case. Its whole thread moves to R, and the thread name becomes the record's name; a user-renamed thread keeps its name.
- **Re-filing an email from R2 to R:** the email leaves R2's panel and appears on R's panel, in R's default thread.

### 3.2 Backfill (dev now; every environment at deploy)

- **Script:** `scripts/Backfill-ThreadFiling.ps1`, dry run by default. It applies §3.1's rules to every active filed email whose thread is not anchored to its primary regarding.
- **Re-owning:** threads that are anchored but owned by an app user get the record's team.
- **Reuse:** the owner resolution is the same as `scripts/Backfill-RecordOwnership.ps1` (task 080). That script gains `sprk_communicationthread` and does not get a second copy of the logic.
- **Safety:** a before-snapshot manifest, reversal by manifest, `-MaxWritesPerRun`, and read-back after each write.

### 3.3 Read side

No change. The panel's query stays the same. The fix is the data it reads.

## 4. Options considered

| Option | Verdict |
|---|---|
| **A. The thread follows the filing, plus a backfill** (above) | **Recommended.** It gives one owner for the invariant, keeps private-thread isolation, and needs no read change |
| B. The panel reads by the email's own regarding | Rejected. It surfaces emails from private threads the caller has no grant for: a message's own read does not check the thread, so this over-discloses |
| C. Both A and B (B limited to readable threads) | Rejected. B adds nothing once A holds, and it makes a second visibility path to keep correct |
| D. A Dataverse plugin on update of the email's regarding | Rejected: no plugins (ADR-002). A client could still write the regarding directly, but `ConnectionsWriteHandler` already refuses non-BFF regarding writes |

## 5. Known limits (stated, not fixed)

- **K-a. Multiple regardings.** An email with several typed regardings (the engine can add a matter and an invoice) shows on its **primary** record only, the same as today.
- **K-b. Unfiled emails in an anchored thread** stay unfiled. They show to anyone who can read them, and S6 already files a reply that joins.

## 6. Placement and justification

- **BFF (§10):** the change extends existing services in `Services/Communication` (ThreadResolver) and calls them from two existing filing cores. It adds no endpoint, package or DI registration. The publish-size delta will be measured at implementation.
- **§11:**
  - *Existing:* the ThreadResolver S6 steps and the 080 backfill.
  - *Extension:* yes, one method on `IThreadResolver` plus one entity in the 080 script.
  - *Cost of doing nothing:* filed emails are invisible on their record, and child-BU users see no messages on most records.
- **Ownership:** the code is the communication subsystem's, and the S6 rules are UAC-r2's. This change applies S6 rather than changing it. Run conflict-check before editing.

## 7. Owner decisions needed

1. Approve option A, including the "move the email to R's default thread" rule for re-files and mixed threads.
2. A go for the dev BFF deploy after implementation and review.
3. A go for the backfill `-Apply` in dev, after you review the dry-run counts.
