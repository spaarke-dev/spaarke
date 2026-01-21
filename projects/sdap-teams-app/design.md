# design.md — SDAP Teams App

Date: 2026-01-19
Audience: Claude Code (primary), Spaarke Engineering/Product (secondary)
Scope: Microsoft Teams app integration for Spaarke DMS

## Project Context

This is **Project 2 of 3** in the Spaarke Office + Teams integration initiative:
1. SDAP-office-integration — Outlook + Word add-ins with shared platform (dependency)
2. **SDAP-teams-app** (this project) — Teams integration
3. SDAP-external-portal — Power Pages external collaboration

### Dependencies

This project depends on **SDAP-office-integration** for:
- Core backend APIs and patterns
- Document, ProcessingJob, and related Dataverse entities
- SpeFileStore facade and SDAP infrastructure
- Job status tracking patterns

---

## 1. Objective

Create a Microsoft Teams app that positions Spaarke as the **DMS front door** in Teams:
- **Personal App**: Browse/search Matters, Projects, Documents, Recent, Favorites
- **Tab**: Context-specific DMS view embedded in a Team/Channel
- **Messaging Extension**: Search Spaarke and insert Document cards/links into chat/channel
- **Message Action**: "Save to Spaarke" from an existing message (capture attachments/shared links)

### Consistency Requirements

Claude Code must produce a detailed spec consistent with:
- Spaarke ADR constraints (Minimal APIs, endpoint-seam auth, SDAP SpeFileStore abstraction)
- SDAP/SPE access model and UAC enforcement
- Best practices for Teams apps as of Jan 2026

---

## 2. Design Principles and Non-Negotiables

### 2.1 DMS Centricity

- Matters and Projects are the primary "workspace" containers
- "Save to Spaarke" is a filing workflow: item must be associated to Matter/Project/Invoice
- Make "Spaarke" clearly the DMS; avoid ambiguity with Teams Files (OneDrive/SharePoint)

### 2.2 Single Canonical File + Multi-Context Association

- A document binary has a single canonical storage location (SPE)
- The same document can be associated to multiple workspaces via relationship records

### 2.3 Authorization is Server-Side

- Teams app never bypasses Spaarke authorization
- All file operations mediated by Spaarke APIs with UAC controlling access
- Sharing from Spaarke posts a Spaarke card/link that resolves through Spaarke access checks
- If user lacks access: show "Request access" workflow (do not leak content)

### 2.4 Asynchronous Processing is First-Class

- Upload and processing are async with job/status tracking
- UX is job/status-based, not blocking

---

## 3. Required Teams Surfaces (V1)

### 3.1 Personal App ("Spaarke" Pinned)

Full DMS experience within Teams:
- Browse Matters, Projects, Documents
- Recent items and Favorites
- Search across all accessible content
- Quick actions: Open, Share, Download

### 3.2 Tab (Matter/Project Workspace)

Contextual DMS view embedded in a Team or Channel:
- Configure tab to show specific Matter or Project
- Team members see documents associated with that workspace
- Actions: Open, Share to chat, Upload new

### 3.3 Messaging Extension

Search and insert Spaarke content into conversations:
- Search by document name, Matter, Project
- Insert rich Adaptive Card preview
- Card includes: document name, type, Matter/Project context, Open link
- Recipient sees card; clicking resolves through Spaarke access check

### 3.4 Message Action

"Save to Spaarke" from existing messages:
- Capture attachments from a message
- Capture shared file links
- File to Matter/Project/Invoice (same flow as Office add-ins)
- Show job status after submission

---

## 4. UX Patterns

### 4.1 Personal App Layout

| Section | Content |
|---------|---------|
| Navigation | Matters, Projects, Documents, Recent, Favorites, Search |
| Main Panel | List/grid view of selected section |
| Details Panel | Document preview, metadata, actions |
| Quick Actions | Open, Share, Download, View in Spaarke |

### 4.2 Tab Configuration

When adding tab to Team/Channel:
1. Select workspace type: Matter or Project
2. Search/select specific workspace
3. Tab displays documents associated with that workspace
4. Team members with Spaarke access see content; others see "Request access"

### 4.3 Messaging Extension Flow

1. User invokes extension (compose box or command)
2. Search interface appears
3. User searches for document
4. Select document to insert
5. Adaptive Card inserted into message
6. Recipients click card → Spaarke access check → Open or Request Access

### 4.4 Message Action Flow

1. User selects message with attachments
2. Clicks "Save to Spaarke" action
3. Task module opens with filing workflow:
    - Select destination (Matter/Project/Invoice)
    - Select which attachments to save
    - Processing options
4. Submit → Job status displayed
5. Confirmation with links to saved documents

### 4.5 Access Denied UX

