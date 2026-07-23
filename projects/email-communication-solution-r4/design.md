# Email Communication Solution R4 — Technical Review, Assessment & Plan

> **Status**: Draft for review — pre-spec (rev 2, incorporates product-owner feedback 2026-07-14). No implementation authorized from this document.
> **Date**: 2026-07-14
> **Scope owner**: Spaarke platform
> **Evidence discipline**: Claims tagged **[Verified]** (confirmed against current `master` source or an authoritative Microsoft doc, July 2026), **[Judgment]**, or **[Open]** (needs a decision or P0 spike). Every code path was confirmed by eight parallel July-2026 audits of `src/` on `master`; every Microsoft-platform claim carries a dated source in §4.
> **Supersedes framing in**: `projects/spaarke-email-intelligence-module/EMAIL-TRIAGE-MODULE-DESIGN.md` where it cites now-legacy components (see §3.5).

---

## 0. Executive Summary

Spaarke's email backbone is **more capable than its own docs suggest, cleanly Graph-based on the go-forward path, and carrying one legacy subsystem that must be retired.** Rev 2 folds in the product owner's direction: this is not just an *association* project — it is the **communication intelligence** iteration, and it must be built channel-extensible, outbound-symmetric, auth-aligned, and Microsoft-compliant.

Seven load-bearing findings:

1. **The canonical path is 100% Graph + `sprk_communication`; there is NO Server-Side Sync and NO dependency on OOB `email` activities on it.** R2 (2026-03) delivered Graph change-notification subscriptions → `IncomingCommunicationProcessor` → `IncomingAssociationResolver` → `sprk_communication` + `.eml`-to-SPE + RAG. **[Verified]**

2. **One legacy subsystem *does* bind to OOB `email` activities and must be retired.** `Services/Email/` + `Api/EmailEndpoints.cs` (`/api/v1/emails/*`) — including `EmailAssociationService` and the `EmailToEmlConverter`, whose webhook literally fires on `PrimaryEntityName=="email"`. It is dead *because* Spaarke produces no `email` activities (no SSS). **R4 retires it** and reuses only its *confidence-scoring signal design*, reimplemented against Graph/`sprk_communication`. This corrects the rev-1 framing. **[Verified — DEC-2]**

3. **Inbound and outbound are asymmetric and must be unified.** Inbound auto-associates + AI-analyzes + RAG-indexes. **Outbound (sent mail) does NONE of the association, no RAG indexing** — only caller-supplied regarding + AI analysis. R4's central architectural move is a **direction-agnostic `ICommunicationEnrichmentService`** invoked by *both* paths, so sent email gets the same association, categorization, and indexing. **[Verified — requirement #6]**

