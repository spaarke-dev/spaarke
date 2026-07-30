---
name: graph-shared-vs-group-mailbox-subscriptions-2026-07-29
description: Graph change-notification + app-only permission semantics for shared vs M365-group mailboxes; why group mailbox capture forks the message-based path (FR-15 spike 050)
metadata:
  type: reference
---

# Graph subscriptions: shared vs M365-group mailbox (FR-15 spike, 2026-07-29)

Investigated for `email-communication-intelligence-r1` task 050 (size FR-15 shared+group mailbox capture over the shipped `GraphSubscriptionManager`).

**Shared mailbox = a User object.** In Exchange Online a shared mailbox is a `UserMailbox` recipient with sign-in disabled; Graph exposes it at `/users/{id}`. So `users/{smtp}/mailFolders/{folder}/messages` subscriptions, `messages/delta`, message-id idempotency, and EML materialization all work **unchanged**. App permission = `Mail.Read` (application permission covers "a folder or mailbox of ANY user in the tenant"; the `*.Shared` delegated scopes are explicitly NOT usable for subscriptions). Least privilege = add the SMTP to the Exchange `ApplicationAccessPolicy` scope group (mail-enabled security group, `RestrictAccess`). **Net: zero code change** — shared mailboxes are a config/operator concern in Spaarke's capture layer.

**M365 group mailbox = a Group object, fundamentally different capture path.** Not a User; no `messages` collection. The subscribable resource is `groups/{id}/conversations` (Microsoft 365 group **conversation**, a distinct entry in the change-notifications supported-resources table). Object model is **conversation → conversationThread → post** (no `message`, no per-message EML). There is **no group `messages/delta`** — the delta-reconciliation backstop cannot be reused. Permission is **`Group.Read.All`** (tenant-wide app read of ALL group conversations), NOT `Mail.Read`. Critically, the Exchange **`ApplicationAccessPolicy` does NOT scope group mailboxes** — it's an Outlook-mailbox RBAC construct; there is no per-group app-access-policy analog, so `Group.Read.All` is a broad tenant-wide consent with no scope-group least-privilege equivalent. Subscription lifetime for `conversation` ~3 days (4,230 min) — slightly under a 3-day literal (4,320 min), so over-requesting fails.

**Implication**: supporting group mailboxes is a **parallel pipeline** (new subscription manager + conversation/thread/post reader + post→normalized mapper + new idempotency + re-designed reconciliation + broad consent + security review), NOT a delta to `GraphSubscriptionManager`. This is a design-decision-beyond-sizing → escalate (descope/defer recommended over build). Finding: `projects/email-communication-intelligence-r1/notes/050-mailbox-capture-spike.md`.

**Sources**: learn.microsoft.com/graph/change-notifications-overview (supported resources + lifetimes); learn.microsoft.com/graph/outlook-change-notifications-overview (app-permission covers any-user mailbox; `*.Shared` not subscribable; message resource = contact/event/message only, no group conversations); Spaarke `docs/guides/auth-deployment-setup.md` §7 (ApplicationAccessPolicy). See also [[spe-ciam-crosstenant-apponly-brokering-2026-07-18]] for app-only Graph brokering patterns.
