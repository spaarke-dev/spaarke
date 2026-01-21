# SDAP Teams App - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-01-19
> **Source**: design.md
> **Project**: 2 of 3 in Office + Teams Integration Initiative
> **Depends On**: SDAP-office-integration (must complete first)

---

## Executive Summary

Build a Microsoft Teams app that positions Spaarke as the DMS front door within Teams. The app provides four surfaces: Personal App for full DMS browsing, configurable Tabs for workspace-specific views, Messaging Extension for inserting document cards, and Message Action for saving attachments to Spaarke. This project leverages APIs and infrastructure established by SDAP-office-integration.

---

## Scope

### In Scope

- **Personal App ("Spaarke" pinned)**
  - Browse Matters, Projects, Documents
  - Recent items and Favorites
  - Search across accessible content
  - Quick actions: Open, Share, Download

- **Configurable Tab**
  - Add tab to Team/Channel showing specific Matter or Project
  - Team members see workspace documents
  - Actions: Open, Share to chat, Upload new

- **Messaging Extension**
  - Search Spaarke content from compose box
  - Insert Adaptive Card with document preview
  - Card resolves through Spaarke access checks

- **Message Action**
  - "Save to Spaarke" from existing messages
  - Capture attachments and shared file links
  - Filing workflow in task module
  - Job status display

- **Access Request Workflow**
  - Request Access button for denied users
  - Notification to workspace owner
  - Approval/denial flow
  - (Descope if significantly increases project size)

- **Teams-Specific Backend**
  - `/teams/*` API endpoints
  - Bot Framework integration via Teams Toolkit
  - Tab configuration storage in Dataverse
  - Adaptive Card templates

### Out of Scope

- **Teams Mobile app** - V1 targets Desktop and Web only
- **Teams App Store publishing** - Internal org catalog only for V1
- **Proactive notifications** - No bot-initiated messages in V1
- **Channel file sync** - Spaarke is separate from Teams Files

### Affected Areas

| Area | Path | Changes |
|------|------|---------|
| BFF API | `src/server/api/Sprk.Bff.Api/` | New `/teams/*` endpoints |
| Teams App | `src/client/teams-app/` | New Teams Toolkit project |
| Dataverse | `src/solutions/` | TabConfiguration table |

---

## Requirements

### Functional Requirements

| ID | Requirement | Acceptance Criteria |
|----|-------------|---------------------|
| **FR-01** | Personal app navigation | User can browse Matters, Projects, Documents, Recent, Favorites |
| **FR-02** | Personal app search | Search returns results within 2 seconds |
| **FR-03** | Document quick actions | User can Open, Share, Download from personal app |
| **FR-04** | Tab configuration | User can add tab and select Matter or Project workspace |
| **FR-05** | Tab workspace view | Tab displays documents in configured workspace |
| **FR-06** | Tab upload | User can upload new document to workspace from tab |
| **FR-07** | Messaging extension search | User can search documents from compose box |
| **FR-08** | Messaging extension insert | Selected document inserts as Adaptive Card |
| **FR-09** | Card access check | Clicking card checks access; shows content or "Request Access" |
| **FR-10** | Message action save | User can save message attachments to Spaarke |
| **FR-11** | Message action workflow | Task module shows destination picker, attachment selector, options |
| **FR-12** | Access request submission | User can request access to denied resources |
| **FR-13** | Access request notification | Workspace owner receives notification of request |
| **FR-14** | Access request approval | Owner can approve or deny request |

### Non-Functional Requirements