4. **The matching engine already exists in thin form and must be generalized — channel-agnostically.** Today `IncomingAssociationResolver` resolves matter/contact/org via 3 deterministic rungs, inbound-only, over a `Microsoft.Graph.Message`. R4 builds a **unified Email/Communication Association Engine** over a **normalized message envelope** (not a Graph type), with confidence + provenance, an AI rung, and the full eight-entity target set. The normalized envelope is what makes Teams/Slack/Gmail/SMS extensible later (#2). **[Verified/Judgment]**

5. **"Responsive intelligence" is mostly a wiring job on shipped infrastructure.** `AppOnlyAnalysisService.AnalyzeEmailAsync` already assesses an email (+ attachments) via the "Email Analysis" playbook. The go-forward action surface is **Action + Binding + OutputRouter + EventRules**. The gap: OutputRouter's `record` and `notification` dispositions are **stubbed (throw `NotSupportedException`)**, and email analysis doesn't fan out to actions. R4 completes those legs so an assessed email can create Events/Tasks, notify an assignee, and emit a summary/checklist under declarative rules. **[Verified — requirement #9]**

6. **Microsoft moved the platform under us, with two hard deadlines, and compliance is mandatory.** EWS **enforced-off 2026-10-01**; non-draft sensitive-property writes need admin-consent `Mail-Advanced.*` **enforced 2026-12-31**; Outlook add-in go-forward is the **unified JSON manifest + NAA** (legacy Exchange tokens already off). Work IQ went GA but is **out of scope** (delegated-only, not our app-only classifier). **[Verified — §4; requirements #11, #12]**

7. **The Outlook add-in is currently broken and the UI needs a purpose-built surface.** The add-in's save path is well-built but non-functional in its current auth/manifest state (§5). The `sprk_communication` record still shows the **auto-generated OOB form**; R3 designed (but did not build) an email Code Page. R4 builds a **channel-aware communication Code Page** — designed for all communication types, not email alone. **[Verified — requirements #7, #13]**

**The R4 thesis (rev 2):** deliver a **channel-extensible Communication Intelligence layer** — (a) a unified, direction-symmetric enrichment service wrapping a deterministic-first, AI-assisted **Association Engine** over a normalized envelope; (b) a **Responsive Intelligence** capability that turns an assessed communication into rule-driven actions; (c) a **channel-aware communication Code Page**; all (d) built on the central Auth/MI primitives, with (e) config-driven indexes and (f) the mandatory Microsoft-currency hardening. Retire the OOB-email legacy path. The Email Triage product (§3.5, §10) consumes this layer.

**What R4 is NOT:** a Teams/Slack/SMS *implementation* (only the seams), the full Triage Workbench, or Work IQ integration.

> **Project consolidation (2026-07-14):** R4 **absorbs R3** (see §0.6). R3 (`email-communication-solution-r3`) was fully designed + decomposed into 79 tasks but **never executed — zero code landed**. Rather than run two overlapping projects and coordinate their four shared surfaces by hand (the `sprk_communication` schema, the Communication ADR, the Code Page, and the server send-path changes), R4 becomes the **single unified project** covering both R3's send-side client consolidation and R4's receive-side intelligence. R3's design/spec/tasks become reference input.

---

## 0.6 Project consolidation — R3 + R4 unified

### Why one project
R3 was designed, spec'd, and decomposed into 79 tasks (8 waves) but **never executed** — confirmed 2026-07-14: no `<EmailComposer />` component, no `sprk_emailcomposer` Code Page, no ADR-033, no code on `master` (the R3 merge PRs were scaffolding + task decomposition only). R3 and R4 collide on **four shared surfaces**; running them separately would force manual coordination on each (and risk missing a coordination point):

| Shared surface | R3 touches | R4 touches | Unified handling |
|---|---|---|---|
| `sprk_communication` schema | `sprk_inreplyto`, `sprk_internetmessageid` (reply-thread) | `Suggested`/`Ambiguous` statuses, `sprk_associationprovenance`, `sprk_regardingservicerequest`, `sprk_event` catalog, org-target fix | **One schema wave (W0)** |
| Communication ADR | new ADR-033 (client canonical send) | association-engine + channel-seam ADR | **One ADR (W0)** — supersedes the separate ADR-033 plan |
| Code Page | `sprk_emailcomposer` (email send) | channel-aware view/review page | **One channel-aware page (W4)** that mounts the composer |
| BFF server changes | `AttachmentDriveItemIds` rename, `Internet-Message-Id` capture | `ICommunicationEnrichmentService`, engine | **One server foundation (W0) + engine waves** |

### The parallelization win
R4's server work (C#: engine, enrichment, intelligence) and R3's client work (TS: composer, wrappers) are **disjoint by file and language** → they execute in parallel. Two separate projects would have serialized them by accident. `/task-create` on the unified design will surface these parallel-safe groups automatically.

### Mechanics of the merge (no code to reconcile)
1. **R4 is the unified home**; R3 is marked **SUPERSEDED — absorbed into R4** (its `spec.md`/`plan.md`/`tasks/` remain as reference; a pointer file is added). No R3 code exists to migrate.
2. This design.md is the single source; R3's send-side scope is folded in as first-class waves (§9).
3. Next step regenerates **one** task decomposition via `/design-to-spec` → `/project-pipeline` → `/task-create`, superseding both R3's 79 tasks and R4's phase list.
4. R3's detailed design is preserved **self-contained** inside R4 at [`reference/r3-send-side-design.md`](reference/r3-send-side-design.md) (§5–§10 — composer props, wrappers, Code Page URL contract, wave-by-wave migration) and summarized inline in §8.5; the retired original lives at `projects/x-email-communication-solution-r3/`. R4 has no runtime dependency on the retired folder.

### Absorbed R3 scope (now R4 waves — see §9; full detail §8.5)
- Canonical `<EmailComposer />` engine + 3 semantic wrappers (`SendEmailStep`/`Dialog`/`Page`).
- `sendCommunication()` refinements (`SendCommunicationError`, `attachmentDriveItemIds` fix).
- Retire the 6 ad-hoc send implementations + LegalWorkspace forks + `sprk_communication_send.js` webresource.
- Migrate 5 create-record wizards + SummarizeFilesDialog + FilePreviewDialog + DocumentEmailWizard.
- Reply/forward thread closure (`Internet-Message-Id` capture) — now feeds R4's thread rung (§5.3 rung 1).

The complete send-side authoring detail (composer props/modes/mounts, wrapper APIs, Code Page URL contract, wave-by-wave caller migration) is preserved **self-contained** at [`reference/r3-send-side-design.md`](reference/r3-send-side-design.md) and summarized inline in **§8.5**. R4 does not depend on the retired R3 folder.

---

## 1. Question 1 — Confirm existing email capabilities & assess the architecture

### 1.1 Graph integration — [Verified]

Centralized in `Infrastructure/Graph/GraphClientFactory.cs`, three paths:

| Path | Method | Auth | Use |
|---|---|---|---|
| App-only (canonical) | `ForApp()` | `DefaultAzureCredential` (MI) via the central `TokenCredential` when `Graph:ManagedIdentity:Enabled=true`; `ClientSecretCredential` = local-dev fallback | Subscriptions, inbound processing, shared-mailbox send. **Beta endpoint.** |
| On-behalf-of | `ForUserAsync(HttpContext)` | OBO (`AcquireTokenOnBehalfOf`); Redis-cached 55 min (`GraphTokenCache`) | User send (`SendMode.User`), user-context ops. v1.0. |

**No SSS anywhere.** Inbound is Graph change-notification subscriptions (`GraphSubscriptionManager`, 3-day lifetime, timer-renew) + HMAC webhook (`CommunicationEndpoints.HandleIncomingWebhookAsync`, returns 202 → Service Bus) + `InboundPollingBackupService`. Processing: `IncomingCommunicationProcessor.ProcessAsync`. Outbound: `CommunicationService.SendAsync` (app-only `Users[x].SendMail`) / `SendAsUserAsync` (OBO `Me.SendMail`). **[Verified]**

### 1.2 `sprk_communication` records — [Verified]

Schema: `docs/data-model/sprk_communication.md`. Content (`sprk_subject/body/from/to/cc/bcc/bodyformat`), classification (`sprk_communicationtype` = **Email/TeamsMessage/SMS/Notification**, `sprk_direction`, `statuscode`), tracking (`sprk_graphmessageid/correlationid/sentat/sentby`), and the **ADR-024 regarding family**: typed lookups `sprk_regardingmatter/project/invoice/workassignment/budget/analysis/organization/person` + denormalized `sprk_regardingrecordtype/id/name/url` + `sprk_associationstatus`.

> ⚠️ **Doc drift [Verified]**: doc omits `sprk_internetmessageid`, `sprk_receiveddate`, `sprk_associationstatus` (all used in code). Fix in docs wave.

### 1.3 The two archival mechanisms — [Verified]

- **(A) Communication module (CANONICAL, live).** Both directions → `.eml` in SPE + `sprk_document`. Inbound also enqueues AI analysis **+ RAG indexing**; outbound enqueues AI analysis **only** (§1.5). Converters: `EmlGenerationService`, `GraphMessageToEmlConverter`.
- **(B) `Services/Email/` OOB-`email`-activity path (LEGACY — RETIRE).** Reads OOB `email` activity: `EmailToEmlConverter` (`emails({id})?$select=...`), `EmailAssociationService` (confidence-scored signals over `emails`/`conversationindex`), webhook expects `PrimaryEntityName=="email"`. Sync `save-as-document` works; the async `ProcessEmailToDocument` handler **does not exist** (dead-ends at enqueue). Dead in production because **Spaarke produces no `email` activities**. **[Verified]**

### 1.4 The regarding-resolver components — how they fit (requirement #3) — [Verified]

The regarding/resolver machinery is Spaarke's canonical "link a record to a parent" system; the engine writes through it and the review UI reuses it:

| Layer | Component | Role in R4 |
|---|---|---|
| Client write primitive | `PolymorphicResolverService.applyResolverFields` (`Spaarke.UI.Components/src/services/`) — owns FR-13 mutual-exclusivity | Review UI override path |
| Client PCF | `RegardingResolver` PCF (`src/client/pcf/RegardingResolver/`) — zero entity-specific branches; host entity is a parameter | **The review/override surface** (§6.6) |
| Canonical target catalog | `TODO_REGARDING_CATALOG` (`TodoRegardingUpdateBuilder.ts`) — 12 targets today | **Extend** for `sprk_servicerequest`, `sprk_event` |
| Server write map | `CommunicationService.RegardingLookupMap` (lines 921-931) + `IncomingAssociationResolver.RegardingFieldPriority` + `PopulateResolverFieldsAsync` | **Extend + generalize** into the engine |

R4 does **not** add a new regarding mechanism — it extends this one (Component Justification §11). Note: `AssociationResolver` PCF is retired; use `RegardingResolver`. **[Verified]**

### 1.5 Inbound vs outbound asymmetry (requirement #6) — [Verified]

| Enrichment | Inbound (`IncomingCommunicationProcessor`) | Outbound (`CommunicationService`) |
|---|---|---|
| Association | **Auto** (`IncomingAssociationResolver`, signal-based) | **Caller-supplied only** (`MapAssociationFields`) |
| Categorization | None (channel type only) | None (channel type only) |
| AI analysis | Yes (`EnqueueDocumentAnalysisAsync`) | Yes |
| RAG indexing | **Yes** (`IPostUploadIndexingEnqueuer`) | **No** |

Both converge on the same `sprk_communication` + `sprk_document` shape → a shared, direction-agnostic enrichment service is clean and closes both outbound gaps. **[Verified]**

### 1.6 Architecture verdict

**Foundation sound; five corrections + three net-new capabilities.** [Judgment]

| # | Finding | Severity | R4 action |
|---|---|---|---|
| A-1 | Legacy OOB-`email` subsystem (`Services/Email/`) — dead async handler, ADR-028 auth drift | High | **Retire** (DEC-2); reuse only its scoring design |
| A-2 | Association thin & inbound-only; matter/contact/org only; over a Graph type | High | **Unified Association Engine over a normalized envelope** (§5) |
| A-3 | Outbound gets no auto-association, no RAG indexing (#6) | High | **`ICommunicationEnrichmentService`** (both directions) (§5.2) |
| A-4 | No response/action intelligence on assessed mail (#9) | High (net-new) | **Responsive Intelligence** on Action+Binding+OutputRouter (§7) |
| A-5 | `sprk_communication` UI is OOB form; no channel-aware surface (#7) | Medium | **Communication Code Page** (§8) |
| A-6 | Graph currency (beta endpoint, no `delta` backstop, `Mail-Advanced`/EWS exposure) (#11) | Medium (deadlines) | **Mandatory hardening track** (§9 Phase H) |
| A-7 | Index names partly hardcoded in deploy template; dual read/write setting (#8) | Medium | Config-driven index hardening (§9 Phase H) |
| A-8 | Model is multi-type-aware but service layer email-hardcoded (#2) | Medium (future) | **Channel-abstraction seams** (§5.5) |
| A-9 | Doc drift (§1.2); `sprk_servicerequest` absent from repo (#14) | Low | Docs + schema authoring (§9) |

---

## 2. Explicit scope boundary: no Server-Side Sync, no OOB activities (requirement #1)

**Binding for R4 [Verified/decision]:**
- R4 stands **only** on the Graph + `sprk_communication` path. It does **not** unify, support, re-enable, or extend Server-Side Sync or OOB Dataverse `email`/activity entities.
- The single subsystem touching OOB `email` activities (`Services/Email/`, `EmailAssociationService`, `EmailToEmlConverter`, the `email`-triggered Service Endpoint webhook, `/api/v1/emails/*`) is **retired** (DEC-2). Its confidence-scoring *design* (TrackingToken/ConversationThread/RecentSenderActivity/Domain/Contact) is **reimplemented against Graph/`sprk_communication`** inside the new engine — the code is deleted, the idea is kept.
- Retiring it also removes an ADR-028 auth-drift point automatically (§1.6 A-1; the service built its own `ConfidentialClientApplication`).

---

## 3. Reconciled current-state truth (for the rest of this doc)

| Capability | Reality on `master` (2026-07-14) |
|---|---|
| Inbound capture | Graph change-notification subscriptions (**not SSS**) |
| Inbound → `sprk_communication` | Live (`IncomingCommunicationProcessor`) |
| Association | Live, thin, inbound-only (`IncomingAssociationResolver`, 3 rungs) |
| Legacy OOB-`email` matcher | `EmailAssociationService` (confidence-scored, OOB-bound) — **retire** |
| `.eml` + RAG | Live (Communication module) |
| Outbound send | Live (R2 server + R3 `<EmailComposer />` client design) |
| Outbound enrichment | Partial (no auto-association, no RAG) |
| App-only email assessment | `AppOnlyAnalysisService.AnalyzeEmailAsync` — **built, mature** |
| Action fan-out from assessment | **Missing** (OutputRouter `record`/`notification` stubbed) |
| Communication record UI | OOB auto-generated form (EmailComposer Code Page designed in R3, not built) |
| Multi-channel | Model-ready (`sprk_communicationtype`); service layer email-hardcoded |

### 3.5 On the Email Triage module (July 10) & coordination (requirement #10)

`EMAIL-TRIAGE-MODULE-DESIGN.md` is a strong **product** concept whose "Existing Foundation" table cites R2-deleted components (`EmailFilterService`, `EmailToDocumentJobHandler`, SSS capture). Its classification ladder maps onto R4's **real** substrate. **Recommended coordination [Judgment — DEC-10]:** treat **R4 as the platform/engine layer** (enrichment service + Association Engine + Responsive Intelligence + Code Page + hardening) and the **Triage Workbench as a downstream product** that consumes it. Run them as **separate, sequenced projects** — R4 first (substrate), Triage after (queue UI, bulk disposition, SLA, Daily Briefing tile, MCP exposure). R4's review surface (§6.6) is deliberately the *minimum* to close the loop, not the Workbench. This keeps R4 shippable and prevents the product scope from blocking the engine.

---

## 4. Question 2 — Microsoft platform changes & compliance (requirements #11, #12)

**Compliance is a first-class MUST for R4**, not advisory. Four shifts, dated sources. **[Verified — Microsoft Learn / devblogs, July 2026]**

### 4.1 Graph mail — two hard deadlines
- **`Mail-Advanced.*` for non-draft sensitive-property writes — enforce 2026-12-31.** Modifying subject/body/recipients/etc. on non-draft messages needs admin-consent `Mail-Advanced.ReadWrite[.All/.Shared]`. Drafts unaffected. Source: [M365 Dev Blog 2026-03-26](https://devblogs.microsoft.com/microsoft365dev/graph-api-updates-to-sensitive-email-properties/). **Our exposure**: only `IsRead=true` PATCH today (`IncomingCommunicationProcessor` line 687) — *likely* exempt; **audit to confirm (DEC-6)**.
- **EWS enforced-off 2026-10-01.** Audit found **no EWS in `src/`**; confirm no scripts/plugins. **[Verified none in `src/`; Open on scripts]**
- **Webhook pattern confirmed correct** (deliveries un-throttled; must ack fast — we return 202 → Service Bus). **Gap**: `GraphSubscriptionManager` renews on a timer but has **no lifecycle-notification subscription and no `delta`-query reconciliation backstop** — Microsoft's required belt-and-suspenders for missed events. Add in Phase H. **[Verified/Open]**
- **App-only mailbox scoping**: `ApplicationAccessPolicy` is "legacy" but remains the *only* supported way to scope **Graph** app-only mail access (RBAC-for-Applications governs native Exchange, not Graph). Keep it; treat as a watch item. Sources: [RBAC for Applications](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac), [App Access Policies](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-access-policies).

### 4.2 Work IQ — GA 2026-06-16, **out of scope (agreed, requirement #12)**
Delegated-only (signed-in user), consumption-billed (Copilot Credits), grounding/prose-oriented — **not** an app-only batch classifier. R4 keeps **Azure OpenAI** as the AI rung. **Action**: refresh the stale `knowledge/work-iq` snapshot to GA (drops the per-user-license assumption); flag Context API as a *future* user-facing augmentation only. Source: [Work IQ APIs 2026-06-02](https://www.microsoft.com/en-us/microsoft-365/blog/2026/06/02/announcing-the-new-work-iq-apis/).

### 4.3 Outlook add-in platform
Unified JSON manifest is go-forward; contextual add-ins → event-based activation + Smart Alerts; **NAA is the SSO path** and **legacy Exchange user-identity/callback tokens are already off tenant-wide**. New Outlook is enterprise default (~April 2026); our web-Office.js add-in is correctly positioned but must finish the NAA migration (§5). Sources: [unified manifest](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/unified-manifest-overview), [enable NAA](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/enable-nested-app-authentication-in-your-add-in).

### 4.4 AI options
**Azure OpenAI** = primary (app-only, structured outputs, in-stack). **Azure AI Content Understanding** (GA, API `2025-11-01`) = attachment parsing. Work IQ = out of scope (§4.2).

---

## 5. Question 4 — The Communication Association Engine (deterministic + AI, channel-extensible)

R4's core build: match each communication (inbound **and** outbound) to related records across **matter, project, invoice, service request, work assignment, event, contact, organization** — deterministic-first, AI-assisted, with confidence + provenance and a human review path.

### 5.1 Principles
1. **Extend, don't replace.** Generalize `IncomingAssociationResolver`; keep writing ADR-024 regarding fields.
2. **Direction-symmetric.** Inbound and outbound get identical treatment via a shared service (§5.2).
3. **Channel-extensible.** Operate over a **normalized envelope**, never `Microsoft.Graph.Message` — so Teams/Slack/Gmail/SMS slot in later (§5.5).
4. **Deterministic peers, deterministic first.** AI rung only on ambiguity/miss — bounded cost, explainable defaults.
5. **Confidence + provenance on every match** (adopt `EmailAssociationService`'s scored-signal design).
6. **Never auto-file low confidence.** High → `Resolved`; medium/AI → `Suggested`; low/none → `Pending Review`; conflict → `Ambiguous`.
7. **Central auth only** (§8.4): inject `TokenCredential`/`IGraphClientFactory`/canonical Dataverse interfaces.
8. **Best-effort, non-fatal** (preserve current invariant).

### 5.2 The central move — `ICommunicationEnrichmentService` (requirement #6)

A single **direction-agnostic** service taking `(communicationId, direction, NormalizedMessage, archivedDocumentId?)`, invoked by **both** `IncomingCommunicationProcessor` (inbound) **and** `CommunicationService` outbound creators. It owns, in order:
1. **Association** (the engine, §5.3) — outbound uses caller-supplied regarding as rung 0, then runs the same ladder to fill gaps.
2. **Categorization** (new — a content class + urgency; today absent both directions).
3. **AI analysis** (`EnqueueDocumentAnalysisAsync` — both already do this).
4. **RAG indexing** (`IPostUploadIndexingEnqueuer` — **adds the missing outbound half**).
5. **Responsive Intelligence trigger** (§7) — emit the assessment event to EventRules.

This makes "sent emails fit the same process" true by construction, and is the seam Responsive Intelligence hooks into.

### 5.3 Target entity set & classification ladder

Normalized envelope fields: `{ direction, from, to[], cc[], subject, bodyText, bodyHtml, internetMessageId, inReplyTo, references[], conversationId, sentAt, attachments[] }`. Rungs resolve-with-confidence or defer; provenance recorded per attribute.

| Rung | Mechanism | Source | Conf. | Resolves |
|---|---|---|---|---|
| **0** | Explicit ref / caller-supplied regarding | subject regex (extend) + outbound associations | ~1.0 | Matter/project/invoice by ID |
| **1** | Thread continuity (`inReplyTo`/`references`/`conversationId` → parent comm, copy regarding) | resolver thread tier + reimplemented `conversationindex` logic | 0.90 | Any target on a filed thread (highest volume) |
| **2** | Participant correlation (from/to → matter contacts, project teams, counsel, **`sprk_organization`** by domain) | `QueryContactByEmailAsync` (extend to membership + org) | 0.60–0.85 | Contact, organization, matter/project |
| **3** | Structural detectors (court e-filing, e-sign completion, invoice #, calendar invite) | **new** `Detectors/` | 0.70–0.95 | Category + invoice/event + obligations |
| **4** | Semantic record match (extracted terms → hybrid search) | `RecordSearchService` / `spaarke-records-index` (`confidenceScore`+`matchReasons`) | model | Fuzzy matter/project/invoice/org |
| **5** | LLM extract+classify (JPS action over body+attachments, `$choices` → record types) | **new JPS action** → `AppOnlyAnalysisService` (app-only) | model+rationale | Ambiguous correlation, category, urgency, obligations |

**Target-entity table:**

| Target | Logical name | Notes |
|---|---|---|
| Matter | `sprk_matter` | alt-key `sprk_referencenumber` |
| Project | `sprk_project` | |
| Invoice | `sprk_invoice` | structural + semantic |
| **Service request** | `sprk_servicerequest` | **Exists in Dataverse; zero repo footprint** — author schema doc + add `sprk_regardingservicerequest` lookup to `sprk_communication`, `RegardingLookupMap` entry, catalog + priority entry (§1.4). **[requirement #14]** |
| Work assignment | `sprk_workassignment` | |
| Event | `sprk_event` | calendar-invite detector; add to catalog/priority |
| Contact | `contact` | exact email match |
| **Organization** | **`sprk_organization`** | **The main org association target. NOT OOB `account` (true-account relationships only), NOT OOB `organization`.** Correct the sender-domain match, which currently writes `account`. **[requirement #15 / DEC-3]** |

### 5.4 Confidence → status
```
≥0.85 deterministic (rung 0–3)      → Resolved       (auto-file)
0.50–0.85 or any AI rung            → Suggested      (1-click confirm)   [NEW status]
<0.50 / none                        → Pending Review (manual)
conflicting high-confidence         → Ambiguous      (disambiguate)      [NEW status]
```
`Suggested`/`Ambiguous` are new `sprk_associationstatus` option-set values — **verify integers via Dataverse MCP before assigning** (DEC-5). Auto-file threshold + per-tenant enable is config (ADR-018 kill-switch, DEC-4). Provenance stored as JSON in new `sprk_associationprovenance`.

### 5.5 Channel extensibility seams (requirement #2) — [Judgment]
The data model is already multi-type (`CommunicationType` = Email/TeamsMessage/SMS/Notification; the regarding layer is channel-agnostic). The **service layer** is email-hardcoded (Graph `SendMail`, `.eml` archival, mailbox ingestion). R4 does **not** build other channels, but designs the seams so they slot in:
- **Association Engine** operates on the **normalized envelope** (§5.3) — inherently channel-agnostic. This is the single most important extensibility decision.
- Define (do not fully implement) **`ICommunicationChannelSender`** (dispatch `SendAsync` by `CommunicationType`; email = Graph impl) and **`ICommunicationArchiver`** (`.eml`/`GenerateEml` = one impl). Email is the only implementation in R4; the interfaces mark the extension points.
- Inbound ingestion (subscription/verification) stays email-specific for now but is documented as the per-channel adapter boundary.

Result: adding Teams/Slack/Gmail/SMS later = new sender/archiver/ingestor adapters + a normalizer to the envelope — **no change to the engine, the enrichment service, the regarding model, or the review UI.**

### 5.6 Review surface (minimal — not the Triage Workbench)
Reuse existing components; **no new UI framework**:
- Dataverse view "Communications Awaiting Association" (`sprk_associationstatus in (Suggested, Pending Review, Ambiguous)`).
- The record form (or the new Code Page §8) embeds the **`RegardingResolver` PCF** (`PolymorphicResolverService.applyResolverFields`) pre-filled with the engine's top suggestion + confidence + provenance rationale; reviewer accepts (1 click) or picks another.
- On accept, optionally run **Field Mapping** (§5.7) to populate fields from the matched parent. Override reasons captured as a feedback signal.

### 5.7 Field Mapping fit (requirement #4) — [Verified]
`FieldMappingService.ts` (client, config-driven: `GET /api/v1/field-mappings/profiles/{source}/{target}`; types Copy/Default/Concat/Template; `sprk_expression`) + BFF `FieldMappingEndpoints` + `IFieldMappingDataverseService` + two Dataverse config tables. **Role in R4:** after a match, copy/template fields from the matched **parent** onto the `sprk_communication` record (e.g., inherit `sprk_organization`, matter reference, billing context). Config-driven, zero code per new mapping. Client engine drives the review-UI accept path; for server-side auto-population, use the BFF `IFieldMappingDataverseService`. It does **not** do matching — it's the post-match enrichment step.

### 5.8 Reuse map (Component Justification §11)
| Need | Reused | New? |
|---|---|---|
| Matching backbone | `IncomingAssociationResolver` (generalize over envelope) | Extend |
| Direction symmetry | new `ICommunicationEnrichmentService` wrapping existing enqueuers | New wrapper, reused parts |
| Confidence design | `EmailAssociationService` signals (reimplement vs Graph; delete OOB service) | Reuse design |
| Semantic match | `RecordSearchService` / `spaarke-records-index` | Reuse |
| AI extract/classify | JPS action → `AppOnlyAnalysisService` | New action; reuse runtime |
| Post-match populate | `FieldMappingService` / `IFieldMappingDataverseService` | Reuse |
| Review/override UI | `RegardingResolver` PCF + `PolymorphicResolverService` | Reuse |
| Regarding catalog/map | `TODO_REGARDING_CATALOG` + `RegardingLookupMap` + `RegardingFieldPriority` | Extend (servicerequest, event) |
| Channel seams | — | New interfaces (email impl only) |
| Structural detectors | — | New |

---

## 6. (reserved — merged into §5)

---

## 7. Responsive Intelligence — assess a communication, trigger rule-driven actions (requirement #9)

**Concept:** the system reads/assesses a communication (in or out), understands content, and — per defined rules — triggers actions: create Event/Task, notify an assigned user, and/or produce a summary/checklist of what must be done. This is the "intelligence to read/assess and invoke required actions."

**This is largely a wiring job on shipped infrastructure. [Verified]** The go-forward action architecture is **Action + Binding + OutputRouter + EventRules** (NOT the frozen node-graph engine).

### 7.1 What exists
- **Assessment**: `AppOnlyAnalysisService.AnalyzeEmailAsync` (mature) — builds email + all-attachment context (100 KB budget) and runs the **"Email Analysis" playbook** resolved via the `sprk_playbookconsumer` **Binding** table. App-only (MI), background-safe.
- **Rules/triggers**: `EventRulesService.FireAsync` — event token (e.g. `communication_received`) → ordered Binding members → each runs as an Action → `OutputRouter`. Ships deterministic gates: per-user daily cost cap, opt-out, explicit-command supersede, precondition checks, and an **M4 confidence gate**. Declarative match via `sprk_matchconditions`. **No `sprk_rule` DSL needed** — rules are Binding data.
- **Actions (reusable executors)**: `CreateNotificationNodeExecutor` (rich `appnotification`: idempotency, priority, `customData.actionUrl/dueDate`, feeds Daily Briefing), `CreateTaskNodeExecutor` (Dataverse `task`), `IEventDataverseService.CreateEventAsync` (`sprk_event`), `TodoGenerationService` (`sprk_todo`).
- **Summary/checklist**: JPS structured output + `DeliverCompositeNodeExecutor`; reference impl `DailyBriefingCompositeService`. Extraction handlers exist (`DateExtractorHandler`, `EntityExtractorHandler`, etc.).

### 7.2 The gap R4 fills
- `OutputRouter`'s **`record` and `notification` dispositions are stubbed** (`DispositionRoutability.cs` throws `NotSupportedException`). **Complete them.**
- Email analysis **does not fan out** to Event/Task/Notification. **Wire** `AnalyzeEmailAsync` output → `EventRulesService` → OutputRouter `record`/`notification` legs → `CreateEvent`/`CreateTask`/`CreateNotification`.
- **Summary/checklist**: author a new **JPS "Communication Triage" Action** (structured output: category, urgency, obligations[], suggested actions[], response checklist) delivered via `DeliverComposite`, following `SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md`.

### 7.3 Shape
```
ICommunicationEnrichmentService (§5.2)
        │ emits assessment event
        ▼
EventRulesService.FireAsync("communication_assessed")
        │  match sprk_matchconditions + gates (cost/opt-out/confidence)
        ▼  ordered Bindings → Actions → OutputRouter
   ┌──────────────┬─────────────────┬──────────────────┐
   ▼              ▼                 ▼                  ▼
 CreateEvent   CreateTask     CreateNotification   Summary/Checklist
 (sprk_event)  (task)         (appnotification →   (JPS action →
                              Daily Briefing)       DeliverComposite)
```
Rules are configured as Binding rows + `sprk_matchconditions` (e.g. "urgency=High & category=Court/Deadline → create Event + notify assignee"). Privilege-aware: AI may *flag* privilege, never decide it (ADR-015).

---

## 8. Question 3 (add-in) + requirement #7 — Outlook add-in & the Communication Code Page

### 8.1 Add-in current state — well-built but broken (requirement #13) — [Verified]
`src/client/office-addins/` (React 19, Fluent v9, Office.js, MSAL 3.x, deployed to Azure SWA). **Save-to-Spaarke path is production-grade** (client sends message id; BFF fetches body/attachments via Graph OBO; idempotency; SSE progress; Create-To-Do + linked-todos banner). **But it is currently non-functional** due to:
- Auth mid-migration: `NaaAuthService`/`DialogAuthService`/`authConfig` are `@deprecated`, to be replaced by `@spaarke/auth` `OfficeNaaStrategy` (tasks 081/082). Legacy Exchange tokens are already off (§4.3), so this is **blocking**, not optional.
- **Two mismatched manifests** (XML shipped v1.0.19 / JSON `devPreview` v1.0.0).
- **BFF Office auth filters stubbed** (`.AddOfficeAuthFilter/.AddJobOwnershipFilter/.AddEntityAccessFilter` = TODOs) — real security gap.
- Hardcoded org URL (`spaarkedev1.crm.dynamics.com`); Share/Grant Access placeholders.

**R4 add-in scope (agreed):** hardening only (§9 Phase H) — finish NAA/`@spaarke/auth` migration, converge on the unified manifest, implement the auth filters, fix the org URL, and make the save task pane a **consumer of the Association Engine** (show suggested associations + confidence, accept/override). Finishing Share/Grant Access and in-Outlook triage are backlog (§10).

### 8.2 The Communication Code Page (requirement #7) — [Verified/Judgment]
Today `sprk_communication` uses the **OOB auto-generated form**. R3 designed a `sprk_emailcomposer` Code Page (`<EmailComposer />`, FR-18/19) but **did not build it**, and it is **email-only**.

**R4 direction:** build a **channel-aware communication Code Page** — not an email-only composer:
- **View/record surface** generalizes across `sprk_communicationtype` (email today; Teams/SMS/Notification render read-only later) — the layout keys off communication type, not "email."
- **Compose** reuses R3's `<EmailComposer />` design for the email case (a channel-specific composer mounted by the page), so R4 does not re-solve send-UX.
- Replaces the OOB form via Form Component Control (standard form retained as admin fallback), per R3's FR-19 entry surfaces.
- Embeds the **`RegardingResolver` PCF** + suggestion/confidence for the review path (§5.6).
- Auth via `@spaarke/auth` v2; exemplar `src/client/code-pages/DocumentRelationshipViewer/`.

**Coordinate with R3 (DEC-9):** R3's EmailComposer is *send-focused* and unstarted. Recommend R4 **subsumes** the record/view Code Page and **consumes** R3's `<EmailComposer />` component for compose — i.e., R4 owns the channel-aware *page*, R3 owns the email *composer component*. Resolve ownership before `/design-to-spec`.

### 8.3 Index configuration hardening (requirement #8) — [Verified]
Index names are config-bound via `AiSearchOptions`/`AnalysisOptions` and a per-record resolver (`SearchIndexNameResolver` → `sprk_aisearchindex` catalog → appsettings fallback) — **good**. But: `appsettings.template.json` **bakes literal index names** in the `AiSearch` section (`:234-248`, incl. an 8-entry `AllowedIndexes`) instead of tokenizing, and there's a **split-brain** between `AiSearch:KnowledgeIndexName` (reads) and `Analysis:SharedIndexName` (writes) (`FAILURE-MODES.md` G-9). **R4 hardening:** tokenize the `AiSearch` index names + `AllowedIndexes` (mirror the existing `#{...}#` pattern), consolidate the read/write setting, and ensure all communication indexing flows through `SearchIndexNameResolver` so multi-tenant/env deploy packages carry no hardcoded index names.

### 8.4 Auth alignment (requirement #5) — [Verified]
The post-March-2026 upgrade is **Spaarke Auth v2 (ADR-028, 2026-05-19)**. No `IAuthService` class — the central primitives are the **DI singleton `Azure.Core.TokenCredential`** (from `Infrastructure/Auth/ManagedIdentityCredentialFactory`) and **`IGraphClientFactory`**. **R4 alignment rules (binding):**
- Server: inject the central `TokenCredential` for outbound tokens; `IGraphClientFactory` for Graph (`ForApp`/`ForUserAsync`); canonical Dataverse interfaces (`IGenericEntityService`, `ICommunicationDataverseService`, `IDataverseService`). **Never** `new` a credential/`ConfidentialClientApplication`.
- Retiring `Services/Email/` removes the `EmailAssociationService` client-secret drift. (Broader `DataverseServiceClientImpl`/`DataverseWebApiService` secret usage is platform-wide drift — **note, don't own** in R4.)
- Client: `@spaarke/auth` only (`useAuth`/`authenticatedFetch`/`buildBffApiUrl`); **`OfficeNaaStrategy`** for the add-in; no token snapshots, no `accessToken` props.

### 8.5 Send-side consolidation detail (absorbed from R3) — [self-contained]

The send-side scope R4 absorbs (waves W2/W4/W6). Summarized here so this design stands alone; full detail (props tables, wave-by-wave migration, risks) in [`reference/r3-send-side-design.md`](reference/r3-send-side-design.md). **Empirical deltas verified 2026-06-05** (from R3's `CLAUDE.md`): the `EmailComposer/` component and Code Page do **not** exist yet (build from scratch); `communicationApi.ts` has `sendCommunication()` but is **missing** `SendCommunicationError` + `attachmentDriveItemIds`; `SendCommunicationRequest.cs` has only `AttachmentDocumentIds`; only `CreateMatter/SendEmailStep.tsx` is a true LegalWorkspace fork (Project/Event/Todo/WorkAssignment are not) — so W6 is smaller than a naive read suggests.

**(a) Canonical `<EmailComposer />` engine** — one component in `@spaarke/ui-components`, injected `authenticatedFetch` (no direct `@spaarke/auth` import), React 18, Fluent v9.
- **Modes**: `compose | view | reply | forward | draft`.
- **Mounts**: `inline` (wizard step — no internal action bar), `dialog` (Fluent Dialog), `page` (Code Page, primary entity-form look).
- **3 thin semantic wrappers** (callers import these, not the engine directly): `<SendEmailStep />` (inline), `<SendEmailDialog />` (dialog), `<SendEmailPage />` (page).
- **Sub-components**: `RecipientField` (single canonical normalization — replaces 5), `BodyEditor` (HTML/plain), `AttachmentList` (sources: local/SPE/related/wizard; caps 150/35 MB), `SendModeRadio` (sharedMailbox vs user OBO), `AssociationChips`, `ComposerActionBar`.

**(b) `sendCommunication()` typed wrapper** — the one programmatic send path.
- Add `SendCommunicationError` (parses ProblemDetails: `status`/`code`/`detail`/`correlationId`) — replaces 3 divergent error shapes.
- Fix the `attachmentDocumentIds` latent bug: canonical field is `attachmentDriveItemIds` (SPE driveItem IDs, **not** `sprk_document` GUIDs); BFF adds `AttachmentDriveItemIds` with `[Obsolete]` alias (non-breaking). `DocumentEmailWizard.tsx:494` currently sends the wrong IDs.

**(c) Reply/forward thread closure** (feeds R4 §5.3 rung 1) — `CommunicationService.SendAsync` captures the real `Internet-Message-Id` post-send and stamps `sprk_internetmessageid`; `reply`/`forward` modes stamp `sprk_inreplyto`. Both columns added in W0 schema.

**(d) Code Page URL contract** (W4, generalized channel-aware per §8.2) — web resource `sprk_emailcomposer`, reads `data=` params: `mode` (required), `id` (comm GUID for view/reply/forward/draft), `to/cc/subject/body` (compose pre-fill), `associatedTo=<entityType>:<guid>` (stamp association), plus `@spaarke/auth` env vars. Three entry surfaces: "+ New Email" ribbon, Form Component Control replacing the OOB form (standard form retained as admin fallback), and embeddable `navigateTo` launch.

**(e) Caller migration (W6)** — replace inline `fetch`/ad-hoc composers in: SummarizeFilesDialog (inline fetch, line 436), FilePreviewDialog (LegalWorkspace), DocumentEmailWizard (the attachment-ID bug), and the shared `SendEmailStep` consumed by CreateProject/Event/Todo/WorkAssignment + the `CreateMatter` LegalWorkspace fork. Retire `sprk_communication_send.js` (~1,150 LOC × 2 copies) after auditing ribbon references. Resolve the `WorkAssignmentWizardDialog.tsx:31` cross-package source import.

---

## 8.6 Hot-Path Declaration (per CLAUDE.md §10 / FR-C04)

```xml
<hot-path-declaration>
  <bff>Y</bff>                 <!-- heavy: Services/Communication/**, Services/Ai/** (OutputRouter, EventRulesService, node executors, AppOnlyAnalysisService), Services/Email/** (RETIRE), Api/Office/OfficeEndpoints.cs, Api/EmailEndpoints.cs (retire) -->
  <spaarke-ai>N</spaarke-ai>   <!-- src/solutions/SpaarkeAi/** not modified; notifications feed Daily Briefing as a CONSUMER only -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives> <!-- W0 authors ADR-045 in .claude/adr/ (main-session write boundary); not .claude/skills or .claude/constraints -->
  <root-claude-md>N</root-claude-md>      <!-- W8 may add a §17 pointer row for the new architecture doc + ADR-045; treat as optional, not a hot-path edit -->
</hot-path-declaration>
```

> **⚠️ HARD WARNING — BFF `Services/Ai/**` overlap with active peers.** W5 (Responsive Intelligence) modifies `Services/Ai/` internals (`OutputRouter`/`DispositionRoutability.cs`, `EventRulesService` wiring, `CreateNotification`/`CreateTask`/`DeliverComposite` executors). Per `projects/INDEX.md`, **`spaarke-ai-architecture-redesign-r2` is the declared sole owner of `Services/Ai/` internals**, and `spaarke-daily-update-service-r5` (`Nodes/UpdateRecordNodeExecutor`, `Services/Ai/Narrators/**`), `spaarkeai-compose-r2`, `chat-routing-redesign-r1`, and `spaarke-ai-platform-unification-r6` also touch `Services/Ai/`. **W5 MUST coordinate with r2-core via `/conflict-check` before each W5 wave, consume r2-core's published `Services/Ai/PublicContracts/` seams rather than forking internals, and MUST NOT begin until the OutputRouter `record`/`notification` disposition ownership is confirmed with r2-core.** W1/W3 (Communication engine, new detectors, JPS action) touch `Services/Communication/**` + add new files — low overlap. See §9 W5 note.

## 9. Plan — unified wave map (R3 + R4)

One project, dependency-ordered waves. **[S] = server (C#), [C] = client (TS), [X] = Dataverse/config, [D] = docs.** The server/client split is the key parallelization axis. Effort is rough; `/task-create` produces the authoritative WBS + parallel-safe groups.

```
        W0 Foundation (schema · ADR · server send-changes · retire OOB-email)
                 │
      ┌──────────┴───────────┐              ┌─────────────────────────────┐
      ▼                      ▼              │  W7 Microsoft hardening      │
  W1 [S] Engine +        W2 [C] Composer    │  (parallel track, runs       │
     Enrichment            engine +         │   alongside W1–W6;           │
     (rungs 0–3)           wrappers +       │   deadline-driven)           │
      │                    sendCommunication└─────────────────────────────┘
      ▼                      │
  W3 [S] Semantic +          │
     AI rungs (4–5)          │
      │                      │
      ▼                      ▼
  W5 [S] Responsive      W4 [C/X] Channel-aware Communication Code Page
     Intelligence            │  (mounts composer for compose;
      │                      │   RegardingResolver review; FCC swap)
      │                      ▼
      │                  W6 [C] Caller migration + fork/webresource retirement
      └──────────┬───────────┘
                 ▼
        W8 [D] Documentation
```

**W0 — Shared foundation** [X/S, main-session for `.claude/`] (≈3 d)
- **One schema pass** (`sprk_communication`): R3 reply-thread (`sprk_inreplyto`, `sprk_internetmessageid`) + R4 (`Suggested`/`Ambiguous` statuses — verify integers via MCP, `sprk_associationprovenance`, `sprk_regardingservicerequest`); confirm `sprk_receiveddate`/`sprk_associationstatus`; add `sprk_event` + `sprk_servicerequest` to catalog/priority/`RegardingLookupMap`; **correct org target to `sprk_organization`** (DEC-3). Author `docs/data-model/sprk_servicerequest.md`.
- **One Communication ADR** (supersedes the separate ADR-033 plan): client canonical send + server association engine + enrichment service + channel seams.
- **BFF server foundation**: `AttachmentDriveItemIds` non-breaking rename (R3), `Internet-Message-Id` post-send capture (R3, feeds W1 thread rung).
- **Retire** the OOB-`email` subsystem (`Services/Email/` async remnants, `/api/v1/emails/*`, `EmailAssociationService`) (DEC-2) — also clears one auth-drift point.

**W1 — Server: enrichment + deterministic engine** [S] (≈4 d) — *parallel with W2*
- `ICommunicationEnrichmentService` (direction-agnostic; wire inbound **and** outbound; add missing outbound RAG indexing).
- Refactor `IncomingAssociationResolver` → **Association Engine** over the **normalized envelope**; rung interface + confidence + provenance; preserve rungs 0–2 behavior.
- Extend targets (project/invoice/work-assignment/event/service-request/organization); extend `RegardingFieldPriority` + `ICommunicationDataverseService`.
- Structural detectors (`Detectors/`): calendar-invite, e-sign, invoice #, court/e-filing.
- Confidence→status + auto-file threshold (ADR-018). Channel seams (`ICommunicationChannelSender`/`ICommunicationArchiver`, email impl). Central auth (§8.4). Tests per rung + direction symmetry.

**W2 — Client: composer engine + wrappers** [C] (≈4 d) — *parallel with W1; detail in §8.5 + [`reference/r3-send-side-design.md`](reference/r3-send-side-design.md) §5–6*
- `<EmailComposer />` engine + `SendEmailStep`/`SendEmailDialog`/`SendEmailPage` wrappers; `sendCommunication()` refinements (`SendCommunicationError`, `attachmentDriveItemIds`). Unit tests.

**W3 — Server: semantic + AI rungs (4–5)** [S] (≈3 d) — *after W1*
- `RecordSearchService` as rung 4; JPS extraction/classification action (`$choices`) → `AppOnlyAnalysisService` as rung 5 (app-only; ADR-016 budget, ADR-014 cache); `Suggested`/`Ambiguous` only + rationale. Per-rung telemetry (DEC-8).

**W4 — Channel-aware Communication Code Page** [C/X] (≈3 d) — *after W2 (composer) + W1 (suggestions)*
- Channel-aware view/record page (generalized by `sprk_communicationtype`); mounts `<EmailComposer />` for email compose; Form Component Control swap (standard form as admin fallback); embeds `RegardingResolver` PCF + suggestion/confidence; "Communications Awaiting Association" view; optional Field Mapping on accept. URL/entry-surface contract in §8.5(d) + [`reference/r3-send-side-design.md`](reference/r3-send-side-design.md) §7.

**W5 — Responsive Intelligence** [S] (≈3 d) — *after W3*
- Complete OutputRouter `record` + `notification` dispositions (remove `NotSupportedException`); wire enrichment → `EventRulesService` ("communication_assessed") → CreateEvent/CreateTask/CreateNotification; "Communication Triage" JPS Action (category/urgency/obligations/checklist) → `DeliverComposite`; rule config via Binding + `sprk_matchconditions`; reuse EventRules gates; privilege-flag only (ADR-015).

**W6 — Client caller migration + retirements** [C] (≈2.5 d) — *after W2 + W4*
- Migrate SummarizeFilesDialog, FilePreviewDialog, DocumentEmailWizard, 5 create-record wizards to the canonical composer; retire LegalWorkspace forks; retire `sprk_communication_send.js` webresource. Migration detail in §8.5(e) + [`reference/r3-send-side-design.md`](reference/r3-send-side-design.md) §8.

**W7 — Microsoft hardening + auth + index** [S/C/X] (≈3–4 d) — *parallel track, deadline-driven*
- Graph audit (`Mail-Advanced.*` by 2026-12-31; EWS by 2026-10-01); subscription lifecycle notifications + `delta` reconciliation; Outlook add-in NAA/`@spaarke/auth` migration + unified JSON manifest + stubbed Office auth filters + hardcoded-org-URL fix + surface engine suggestions in save pane; index-config tokenization + read/write consolidation via `SearchIndexNameResolver`; refresh `knowledge/work-iq`.

**W8 — Documentation** [D] (≈1.5 d)
- `docs/architecture/communication-intelligence-architecture.md`; update `email-processing-architecture.md`/`communication-service-architecture.md`/`sprk_communication.md`; mark OOB-`email` + fragmented-send docs RETIRED; ADR cross-refs.

**Rough total: ~26–30 focused days**, but the W1‖W2 and W7 parallelism compresses **wall-clock** materially. W0→W1→(deterministic association, direction-symmetric) lands the majority of receive-side value before any AI cost; W2 lands the send-side consolidation concurrently.

### Parallel-execution summary (the merge payoff)
| Can run concurrently | Why safe |
|---|---|
| W1 (server) ‖ W2 (client) | Disjoint files + languages (C# vs TS) |
| W7 (hardening) ‖ W1–W6 | Mostly separate surfaces (Graph subs, add-in, config) |
| Within W1: detectors ‖ query methods ‖ channel seams | Separate files |
| Within W6: each caller migration | Separate solutions (R3 verified disjoint) |
| Within W8: per-file doc targets | Separate files |

Serial dependencies: W0 before all; W3 after W1; W4 after W2+W1; W5 after W3; W6 after W2+W4; W8 last.

---

## 10. Decisions to resolve before `/design-to-spec`

| ID | Decision | Answer / recommendation |
|---|---|---|
| DEC-1 | Service request entity | **Resolved**: `sprk_servicerequest` exists in Dataverse; author schema doc + add regarding fields (Phase 0). |
| DEC-2 | Retire OOB-`email` path | **Resolved: retire.** |
| DEC-3 | Org target | **Resolved: `sprk_organization`** (not `account`, not OOB `organization`). Correct sender-domain match. |
| DEC-4 | Auto-file threshold / default-on tenants | Suggest-only first; enable auto-file per deterministic rung after measuring (DEC-8). |
| DEC-5 | New status option-set integers | Assign ~100000002/3; **verify via Dataverse MCP** for conflicts. |
| DEC-6 | Non-draft sensitive-property writes? (`Mail-Advanced.*`) | **Investigate** in Phase H; `IsRead` PATCH likely exempt — confirm. |
| DEC-7 | Work IQ | **Out of scope** (agreed). Refresh `knowledge/work-iq`. |
| DEC-8 | Success target gating AI-rung/auto-file | Measure deterministic rungs 0–3 on real volume first. |
| **DEC-9** | Relationship to R3 | **Resolved: R3 is absorbed into R4** as one unified project (§0.6). R3 was designed but unexecuted; its send-side scope becomes R4 waves W2/W6, its design detail is cited verbatim. R3 marked SUPERSEDED. |
| **DEC-10** | Relationship to Email Triage module | Triage remains a **downstream product** but MUST have full context of R3+R4. **Action**: update `EMAIL-TRIAGE-MODULE-DESIGN.md` to (a) fix its stale component references (§3.5), (b) point its classification-ladder/disposition model at R4's Association Engine + Responsive Intelligence as the substrate, (c) sequence after R4. Do this as part of W8 (docs) so alignment is captured, not left to manual coordination. |
| **DEC-11** | Multi-channel seam depth | **Resolved (follow rec)**: interfaces + normalized envelope + email impl only; no Teams/Slack/SMS build in this project. |

---

## 11. Risks

| # | Risk | Sev | Mitigation |
|---|---|---|---|
| R-1 | Misassociation with auto-file on | High | Suggest-by-default; auto-file only deterministic ≥ threshold; misfile is re-file (audited), never delete; per-tenant kill switch |
| R-2 | Responsive Intelligence over-fires (spurious tasks/notifications) | High | EventRules gates (cost cap, opt-out, confidence gate); Suggest-mode default; privilege-flag-only |
| R-3 | AI cost at volume | Medium | Deterministic-first ladder; AI only on ambiguity; ADR-014 cache; ADR-016 budget |
| R-4 | Graph deadlines (EWS 10-01, `Mail-Advanced` 12-31) | Medium (compliance) | Phase H audit early; mandatory |
| R-5 | Subscription gaps without `delta` backstop | Medium | Phase H lifecycle + delta; polling backup is partial cover today |
| R-6 | Privilege/authorization leakage in review queue | High | Matter-level auth on the view (ADR-003/008); AI flags, never decides (ADR-015) |
| R-7 | Consolidating engines / adding enrichment regresses current matching | Medium | Phase 1 preserves rungs 0–2 under test before extending |
| R-8 | Add-in NAA/manifest migration breaks the shipped save flow | Medium | Behind existing Auth-v2 tasks 081/082; smoke-test in dev |
| R-9 | Channel abstraction over-engineered for a single (email) impl | Low-Med | Interfaces + normalized envelope only; no speculative channel code (DEC-11) |
| R-10 | R3/R4 Code Page ownership collision | Medium | Resolve DEC-9 before spec |

---

## 12. Backlog referrals (not R4)

- **Email Triage Workbench product** (queue, bulk disposition, SLA, Daily Briefing tile, MCP) — consumes R4.
- **Teams / Slack / Gmail / SMS channel implementations** (R4 ships only the seams).
- **Finish Outlook Share / Grant Access**; **in-Outlook triage** surface.
- **Work IQ Context-API augmentation** of the review UI (delegated, P2+).
- **Feedback-learning loop** from reviewer overrides.
- **Consolidate platform-wide Dataverse client-secret drift** (`DataverseServiceClientImpl`/`DataverseWebApiService`) to MI — bigger than R4.
- **`sprk_communication` inbox/thread browse UI** (R3-deferred).

---

## 13. Appendix — one-paragraph summary

> R4 turns Spaarke's already-live, 100%-Graph inbound pipeline (no SSS, no OOB `email` activities — that legacy `Services/Email/` subsystem is retired) into a **channel-extensible Communication Intelligence layer**. A direction-agnostic **`ICommunicationEnrichmentService`** gives inbound *and* outbound the same treatment (fixing outbound's missing auto-association and RAG indexing), wrapping a **unified Association Engine** that operates over a normalized message envelope — deterministic-first (explicit-ref → thread → participant → structural detectors), then semantic (`RecordSearchService`) and LLM (`AppOnlyAnalysisService` + a JPS action), with confidence, provenance, and the eight target entities (matter, project, invoice, **`sprk_servicerequest`**, work assignment, event, contact, **`sprk_organization`**). Assessed communications feed **Responsive Intelligence** — completing the stubbed OutputRouter `record`/`notification` legs so declarative Binding + `sprk_matchconditions` rules create Events/Tasks, notify assignees, and emit summaries/checklists via EventRules gating. A channel-aware **Communication Code Page** (consuming R3's `<EmailComposer />` for email compose) replaces the OOB form and hosts the `RegardingResolver`-based review of low-confidence matches, with Field Mapping populating fields from the matched parent. Everything uses the central Auth-v2 MI primitives and `@spaarke/auth`; a mandatory hardening track closes the Microsoft-2026 gaps (Graph `Mail-Advanced`/EWS audit, subscription `delta` backstop, Outlook NAA + unified manifest + auth filters) and makes indexes deployment-flexible. The channel seams (`ICommunicationChannelSender`/`ICommunicationArchiver`, email-only in R4) let Teams/Slack/Gmail/SMS slot in later with no change to the engine. The Email Triage product consumes this layer; Work IQ is out of scope.

---

*Prepared for review. No component names herein may be cited authoritatively by implementation agents prior to resolving the §10 decisions and running `/design-to-spec`.*
</content>
