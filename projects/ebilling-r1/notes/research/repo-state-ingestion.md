# Repo state — ingestion, documents, search, notifications (2026-10-14)

Code-mapper inventory of `work/ebilling-r1` at `630373cbb` (= master). Read only; nothing was run. `B` = `src/server/api/Sprk.Bff.Api`. Interpretation lives in `../solution-overview.md`.

## 1. External SPA (Spaarke External Access) — working

- `src/client/external-spa/` (React 18 + Vite, Azure Static Web Apps). Doc: `docs/architecture/external-access-spa-architecture.md`.
- Routes (`src/App.tsx:156-165`): `/`, `/project/:id`, `/upload` (DocumentUploadPage), `/settings`; also JoinPage, OutsideCounselDashboard.
- Widgets (`registry/widgetRegistry.ts:118-256`); outside-counsel (CIAM) users see Projects, Matters, Work Assignments, Documents, Invoices (l.108).
- `widgets/InvoicesWidget.tsx` — **read-only** grid over `sprk_invoice` ("Outside Counsel — Invoices" grid config); access = caller's accessible project set vs `sprk_invoice.sprk_project`. No invoice submission UI.
- Upload: `pages/DocumentUploadPage.tsx` → `POST /api/v1/external/{projects|matters|workassignments|servicerequests}/{id}/documents`; server checks Read then Create, derives the container, stamps creator. `B/Api/ExternalAccess/ExternalProjectDataEndpoints.cs:34-36` header mentions `invoices` routes — mapping unconfirmed.
- Auth: Entra External ID (CIAM) tenant `spaarkeextid`, MSAL in SPA; BFF is a broker (no OBO), app-only to SPE/Dataverse; accessible-record set per caller (`B/Api/ExternalAccess/ExternalModuleDataEndpoints.cs:124-169`).

## 2. Inbound email — working

- `B/Services/Communication/GraphSubscriptionManager.cs` (Graph webhooks, 3-day renew), `InboundPollingBackupService.cs` (5-min poll), `MailboxDeltaReconciliationService.cs`, `IncomingCommunicationProcessor.cs` (dedup, fetch, record, association, attachments, `.eml` archive), `Jobs/Handlers/IncomingCommunicationJobHandler.cs`.
- Mailboxes = `sprk_communicationaccount` rows with `sprk_receiveenabled`; `MailboxVerificationService.cs` auto-subscribes. Shared mailbox unassociated mail → Pending Review (`docs/architecture/communication-service-architecture.md:106,112`).
- Attachments: `B/Services/Email/EmailAttachmentProcessor.cs`, `AttachmentFilterService.cs`, `Services/Communication/CommunicationAttachmentTextService.cs`, `AttachmentActionGate.cs`.
- Association engine (`communication-intelligence-architecture.md:89-133`): name match, attachment→existing document, semantic; `InvoiceNumberDetector` (`INV-…`); Invoice is suggest-only, never auto-filed.
- Finance hook: `AttachmentClassificationJobHandler` (`B/Infrastructure/DI/FinanceModule.cs:158`, feature-flagged) was triggered by `EmailToDocumentJobHandler`, **which no longer exists** — trigger probably disconnected (not traced).
- No reference to `ebilling.spaarke.com` or an `ap@` mailbox anywhere. `src/client/pcf/EmailProcessingMonitor/` has no source.

## 3. Outlook add-in "Save to Spaarke" — working (unified 1.1.1)

- `src/client/office-addins/` (`outlook/`, `word/`, `shared/`); docs `src/client/office-addins/CLAUDE.md`, `docs/architecture/office-outlook-teams-integration-architecture.md`.
- Saves emails/attachments/documents to SPE against a Matter, Project **or Invoice**: `SaveView.tsx` → `useSaveFlow.ts` → `POST /api/office/save` (`B/Api/Office/OfficeEndpoints.cs:185`) → `Workers/Office/UploadFinalizationWorker.cs`.
- Target chosen by the user in `RelatedToPicker.tsx` (`/api/office/search/entities`); `/api/office/quickcreate/{entityType}` can create an Invoice.

## 4. Document create → SPE → profile → index — working, with caveat

