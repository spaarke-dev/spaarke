# P0-Review Additions — Feasibility & Scope Analysis (2026-08-06)

> Five capabilities surfaced by the owner during the P0 prototype review. Grounded by 4 parallel
> investigations (1 web researcher + 3 codebase Explore). This is the input to a scope decision
> (R2 vs R3) and, once decided, to spec.md/design.md amendments.

## The unifying insight

**Every one of these already exists as a Spaarke platform capability — but bound to the workforce /
OBO plane. Each becomes available to SPA users by re-hosting it on the exact enabler R2 is already
building in P1/P2: the external module framework + `CallerPrincipalResolver` (broker-only, dual-scheme
CIAM+workforce, entitlement/participation-scoped, app-only downstream, no OBO).** So these are not five
separate builds — they are "expose capability X as a module/endpoint on the R2 external framework."
The recurring thin-new-surface is the same shape in all cases: a new `/api/v1/external/...` group on the
`ExternalCollaboration` policy, app-only, Tier-2-scoped, reusing the delivered external-access filters.

## Consolidated findings

### 1. NDA wizard: upload → AI classify/profile → auto-fill (owner directive)
- **Reuse (as-is)**: `MatterPreFillService`/`ProjectPreFillService` + `IWorkspacePrefillAi` + `IConsumerRoutingService` already do "upload → extract structured fields → `PreFillResponse {fields, confidence, prefilledFields[]}`" for Create-Matter/Project wizards (`Api/Workspace/WorkspaceProjectEndpoints.cs:33`). `DocumentClassifierHandler` already classifies `NDA/MSA/SOW…`.
- **Thin-new**: an NDA-intake extraction JPS **Action + one `sprk_playbookconsumer` binding row (zero executor code)** + a broker-only external pre-fill endpoint mirroring the workspace one but on the CIAM/workforce-external schemes with **app-only** SPE staging (not OBO + `WorkspaceAuthorizationFilter`).
- **Blocker**: existing pre-fill is OBO/workforce-only → re-host broker-only (the shared pattern).
- **Effort**: Medium (mostly config + one thin endpoint). **Recommend: R2** — direct enhancement to the NDA module already in R2 P3; highest reuse-to-value.

### 2. Self-service review feedback (submit → get an answer back)
- **Email-with-documents (recommend as primary)**: near-zero new infra — `POST /api/communications/send` already accepts arbitrary recipient emails + `AttachmentDocumentIds` resolved from SPE **app-only** (`CommunicationService.cs:2240` → `SpeFileStore.DownloadFileAsync`) + request associations. Only new work: a trigger on legal's MDA review → `SendAsync`.
- **In-app feedback**: render legal's decision/response on the "my requests" detail via the `requester==caller` endpoints (task 034/035). Push/badge via notification-spine works for **workforce** submitters but **not CIAM contacts** (spine is `systemuser`-keyed, `OutboxService.cs:135`) → for CIAM, read-on-request (no spine).
- **New schema (additive to task 030)**: a decision/outcome field + response text + a **response**-document linkage (distinct from the submitted doc).
- **Effort**: Low–Medium. **Recommend: R2** (email + in-app render of decision/response) — completes the Front Door value loop cheaply; defer live push-badge.

### 3. Q&A Assistant (leverage the Spaarke AI Assistant)
- **Reuse (as-is)**: `SprkChat` is context-agnostic per ADR-012 (pluggable `authenticatedFetch`/`getAccessToken`, no Xrm) — embed in the SPA/Teams shell in generic conversational mode.
- **Thin-new**: a **new dual-scheme, broker-only, entitlement-scoped assistant endpoint group** (existing `/api/ai/chat` is workforce-only + OBO), with an **ADR-039 closed tool catalog** restricted to Q&A + "submit a Front Door request", and **app-only RAG grounding** (no OBO `RetrievePrincipalAccess`). The surface-launch spine already exists to route "submit NDA" → the intake wizard.
- **Blocker**: OBO document-ACL + workforce scheme → must re-host broker-only + app-only grounding.
- **Effort**: Medium–High (new AI endpoint on the external plane; auth-sensitive; ADR-013/§10 placement). **Recommend: R2-stretch or R3 fast-follow** (built on the P1/P2 framework). Grounds naturally on the P&P RAG (item 5).

