# 050 — FR-15 Mailbox Capture Spike (shared + M365-group coverage)

> **Task**: 050 (SPIKE / written finding only — no product code). **Rigor**: STANDARD.
> **Date**: 2026-07-29 · **Gates**: 051 (implement-or-descope) is gated on this finding.
> **Escalation trigger**: **FIRED** — the M365-group-mailbox path is a design decision beyond sizing (see §6).

---

## 0. TL;DR verdicts

| Path | Supportable via current subscription model? | Code delta | Verdict |
|---|---|---|---|
| **Shared mailbox** | **YES — already works today** | **~zero** (config/operator only; optional cosmetic) | **Ship as config; 051 = a docs/validation task, not a code task** |
| **M365 group mailbox** | **NO — not via the current `message`-based capture path** | **Large, forked path** | **ESCALATE — decide descope vs. build a second capture path before 051** |

The two mailbox types are **not** a single sizing exercise. A **shared mailbox is a User object** — the existing `/users/{id}/mailFolders/{folder}/messages` subscription + `messages/delta` backstop already cover it byte-for-byte. An **M365 group mailbox is not a User object and has no `messages` collection** — it uses the `conversation → thread → post` model on a different resource (`groups/{id}/conversations`), a different app permission (`Group.Read.All`, not `Mail.Read`), and is **not scoped by the Exchange `ApplicationAccessPolicy`** the whole capture path relies on for least privilege. Supporting it means a **parallel capture path**, not a delta to `GraphSubscriptionManager`. That is the decision-beyond-sizing the spike is required to surface rather than pre-decide.

---

## 1. How capture subscribes today

All under `src/server/api/Sprk.Bff.Api/Services/Communication/`.

- **`GraphSubscriptionManager`** (`BackgroundService`, 30-min tick) enumerates **receive-enabled `sprk_communicationaccount` rows** (`CommunicationAccountService.QueryReceiveEnabledAccountsAsync`) and, per account, creates/renews/recreates one Graph webhook subscription:
  - **Resource**: `users/{account.EmailAddress}/mailFolders/{MonitorFolder ?? "Inbox"}/messages` (`CreateSubscriptionAsync`, L435).
  - **ChangeType** `created`; `NotificationUrl` + `LifecycleNotificationUrl`; `ClientState`.
  - **Lifetime** 3 days (`SubscriptionLifetime`), renewed when < 24 h remain (`RenewalThreshold`). Graph client is app-only (`_graphClientFactory.ForApp()` → ADR-028 MI).
  - Subscription id/expiry persisted back to Dataverse (`sprk_graphsubscriptionid` / `…expiry` / `…status`).
- **Resilience (FR-24)**: lifecycle notifications drive `HandleLifecycleNotificationAsync` — `reauthorizationRequired`→renew, `subscriptionRemoved`→recreate, `missed`→delta reconcile.
- **Delta backstop**: `MailboxDeltaReconciliationService` (15-min tick) runs `GraphMailFolderDeltaReader.QueryDeltaAsync` = `graph.Users[email].MailFolders[folder].Messages.Delta` per receive-enabled account, enqueues an `IncomingCommunication` job keyed `Communication:{messageId}:Process`.
- **Job payload shape** (webhook + delta both): `Resource = users/{email}/mailFolders/{folder}/messages/{messageId}`, dedup on Graph message id / `sprk_graphmessageid`.

**Every stage assumes the Outlook `message` object model** (message id, `messages/delta`, per-message EML materialization). This is the load-bearing assumption for the group-mailbox verdict.

**Account model already anticipates shared mailboxes**: `AccountType` enum = `SharedAccount (100000000, default)`, `ServiceAccount`, `UserAccount`. There is **no** group-mailbox account type.

**Permission posture (ADR-028 + auth-deployment-setup §7)**: app-only Graph `Mail.Read`/`Mail.ReadWrite` + the Exchange **`ApplicationAccessPolicy`** (`RestrictAccess`, scoped to the `Spaarke Email Access` mail-enabled security group) gates *which mailboxes* the MI/BFF app may touch. Adding a mailbox = `Add-DistributionGroupMember`; no code.

