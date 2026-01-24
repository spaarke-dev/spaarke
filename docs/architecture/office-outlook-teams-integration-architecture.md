# Office Add-ins Integration Architecture

> **Last Updated**: January 24, 2026
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
│  │  │                 AuthService (Dialog API - V1)                 │   │   │
│  │  │  ┌──────────────┐  ┌──────────────┐  ┌────────────────────┐  │   │   │
│  │  │  │ MSAL.js 3.x  │  │ Dialog API   │  │  Token Cache       │  │   │   │
│  │  │  │ (auth-dialog)│  │ (Primary)    │  │  (Memory+Expiry)   │  │   │   │
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
- **Manifest Format**: XML manifest (recommended for M365 Admin Center deployment)
- **Manifest Location**: `src/client/office-addins/outlook/outlook-manifest.xml`
- **Build Output**: `dist/outlook/manifest.xml`
- **Entry Points**:
  - Task Pane: `outlook/taskpane.html`
- **Capabilities**: Read emails, access attachments, compose integration

#### Word Add-in
- **Manifest Format**: XML manifest
- **Manifest Location**: `src/client/office-addins/word/word-manifest.xml`
- **Build Output**: `dist/word/manifest.xml`
- **Entry Points**:
  - Task Pane: `word/taskpane.html`
- **Capabilities**: Access document content, save to SPE, version tracking

### Azure Static Web App (Add-in Hosting)

| Environment | Resource Name | Hostname |
|-------------|---------------|----------|
| Dev | `spaarke-office-addins` | `icy-desert-0bfdbb61e.6.azurestaticapps.net` |
| Prod | `spe-office-addins-prod` | `spe-office-addins-prod.azurestaticapps.net` |

---

## Manifest Format Requirements

> **CRITICAL**: These requirements were validated through production testing. Non-compliance causes M365 Admin Center validation failures.

### Common Requirements (All Add-ins)

| Element | Requirement | Example |
|---------|-------------|---------|
| **Version** | Must be 4-part format | `1.0.0.0` (NOT `1.0.0`) |
| **Icon URLs** | Must return HTTP 200 | All icon sizes must be accessible |
| **DefaultLocale** | Required | `en-US` |
| **SupportUrl** | Recommended | `https://spaarke.com/support` |
| **AppDomains** | Required | List all domains the add-in uses |

### Outlook Add-in Manifest (MailApp)

**Working Structure** (validated with M365 Admin Center + sideloading):

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<OfficeApp xmlns="http://schemas.microsoft.com/office/appforoffice/1.1"
  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
  xmlns:bt="http://schemas.microsoft.com/office/officeappbasictypes/1.0"
  xmlns:mailappor="http://schemas.microsoft.com/office/mailappversionoverrides/1.0"
  xsi:type="MailApp">

  <Id>{GUID}</Id>
  <Version>1.0.0.0</Version>
  <ProviderName>Spaarke</ProviderName>
  <DefaultLocale>en-US</DefaultLocale>
  <DisplayName DefaultValue="Spaarke Outlook"/>
  <Description DefaultValue="..."/>
  <IconUrl DefaultValue="https://.../icon-32.png"/>
  <HighResolutionIconUrl DefaultValue="https://.../icon-64.png"/>
  <SupportUrl DefaultValue="https://..."/>

  <AppDomains>
    <AppDomain>https://your-domain.azurestaticapps.net</AppDomain>
  </AppDomains>

  <Hosts>
    <Host Name="Mailbox"/>
  </Hosts>

  <Requirements>
    <Sets>
      <Set Name="Mailbox" MinVersion="1.1"/>
    </Sets>
  </Requirements>

  <FormSettings>
    <Form xsi:type="ItemRead">
      <DesktopSettings>
        <SourceLocation DefaultValue="https://.../taskpane.html"/>
        <RequestedHeight>250</RequestedHeight>
      </DesktopSettings>
    </Form>
  </FormSettings>

  <Permissions>ReadWriteItem</Permissions>

  <Rule xsi:type="RuleCollection" Mode="Or">
    <Rule xsi:type="ItemIs" ItemType="Message" FormType="Read"/>
    <Rule xsi:type="ItemIs" ItemType="Message" FormType="Edit"/>
  </Rule>

  <DisableEntityHighlighting>false</DisableEntityHighlighting>

  <!-- SINGLE VersionOverrides V1.0 - NOT nested -->
  <VersionOverrides xmlns="http://schemas.microsoft.com/office/mailappversionoverrides"
    xsi:type="VersionOverridesV1_0">
    <Requirements>
      <bt:Sets DefaultMinVersion="1.3">
        <bt:Set Name="Mailbox"/>
      </bt:Sets>
    </Requirements>
    <Hosts>
      <Host xsi:type="MailHost">
        <DesktopFormFactor>
          <!-- NO FunctionFile element -->
          <ExtensionPoint xsi:type="MessageReadCommandSurface">
            <!-- Button definition -->
          </ExtensionPoint>
        </DesktopFormFactor>
      </Host>
    </Hosts>
    <Resources>
      <!-- Images, URLs, Strings -->
    </Resources>
  </VersionOverrides>
