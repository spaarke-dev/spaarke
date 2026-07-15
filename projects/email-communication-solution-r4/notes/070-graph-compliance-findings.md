# Task 070 — Graph Compliance Audit Findings

> **Task**: `070-graph-compliance-audit.poml` (W7, FR-23 / NFR-01)
> **Audit date**: 2026-07-15
> **Auditor**: Claude Code (task-execute, FULL rigor)
> **Scope**: Shipping BFF solution (`src/`), scripts (`scripts/`), Dataverse plugins (`src/dataverse/plugins/`)
> **Type**: Read-only audit. No code changed by this task except this note.
> **Verdict (both deadlines)**: ✅ **NOT EXPOSED** — no remediation task filed, no Human Escalation raised.

This note is the compliance sign-off evidence for the two hard Microsoft 2026 deadlines. NFR-01 makes compliance first-class; each finding below is dated and tied to its deadline with file:line evidence.

---

## Finding A — `Mail-Advanced.*` (enforced 2026-12-31)

**Deadline**: 2026-12-31 — after this date, Graph writes to *sensitive properties* of an already-delivered (non-draft) Exchange Online mail item require the new admin-consent permission `Mail-Advanced.ReadWrite` (Message Center notice **MC1304287**), replacing ordinary `Mail.ReadWrite` for those writes.

### What counts as a "sensitive property" (the trigger set)

Microsoft defines the sensitive set as exactly the properties flagged **"Updatable only if isDraft = true"** on the Graph `message: update` reference:

`bccRecipients`, `body`, `ccRecipients`, `internetMessageId`, `multiValueExtendedProperties`, `replyTo`, `singleValueExtendedProperties`, `subject`, `toRecipients`.

Properties that remain freely updatable with ordinary `Mail.ReadWrite` (explicitly named exempt by Microsoft): **`isRead`, `categories`, `flag`, `importance`**, `inferenceClassification`, `isDeliveryReceiptRequested`, `isReadReceiptRequested`.

### Enumeration of ALL Graph mail-item operations in the shipping solution

Grep of the full `Services/` tree for message/mailFolder operations, PATCH/update calls, and extended-property/categories/flag/importance writes. Every call site classified:

| # | File:line | Operation | Verb | Sensitive-property write to a non-draft item? |
|---|-----------|-----------|------|-----------------------------------------------|
| 1 | `Services/Communication/IncomingCommunicationProcessor.cs:724-725` | `Users[mbx].Messages[id].PatchAsync(new Message { IsRead = true })` | PATCH | **NO** — sets only `isRead` (exempt property) |
| 2 | `Services/Communication/IncomingCommunicationProcessor.cs:207-208` | `Users[mbx].Messages[id].GetAsync(...)` | GET (read) | No — read only |
| 3 | `Services/Communication/CommunicationService.cs:200` | `Users[email].SendMail.PostAsync(...)` | POST /sendMail | No — sends a NEW message (`Mail.Send`), not an update to an existing item |
| 4 | `Services/Communication/CommunicationService.cs:557` | `Me.SendMail.PostAsync(...)` | POST /sendMail | No — send (new message) |
| 5 | `Services/Communication/CommunicationService.cs:1568,1579` | `…MailFolders["sentitems"].Messages.GetAsync(...)` | GET (read) | No — read (Internet-Message-Id capture) |
| 6 | `Services/Communication/InboundPollingBackupService.cs:194` | `…MailFolders[folder].Messages…` | GET (read) | No — backup poll read |
| 7 | `Services/Communication/MailboxVerificationService.cs:185` | `Users[email].SendMail.PostAsync(...)` | POST /sendMail | No — verification test send |
| 8 | `Services/Office/OfficeEmailEnricher.cs:51,179` | `Me.Messages[id].GetAsync(...)` | GET (read) | No — read for enrichment |
| 9 | `Services/Ai/Export/EmailExportService.cs:88` | `Me.SendMail.PostAsync(...)` | POST /sendMail | No — send (new message) |
| 10 | `Services/Ai/Nodes/SendEmailNodeExecutor.cs:237` | `Me.SendMail.PostAsync(...)` | POST /sendMail | No — send (new message) |

**Non-mail PATCH calls** (surfaced by the same grep; confirmed NOT mail-item writes, so out of Mail-Advanced scope):
- `Services/Communication/GraphSubscriptionManager.cs:302` — `Subscriptions[id].PatchAsync(...)` renews a **webhook subscription** lifetime (not a mail item).
- `Services/Registration/GraphUserService.cs:332` — `Users[id].PatchAsync(new User { AccountEnabled = false })` writes a **directory user** object (not a mail item).
- `Services/Ai/Handlers/**`, `Services/Ai/WorkProductRecordPersister.cs` — `PatchAsync` on the internal **Dataverse** user-client (not Graph mail).

### The `IsRead=true` PATCH — exemption determination