---

## 2. Shared-mailbox delta

**Finding: shared mailboxes are already fully supported by the shipped code. No subscription-model change.**

- **Subscription target**: identical. A shared mailbox in Exchange Online is a **User object** (a `UserMailbox` recipient with sign-in disabled). Graph exposes it at `/users/{shared-mailbox-upn-or-id}`. The existing `users/{EmailAddress}/mailFolders/{folder}/messages` resource, the `messages/delta` backstop, the message-id idempotency, and the EML materializer all work unchanged.
- **App permission**: unchanged — `Mail.Read` (application) subscribes to "items in a folder or mailbox of *any* user in the tenant" (Outlook change-notifications doc, explicit). Shared/delegated folders are covered by the **application** permission; the `*.Shared` delegated scopes are explicitly *not* usable for subscriptions and are not in play here.
- **`ApplicationAccessPolicy` scoping**: unchanged mechanism — **add the shared mailbox's SMTP to the `Spaarke Email Access` security group** (§7f runbook already documents "adding mailboxes later"). Least-privilege posture is preserved automatically.
- **Subscription lifetime headroom**: current 3-day literal is safely under the Outlook `message` max (7 days basic / 1 day rich). Fine.

**What (optionally) changes** — cosmetic only, not required for function:
- Nothing in `GraphSubscriptionManager`, `GraphMailFolderDeltaReader`, or `MailboxDeltaReconciliationService`.
- *Optional* operator ergonomics: none needed — a shared mailbox is just another receive-enabled `sprk_communicationaccount` row (`AccountType = SharedAccount`, which is already the default) plus one group-membership add.

**Net**: shared-mailbox coverage is a **configuration + validation** deliverable, not a code deliverable.

---

## 3. M365-group-mailbox delta

**Finding: an M365 group mailbox is NOT capturable through the current `message`-based path. Coverage requires a forked capture path, a different permission, and it falls outside the `ApplicationAccessPolicy` least-privilege model.** This is the escalation item.

Divergences (all confirmed against current Microsoft Learn, 2026-04/2025-08 doc revisions):

| Dimension | User / shared mailbox (today) | M365 group mailbox |
|---|---|---|
| Directory object | `User` | `Group` (not a user; no UPN mailbox surface) |
| Subscribable resource | `users/{id}/mailFolders/{f}/messages` (Outlook **message**) | `groups/{id}/conversations` (M365 group **conversation**) — a distinct resource in the supported-resources table |
| Object model | message → attachment (EML-shaped) | **conversation → conversationThread → post** (no `message`, no per-message EML) |
| `messages/delta` backstop | `Users[..].MailFolders[..].Messages.Delta` | **Does not exist** for groups — the entire `MailboxDeltaReconciliationService` / `GraphMailFolderDeltaReader` reuse is void |
| App permission | `Mail.Read` | **`Group.Read.All` / `Group.ReadWrite.All`** (conversations are group data, not Outlook mailbox data) |
| Exchange `ApplicationAccessPolicy` | Gates access (least privilege via scope group) | **Not the governing control** — `ApplicationAccessPolicy` is an Outlook-mailbox RBAC construct over user/shared mailboxes; `Group.Read.All` grants **tenant-wide** read of *all* group conversations. There is no per-group scope-group equivalent, so least-privilege posture is materially weaker/different |
| Change types / notification payload | `created` message; payload carries message id → GET message → EML | conversation `created/updated`; payload carries conversation id → must walk threads/posts; no EML equivalent |
| Subscription lifetime | 7 days (message) | ~3 days (4,230 min) for `conversation` — **shorter than the current 3-day literal (4,320 min); would over-request and fail** |
| Downstream idempotency | `sprk_graphmessageid` per message | no message id — needs post/thread identity; the exactly-once dedup contract must be redesigned |

