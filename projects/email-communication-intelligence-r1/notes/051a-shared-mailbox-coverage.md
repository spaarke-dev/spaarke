# 051a — Shared-Mailbox Capture Coverage (verification + operator runbook)

> **Task**: 051a (scoped subset of `tasks/051-shared-group-mailbox-capture-impl.poml` — shared-mailbox path ONLY).
> **Date**: 2026-07-29. **Rigor**: FULL (per POML), but XS / no-code per the 050 spike finding.
> **Gate**: 050 (`notes/050-mailbox-capture-spike.md`) — finding: shared mailboxes already work, zero code delta.
> **051b (M365 group mailbox) remains BLOCKED** on the owner escalation in spike §6 — NOT started, NOT implemented as part of this task.

---

## 1. Verdict

**PASS. No `.cs` production change was needed or made.** The 050 spike's shared-mailbox finding (§2, §5 "051a") holds under direct code inspection: `GraphSubscriptionManager`, `MailboxDeltaReconciliationService`, and the webhook receiver are all **account-type-agnostic** — none of them branch on `sprk_accounttype` / `AccountType`. A shared mailbox (`AccountType.SharedAccount`, the enum default `100000000`) flows through the exact same subscription-create/renew, delta-reconciliation, and webhook-notification code paths as a user mailbox, with the same message-id idempotency guarantee.

---

## 2. Behavior 1 — subscription creation is account-type-agnostic

**Closed-set item**: confirm a receive-enabled `sprk_communicationaccount` with `AccountType = SharedAccount` produces a working `users/{smtp}/mailFolders/Inbox/messages` subscription.

**Characterization-test decision: CITED EXISTING — no new test added.**

`tests/unit/Sprk.Bff.Api.Tests/Services/Communication/SubscriptionReconciliationTests.cs` already builds every account fixture as a shared mailbox:

```csharp
private const string Mailbox = "shared@contoso.com";
private static CommunicationAccount CreateReceiveAccount(...)
    => new() { ..., AccountType = AccountType.SharedAccount, ... }
```

and exercises the full receive-enabled-account → job-enqueue path against that fixture (`ReconcileAccountAsync_WhenDeltaReturnsMissedMessage_EnqueuesIncomingCommunicationJob`, `..._WhenSameMessageAppearsTwice_EnqueuesExactlyOnce`, `..._WhenMessageIsRemoved_DoesNotEnqueue`, plus the `GraphSubscriptionManager.HandleLifecycleNotificationAsync` lifecycle-dispatch tests). No test in the suite asserts different behavior for `UserAccount`/`ServiceAccount` vs `SharedAccount` — because the production code (below) has no such branch to test. Adding a second, separately-labeled "shared mailbox" characterization test would duplicate this existing coverage per ADR-038/`tests/CLAUDE.md` (B7/B9-shaped: same collaborators, same assertions, no new behavior surface) — so none was added.

**Code trace confirming the account-type-agnostic path** (read, not modified):

- `CommunicationAccountService.QueryReceiveEnabledAccountsAsync` (`Services/Communication/CommunicationAccountService.cs:54-61`) — Dataverse filter is `sprk_receiveenabled eq true and statecode eq 0`. No `sprk_accounttype` predicate; a shared-mailbox row and a user-mailbox row are returned identically.
- `GraphSubscriptionManager.CreateSubscriptionAsync` (`Services/Communication/GraphSubscriptionManager.cs:422-451`) — builds `Resource = $"users/{account.EmailAddress}/mailFolders/{monitorFolder}/messages"` directly from `account.EmailAddress`; `account.AccountType` is never read in this method or anywhere else in `GraphSubscriptionManager.cs`.
- `AccountType` enum (`Services/Communication/Models/AccountType.cs`) — `SharedAccount = 100000000` and is the fallback default when `sprk_accounttype` is unset (`CommunicationAccountService.cs:147-149`), matching the 050 finding's "already the default" claim.

---

## 3. Behavior 2 — exactly-once capture trace (webhook + delta backstop)

**Closed-set item**: confirm webhook + delta both capture a shared-mailbox message exactly once via existing idempotency.

**Trace (read-only, no code change):**

