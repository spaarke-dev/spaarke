# Office Add-ins Integration Architecture

> **Last Updated**: January 21, 2026
> **Status**: Production Ready
> **Project**: SDAP Office Integration

---

## Overview

The SDAP Office Add-ins provide integration between Microsoft Office applications (Outlook and Word) and the Spaarke Document Access Platform (SDAP). This enables users to save emails, attachments, and documents directly to SharePoint Embedded containers with AI-powered metadata extraction.

### Key Capabilities

- **Email Artifact Capture**: Save emails with full metadata (sender, recipients, dates, subjects)
- **Attachment Processing**: Extract and process email attachments with AI analysis
- **Document Integration**: Save Word documents with version tracking
- **AI-Powered Metadata**: Automatic extraction of topics, entities, and summaries
- **Unified Experience**: Consistent UI across Outlook and Word using Fluent UI v9

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Microsoft Office Host                              │
│  ┌─────────────────────────────┐    ┌─────────────────────────────┐        │
│  │      Outlook Client         │    │       Word Client           │        │
│  │  (Desktop/Web/Mobile)       │    │   (Desktop/Web/Mobile)      │        │
│  └─────────────┬───────────────┘    └─────────────┬───────────────┘        │
│                │                                  │                         │
│  ┌─────────────▼───────────────┐    ┌─────────────▼───────────────┐        │
│  │    Outlook Add-in           │    │      Word Add-in            │        │
│  │   (manifest.json)           │    │    (manifest.xml)           │        │
│  └─────────────┬───────────────┘    └─────────────┬───────────────┘        │
└────────────────┼────────────────────────────────────┼───────────────────────┘
                 │                                    │
                 └──────────────┬─────────────────────┘
                                │
┌───────────────────────────────▼─────────────────────────────────────────────┐
│                         Office Add-in Runtime                                │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                        Task Pane (React)                             │   │
│  │  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────┐  │   │
│  │  │   SavePanel     │  │  FolderPicker   │  │   StatusDisplay     │  │   │
│  │  │   Component     │  │   Component     │  │   Component         │  │   │
│  │  └────────┬────────┘  └────────┬────────┘  └─────────────────────┘  │   │
│  │           │                    │                                     │   │
│  │  ┌────────▼────────────────────▼────────────────────────────────┐   │   │
│  │  │                    Host Adapter Layer                         │   │   │
│  │  │  ┌──────────────────┐    ┌──────────────────┐                │   │   │
│  │  │  │  OutlookAdapter  │    │   WordAdapter    │                │   │   │
│  │  │  │  (IHostAdapter)  │    │  (IHostAdapter)  │                │   │   │
│  │  │  └──────────────────┘    └──────────────────┘                │   │   │
│  │  └──────────────────────────────┬───────────────────────────────┘   │   │
│  │                                 │                                    │   │
│  │  ┌──────────────────────────────▼───────────────────────────────┐   │   │
│  │  │                      AuthService (NAA)                        │   │   │
│  │  │  ┌──────────────┐  ┌──────────────┐  ┌────────────────────┐  │   │   │
│  │  │  │ MSAL.js 3.x  │  │  NAA Auth    │  │  Dialog Fallback   │  │   │   │
│  │  │  │ (Browser)    │  │  Primary     │  │  (Legacy hosts)    │  │   │   │
│  │  │  └──────────────┘  └──────────────┘  └────────────────────┘  │   │   │
│  │  └──────────────────────────────────────────────────────────────┘   │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
                                │
                                │ HTTPS (Bearer Token)
                                ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                            BFF API Layer                                     │
│                  (spe-api-dev-67e2xz.azurewebsites.net)                     │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                     Office Integration Endpoints                     │   │
│  │  POST /api/office/emails           - Create email artifact          │   │
│  │  POST /api/office/attachments      - Save attachment                │   │
│  │  POST /api/office/documents        - Save document                  │   │
│  │  GET  /api/office/folders          - Browse container folders       │   │
│  │  GET  /api/office/recent           - Recent save locations          │   │
│  │  POST /api/office/process          - Trigger AI processing          │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                     Token Exchange (OBO Flow)                        │   │
│  │  User Token → BFF API Token → Microsoft Graph Token                 │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└────────────────────────────────┬────────────────────────────────────────────┘
                                 │
          ┌──────────────────────┼──────────────────────┐
          │                      │                      │
          ▼                      ▼                      ▼