### 4. Messaging: MDA-user ↔ external-workspace-user chat (owner directive)
- **Reuse (as-is)**: the `sprk_communicationparticipant` junction already supports **mixed `systemuser` + `contact`** participants in one thread; the `sprk_communicationthread` model is channel-agnostic; `ConversationView`/`ConversationWorkspace` are context-agnostic + already exported and consumed by external-spa's lib dep; `AcsIdentityService` mints ACS identities uniformly server-side for **both** systemuser and contact (no OBO, no browser token for the polling UI).
- **Thin-new**: **one** new `/api/v1/external/.../threads` group (CIAM/`ExternalCollaboration` policy, **app-only, participation-scoped** to the contact's junction rows, re-running `CommunicationAccessFilter` with `IsInternalUser=false`), external ACS mint, + add the external contact to the ACS membership reconciliation set. MDA user posts via existing internal `/send`; external posts via the new broker write; both land on the same `sprk_thread`.
- **Blocker**: current reads are `systemuserid`-impersonated (fail-closed, no app-only fallback, wrong scheme) → resolved entirely by the new external group; nothing external users touch changes.
- **Coordination**: `Services/Communication/**` is a HEAVILY shared surface (`/conflict-check` mandatory; email-r5, messaging-r1/r2/r3, notification-spine).
- **Effort**: Medium (one thin group; heavy reuse). **Recommend: R2-stretch or R3** — high value (cross-boundary collaboration), natural module on the framework, but adds a shared-surface module. Overlaps item 2 (a chat thread IS a feedback channel).

### 5. Policy & Procedures content library — where content resides
- **Decision (researcher, well-sourced)**: home it on **SPE (content bytes) + a custom unrestricted `sprk_policy` Dataverse entity (lifecycle/metadata: title, category, owner, version, effective/expiration, status, SPE pointer or rich-text) + the existing RAG layer**. Reuses SPE broker + RAG already run.
- **AVOID Dataverse `knowledgearticle`**: it is a **restricted table** — authoring needs a **D365 Customer Service Enterprise license per author**, and serving it to unlicensed external readers hits Microsoft's **multiplexing** rule (a genuine licensing landmine). The free S2S app user does not launder the requirement.
- **§11 check**: no `sprk_policy`/`sprk_procedure` exists yet; a rich AI-knowledge system does (`sprk_knowledgesource`, knowledge-packs `sprk_knowledgepackref`, `AnalysisKnowledgeService`) — the RAG-grounding half can lean on that.
- **Scope nuance to resolve with owner**: current R2 P3 models P&P as a *submit/read a P&P **request*** module. The owner's question reframes P&P as a *browse/read the policy **library***. These are different surfaces (a request workflow vs a content library). Likely both, but must be scoped.
- **Effort**: Medium (new `sprk_policy` entity + SPE authoring/versioning conventions + browse/read UI + RAG pointing). **Recommend: R2 for the storage decision + basic browse/read**; defer rich authoring workflow. One residual: get a Microsoft licensing-desk answer on external non-employee read of a custom unrestricted Dataverse table via a custom app (very likely fine).

## Reinforcing interactions
- **P&P (5) → Q&A assistant (3)**: if P&P lands in SPE+RAG, the same retrieval layer grounds the assistant's answers — build 5 first, 3 gets cheaper + more useful.
- **Messaging (4) ↔ feedback (2)**: a cross-boundary chat thread is one form of "direct feedback in the Front Door" — they may share one delivery story rather than two.

## New ADR tensions these introduce (for spec.md ADR Tensions on amendment)
- **ADR-013 / §10 (BFF AI facade + hygiene)**: exposing an AI assistant + AI pre-fill on the external plane → Placement Justification + PublicContracts facade + closed tool catalog (ADR-039). Path C (comply) with a documented external-scoped surface.
- **ADR-028 (+A3)**: the assistant/messaging/pre-fill external endpoints extend the dual-plane principal-agnostic pattern A3 already ratifies → covered by A3, cite it.
- **ADR-039 (closed tool catalog)**: external assistant tool catalog must be a restricted subset — an explicit external catalog projection.

## Recommended scope envelope (owner decides)
- **R2 core** (cheap, completes the Front Door value loop, enhances already-planned modules): **(1) NDA auto-fill**, **(2) feedback loop (email + in-app render)**, **(5) P&P storage decision + basic browse/read**.
- **R2-stretch or R3 fast-follow** (heavier new surfaces, but the R2 framework makes them cheap later): **(3) Q&A assistant**, **(4) cross-boundary messaging**.
- All five are architecturally coherent on the R2 external module framework; the split is about keeping R2 shippable, not about whether they fit.

## OWNER DECISION (2026-08-06, post-P0-review)
**ALL FIVE land in R2** (owner overrode the R3 deferral for assistant + messaging). **P&P = BOTH** (browse/read
library `sprk_policy`+SPE+RAG AND a submit P&P request workflow). Consequence: R2 ~doubles in build surface.
**Plan restructure**: expand **P3** (Legal Front Door) to absorb NDA auto-fill + P&P library + feedback loop;
add a new **P5 "Collaboration Surfaces"** phase for the Q&A assistant + cross-boundary messaging. Specced as
**FR-23–FR-27** (see spec.md amendment). Prototype extended to cover all five for the task-004 visual gate.