</OfficeApp>
```

**Critical Outlook-Specific Rules**:

| Rule | Reason |
|------|--------|
| **NO FunctionFile** | Causes validation failures in M365 Admin Center |
| **Single VersionOverrides** | Do NOT nest V1.1 inside V1.0 |
| **RuleCollection Mode="Or"** | Use collection, not single Rule |
| **DisableEntityHighlighting** | Must be present |
| **FormType="Read"** for MessageReadCommandSurface | Match extension point to form type |

### Word Add-in Manifest (TaskPaneApp)

**Working Structure**:

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<OfficeApp xmlns="http://schemas.microsoft.com/office/appforoffice/1.1"
  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
  xmlns:bt="http://schemas.microsoft.com/office/officeappbasictypes/1.0"
  xmlns:ov="http://schemas.microsoft.com/office/taskpaneappversionoverrides"
  xsi:type="TaskPaneApp">

  <Id>{GUID}</Id>
  <Version>1.0.0.0</Version>
  <!-- ... basic metadata ... -->

  <AppDomains>
    <AppDomain>https://your-domain.azurestaticapps.net</AppDomain>
  </AppDomains>

  <Hosts>
    <Host Name="Document"/>
  </Hosts>

  <Requirements>
    <Sets>
      <Set Name="WordApi" MinVersion="1.1"/>
    </Sets>
  </Requirements>

  <DefaultSettings>
    <SourceLocation DefaultValue="https://.../taskpane.html"/>
  </DefaultSettings>

  <Permissions>ReadWriteDocument</Permissions>

  <VersionOverrides xmlns="http://schemas.microsoft.com/office/taskpaneappversionoverrides"
    xsi:type="VersionOverridesV1_0">
    <Hosts>
      <Host xsi:type="Document">
        <DesktopFormFactor>
          <ExtensionPoint xsi:type="PrimaryCommandSurface">
            <!-- Button definition -->
          </ExtensionPoint>
        </DesktopFormFactor>
      </Host>
    </Hosts>
    <Resources>
      <!-- Images, URLs, Strings -->
    </Resources>
  </VersionOverrides>
</OfficeApp>
```

### Manifest Validation Checklist

Before uploading to M365 Admin Center:

- [ ] Version is 4-part format: `X.X.X.X`
- [ ] All icon URLs return HTTP 200
- [ ] AppDomains includes all external domains
- [ ] DefaultLocale is set
- [ ] SupportUrl is valid
- [ ] Outlook: NO FunctionFile element
- [ ] Outlook: Single VersionOverrides V1.0 (not nested)
- [ ] Outlook: RuleCollection (not single Rule)
- [ ] Outlook: DisableEntityHighlighting present
- [ ] Word: PrimaryCommandSurface extension point
- [ ] All resource IDs (resid) have matching definitions

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

