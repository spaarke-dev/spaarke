## SharePoint Embedded + Graph
- [SPE version comment (2026-09-12)](spe-version-comment-2026-09-12.md) — WRITE-ONLY via checkin{comment}; no read-back in Graph (CSDL); repo checkin drops comment
- [SPE WOPI co-auth lock / 423 (2026-07-30)](spe-wopi-coauthoring-lock-423-2026-07-30.md) — no Graph API releases co-auth lock; checkout/checkin = formal checkout only; ~30-min timeout
- [SPE dedup / content identity (2026-07-14)](spe-dedup-content-identity-2026-07.md) — quickXorHash only; versions API no per-version hash; custom columns queryable
- [SPE + CIAM cross-tenant app-only (2026-07-18)](spe-ciam-crosstenant-apponly-brokering-2026-07-18.md) — CIAM user can't be delegated member; app-only read brokering GREEN
- [Graph + SPE standards spike (2026-08-16)](graph-spe-standards-2026-08-16.md) — Graph 6.5.0/Kiota 2.0 latest; Exchange RBAC-for-Apps; MI-as-FIC GA
- [Graph shared vs group mailbox subs (2026-07-29)](graph-shared-vs-group-mailbox-subscriptions-2026-07-29.md) — shared = User (zero code); M365-group forks pipeline
- [CIAM user provisioning via Graph (2026-07-19)](ciam-user-provisioning-graph-2026-07-19.md) — POST /users; `oid` link key; cross-tenant needs app in CIAM tenant
- [Power Pages vs Entra External ID portal (2026-07-17)](power-pages-vs-entra-external-id-portal-2026-07-17.md) — external portal choice; licensing + SPE-external story

## Word add-in / Office
- [Word add-in save collision path (2026-09-08)](word-addin-save-collision-path-2026-09-08.md) — /api/office/save uses Replace (silent overwrite) vs OBO Fail/409
- [Word desktop document.url for SPE (2026-09-08)](word-desktop-document-url-spe-2026-09-08.md) — AMBER; docs silent on SPE desktop; needs live probe
- [Office Dialog for MDA record (2026-09-08)](office-dialog-mda-record-open-2026-09-08.md) — no evidence MDA works in dialog; REC read-only pane + openBrowserWindow
- [Word doc-identity desk research (2026-09-09)](word-document-identity-desk-research-2026-09-09.md) — customXmlParts=WordApi 1.4 vs manifest 1.3; path/fullName desktop-only
- [Custom XML parts vs markup (2026-09-09)](customxml-parts-vs-markup-manifest-2026-09-09.md) — i4i removed in-body w:customXml only, not parts; manifest capability names
- [Word extensibility platform (2026-09-01)](word-extensibility-platform-2026-09-01.md) — WordApi 1.9 GA; TrackAll only revision-author path; unified manifest + NAA GA
- [Legal-AI redline surfaces (2026-07-22)](legal-ai-redline-surface-landscape-2026-07-22.md) — incumbents redline in Word add-in; TrackAll → native revisions
- [Enterprise Teams tab + Entra SSO (2026-08-02)](teams-app-embed-external-webapp-enterprise-2026-08-02.md) — install paths, SSO/OBO, manifest v1.29, CSP frame-ancestors