┌─────────────────┐   ┌─────────────────┐   ┌─────────────────────┐
│ SharePoint      │   │   Dataverse     │   │   Azure AI          │
│ Embedded (SPE)  │   │   (CRM)         │   │   Services          │
│                 │   │                 │   │                     │
│ - File Storage  │   │ - EmailArtifact │   │ - Azure OpenAI      │
│ - Containers    │   │ - Attachment    │   │ - Doc Intelligence  │
│ - Permissions   │   │ - ProcessingJob │   │ - AI Search         │
└─────────────────┘   └─────────────────┘   └─────────────────────┘
```

---

## Core Components

### 1. Office Add-ins

#### Outlook Add-in
- **Manifest Format**: Unified JSON manifest (GA for Outlook)
- **Manifest Location**: `src/client/office-addins/outlook/manifest.json`
- **Entry Points**:
  - Task Pane: `outlook/taskpane.html`
  - Commands: `outlook/commands.html`
- **Capabilities**: Read emails, access attachments, compose integration

#### Word Add-in
- **Manifest Format**: XML manifest (Unified is preview for Word)
- **Manifest Location**: `src/client/office-addins/word/manifest.xml`
- **Entry Points**:
  - Task Pane: `word/taskpane.html`
  - Commands: `word/commands.html`
- **Capabilities**: Access document content, save to SPE, version tracking

### 2. Host Adapter Layer

The Host Adapter pattern provides a unified interface for interacting with different Office hosts:

```typescript
interface IHostAdapter {
  // Host identification
  getHostType(): HostType;

  // Content extraction
  getCurrentItem(): Promise<IOfficeItem>;
  getAttachments(): Promise<IAttachment[]>;
  getDocumentContent(): Promise<IDocumentContent>;

  // UI integration
  showNotification(message: string, type: NotificationType): void;

  // Lifecycle
  initialize(): Promise<void>;
  dispose(): void;
}
```

**Implementations**:
- `OutlookAdapter`: Handles email and attachment operations via `Office.context.mailbox`
- `WordAdapter`: Handles document operations via `Office.context.document`

### 3. Authentication Service

**Pattern**: Nested App Authentication (NAA) with Dialog API fallback

```typescript
// AuthService.ts - Core authentication flow
class AuthService {
  // Primary: NAA (Nested App Authentication)
  async getAccessToken(): Promise<string> {
    if (this.supportsNAA()) {
      return this.getNAAToken();
    }
    return this.getDialogToken();
  }

  // Fallback: Dialog API for legacy hosts
  private async getDialogToken(): Promise<string> {
    // Opens popup for MSAL authentication
  }
}
```

**Configuration**:
```typescript
const AUTH_CONFIG = {
  clientId: 'c1258e2d-1688-49d2-ac99-a7485ebd9995',
  tenantId: 'a221a95e-6abc-4434-aecc-e48338a1b2f2',
  bffApiClientId: '1e40baad-e065-4aea-a8d4-4b7ab273458c',
  bffApiBaseUrl: 'https://spe-api-dev-67e2xz.azurewebsites.net',
  redirectUri: 'brk-multihub://localhost',
};
```

### 4. UI Components

All UI components use **Fluent UI v9** per ADR-021:

| Component | Purpose | Location |
|-----------|---------|----------|
| `SavePanel` | Main save workflow UI | `shared/components/SavePanel.tsx` |
| `FolderPicker` | Container/folder browser | `shared/components/FolderPicker.tsx` |
| `MetadataEditor` | Edit extracted metadata | `shared/components/MetadataEditor.tsx` |
| `StatusDisplay` | Processing status indicator | `shared/components/StatusDisplay.tsx` |
| `RecentLocations` | Quick access to recent saves | `shared/components/RecentLocations.tsx` |

**Theme Support**: Full dark mode support using `webLightTheme` and `webDarkTheme` tokens.

---

## Azure Resources

### App Registration (Add-in)

| Property | Value |
|----------|-------|
| **Application Name** | SDAP Office Add-in |
| **Application (Client) ID** | `c1258e2d-1688-49d2-ac99-a7485ebd9995` |
| **Directory (Tenant) ID** | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| **Supported Account Types** | Single tenant |

**Redirect URIs**:

| Platform | URI | Purpose |
|----------|-----|---------|
| Mobile and desktop applications | `brk-multihub://localhost` | NAA authentication (required) |
| Single-page application | `https://{production-domain}/taskpane.html` | Production deployment |