When user lacks access to a document/workspace:
- Show clear "Access Denied" message
- Provide "Request Access" button
- Request goes to workspace owner for approval
- Never leak document content or metadata beyond name

---

## 5. Security and Authorization

### 5.1 Authorization Model

- Teams app authenticates user via Teams SSO
- Spaarke APIs validate user access per request
- Tab/Personal app shows only accessible content
- Messaging extension cards respect access at view time

### 5.2 Token Strategy

- Teams SSO provides user identity
- Spaarke API exchanges Teams token for Spaarke session
- OBO flow for Graph/SPE operations where needed

### 5.3 Auditability

- Log: user, action, object IDs, Team/Channel context, timestamps, correlation IDs

---

## 6. Backend Component Architecture

### 6.1 Teams Integration Module

| Component | Responsibility |
|-----------|----------------|
| Bot Framework endpoints | Handle messaging extension, message actions |
| Tab configuration API | Store/retrieve tab settings |
| Adaptive Card templates | Generate rich cards for messages |
| Teams auth handler | SSO token validation and exchange |

### 6.2 Reused from SDAP-office-integration

| Component | Usage |
|-----------|-------|
| /office/save API | Adapted for message action saves |
| /office/jobs API | Job status tracking |
| /office/search/* APIs | Document and workspace search |
| SpeFileStore | File operations |
| ProcessingJob entities | Job tracking |

### 6.3 Teams-Specific APIs

| Endpoint | Purpose |
|----------|---------|
| POST /teams/message-actions/save | Save attachments from Teams message |
| POST /teams/messaging-extension/search | Search for messaging extension |
| POST /teams/messaging-extension/select | Generate card for selected item |
| GET /teams/tabs/{tabId}/config | Get tab configuration |
| PUT /teams/tabs/{tabId}/config | Save tab configuration |
| POST /teams/access-request | Submit access request |

---

## 7. Packaging and Deployment

### 7.1 Teams App Package

- Standard Teams app manifest (manifest.json)
- App icons (color and outline)
- Localization files if needed

### 7.2 App Registration

- Azure AD app registration for Teams SSO
- API permissions for user identity
- Redirect URIs for tab and task module auth

### 7.3 Distribution

- Organization app catalog (internal deployment)
- Optional: Teams App Store (future public availability)
- Admin consent for org-wide deployment

---

## 8. Non-Functional Requirements

### 8.1 Performance

- Messaging extension search responds within 2 seconds
- Personal app initial load under 3 seconds
- Tab renders workspace content progressively

### 8.2 Reliability

- Graceful degradation if Spaarke API unavailable
- Retry logic for transient failures
- Offline indicators where applicable

### 8.3 Security

- Teams SSO with minimal permissions
- No sensitive data cached in Teams client
- Access checks on every operation

### 8.4 Observability

- Correlation IDs from Teams → API → workers
- Metrics: search latency, save success rate, access denials

---

## 9. Required Deliverables

Claude Code must produce for this project:

1. **Detailed UX Spec**
    - Personal app screens and navigation
    - Tab configuration and runtime views
    - Messaging extension search and card design
    - Message action task module flow
    - Access denied and request access flows

2. **Architecture Spec**
    - Teams-specific components
    - Integration with SDAP-office-integration backend
    - Bot Framework integration design

3. **API Contract Spec**
    - Teams-specific endpoints
    - Adaptive Card schemas
    - Tab configuration model

4. **Deployment Spec**
    - Teams manifest configuration
    - App registration requirements
    - Admin deployment guide

5. **Test Plan**
    - Teams client matrix (desktop, web, mobile)
    - SSO scenarios
    - Messaging extension scenarios
    - Permission and access scenarios

---

## 10. Reference Pointers

Teams app development:
- https://learn.microsoft.com/en-us/microsoftteams/platform/overview
- https://learn.microsoft.com/en-us/microsoftteams/platform/tabs/what-are-tabs
- https://learn.microsoft.com/en-us/microsoftteams/platform/messaging-extensions/what-are-messaging-extensions

Adaptive Cards:
- https://adaptivecards.io/
- https://learn.microsoft.com/en-us/microsoftteams/platform/task-modules-and-cards/cards/cards-reference

Teams SSO:
- https://learn.microsoft.com/en-us/microsoftteams/platform/tabs/how-to/authentication/tab-sso-overview

---

## 11. Integration Points

### Depends On (from SDAP-office-integration)

| Artifact | Usage |
|----------|-------|
| Document, Matter, Project entities | Core data model |
| ProcessingJob entity | Job tracking |
| /office/search/* APIs | Search functionality |
| /office/jobs API | Status tracking |
| SpeFileStore | File operations |
| UAC/AuthZ module | Access control |

### Provides To (SDAP-external-portal)

| Artifact | Usage |
|----------|-------|
| Access request patterns | Similar UX for external users |

---

**EOF**