## Compose / DOCX / OOXML / editors
- [docx editor fidelity "third answer" (2026-08-19)](docx-editor-fidelity-third-answer-2026-08-19.md) — paraId legally duplicate in AlternateContent; grab-bag carry pattern
- [Word pagination + NFR-03 (2026-07-28)](word-rendering-pagination-nfr03-2026-07-28.md) — page/line is layout-time; Graph format=pdf only Word-native path
- [MS-platform multiformat AI editing (2026-07-22)](ms-platform-multiformat-ai-editing-2026-07-22.md) — SPE+WOPI+Office delegates fidelity; no web Word-clone
- [Editor↔server bridge primitives (2026-07-22)](editor-server-bridge-primitives-2026-07-22.md) — stable-ID anchoring, op schemas, rebasing, .NET OOXML patch
- [SuperDoc license v2 (2026-07-22)](superdoc-license-v2-buildvsbuy-2026-07-22.md) — AGPL-3.0 v1.45.0; no verifiable v2; not byte-preserving
- [Browser docx editor engines (2026-07-22)](browser-docx-editor-engines-landscape-2026-07-22.md) — SuperDoc vs OnlyOffice/Collabora vs Syncfusion vs Aspose
- [Stable position anchoring (2026-07-22)](stable-position-anchoring-ooxml-editor-2026-07-22.md) — paraId/bookmarks/SDT/Yjs relative positions
- [Browser docx market patterns (2026-07-21)](browser-docx-editing-market-patterns-2026-07-21.md) — Word-web/GDocs/OnlyOffice/SDK lane fidelity survey
- [Legal-AI docx editing comparison (2026-07-21)](legal-ai-docx-editing-comparison-2026-07-21.md) — Harvey/Legora/OSS editors round-trip without per-seat engine
- [Server .docx authoring + numbering (2026-07-18)](server-docx-authoring-numbering-2026-07-18.md) — abstractNum recipe, lvlRestart pitfall
- [docx delta fidelity round-trip (2026-07-16)](docx-delta-fidelity-roundtrip-2026-07-16.md) — retained-original+delta; w14:paraId identity; lib licenses
- [ProseMirror AI authoring + TC (2026-07-16)](prosemirror-ai-authoring-trackchanges-2026-07-16.md) — OSS track-changes libs; AI inline-edit UX
- [TipTap licensing + editor arch (2026-07-16)](tiptap-licensing-editor-arch-2026-07-16.md) — MIT base OK; DOCX import/export is Pro → keep server-side
- [OpenXML/DOCX for Compose R2 (2026-06-29)](openxml-docx-compose-r2-2026-06-29.md) — OpenXml SDK 3.x MIT; OpenXmlPowerTools redline; 423 race
- [Adeu architecture study (2026-06-29)](adeu-architecture-study-2026-06-29.md) — CriticMarkup read/write asymmetry; reverse-order batched apply
- [Dataverse Word templates (2026-08-13)](dataverse-word-templates-storage-merge-2026-08-13.md) — documenttemplate vs template vs File column; merge encoding

## Dataverse / Power Platform / MDA
- [Dataverse env provisioning E2E (2026-08-22)](dataverse-env-provisioning-e2e-2026-08-22.md) — 8 API surfaces; restrictGuestUserAccess default TRUE since Mar 2026
- [Record restriction / Secure Project (2026-08-20)](dataverse-record-restriction-secure-project-2026-08-20.md) — no record deny; matrix BUs; secure BU + owner-team
- [Cascade-share parent→child (2026-08-18)](dataverse-cascade-share-parent-child-access-2026-08-18.md) — one parental 1:N Cascade All; rest needs code
- [Record-access security for messaging (2026-07-16)](dataverse-record-access-security-2026-07-16.md) — additive union; impersonation as list filter
- [Web API create rows (2026-07)](dataverse-webapi-create-rows-2026-07.md) — 204+OData-EntityId; @odata.bind schema nav names; upsert headers
- [Dataverse Search config (2026-07-21)](dataverse-search-config-2026-07-21.md) — org enable UI-only; per-table SyncToExternalSearchIndex
- [Dataverse MCP + Foundry MCP auth (2026-07-05)](dataverse-mcp-refresh-2026-07-05.md) — delegated-only; metered; REC native handlers + MCP-shaped contracts
- [knowledgearticle vs SPE P&P library (2026-08-06)](dataverse-knowledgearticle-vs-spe-pnp-library-2026-08-06.md) — restricted table; skip; SPE + sprk_policy
- [appnotification modal-on-click (2026-08-20)](appnotification-modal-click-schema-2026-08-20.md) — navigationTarget:"dialog"; no read/dismiss field
- [navigateTo pre-populate lookup (2026-08-17)](navigateto-prepopulate-lookup-createfromentity-2026-08-17.md) — 3-key {f}/{f}name/{f}type convention
- [Hide form-header entity name (2026-08-18)](uci-hide-form-header-entity-name-2026-08-18.md) — formContext.ui.setFormEntityName(" ")
- [Hide single-tab pivot (2026-08-18)](uci-single-tab-navigator-hide-2026-08-18.md) — headerSection.setTabNavigatorVisible(false) OnLoad
- [UCI open specific view (2026-08-14)](uci-navigate-to-specific-view-2026-08-14.md) — navigateTo viewId unreliable; main.aspx URL reliable
- [UCI global onload hook (2026-08-14)](uci-global-app-onload-hook-2026-08-14.md) — no supported global app-load event
- [MDA custom help pane (2026-08-14)](mda-custom-help-pane-2026-08-14.md) — not a fit; MDA-shell-only, invisible to CIAM
- [PCF React/Fluent versions (2026-07)](pcf-react-platform-library-2026-07.md) — max React 16.14.0 declared; no React 18/19 platform lib
- [PCF client-quality ESLint CI (2026-06)](spaarke-pcf-client-quality-eslint-2026-06.md) — --legacy-peer-deps skipped eslint peer → broke CI lint