| ID | Requirement | Target |
|----|-------------|--------|
| **NFR-01** | Messaging extension search | Response within 2 seconds |
| **NFR-02** | Personal app load | Initial load under 3 seconds |
| **NFR-03** | Tab render | Progressive loading for workspace content |
| **NFR-04** | Teams SSO | Silent auth with minimal permissions |
| **NFR-05** | Graceful degradation | Show error state if Spaarke API unavailable |
| **NFR-06** | Observability | Correlation IDs from Teams → API → workers |
| **NFR-07** | Dark mode | Support Teams dark theme |

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|-----|-----------|
| **ADR-001** | Minimal API for `/teams/*` endpoints |
| **ADR-007** | SpeFileStore for document operations |
| **ADR-008** | Endpoint filters for authorization |
| **ADR-017** | Job status polling pattern |
| **ADR-019** | ProblemDetails for errors |
| **ADR-021** | Fluent UI v9 for React components |

### MUST Rules

- ✅ MUST use Teams Toolkit (Jan 2026 version) for project scaffolding
- ✅ MUST use Bot Framework SDK for messaging extension and message actions
- ✅ MUST use Teams SSO for authentication
- ✅ MUST exchange Teams token for Spaarke session via API
- ✅ MUST use Adaptive Cards (Jan 2026 schema) for message cards
- ✅ MUST store tab configuration in Dataverse
- ✅ MUST reuse `/office/*` APIs where applicable
- ✅ MUST use Fluent UI v9 for React components in tabs/personal app
- ✅ MUST support Teams light and dark themes

### MUST NOT Rules

- ❌ MUST NOT cache sensitive data in Teams client
- ❌ MUST NOT bypass Spaarke authorization
- ❌ MUST NOT expose document content in cards (only metadata)
- ❌ MUST NOT target Teams Mobile in V1

### Technology Stack

| Technology | Version | Notes |
|------------|---------|-------|
| Teams Toolkit | Latest (Jan 2026) | Verify latest version during implementation |
| Bot Framework SDK | v4.x | For messaging extension |
| Adaptive Cards | 1.5+ | Verify Jan 2026 supported schema |
| React | 18.2.x | For tabs and personal app |
| Fluent UI | v9.x | Teams-compatible theming |

---

## Architecture Overview

### Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                      Microsoft Teams Client                     │
├─────────────────────────────────────────────────────────────────┤
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌───────────┐ │
│  │ Personal App│ │    Tab      │ │  Messaging  │ │  Message  │ │
│  │   (React)   │ │   (React)   │ │  Extension  │ │  Action   │ │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘ └─────┬─────┘ │
│         │               │               │               │       │
│         └───────────────┴───────┬───────┴───────────────┘       │
│                                 │                               │
│                    ┌────────────▼────────────┐                  │
│                    │      Teams SDK          │                  │
│                    │   (SSO + Bot Framework) │                  │
│                    └────────────┬────────────┘                  │
└─────────────────────────────────┼───────────────────────────────┘
                                  │ HTTPS
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Spaarke BFF API                            │
├─────────────────────────────────────────────────────────────────┤
│  ┌─────────────────────────────────────────┐                   │
│  │         /teams/* Endpoints              │                   │
│  │  (Bot Framework + Minimal API)          │                   │
│  ├─────────────────────────────────────────┤                   │
│  │  • POST /teams/auth/exchange            │ ← Token exchange  │
│  │  • POST /teams/messaging-extension/*    │ ← Bot Framework   │
│  │  • POST /teams/message-actions/*        │ ← Bot Framework   │
│  │  • GET/PUT /teams/tabs/{id}/config      │ ← Tab config      │
│  │  • POST /teams/access-request           │ ← Access requests │
│  └─────────────────────────────────────────┘                   │
│                         │                                       │
│           ┌─────────────┼─────────────┐                        │
│           ▼             ▼             ▼                        │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐              │
│  │ /office/*   │ │  UAC/AuthZ  │ │  Dataverse  │              │
│  │ (Reused)    │ │  (Access)   │ │  (Records)  │              │
│  └─────────────┘ └─────────────┘ └─────────────┘              │
└─────────────────────────────────────────────────────────────────┘
```

### Teams Surfaces Detail

```
┌─────────────────────────────────────────────────────────────────┐
│                        Personal App                             │
├─────────────────────────────────────────────────────────────────┤
│  ┌──────────────┐  ┌────────────────────────────────────────┐  │
│  │  Navigation  │  │              Main Panel                │  │
│  │  ──────────  │  │  ┌──────────────────────────────────┐  │  │
│  │  • Matters   │  │  │  Document List / Grid            │  │  │
│  │  • Projects  │  │  │  - Name, Type, Modified, Actions │  │  │
│  │  • Documents │  │  └──────────────────────────────────┘  │  │
│  │  • Recent    │  │                                        │  │
│  │  • Favorites │  │  ┌──────────────────────────────────┐  │  │
│  │  ──────────  │  │  │  Details Panel (on select)       │  │  │
│  │  🔍 Search   │  │  │  - Preview, Metadata, Actions    │  │  │
│  └──────────────┘  │  └──────────────────────────────────┘  │  │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                     Configurable Tab                            │
├─────────────────────────────────────────────────────────────────┤
│  Configuration Mode:           │  Runtime Mode:                 │
│  ┌─────────────────────────┐   │  ┌─────────────────────────┐   │
│  │ Select Workspace Type   │   │  │ Matter: Smith vs Jones  │   │
│  │ ○ Matter  ○ Project     │   │  │ ───────────────────────│   │
│  │                         │   │  │ Documents:              │   │
│  │ Search: [___________]   │   │  │ • Contract.docx         │   │
│  │                         │   │  │ • Evidence-01.pdf       │   │
│  │ Results:                │   │  │ • Correspondence.msg    │   │
│  │ • Smith vs Jones        │   │  │                         │   │
│  │ • Johnson Matter        │   │  │ [Open] [Share] [Upload] │   │
│  └─────────────────────────┘   │  └─────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                     Adaptive Card (Document)                    │
├─────────────────────────────────────────────────────────────────┤
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  📄 Contract-Final.docx                                 │   │
│  │  ─────────────────────────────────────────────────────  │   │
│  │  Type: Word Document                                    │   │
│  │  Matter: Smith vs Jones                                 │   │
│  │  Modified: Jan 15, 2026                                 │   │
│  │                                                         │   │
│  │  [Open in Spaarke]                                      │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

### Access Request Flow

```
User clicks document card
         │
         ▼
┌─────────────────┐
│ Access Check    │
│ (API call)      │
└────────┬────────┘
         │
    ┌────┴────┐
    ▼         ▼
 Granted   Denied
    │         │
    ▼         ▼
 Open Doc  ┌─────────────────┐
           │ Access Denied   │
           │ ─────────────── │
           │ You don't have  │
           │ access to this  │
           │ document.       │
           │                 │
           │ [Request Access]│
           └────────┬────────┘
                    │
                    ▼
           ┌─────────────────┐
           │ Create Request  │
           │ (Dataverse)     │
           └────────┬────────┘
                    │
                    ▼
           ┌─────────────────┐
           │ Notify Owner    │
           │ (Email/Teams)   │
           └────────┬────────┘
                    │
                    ▼
           ┌─────────────────┐
           │ Owner Reviews   │
           │ [Approve][Deny] │
           └────────┬────────┘
                    │
               ┌────┴────┐
               ▼         ▼
           Approved   Denied
               │         │
               ▼         ▼
           Grant      Notify
           Access     Requester
```

---

## API Contracts

### POST /teams/auth/exchange

Exchange Teams SSO token for Spaarke session.

**Request:**
```json
{
  "teamsToken": "eyJ...",
  "tenantId": "guid"
}
```

**Response:**
```json
{
  "sessionToken": "spaarke-session-token",
  "expiresAt": "2026-01-19T12:00:00Z",
  "userId": "guid",
  "displayName": "John Doe"
}
```

### POST /teams/messaging-extension/search

Bot Framework messaging extension query.

**Request (Bot Framework activity):**
```json
{
  "type": "invoke",
  "name": "composeExtension/query",
  "value": {
    "queryText": "contract smith",
    "queryOptions": { "skip": 0, "count": 10 }
  }
}
```

**Response:**
```json
{
  "composeExtension": {
    "type": "result",
    "attachmentLayout": "list",
    "attachments": [
      {
        "contentType": "application/vnd.microsoft.card.adaptive",
        "content": { /* Adaptive Card JSON */ },
        "preview": {
          "contentType": "application/vnd.microsoft.card.thumbnail",
          "content": {
            "title": "Contract-Final.docx",
            "text": "Matter: Smith vs Jones"
          }
        }
      }
    ]
  }
}
```

### POST /teams/messaging-extension/select

Generate full card for selected item.

**Request:**
```json
{
  "type": "invoke",
  "name": "composeExtension/selectItem",
  "value": {
    "documentId": "guid"
  }
}
```

**Response:** Full Adaptive Card attachment

### POST /teams/message-actions/save

Save attachments from Teams message.

**Request:**
```json
{
  "messageId": "teams-message-id",
  "attachments": [
    { "id": "att1", "name": "file.pdf", "contentType": "application/pdf" }
  ],
  "targetType": "Matter",
  "targetId": "guid",
  "processing": {
    "profileSummary": true,
    "ragIndex": true
  }
}
```

**Response (202 Accepted):**
```json
{
  "jobId": "guid",
  "statusUrl": "/office/jobs/{jobId}"
}
```

### GET /teams/tabs/{tabId}/config

Get tab configuration.

**Response:**
```json
{
  "tabId": "guid",
  "workspaceType": "Matter",
  "workspaceId": "guid",
  "workspaceName": "Smith vs Jones",
  "configuredBy": "guid",
  "configuredAt": "2026-01-19T10:00:00Z"
}
```

### PUT /teams/tabs/{tabId}/config

Save tab configuration.

**Request:**
```json
{
  "workspaceType": "Matter",
  "workspaceId": "guid"
}
```

### POST /teams/access-request

Submit access request.

**Request:**
```json
{
  "resourceType": "Document",
  "resourceId": "guid",
  "reason": "Need to review for case preparation"
}
```

**Response (201 Created):**
```json
{
  "requestId": "guid",
  "status": "Pending",
  "message": "Your request has been submitted to the workspace owner"
}
```

### GET /teams/access-requests (Owner)

List pending access requests for owner.

**Response:**
```json
{
  "requests": [
    {
      "requestId": "guid",
      "requester": { "id": "guid", "name": "Jane Doe", "email": "jane@..." },
      "resourceType": "Document",
      "resourceId": "guid",
      "resourceName": "Contract.docx",
      "reason": "Need to review...",
      "requestedAt": "2026-01-19T10:00:00Z"
    }
  ]
}
```

### POST /teams/access-requests/{id}/approve

Approve access request.

**Request:**
```json
{
  "role": "ViewOnly"
}
```

### POST /teams/access-requests/{id}/deny

Deny access request.

**Request:**
```json
{
  "reason": "Not authorized for this matter"
}
```

---

## Dataverse Schema

### TabConfiguration Table

| Column | Type | Description |
|--------|------|-------------|
| sprk_tabconfigurationid | GUID | Primary key |
| sprk_tabid | String | Teams tab ID |
| sprk_teamid | String | Teams team ID |
| sprk_channelid | String | Teams channel ID |
| sprk_workspacetype | OptionSet | Matter, Project |
| sprk_workspaceid | String | Workspace ID |
| sprk_configuredby | Lookup(SystemUser) | Who configured |
| sprk_configureddate | DateTime | When configured |

### AccessRequest Table

| Column | Type | Description |
|--------|------|-------------|
| sprk_accessrequestid | GUID | Primary key |
| sprk_requester | Lookup(SystemUser) | Who requested |
| sprk_resourcetype | OptionSet | Document, Matter, Project |
| sprk_resourceid | String | Resource ID |
| sprk_reason | Memo | Request reason |
| sprk_status | OptionSet | Pending, Approved, Denied |
| sprk_requestedat | DateTime | Request timestamp |
| sprk_reviewedby | Lookup(SystemUser) | Who reviewed |
| sprk_reviewedat | DateTime | Review timestamp |
| sprk_denialreason | String | If denied |
| sprk_grantedrole | OptionSet | If approved: ViewOnly, Download |

---

## Success Criteria

| # | Criterion | Verification Method |
|---|-----------|---------------------|
| 1 | Personal app loads and shows Matters/Projects | Manual test in Teams desktop/web |
| 2 | Tab can be configured with Matter or Project | Add tab to test channel |
| 3 | Messaging extension returns search results | Search from compose box |
| 4 | Adaptive Card inserts correctly | Send message with card |
| 5 | Card click checks access and opens or shows denied | Test with authorized/unauthorized user |
| 6 | Message action saves attachments | Save from test message |
| 7 | Access request creates record and notifies owner | Submit request, verify notification |
| 8 | Owner can approve/deny requests | Test approval flow |
| 9 | Teams dark theme displays correctly | Toggle Teams theme |
| 10 | SSO works silently | No login prompts for authenticated users |

---

## Dependencies

### Prerequisites (from SDAP-office-integration)

| Dependency | Required For |
|------------|--------------|
| `/office/search/*` APIs | Personal app, messaging extension search |
| `/office/jobs/*` API | Message action job status |
| `/office/save` API patterns | Message action save (adapted) |
| Document, Matter, Project entities | Core data model |
| ProcessingJob entity | Job tracking |
| SpeFileStore | File operations |
| UAC module | Authorization |

### External Dependencies

| Dependency | Purpose |
|------------|---------|
| Teams Toolkit (Jan 2026) | Project scaffolding, manifest |
| Bot Framework SDK v4 | Messaging extension, message actions |
| Azure AD App Registration | SSO, bot identity |
| Teams Admin Center | App deployment |

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Teams Mobile | In scope for V1? | Out of scope | Desktop + Web clients only |
| Access request workflow | Full workflow or stub? | Full workflow (can descope if too large) | Includes notification and approval UI |
| Tab config storage | Where to store? | Dataverse | TabConfiguration table |
| Bot Framework | Which approach? | Teams Toolkit (Jan 2026) | Verify latest version during implementation |
| Adaptive Cards | Complexity level? | Simple card | Verify Jan 2026 schema support |

---

## Assumptions

| Topic | Assumption | Affects |
|-------|------------|---------|
| Teams Toolkit version | Jan 2026 latest available | Project scaffolding |
| Adaptive Cards schema | 1.5+ supported in Teams | Card design |
| Notification mechanism | Email + Teams activity feed | Access request notifications |
| SDAP-office-integration complete | APIs available | All Teams functionality |

---

## Risk: Access Request Workflow Scope

The full access request workflow (FR-12, FR-13, FR-14) includes:
- Request submission
- Owner notification (email + Teams)
- Approval/denial UI
- Requester notification of outcome

**If this significantly increases scope**, descope to:
- Request submission only (creates AccessRequest record)
- No notifications
- Owner reviews in Spaarke model-driven app (existing UI)
- Defer full workflow to future iteration

---

## Test Plan Overview

### Client Matrix

| Client | Platform | In Scope |
|--------|----------|----------|
| Teams Desktop | Windows/Mac | ✅ Yes |
| Teams Web | Browser | ✅ Yes |
| Teams Mobile | iOS/Android | ❌ No |

### Test Categories

1. **Unit Tests**: API endpoints, card generation
2. **Integration Tests**: Bot Framework message handling
3. **E2E Tests**: Full flows in Teams client
4. **SSO Tests**: Token exchange, session management
5. **Theme Tests**: Light/dark mode rendering

---

*AI-optimized specification. Original design: design.md*
