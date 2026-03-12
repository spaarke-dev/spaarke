# Send Email Integration Pattern

> **Domain**: Communication Service — Email Send Integration
> **Last Validated**: 2026-03-12
> **Source ADRs**: ADR-001, ADR-008, ADR-010, ADR-013

---

## Purpose

Standardized patterns for sending email from any module in Spaarke. All email goes through the Communication Service (`CommunicationService.SendAsync()`), ensuring consistent sender resolution, archival, tracking, and telemetry.

---

## Three Integration Patterns

| Pattern | When to Use | Integration Point |
|---------|-------------|-------------------|
| **UI Module → API** | Code pages, wizards, PCF controls sending email | `POST /api/communications/send` |
| **AI Playbook → Tool** | AI-initiated email via playbook actions | `send_communication` tool handler |
| **Server-Side → Direct** | New BFF services needing to send email programmatically | Inject `CommunicationService` directly |

---

## Pattern 1: UI Module → POST /api/communications/send

For any UI component (Code Page, wizard step, PCF control) that needs to send email.

### Frontend (TypeScript)

```typescript
// 1. Build the request
const request: SendCommunicationRequest = {
  to: ["recipient@example.com"],
  cc: ["cc@example.com"],         // optional
  subject: "Subject line",
  body: "<p>HTML body content</p>",
  bodyFormat: "HTML",              // or "PlainText"
  fromMailbox: null,               // null = default shared mailbox
  sendMode: "SharedMailbox",       // or "User" for OBO
  associations: [{
    entityType: "sprk_matter",
    entityId: matterId,
    entityName: matterName,
  }],
  archiveToSpe: true,             // archive .eml to SharePoint Embedded
  attachmentDocumentIds: ["guid1", "guid2"],  // optional SPE document IDs
};

// 2. Send via BFF API
const response = await fetch(`${bffBaseUrl}/api/communications/send`, {
  method: "POST",
  headers: {
    "Content-Type": "application/json",
    "Authorization": `Bearer ${token}`,
  },
  body: JSON.stringify(request),
});

// 3. Handle response
if (response.ok) {
  const result = await response.json();
  // result.communicationId — Dataverse record GUID
  // result.status — "Send" or "Delivered"
  // result.warnings — array of non-fatal issues (archival, attachment records)
} else {
  const problem = await response.json(); // ProblemDetails (RFC 7807)
  // problem.detail — human-readable error
  // problem.extensions.errorCode — machine-readable code (e.g., "DAILY_SEND_LIMIT_REACHED")
}
```

### Wizard Step Integration (SendEmailStep)

For wizards using the shared `@spaarke/ui-components` library, use the `SendEmailStep` component:

```typescript
import { SendEmailStep } from "@spaarke/ui-components";

// In your wizard step configuration:
{
  label: "Send Email",
  component: SendEmailStep,
  props: {
    defaultRecipients: contacts.map(c => c.email),
    defaultSubject: `RE: ${matterName}`,
    fromMailbox: null,  // default shared mailbox
    associations: [{ entityType: "sprk_matter", entityId: matterId }],
    onSendComplete: (result) => {
      // Handle success — advance wizard, show confirmation
    },
    onSendError: (problem) => {
      // Handle error — show inline error, allow retry
    },
  },
}
```

### BFF URL Resolution

UI components resolve the BFF API base URL in this order:
1. Dataverse environment variable `sprk_BffApiBaseUrl`
2. Hardcoded default: `https://spe-api-dev-67e2xz.azurewebsites.net`

### Auth Token Acquisition

```typescript
// For Dataverse-hosted UI (PCF, Code Pages)
const token = await Xrm.Utility.getGlobalContext()
  .getCurrentAppUrl()  // Get auth context
  // OR use MSAL.js for standalone apps
```

---

## Pattern 2: AI Playbook → send_communication Tool

For AI playbooks that need to send email as part of an automated workflow.

### JPS Definition

Add `send_communication` to the playbook's tool list:

```json
{
  "tools": [
    {
      "name": "send_communication",
      "description": "Send an email via the Communication Service",
      "parameters": {
        "to": { "type": "string", "description": "Comma-separated recipient emails", "required": true },
        "cc": { "type": "string", "description": "Comma-separated CC emails" },
        "subject": { "type": "string", "description": "Email subject", "required": true },
        "body": { "type": "string", "description": "Email body (HTML)", "required": true },
        "fromMailbox": { "type": "string", "description": "Sender mailbox (null = default)" },
        "regardingEntity": { "type": "string", "description": "Dataverse entity logical name" },
        "regardingId": { "type": "string", "description": "Dataverse record GUID" }
      }
    }
  ]
}
```

### How It Works