**API Permissions**:

| API | Permission | Type | Purpose |
|-----|------------|------|---------|
| Microsoft Graph | `User.Read` | Delegated | User profile |
| Microsoft Graph | `Mail.Read` | Delegated | Read emails |
| Microsoft Graph | `Files.ReadWrite.All` | Delegated | SPE file access |
| BFF API | `access_as_user` | Delegated | API access |

**Token Configuration**:
- ID Token Claims: `login_hint`, `preferred_username`
- Microsoft Graph permissions enabled

### BFF API (Backend)

| Property | Value |
|----------|-------|
| **Service Name** | spe-api-dev-67e2xz |
| **URL** | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| **Application ID** | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| **Runtime** | .NET 8 |

**Office Integration Endpoints**:

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/office/emails` | Create email artifact in Dataverse + SPE |
| `POST` | `/api/office/attachments` | Save attachment to SPE container |
| `POST` | `/api/office/documents` | Save Word document to SPE |
| `GET` | `/api/office/folders` | List available containers and folders |
| `GET` | `/api/office/folders/{id}` | Get folder contents |
| `GET` | `/api/office/recent` | Get user's recent save locations |
| `POST` | `/api/office/process` | Trigger AI processing job |
| `GET` | `/api/office/process/{id}/status` | Check processing status |

### Azure AI Services

| Service | Resource Name | Purpose |
|---------|---------------|---------|
| Azure OpenAI | `spaarke-openai-dev` | Entity extraction, summarization |
| Document Intelligence | `spaarke-docintel-dev` | Document parsing, OCR |
| AI Search | `spaarke-search-dev` | Full-text search indexing |

---

## Dataverse Schema

> **Reference**: [DATAVERSE-TABLE-SCHEMAS.md](../../projects/sdap-office-integration/notes/DATAVERSE-TABLE-SCHEMAS.md)

### EmailArtifact Table (`sprk_emailartifact`)

Stores metadata and body snapshots for emails saved from Outlook.

| Column | Type | Max Length | Description |
|--------|------|------------|-------------|
| `sprk_emailartifactid` | GUID (PK) | - | Unique identifier |
| `sprk_name` | String (Primary) | 400 | Auto-generated from Subject + Date |
| `sprk_subject` | String | 400 | Email subject line (searchable) |
| `sprk_sender` | String | 320 | Sender email address (searchable) |
| `sprk_recipients` | Multiline | 10000 | JSON array of recipient objects |
| `sprk_ccrecipients` | Multiline | 10000 | JSON array of CC recipient objects |
| `sprk_sentdate` | DateTime | - | When email was sent |
| `sprk_receiveddate` | DateTime | - | When email was received |
| `sprk_messageid` | String (indexed) | 256 | Internet message ID from headers |
| `sprk_internetheadershash` | String (indexed) | 64 | SHA256 hash for duplicate detection |
| `sprk_conversationid` | String | 256 | Email conversation/thread ID |
| `sprk_importance` | Choice | - | Low=0, Normal=1, High=2 |
| `sprk_hasattachments` | Yes/No | - | Boolean flag |
| `sprk_bodypreview` | Multiline | 2000 | First 2000 chars of email body (searchable) |
| `sprk_document` | Lookup | - | Lookup to Document (`sprk_document`) |

**Indexes**:
- `sprk_messageid` (duplicate detection)
- `sprk_internetheadershash` (duplicate detection)

### AttachmentArtifact Table (`sprk_attachmentartifact`)

Tracks email attachments saved as separate documents.

| Column | Type | Max Length | Description |
|--------|------|------------|-------------|
| `sprk_attachmentartifactid` | GUID (PK) | - | Unique identifier |
| `sprk_name` | String (Primary) | 260 | Original filename |
| `sprk_originalfilename` | String | 260 | Filename from email (searchable) |
| `sprk_contenttype` | String | 100 | MIME type (e.g., application/pdf) |
| `sprk_size` | Whole Number | - | File size in bytes |
| `sprk_contentid` | String | 256 | For inline attachments (embedded images) |
| `sprk_isinline` | Yes/No | - | True for embedded images in HTML |
| `sprk_emailartifact` | Lookup | - | Lookup to EmailArtifact (N:1) |
| `sprk_document` | Lookup | - | Lookup to Document (N:1) |

**Relationships**:
- `sprk_attachmentartifact_EmailArtifact_1n` (Many attachments to one email)
- `sprk_attachmentartifact_Document_1n` (Many attachments to one document)

### ProcessingJob Table (`sprk_processingjob`)

Tracks async processing jobs following **ADR-004** job contract pattern.

| Column | Type | Max Length | Description |
|--------|------|------------|-------------|
| `sprk_processingjobid` | GUID (PK) | - | Unique identifier |
| `sprk_name` | String (Primary) | 100 | Auto-generated job ID (GUID) |
| `sprk_jobtype` | Choice | - | DocumentSave=0, EmailSave=1, ShareLinks=2, QuickCreate=3, ProfileSummary=4, Indexing=5, DeepAnalysis=6 |
| `sprk_status` | Choice (Required) | - | Pending=0, InProgress=1, Completed=2, Failed=3, Cancelled=4 |
| `sprk_stages` | Multiline | 10000 | JSON array of stage definitions |
| `sprk_currentstage` | String | 100 | Name of currently executing stage |
| `sprk_stagestatus` | Multiline | 10000 | JSON object of stage statuses |
| `sprk_progress` | Whole Number | - | 0-100 percentage |
| `sprk_starteddate` | DateTime | - | When job began processing |
| `sprk_completeddate` | DateTime | - | When job finished (success or failure) |
| `sprk_errorcode` | String | 50 | Error code if failed (e.g., OFFICE_001) |
| `sprk_errormessage` | Multiline | 2000 | Detailed error message |
| `sprk_retrycount` | Whole Number | - | Number of retry attempts |
| `sprk_idempotencykey` | String (indexed) | 64 | SHA256 hash for duplicate prevention |
| `sprk_correlationid` | String | 36 | GUID for distributed tracing |
| `sprk_initiatedby` | Lookup | - | Lookup to User (`systemuser`) |
| `sprk_document` | Lookup | - | Lookup to Document (`sprk_document`) |
| `sprk_payload` | Multiline | 50000 | JSON input data for the job |
| `sprk_result` | Multiline | 50000 | JSON output data from the job |

**Indexes**:
- `sprk_idempotencykey` (duplicate job prevention)
- `sprk_status` (active job queries)

**Relationships**:
- `sprk_processingjob_SystemUser_1n` (Many jobs to one user)
- `sprk_processingjob_Document_1n` (Many jobs to one document)

---

## Authentication Flow

### NAA Flow (Primary - Modern Hosts)

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   Add-in    │     │  Office     │     │  Azure AD   │     │   BFF API   │
│  (MSAL.js)  │     │   Host      │     │             │     │             │
└──────┬──────┘     └──────┬──────┘     └──────┬──────┘     └──────┬──────┘
       │                   │                   │                   │
       │ 1. getAccessToken │                   │                   │
       │──────────────────>│                   │                   │
       │                   │                   │                   │
       │                   │ 2. Token Request  │                   │
       │                   │──────────────────>│                   │
       │                   │                   │                   │
       │                   │ 3. Access Token   │                   │
       │                   │<──────────────────│                   │
       │                   │                   │                   │
       │ 4. Token (nested) │                   │                   │
       │<──────────────────│                   │                   │
       │                   │                   │                   │
       │ 5. API Call + Bearer Token            │                   │
       │──────────────────────────────────────────────────────────>│
       │                   │                   │                   │
       │                   │                   │ 6. OBO Exchange   │
       │                   │                   │<──────────────────│
       │                   │                   │                   │
       │                   │                   │ 7. Graph Token    │
       │                   │                   │──────────────────>│
       │                   │                   │                   │
       │ 8. API Response                       │                   │
       │<──────────────────────────────────────────────────────────│
```