## .NET / Azure hosting / messaging
- [.NET 8→10 migration (2026-08-10)](dotnet-8-to-10-migration-2026-08-10.md) — .NET 8 EOS 2026-11-10; top BFF breaking changes
- [.NET 10 breaking-changes re-scrape (2026-08-11)](net10-breaking-changes-rescrape-2026-08-11.md) — no new entries; no scope change
- [.NET 8→10 NuGet compat (2026-08-10)](net10-upgrade-package-compat-2026-08-10.md) — zero blockers
- [Kiota CVE-2026-44503 + TFM (2026-08-11)](kiota-cve-2026-44503-tfm-2026-08-11.md) — fix Kiota.Abstractions 1.22.0; r1 chose Graph 6.5.0/Kiota 2.0
- [.NET 10 on App Service Linux (2026-08-10)](dotnet10-appservice-linux-2026-08-10.md) — DOTNETCORE:10.0 vs DOTNETCORE|10.0; port 8080
- [Functions Flex + .NET 10 (2026-08-13)](functions-flex-consumption-net10-2026-08-13.md) — supported; runtime version '10.0'; Core Tools 4.7.0 regression
- [Azure Managed Redis (2026-06-26)](azure-managed-redis-2026-06-26.md) — RediSearch needs Enterprise policy; pays off only for semantic dedup
- [Assistant push channel (2026-07-15)](assistant-push-channel-2026-07-15.md) — REC Azure SignalR + durable outbox
- [SignalR vs SSE notification fabric (2026-07-16)](signalr-vs-sse-notification-fabric-2026-07-16.md) — defer SignalR to r2 for MDA-only r1
- [ACS Chat integration (2026-07-16)](acs-chat-integration-2026-07-16.md) — transport vs system-of-record; BYOI tokens; UI Library React-19 gap
- [BFF Dataverse HTTP unification (2026-06-23)](bff-dataverse-http-unification-2026-06.md) — orphan named clients; REC IDataverseHttpClient
- [Customer-stamp pricing (2026-08-12)](spaarke-customer-stamp-pricing-2026-08-12.md) — ~$3.5-4.5K/mo per customer; AI Search S1 biggest floor

## AI platform / models / competitors
- [Legal Word-AI + MCP landscape (2026-09-01)](legal-word-ai-mcp-landscape-2026-09-01.md) — Legora MCP client; Harvey server+client; MS Legal Agent in Word
- [MCP ecosystem for publishing (2026-09-01)](mcp-ecosystem-publishing-2026-09-01.md) — spec stateless; Entra no DCR/CIMD → pre-registered clients
- [Legal-AI competitive landscape (2026-07)](legal-ai-competitive-landscape-2026-07.md) — assistant/grid/research/agent-builder quartet validated
- [Azure OpenAI reasoning models (2026-07)](azure-openai-reasoning-models-2026-07.md) — gpt-5 @ medium; westus3 has gpt-5, westus2 not
- [Work IQ GA refresh (2026-07-14)](work-iq-ga-refresh-2026-07-14.md) — GA 2026-06-16; delegated-only; Copilot Credits billing
- [Foundry memory now public](foundry-memory-now-public.md) — concept + how-to docs exist; GAP-memory.md superseded
- [Cosmos Gremlin direction](cosmos-gremlin-direction.md) — soft signals away from Gremlin; no vector support
- [Insights Engine pre-design (2026-05-19)](insights-engine-pre-design-2026-05.md) — five-topic research; four knowledge folders
- [Insights Engine vs /narrate (2026-06-25)](insights-engine-vs-narrate-2026-06-25.md) — /narrate for briefing summaries; IE for matter context
- [File-aware playbook routing (2026-06-19)](file-aware-playbook-routing-2026-06.md) — filename TF-IDF 96%; DI contract model = extraction
- [R6 destination routing (2026-06-19)](r6-destination-routing-2026-06-19.md) — NodeRoutingConfig per-node; PlaybookOutputHandler open
- [Stateful chat memory (2026-06-19)](stateful-chat-memory-2026-06.md) — JIT retrieval; 8K split + recall_session_file tool
- [Lavern multi-agent legal system (2026-05-20)](lavern-multi-agent-legal-system.md) — debate, cited findings, grounding verifier
- [Lavern follow-up (2026-05-20)](lavern-followup-2026-05-20.md) — WebSocket streaming; bounded evaluator gate
- [Lavern seeded datasets (2026-05-20)](lavern-seeded-datasets.md) — 5 legal datasets in SQLite FTS5; pure retrieval
- [Lavern Precedent Board (2026-05-20)](lavern-precedent-board.md) — cross-engagement memory with promotion/decay

## Meta
- [Knowledge base layout](knowledge-base-layout.md) — `knowledge/` tree convention; SOURCE/NOTES/README