1. **Webhook path** — `Api/CommunicationEndpoints.cs` (~L920-978): parses the Graph change-notification `Resource` string (`users/{mailbox}/mailFolders/{folder}/messages/{messageId}`) generically — no branch on which kind of mailbox the resource belongs to. Enqueues an `IncomingCommunication` job with `IdempotencyKey = $"Communication:{messageId}:Process"` (L972).
2. **Delta backstop** — `MailboxDeltaReconciliationService.ReconcileAccountAsync` (`Services/Communication/MailboxDeltaReconciliationService.cs:161-220`): calls `GraphMailFolderDeltaReader.QueryDeltaAsync(account.EmailAddress, monitorFolder, cursor, ct)` — again keyed only on `account.EmailAddress`, not `AccountType` — dedupes within the batch (`HashSet<string> seen`), and enqueues via `EnqueueReconciledMessageAsync` with the **identical** `IdempotencyKey = $"Communication:{messageId}:Process"` format (L243).
3. **Downstream dedup (4 layers, `IncomingCommunicationProcessor.cs:475-498`)**: (1) in-memory `ConcurrentDictionary` in the webhook endpoint (same-process), (2) Service Bus `IdempotencyKey` (cross-process, shared format from steps 1+2), (3) Dataverse query on `sprk_graphmessageid` (`ExistsByGraphMessageIdAsync`), (4) Dataverse duplicate-detection rule on `sprk_graphmessageid` if configured.
4. Because both capture sources (webhook, delta) key off the same Graph `messageId` into the same idempotency-key format, and neither source nor the 4-layer downstream dedup inspects `AccountType`, a shared-mailbox message is captured **exactly once** by the same mechanism that already protects user-mailbox messages — confirmed by reading the code; not a new/different mechanism.

**No `.cs` change made** — the finding held; no gap was found.

---

## 4. Behavior 3 — operator runbook

**Closed-set item**: document the SMTP-onboarding runbook line.

Added to [`docs/guides/auth-deployment-setup.md`](../../../docs/guides/auth-deployment-setup.md) **§7 Step 7f — "Adding mailboxes later (operator runbook)"** (existing section; new paragraph appended after "No App Service restart required."). The added text:

- States that a shared mailbox is a Graph `User` object, so the existing Step 7f procedure onboards it unchanged — no separate mechanism.
- Spells out the 3-step operator sequence: (1) create/confirm the receive-enabled `sprk_communicationaccount` row with `AccountType = SharedAccount` (already the field default); (2) `Add-DistributionGroupMember` the shared mailbox's SMTP into `Spaarke Email Access`; (3) confirm `Test-ApplicationAccessPolicy = Granted` for both the BFF app-reg and MI principals.
- Notes `GraphSubscriptionManager` enumerates all receive-enabled accounts regardless of `AccountType` — no additional Graph permission or code path beyond §5/§7 is needed.

This reuses the pre-existing Step 7f runbook rather than adding a new section, since the procedure is byte-identical for shared and user mailboxes.

---

## 5. Build/test verification

No source files were modified, so no build/test run was required by the task's own gate ("If you add a test, `dotnet build` + run it"). No test was added (§2 above). `dotnet build` was not re-run since zero `.cs`/test files changed.

---

## 6. 051b status — explicitly BLOCKED, not started

**051b (M365 group mailbox) is BLOCKED on the owner decision in spike §6 (Options A/B/C — descope / build the forked `Group.Read.All` path / defer).** Per this task's scope instruction, 051b was **not implemented, not started, and no code or docs were touched for it.** The owner must resolve the escalation (spike §6) before any 051b work — including sizing detail — proceeds.

---

## 7. Files touched by this task

- `docs/guides/auth-deployment-setup.md` — §7 Step 7f, one paragraph appended (operator runbook).
- `projects/email-communication-intelligence-r1/notes/051a-shared-mailbox-coverage.md` — this note (new).

No `.cs` files were touched. `tasks/TASK-INDEX.md`, `current-task.md`, and `.claude/` paths were intentionally left untouched per this task's constraints (051a is a scoped subset of the full 051 POML; the full 051 task, including 051b, is not being marked complete here).