**Consequence**: there is no minimal `GraphSubscriptionManager` delta that adds group coverage. It needs a **second, parallel capture pipeline** (subscription creator, notification handler, a conversation/thread/post reader replacing `GraphMailFolderDeltaReader`, a post→normalized-message mapper replacing the EML path, and a distinct idempotency key). It also changes the **permission-grant ownership**: `Group.Read.All` is tenant-wide app read of all group mail — a broader consent than the deliberately-scoped `Mail.Read + ApplicationAccessPolicy` posture, which is an ADR-028 least-privilege / security-review question, not a sizing detail.

---

## 4. `GraphSubscriptionManager` delta + permission-grant delta

### Shared mailbox (the only path that maps cleanly onto the existing manager)
- **`GraphSubscriptionManager`**: **no delta.** Already enumerates all receive-enabled accounts regardless of `AccountType`; a shared mailbox row flows through unchanged.
- **Permission-grant delta**: `Add-DistributionGroupMember -Identity spaarke-central-email@… -Member {shared-mailbox-smtp}` (auth-deployment §7f). No new Graph app role. `Test-ApplicationAccessPolicy` to confirm `Granted`.

### M365 group mailbox (does NOT fit the existing manager — do not shoehorn)
If (and only if) the escalation resolves to "build":
- **New** `GroupConversationSubscriptionManager` (sibling `BackgroundService`) creating `groups/{id}/conversations` subscriptions with a group-conversation-appropriate lifetime (≤ ~3 days) and lifecycle URL.
- **New** `GroupConversationReader` (replaces the `messages/delta` reader; there is no delta backstop for groups — reconciliation strategy must be re-designed, e.g. periodic `groups/{id}/conversations?$filter=lastDeliveredDateTime ge …` sweep).
- **New** post→`NormalizedMessage` mapper (no EML materialization path reuse).
- **New** account-type value (`GroupMailbox`) + `sprk_communicationaccount` shape carrying a group **object id** (not an SMTP/UPN).
- **Permission-grant delta**: grant the MI **`Group.Read.All`** (application) — a **new, broad, tenant-wide** consent — plus admin consent; **`ApplicationAccessPolicy` does not scope it**, so any least-privilege story needs a separate design (there is no supported per-group app-access policy analogous to the mailbox scope group).

---

## 5. Sizing recommendation for task 051

**Split FR-15 into two independently-shippable outcomes. They are not one task.**

### 051a — Shared-mailbox coverage: SHIP (tiny)
- **Effort**: XS. No `.cs` change required.
- **Closed set of behaviors 051a must implement/verify**:
  1. Confirm a receive-enabled `sprk_communicationaccount` with `AccountType = SharedAccount` produces a working `users/{smtp}/mailFolders/Inbox/messages` subscription (characterization test / manual smoke).
  2. Confirm the delta backstop + webhook both capture a shared-mailbox message exactly once (existing idempotency).
  3. Operator runbook line: add the shared mailbox SMTP to `Spaarke Email Access`; `Test-ApplicationAccessPolicy = Granted`.
  4. Optional: a one-line doc note that `SharedAccount` is the intended type for shared mailboxes.
- **Risks**: essentially none. Watch: shared mailbox must be in the EXO scope group or Graph returns 403 `ErrorAccessDenied` (same failure mode as any mailbox).

### 051b — M365-group-mailbox coverage: **DO NOT START — gated on escalation (§6)**
- **If descoped** (recommended default): FR-15 delivers shared-mailbox coverage; group mailboxes are documented as **not supported** with the rationale below. Update spec/design D-07 to "shared mailbox (group mailbox deferred — different capture path + broad consent)".
- **If built**: size **M/L, forked** — new background service + reader + mapper + account shape + a re-designed reconciliation (no group delta) + a new tenant-wide `Group.Read.All` consent + security review of the weakened least-privilege posture. Sequence *after* the escalation decision; it cannot reuse the `message`/EML spine.
- **Risks 051b must handle**: (a) no `messages/delta` → missed-notification recovery must be re-invented; (b) `Group.Read.All` is tenant-wide, not scoped — security/ADR-028 sign-off; (c) conversation/thread/post → `NormalizedMessage` fidelity (attachments, participants) is new mapping surface; (d) subscription lifetime ceiling ~3 days (tighten the 3-day literal); (e) idempotency without a message id.