- **Call site**: `IncomingCommunicationProcessor.MarkAsReadAsync` (line 719), body `new Message { IsRead = true }`, PATCH `/users/{mailboxEmail}/messages/{graphMessageId}`. Purpose: prevent a processed inbound message from being re-picked-up by backup polling. App-only via `_graphClientFactory.ForApp()`.
- **Payload verified minimal**: the request object sets ONLY `IsRead` — no `categories`, `flag`, `importance`, `subject`, `body`, recipients, or extended properties are opportunistically included. This makes the exemption trivially defensible.
- **Determination**: `isRead` is NOT in the sensitive set (it is not `isDraft`-only on the `message: update` reference and is explicitly named by Microsoft among the properties that stay editable with `Mail.ReadWrite`).

> **Verdict A — NOT EXPOSED.** The only non-draft write to a Graph mail item is the `IsRead=true` PATCH, which is **exempt** from `Mail-Advanced.ReadWrite`. It continues to work under the existing app-only `Mail.ReadWrite` permission after 2026-12-31. **DEC-6 is resolved: exempt.** No remediation task required. No permission or code change required.

### Sources (Mail-Advanced determination)

- Graph `message: update` reference (canonical sensitive-property list via the isDraft-only flags): https://learn.microsoft.com/en-us/graph/api/message-update
- M365 Developer Blog — "Graph API updates to sensitive email properties" (names `Mail-Advanced.*` + 2026-12-31): https://devblogs.microsoft.com/microsoft365dev/graph-api-updates-to-sensitive-email-properties/
- Exchange Team blog — "Upcoming breaking changes to modifying sensitive email properties via Graph API": https://techcommunity.microsoft.com/blog/exchange/upcoming-breaking-changes-to-modifying-sensitive-email-properties-via-graph-api/4505227
- Message Center notice **MC1304287** — secure-by-default Exchange API changes.

### Standing watch item (informational — not a current exposure)

If any future Spaarke feature adds a server-side write of `subject`/`body`/recipients/`internetMessageId`/extended-properties to an *existing received* message, that WOULD trigger the `Mail-Advanced.ReadWrite` requirement. No such write exists today. Re-check the `message: update` reference closer to 2026-12-31 in case Microsoft re-flags a property (low probability). For the binding audit record, cite **MC1304287** from the tenant's own Message Center (dates in MC notices can slip).

---

## Finding B — EWS (Exchange Web Services, enforced-off 2026-10-01)

**Deadline**: 2026-10-01 — EWS is turned off for the tenant class. Any EWS dependency in the shipping solution would break.

### Scan performed

Case-sensitive greps for EWS API surface across the required trees:

| Pattern | `src/` | `scripts/` | `src/dataverse/plugins/` |
|---------|--------|-----------|--------------------------|
| `ExchangeService` | 0 | 0 | 0 |
| `Microsoft.Exchange.WebServices` | 0 | 0 | 0 |
| `exchange.asmx` | 0 | 0 | 0 |
| `Autodiscover` | 0 | 0 | 0 |
| `WebCredentials` / `ExchangeVersion` / `EwsUrl` | 0 | 0 | 0 |

**Only** matches for the literal string "EWS" anywhere in the repo were:
- Substring noise inside unrelated identifiers (`EVENT_VIEWS`, `reVIEWS`) and base64 blobs in `package-lock.json` files — not EWS API usage.
- Documentation/spec prose: `projects/sdap-office-integration/spec.md:476,480` discusses "EWS vs REST message-ID canonicalization" as an **out-of-scope** design note (no code), and this project's own spec/design/plan/task files describe the audit itself.

Dataverse plugins present are 3 files under `Spaarke.CustomApiProxy` (`BaseProxyPlugin.cs`, `GetFilePreviewUrlPlugin.cs`, `SimpleAuthHelper.cs`) — file-preview URL proxy, no Exchange/EWS usage.

> **Verdict B — NOT EXPOSED.** No Exchange Web Services usage exists anywhere in the shipping solution (`src/`, `scripts/`, `src/dataverse/plugins/`). The inbound and outbound pipelines are 100% Microsoft Graph. **The "EWS in scripts/plugins" open item from spec §Unresolved-Questions is closed: none found.** No remediation task required.

---

## ADR-028 credential check (constraint from POML)

All inspected Graph calls obtain their client from the central `IGraphClientFactory` (`ForApp()` / `ForUserAsync()`). Grep of `Services/Communication/` for self-built credentials (`new ClientSecretCredential`, `ConfidentialClientApplication`, `new DefaultAzureCredential`, `new GraphServiceClient(...)`) returned **zero** matches. No ADR-028 violation found — no separate finding filed.

---

## Overall verdict & disposition

| Deadline | Item | Verdict | Remediation task | Escalation |
|----------|------|---------|------------------|------------|
| 2026-12-31 | `Mail-Advanced.*` sensitive-property writes | ✅ NOT EXPOSED (isRead exempt) | None | None |
| 2026-10-01 | EWS usage | ✅ NOT EXPOSED (no EWS anywhere) | None | None |

Both W7 compliance sign-off gates (DEC-6 exemption; EWS scripts/plugins confirmation) are **cleared**. No Human Escalation is raised because no exposure was found.