- Entry points: `POST /api/office/save`, `POST /api/v1/external/.../documents`, `src/solutions/DocumentUploadWizard/`, `POST /api/v1/documents`, `POST /api/v1/child-records/{table}`.
- Profile: `docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md` (ACT-011). **Doc says the live playbook's Update Record node fails (l.136) and Index Document is never reached (l.137, 141)** — current status unverified.
- Document type option set: EmailAttachment `100000007`; Invoice `100000001` (`docs/architecture/auth-AI-azure-resources.md:475`).
- `sprk_document` → invoice link; `Engine/RegardingFieldMap.cs` maps `sprk_invoice`. Container derived server-side (`B/Services/Documents/DocumentContainerRelocator.cs`, `RecordContainerResolver`; `docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md`).
- Indexing converges on `FileIndexingService.IndexTextInternalAsync` (`.claude/patterns/ai/indexing-pipeline.md`).

## 5. Azure AI Search — invoice index exists but unproven

- `infrastructure/ai-search/` incl. `spaarke-invoices-index.json`; catalog `docs/architecture/AI-SEARCH-INDEX-CATALOG.md` (invoices: **"Schema only", deferred, empty**; `AI-ARCHITECTURE.md:356` "no ingestion"). Deploy: `scripts/ai-search/Deploy-AllIndexes.ps1:264-266`; bicep `infrastructure/bicep/modules/ai-search.bicep`.
- Schema: 3072-dim HNSW cosine; `tenantId`, `invoiceId`, `matterId`, `projectId` filterable.
- `B/Services/Finance/InvoiceSearchService.cs:47` (name `spaarke-invoices-index`; doc says `spaarke-invoices-dev` — code wins), `B/Services/Jobs/Handlers/InvoiceIndexingJobHandler.cs` (registered `FinanceModule.cs:188`).
- New index recipe: schema JSON + `Deploy-AllIndexes.ps1` entry + catalog row + indexing handler + search service; name `spaarke-{noun}-index`.

## 6. Outbound notifications — working

- Email: `B/Services/Communication/CommunicationService.cs` (Graph sendMail; SharedMailbox app-only), `EmailChannelSender.cs`, `ApprovedSenderValidator`, daily limit; playbook `SendEmailNodeExecutor`, `EmailDispositionSender`. No external email template framework found.
- In-SPA: `ExternalNotificationFeedEndpoints.cs:93,109` (feed, mark-seen), `ExternalMessageEndpoints.cs:93,107` (record messages); SPA `api/notifications-client.ts`, `api/messages-client.ts`.
- Spine: `docs/architecture/SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE.md`; `B/Services/Notifications/OutboxService.cs` (`sprk_notificationoutbox`), `SignalRDeliveryService.cs`, `B/Services/NotificationService.cs`.

## 7. Background jobs — working

`B/Services/Jobs/IJobHandler.cs`, `ServiceBusJobProcessor.cs`, `JobSubmissionService.cs`. Handlers incl. `InvoiceExtractionJobHandler`, `InvoiceIndexingJobHandler`, `AttachmentClassificationJobHandler`, `SpendSnapshotGenerationJobHandler`. Scheduled: `IScheduledJob` (e.g. `B/Services/Access/SecureChildReconciliationJob.cs:85`), `RedisScheduledJobLease`.

## Existing Finance pipeline (cross-cutting)

`docs/architecture/finance-intelligence-architecture.md`: classify → human review gate → extract → snapshot → signals → rollup → index.

| Item | Where |
|---|---|
| Services | `B/Services/Finance/` (`InvoiceAnalysisService`, `InvoiceReviewService`, `InvoiceSearchService`, `SpendSnapshotService`, `FinanceRollupService`) |
| Endpoints | `B/Api/Finance/FinanceEndpoints.cs:37,51,66,81` (review confirm/reject, invoice search, matter summary) |
| DI | `B/Infrastructure/DI/FinanceModule.cs:158,175,188` (classification + indexing conditional; extraction always) |
| Playbook config | `B/Configuration/FinanceOptions.cs:55` — `InvoiceExtractionPlaybookId` empty in lower environments |
| Client | `src/solutions/CreateInvoiceWizard/` (client-only create, bypasses server invariants), `src/solutions/sprk_invoicespage/` |

Not found: outside-counsel PDF submission endpoint, guideline/rate checks, LEDES/UTBMS handling. Not covered: whether Finance flags are on in deployed environments; tests.