---

## 6. ESCALATION (root §6 / §6.5) — decision beyond sizing

🔔 **FR-15 mailbox-capture — Resolution Required (decision beyond sizing)**

- **What surfaced**: shared and M365-group mailboxes require **fundamentally different capture paths**. Shared = zero-code (already works). Group = a parallel pipeline on a different Graph resource (`groups/{id}/conversations`), a different object model (conversation/thread/post, no EML), a different app permission (`Group.Read.All`), and it is **outside** the Exchange `ApplicationAccessPolicy` least-privilege model the whole capture layer depends on — `Group.Read.All` is a **tenant-wide** app grant with no per-group scoping equivalent.
- **Why it's beyond sizing**: (1) it forks the capture architecture rather than extending `GraphSubscriptionManager`; (2) it forces a security/ADR-028 posture change (broad tenant-wide consent, no scope-group least privilege) that is an owner/security decision, not an implementer's.
- **Options**:
  - **A (recommended) — Descope group mailboxes from FR-15.** Ship shared-mailbox coverage (051a); document group mailboxes as unsupported-for-now with this rationale; amend D-07 wording. Lowest risk, preserves the least-privilege posture.
  - **B — Build the forked group path (051b).** Accept `Group.Read.All` tenant-wide consent + a second capture pipeline + re-designed reconciliation. Requires security sign-off on the weakened least-privilege story.
  - **C — Defer group mailboxes to a later round** as an explicit backlog item once a scoped-access story (or Microsoft feature) exists.
- **Recommendation**: **A** (descope now, ship shared) or **C** (defer group), not B, unless a stakeholder specifically needs group-mailbox capture and accepts the tenant-wide consent.
- **Alternative considered/rejected**: shoehorning group mail into the existing `users/{id}/messages` path — rejected: a group is not a User object and exposes no `messages` collection; the path returns nothing/errors.

**Owner action required before 051b is created.** 051a can proceed independently.

---

## Sources consulted

- **Code (ground truth)**: `Services/Communication/GraphSubscriptionManager.cs`, `CommunicationAccountService.cs`, `MailboxDeltaReconciliationService.cs`, `GraphMailFolderDeltaReader.cs`, `Models/AccountType.cs` — the shipped capture path (message-model, per-account, app-only).
- `docs/guides/auth-deployment-setup.md` §7 — Exchange `ApplicationAccessPolicy` least-privilege posture + §5 Graph app roles (`Mail.Read` etc.). **Most authoritative for the permission/operator model.**
- `src/server/api/Sprk.Bff.Api/CLAUDE.md` (auth §MI / ApplicationAccessPolicy Phase C) — app-only + policy layering.
- Microsoft Learn — *Set up notifications for changes in resource data* (change-notifications-overview; supported resources table incl. `Microsoft 365 group conversation: groups/{id}/conversations` vs `Outlook message: /users/{id}/messages`; lifetimes). **Most authoritative for the subscription model.**
- Microsoft Learn — *Change notifications for Outlook resources* (outlook-change-notifications-overview; application permission covers "mailbox of any user"; `*.Shared` scopes NOT usable for subscriptions; message = contact/event/message only, no group conversations).
- `projects/email-communication-intelligence-r1/spec.md` FR-15 + Owner Clarifications (D-07: shared + M365 group; r1 owns; spike-first).

## Open questions (tuning, not blockers)
- If option B is ever chosen: confirm whether Graph offers any **scoped** app-access control for group conversations (as of this spike: no `ApplicationAccessPolicy` analog) — re-check before committing to tenant-wide `Group.Read.All`.