### Dialog Flow (Fallback - Legacy Hosts)

Used when NAA is not supported (older Office versions):

1. Add-in opens popup dialog
2. Dialog loads MSAL.js authentication page
3. User authenticates in popup
4. Token passed back to add-in via `Office.context.ui.messageParent()`
5. Add-in uses token for API calls

---

## Configuration

### Environment Variables

| Variable | Description | Default (Dev) |
|----------|-------------|---------------|
| `ADDIN_CLIENT_ID` | Add-in app registration client ID | `c1258e2d-1688-49d2-ac99-a7485ebd9995` |
| `TENANT_ID` | Azure AD tenant ID | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `BFF_API_CLIENT_ID` | BFF API app registration ID | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `BFF_API_BASE_URL` | BFF API endpoint URL | `https://spe-api-dev-67e2xz.azurewebsites.net` |

### Configuration Files

| File | Purpose |
|------|---------|
| `.env` | Local environment variables (gitignored) |
| `.env.example` | Template for environment setup |
| `webpack.config.js` | Build configuration with DefinePlugin |

### Build Configuration

The webpack configuration injects environment variables at build time:

```javascript
new webpack.DefinePlugin({
  'process.env.ADDIN_CLIENT_ID': JSON.stringify(ENV_CONFIG.ADDIN_CLIENT_ID),
  'process.env.TENANT_ID': JSON.stringify(ENV_CONFIG.TENANT_ID),
  'process.env.BFF_API_CLIENT_ID': JSON.stringify(ENV_CONFIG.BFF_API_CLIENT_ID),
  'process.env.BFF_API_BASE_URL': JSON.stringify(ENV_CONFIG.BFF_API_BASE_URL),
}),
```