**Pattern**: Dialog API (Primary) with NAA support (Future)

> **V1 Status (January 2026)**: Dialog API is the production authentication method. NAA (Nested App Authentication) requires dynamic broker redirect URIs (`brk-{GUID}://`) that cannot be pre-registered in Azure AD, making it impractical for current deployments.

```typescript
// AuthService.ts - Core authentication flow
class AuthService {
  private isNaaSupported: boolean = false; // NAA disabled in V1
  private cachedAccessToken: string | null = null;
  private tokenExpiresAt: number | null = null;

  // V1: Dialog API with token caching
  async getAccessToken(): Promise<string> {
    // Check cached token validity (with 5-minute buffer)
    if (this.cachedAccessToken && this.tokenExpiresAt) {
      const bufferMs = 5 * 60 * 1000;
      if ((this.tokenExpiresAt - bufferMs) > Date.now()) {
        return this.cachedAccessToken;
      }
    }
    // Token expired or missing - authenticate via dialog
    return this.getDialogToken();
  }

  // Dialog API: Opens popup for MSAL authentication
  private async getDialogToken(): Promise<string> {
    // Opens auth-dialog.html which handles MSAL.js redirect flow
    // Returns token via Office.context.ui.messageParent()
  }
}
```

**Token Lifecycle**:
- Tokens are cached in memory with expiration tracking
- 5-minute buffer ensures tokens are refreshed before expiry
- Re-authentication triggers automatically when token expires
- Same browser session = silent auth (no login prompts)

**Configuration**:
```typescript
const AUTH_CONFIG = {
  clientId: 'c1258e2d-1688-49d2-ac99-a7485ebd9995',
  tenantId: 'a221a95e-6abc-4434-aecc-e48338a1b2f2',
  bffApiClientId: '1e40baad-e065-4aea-a8d4-4b7ab273458c',
  bffApiBaseUrl: 'https://spe-api-dev-67e2xz.azurewebsites.net',
  redirectUri: 'brk-multihub://localhost', // For future NAA
  fallbackRedirectUri: '', // Production SPA redirect
};
```

**Why Dialog API (Not NAA) in V1**:

| Concern | NAA | Dialog API |
|---------|-----|------------|
| Azure AD Configuration | Requires dynamic `brk-{GUID}://` URIs that vary per Office host session | Standard SPA redirect URIs |
| Microsoft GA Status | Not yet GA (as of Jan 2026) | Production-ready, used for years |
| Cross-host Support | Varies by Office version | Works universally |
| User Experience | Seamless (no popup) | Popup window for auth |