1. AI model decides to send email based on playbook instructions
2. Model invokes `send_communication` tool with parameters
3. `SendCommunicationToolHandler.HandleAsync()` is called
4. Handler parses parameters into `SendCommunicationRequest`
5. Delegates to `CommunicationService.SendAsync()` (same pipeline as UI)
6. Returns structured result to AI model

### Registration

`SendCommunicationToolHandler` is registered in `CommunicationModule` as a singleton:

```csharp
services.AddSingleton<SendCommunicationToolHandler>();
```

It is NOT auto-discovered — it must be explicitly registered.

---

## Pattern 3: Server-Side → Inject CommunicationService

For new BFF services that need to send email programmatically (e.g., scheduled notifications, event-driven emails).

### Implementation

```csharp
public class NotificationService
{
    private readonly CommunicationService _communicationService;

    public NotificationService(CommunicationService communicationService)
    {
        _communicationService = communicationService;
    }

    public async Task SendMatterUpdateNotificationAsync(
        Guid matterId, string matterName, string recipientEmail, CancellationToken ct)
    {
        var request = new SendCommunicationRequest
        {
            To = [recipientEmail],
            Subject = $"Matter Update: {matterName}",
            Body = $"<p>Matter <b>{matterName}</b> has been updated.</p>",
            BodyFormat = BodyFormat.HTML,
            SendMode = SendMode.SharedMailbox,  // App-only auth
            Associations =
            [
                new CommunicationAssociation
                {
                    EntityType = "sprk_matter",
                    EntityId = matterId.ToString(),
                    EntityName = matterName,
                }
            ],
            ArchiveToSpe = true,
            CorrelationId = Guid.NewGuid().ToString(),
        };

        var response = await _communicationService.SendAsync(request, httpContext: null, ct);
        // response.CommunicationId, response.Status, response.Warnings
    }
}
```

### DI Registration

Register the new service in its feature module (NOT in CommunicationModule):

```csharp
// In your module's DI registration
services.AddSingleton<NotificationService>();
```

`CommunicationService` is already registered as a singleton by `CommunicationModule`.

---

## Common Parameters Reference

### SendCommunicationRequest

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `to` | `string[]` | Yes | — | Recipient email addresses (>=1) |
| `cc` | `string[]` | No | null | CC recipients |
| `bcc` | `string[]` | No | null | BCC recipients |
| `subject` | `string` | Yes | — | Email subject line |
| `body` | `string` | Yes | — | Email body content |
| `bodyFormat` | `BodyFormat` | No | `HTML` | `HTML` or `PlainText` |
| `fromMailbox` | `string` | No | null | Sender mailbox (null = default) |
| `sendMode` | `SendMode` | No | `SharedMailbox` | `SharedMailbox` or `User` |
| `associations` | `CommunicationAssociation[]` | No | null | Entity associations |
| `archiveToSpe` | `bool` | No | `false` | Archive .eml to SPE |
| `attachmentDocumentIds` | `string[]` | No | null | SPE document IDs to attach |
| `correlationId` | `string` | No | auto | Tracing correlation ID |

### Error Codes

| Code | HTTP | When |
|------|------|------|
| `VALIDATION_ERROR` | 400 | Missing To, Subject, or Body |
| `INVALID_SENDER` | 400 | Sender not in approved list |
| `NO_DEFAULT_SENDER` | 400 | No sender configured |
| `DAILY_SEND_LIMIT_REACHED` | 429 | Account hit daily quota |
| `ATTACHMENT_LIMIT_EXCEEDED` | 400 | >150 attachments or >35MB |
| `GRAPH_SEND_FAILED` | 502 | Graph API error |

---

## Checklist for Adding Email to a New Module

1. **Choose the pattern** — UI (Pattern 1), AI Playbook (Pattern 2), or Server-Side (Pattern 3)
2. **Determine sender** — `fromMailbox: null` for default shared mailbox, or specify a configured account
3. **Determine send mode** — `SharedMailbox` (most common) or `User` (for "send as me")
4. **Set associations** — Link email to the relevant entity (matter, account, contact, etc.)
5. **Handle errors** — Parse `ProblemDetails` responses; handle `429` (daily limit) gracefully
6. **Test with verification** — Run `POST /api/communications/accounts/{id}/verify` to confirm mailbox connectivity
7. **Monitor** — Check `CommunicationTelemetry` metrics for send success/failure rates

---

## Related Patterns

- [Endpoint Definition](endpoint-definition.md) — API endpoint conventions
- [Error Handling](error-handling.md) — ProblemDetails error format
- [Graph Webhooks](../auth/graph-webhooks.md) — Inbound email webhook subscriptions
- [Service Registration](service-registration.md) — DI registration patterns

---

**Lines**: ~200
**Purpose**: Repeatable email integration guide for all Spaarke modules