---

## Development Setup

### Prerequisites

- Node.js 18+
- npm 9+
- Office desktop application or Office Online access
- Azure AD credentials for test tenant

### Local Development

```bash
# Install dependencies
cd src/client/office-addins
npm install

# Create .env from template
cp .env.example .env
# Edit .env with your values

# Start development server
npm start

# Build for production
npm run build
```

### Sideloading for Testing

**Outlook (Web)**:
1. Go to Outlook.com or Office.com
2. Open Settings > Integrated apps > Get add-ins
3. Select "My add-ins" > "Add a custom add-in" > "Add from file"
4. Upload `dist/outlook/manifest.json`

**Word (Desktop)**:
1. Open Word
2. Go to Insert > Add-ins > My Add-ins
3. Select "Upload My Add-in"
4. Upload `dist/word/manifest.xml`

---

## Security Considerations

### Token Handling
- Tokens are never stored in localStorage (memory only)
- Token refresh handled automatically by MSAL.js
- OBO flow ensures BFF API tokens have minimal scope

### Data Protection
- All API calls over HTTPS
- No sensitive data cached client-side
- Email content processed server-side only

### Permissions
- Minimal Graph permissions requested
- User consent required for each permission
- Admin consent available for organization-wide deployment

---

## Related Documentation

- [spec.md](../../projects/sdap-office-integration/spec.md) - Full project specification
- [ADR-021](../../.claude/adr/ADR-021-fluent-ui-v9-design-system.md) - Fluent UI v9 requirements
- [PCF Development Guide](../guides/PCF-V9-PACKAGING.md) - Shared component patterns
- [Auth Standards](../standards/auth-standards.md) - Authentication patterns

---

## Revision History

| Date | Author | Changes |
|------|--------|---------|
| January 2026 | AI-Assisted | Initial architecture documentation |