**Future NAA Support**: When Microsoft provides a stable broker URI format (e.g., fixed `brk-multihub://`), NAA can be re-enabled for seamless authentication.

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
| **App ID URI** | `api://1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| **Runtime** | .NET 8 |

**Exposed API Scopes**:

| Scope | Description |
|-------|-------------|
| `SDAP.Access` | Access SDAP resources |
| `user_impersonation` | Access Spaarke BFF API on behalf of user |

> **CRITICAL: Authorized Client Applications**
>
> The Office Add-in client ID **MUST** be registered as an authorized client application in the BFF API's "Expose an API" configuration. Without this, the add-in will receive 401 Unauthorized errors when calling the BFF API.
>
> | Authorized Client | Client ID | Required Scopes |
> |-------------------|-----------|-----------------|
> | **Spaarke Office Add-in** | `c1258e2d-1688-49d2-ac99-a7485ebd9995` | `SDAP.Access`, `user_impersonation` |
> | SDAP-PCF-CLIENT | `170c98e1-d486-4355-bcbe-170454e0207c` | `SDAP.Access`, `user_impersonation` |
>
> **To configure**: Azure Portal → App registrations → SDAP-BFF-SPE-API → Expose an API → Authorized client applications → Add the Office Add-in client ID with both scopes selected.

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

### V1: Dialog API Flow (Primary - Production)

The Dialog API is the current production authentication method, providing reliable cross-platform support:

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   Add-in    │     │ Auth Dialog │     │  Azure AD   │     │   BFF API   │
│ (TaskPane)  │     │(auth-dialog)│     │             │     │             │
└──────┬──────┘     └──────┬──────┘     └──────┬──────┘     └──────┬──────┘
       │                   │                   │                   │
       │ 1. Check cached   │                   │                   │
       │    token valid?   │                   │                   │
       │    ──────────>    │                   │                   │
       │                   │                   │                   │
       │ If expired or missing:                │                   │
       │ 2. displayDialogAsync                 │                   │
       │──────────────────>│                   │                   │
       │                   │                   │                   │
       │                   │ 3. MSAL.js loginRedirect              │
       │                   │──────────────────>│                   │
       │                   │                   │                   │
       │                   │ 4. User authenticates (popup)         │
       │                   │<─────────────────>│                   │
       │                   │                   │                   │
       │                   │ 5. Token + expiresOn                  │
       │                   │<──────────────────│                   │
       │                   │                   │                   │
       │ 6. messageParent  │                   │                   │
       │<──────────────────│                   │                   │
       │   (token cached)  │                   │                   │
       │                   │                   │                   │
       │ 7. API Call + Bearer Token            │                   │
       │──────────────────────────────────────────────────────────>│
       │                   │                   │                   │
       │                   │                   │ 8. OBO Exchange   │
       │                   │                   │<──────────────────│
       │                   │                   │                   │
       │                   │                   │ 9. Graph Token    │
       │                   │                   │──────────────────>│
       │                   │                   │                   │
       │ 10. API Response                      │                   │
       │<──────────────────────────────────────────────────────────│
```

**Key V1 Flow Characteristics**:
- Token cached in memory with expiration timestamp
- 5-minute buffer ensures proactive refresh
- Same browser session = token reuse (no popup)
- Dialog only opens when token missing or expired
- Works across all Office hosts and versions

### Future: NAA Flow (Nested App Authentication)

NAA provides seamless authentication without popups, but requires Azure AD configuration changes not yet available:

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   Add-in    │     │  Office     │     │  Azure AD   │     │   BFF API   │
│  (MSAL.js)  │     │   Host      │     │             │     │             │
└──────┬──────┘     └──────┬──────┘     └──────┬──────┘     └──────┬──────┘
       │                   │                   │                   │
       │ 1. acquireTokenSilent                 │                   │
       │──────────────────>│                   │                   │
       │                   │ 2. Broker request │                   │
       │                   │──────────────────>│                   │
       │                   │ 3. Token          │                   │
       │<──────────────────│<──────────────────│                   │
       │                   │                   │                   │
       │ 4. API Call + Bearer Token            │                   │
       │──────────────────────────────────────────────────────────>│
```

**NAA Blockers (as of January 2026)**:
- Requires dynamic broker URIs: `brk-{GUID}://` where GUID is generated per Office session
- These URIs cannot be pre-registered in Azure AD
- Error: `AADSTS700046: Invalid Reply Address`
- Microsoft has not yet provided a stable `brk-multihub://` URI for registration

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

## Manifest Format Strategy

### V1: XML Manifest (Current Production)

The current V1 implementation uses **XML manifests** for Office Add-ins:

| Host | Manifest Type | Format | Location |
|------|--------------|--------|----------|
| Outlook | MailApp | XML | `outlook/manifest-working.xml` |
| Word | TaskPaneApp | XML | `word/manifest-working.xml` |

**Why XML Manifest for V1**:
- Production-proven format used for years
- Full M365 Admin Center support
- Works with Dialog API authentication
- No dependency on NAA or broker URIs
- Widely documented with clear validation rules

### V2: Unified Manifest (Future - Office + Teams)

Microsoft is developing a **Unified Manifest** (JSON format) that works across platforms:

| Feature | XML Manifest (V1) | Unified Manifest (V2) |
|---------|-------------------|----------------------|
| **Format** | XML per Office host | Single JSON file |
| **Platforms** | Individual Office apps | Office + Teams + Outlook.com |
| **Authentication** | Dialog API or NAA | SSO-first with NAA |
| **Distribution** | Separate sideload per host | Single package deployment |
| **Teams Integration** | Separate Teams app | Unified with Office Add-in |
| **Status** | Production GA | Preview (as of Jan 2026) |

**Unified Manifest Structure Preview**:
```json
{
  "$schema": "https://developer.microsoft.com/json-schemas/teams/vDevPreview/MicrosoftTeams.schema.json",
  "manifestVersion": "devPreview",
  "id": "{app-guid}",
  "version": "1.0.0",
  "name": {
    "short": "Spaarke",
    "full": "Spaarke Document Access Platform"
  },
  "developer": {
    "name": "Spaarke",
    "websiteUrl": "https://spaarke.com"
  },
  "extensions": [
    {
      "requirements": {
        "capabilities": [{ "name": "Mailbox", "minVersion": "1.5" }]
      },
      "runtimes": [...],
      "ribbons": [...]
    }
  ],
  "webApplicationInfo": {
    "id": "{client-id}",
    "resource": "api://{domain}/{client-id}"
  }
}
```

### Migration Path: V1 → V2

| Phase | Timeline | Action |
|-------|----------|--------|
| **V1 (Current)** | Now - 2026 | Use XML manifests with Dialog API |
| **V2 Preparation** | When NAA GA | Test Unified Manifest in dev environment |
| **V2 Migration** | Post-NAA GA | Convert to Unified Manifest for combined Office+Teams deployment |
| **Legacy Support** | Ongoing | Maintain XML manifests for customers not on latest Office |

**When to Migrate to Unified Manifest**:
- ✅ NAA becomes GA with stable broker URI format
- ✅ Microsoft deprecates XML manifest support (not announced)
- ✅ Need unified Office + Teams deployment
- ✅ Customer has latest Office 365 versions

**When to Stay on XML Manifest**:
- ❌ NAA not yet GA
- ❌ Need to support older Office versions
- ❌ Separate Office and Teams apps acceptable
- ❌ Production stability priority

### SDAP Teams App (Project 2/3)

The SDAP Teams App uses a separate **Teams App Manifest** (JSON, Teams-native) since it targets Teams-specific surfaces:

| Surface | Platform | Manifest |
|---------|----------|----------|
| Personal App | Teams | Teams App Manifest |
| Configurable Tab | Teams | Teams App Manifest |
| Messaging Extension | Teams | Teams App Manifest |
| Message Action | Teams | Teams App Manifest |

This will merge with the Office Add-in manifest when Unified Manifest becomes production-ready.

---

## Related Documentation

- [spec.md](../../projects/sdap-office-integration/spec.md) - Full project specification
- [ADR-021](../../.claude/adr/ADR-021-fluent-ui-v9-design-system.md) - Fluent UI v9 requirements
- [PCF Development Guide](../guides/PCF-V9-PACKAGING.md) - Shared component patterns
- [Auth Standards](../standards/auth-standards.md) - Authentication patterns
- [Office Add-ins Admin Guide](../guides/office-addins-admin-guide.md) - Deployment and administration
- [Office Add-ins Deployment Checklist](../guides/office-addins-deployment-checklist.md) - Pre-deployment validation

---

## Revision History

| Date | Author | Changes |
|------|--------|---------|
| January 24, 2026 | AI-Assisted | Added authorized client configuration requirement; documented CORS; updated manifest file locations; added SWA hostnames |
| January 22, 2026 | AI-Assisted | Updated auth to Dialog API primary; added Manifest Format Strategy section |
| January 21, 2026 | AI-Assisted | Initial architecture documentation |
