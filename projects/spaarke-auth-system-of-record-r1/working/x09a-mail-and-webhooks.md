# x09a — Guest mail paths, Graph mail setup for a new stamp, and webhook routes

> Read-only research, 2026-10-09. Code is the authority; notes and guides were leads that were checked against code.
> Commits read: `origin/master` at `41ce8e316` (fetched during this session; newer than the `231c5ab2b` named in the brief);
> provisioning worktree `C:\code_files\spaarke-wt-customer-provisioning-orchestration-r1` at `6b5dd1527` (42 ahead / 30 behind
> master, **byte-identical** to master in `Handlers/IntegrationWiring/**`, `CommunicationEndpoints.cs`, `Api/Compose/**` and
> `Services/Communication/**`); Word add-in worktree at `0df508eab` (2 ahead, identical to master in `EmailView.tsx`).
> Paths below are relative to the repo root; `BFF/` = `src/server/api/Sprk.Bff.Api/`, `L2/` = `src/server/services/Sprk.Provisioning.ControlPlane.Core/`,
> `ADDIN/` = `src/client/office-addins/`. Labels: **CODE** = read in code at the cited commit; **DOC** = document claim not provable
> from code; **LIVE** = prior live read-out (x04/x06); **NOT VERIFIED** = cannot be proven from this repo.

Terms for the owner. **OBO** ("as the user"): the BFF swaps the user's sign-in token for a Graph token that carries the user's identity;
the swap always happens in Spaarke's tenant (`BFF/Infrastructure/Graph/GraphClientFactory.cs:279-293`, a06 Flow C). **App-only**: the
BFF acts as itself (the stamp's managed identity). **Model 1 guest**: a customer employee signed in to Spaarke's tenant as a B2B guest;
their mailbox is in their own company's tenant, which no Spaarke-tenant token — user or app — can open.

---

## Q1 — Guest mail paths

### Q1.1 Paths that read or send mail "as the user" (Graph OBO)

| # | What the user does | Code path (CODE) | Mailbox addressed | Model 1 guest | Loud or silent? | Fix on master? |
|---|---|---|---|---|---|---|
| 1 | **Outlook add-in task pane — Save an email** | `POST /api/office/save` → `OfficeEmailEnricher.EnrichEmailFromGraphAsync` → `ForUserAsync` → `Me.Messages[itemId]` (`BFF/Services/Office/OfficeEmailEnricher.cs:40-71`). Runs **only when the request has no body** (`:40`). | `/me` = the guest's non-existent Spaarke-tenant mailbox | Graph call fails | **Silent**: the catch returns the request unchanged (`:144-154`) → a headers-only `.eml` is saved as success (H-7) | **Yes, client-side**: the add-in now reads body + every attachment with Office.js and sends them, so the server never calls Graph (`ADDIN/shared/taskpane/hooks/useSaveFlow.ts:518,1065-1066`; `ADDIN/shared/taskpane/services/emailContentCapture.ts:251`). First on master in `b366b3336` (task 116/116a), merged via PR #1454 (`b0a78f880`). **Residual**: capture is gated on `canGetAttachments` = Outlook read mode AND Mailbox 1.8 (`ADDIN/shared/adapters/OutlookAdapter.ts:645`); outside that gate the request reverts to the Graph fallback and a guest gets the silent headers-only save again. |
| 2 | **Outlook ribbon Quick Save** | same capture, same gate (`ADDIN/outlook/commands/index.ts:97-100`); posts only when the association engine has a prediction (DOC: add-in note 116a §1) | as 1 | as 1 | as 1 | as 1 |
| 3 | **Attachment-only save** (`SaveRequest.Attachment` without content) | `OfficeEmailEnricher.EnrichAttachmentFromGraphAsync` → `ForUserAsync` → `Me.Messages[parentEmailId]` (`:161-248`) | `/me` | fails | **Loud**: rethrows `InvalidOperationException` (`:244-248`) | No add-in caller sends `parentEmailId` on master (grep over `ADDIN/` returned none) — whether any other client reaches this path is NOT VERIFIED |
| 4 | **Word add-in "Email" tab — send the open document** | `EmailView.tsx` locks `sendMode="user"` (owner decision) (`ADDIN/shared/taskpane/components/views/EmailView.tsx:45,312`) → `POST /api/communications/send` → `CommunicationService.SendAsUserAsync` (`BFF/Services/Communication/CommunicationService.cs:1131-1133,1478-1567`) → `EmailChannelSender` `ForUserAsync` + `Me.SendMail` (`BFF/Services/Communication/Channels/EmailChannelSender.cs:55-64`). Word only (`EmailView.tsx:54-55`). | `/me/sendMail` | **fails every time** | **Loud**: `GRAPH_SEND_FAILED` with `sendMode=User` (`EmailChannelSender.cs:85-110`); the tab shows the server's reason (`EmailView.tsx:46-47`) | **No** — not on master, not on the add-in branch (`EmailView.tsx` identical in both). Exact Graph error for a guest NOT VERIFIED (x03 E3). |
| 5 | **Shared `EmailComposer`** (CommunicationActions PCF, EmailPage, wizard steps) — user picks "my mailbox" | default is `sharedMailbox`; the From radio is offered whenever the host does not lock `sendMode` (`src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/EmailComposer.reducer.ts:171-173`; `EmailComposer.tsx:527-530,1283-1291`) → path 4 | `/me/sendMail` | fails when the guest chooses their own mailbox; works on the default (shared) | loud (as 4) | No guard; nothing hides the choice for guests |
| 6 | **Daily Briefing — "email me / a colleague"** | `POST /api/ai/daily-briefing/email` (`BFF/Api/Ai/DailyBriefingEndpoints.cs:20,85`) → `OutputRouter` → `CommunicationEmailDispositionSender` with `SendMode.User` (`BFF/Services/Ai/EmailDispositionSender.cs:38-41,114`) → path 4 | `/me/sendMail` | fails | **Loud** ("Throws on failure — never silent", `:46`) | No |
| 7 | **Playbook `sendEmail` node** | `SendEmailNodeExecutor` → `ForUserAsync` + `Me.SendMail` (`BFF/Services/Ai/Nodes/SendEmailNodeExecutor.cs:234-241`) | `/me/sendMail` | fails | **Loud** in the run output: `NodeOutput.Error` on Graph error (`:261-276`); with no `HttpContext` (Service-Bus-run playbooks) it errors for everyone (`:222-231`) | No |
| 8 | **AI privilege groups** — `/me/memberOf` fallback when the groups claim is absent/overage | `BFF/Services/Ai/Security/PrivilegeGroupResolver.cs:170-181`; fail-closed to an empty list (`:227-233`) | the guest object in **Spaarke's** directory — not a mailbox | expected to **work** (guest object and Spaarke groups are in the token's tenant); NOT VERIFIED live | fail-closed, logged | n/a |
| 9 | **`/me/todo` sync** | feature flag `Spaarke:Graph:TodoSync:Enabled` (default false) binds `Null*` no-ops; flag on binds `NotImplemented*` placeholders that throw — only those two families exist (`BFF/Infrastructure/DI/TodoSyncModule.cs:47,68-80`; `BFF/Services/Todo/{NullObject,Placeholder}/*`) | none today | no exposure today; as designed (OBO `/me/todo`) it would hit the same wall | n/a | design only (appsettings.template.json:162) |

### Q1.2 Paths that send or read mail app-only (shared / communication accounts)

| Path | Code (CODE) | Identity / mailbox | Model 1 guest |
|---|---|---|---|
| **Outbound shared-mailbox send** (default of every `EmailComposer` host; `SendMode.SharedMailbox` is the request default, `BFF/Services/Communication/Models/SendCommunicationRequest.cs:71`) | `CommunicationService` shared branch (`:1136-1230`) → `ApprovedSenderValidator.Resolve` (config `Communication:ApprovedSenders` + `Communication:DefaultMailbox`, overlaid by `sprk_communicationaccount` rows with `sprk_sendenabled`, Dataverse wins — `BFF/Services/Communication/ApprovedSenderValidator.cs:164-209`) → `EmailChannelSender` `ForApp()` + `Users[from].SendMail` (`:68-75`); daily limit from the account row (`CommunicationService.cs:1172-1202`) | stamp managed identity; the configured **shared mailbox in the stamp's tenant** | **Works** for the send itself. The communication record is still filed as the caller (`BuildDataverseRecordAsync` with `RecordRequester.OfCaller`, `:1208-1209`), which needs the guest's Dataverse `systemuser` (cross-ref record §6.2 path F). |
| **Inbound intake** (mailbox → `sprk_communication`) | `GraphSubscriptionManager` subscribes `users/{account}/mailFolders/{folder}/messages` app-only (`BFF/Services/Communication/GraphSubscriptionManager.cs:424-435`); `IncomingCommunicationProcessor` reads `Users[mailboxEmail]` app-only (`:231-233,927-928`); `InboundPollingBackupService`, `MailboxDeltaReconciliationService`, `MailboxVerificationService` (`:166,185,218-220`) — all over `sprk_receiveenabled` accounts | stamp managed identity; **shared mailboxes in the stamp's tenant only** | guest-neutral: no user token involved. A guest's own home-tenant inbox is never read. |
| Account type "User Account" → `AuthMethod.OnBehalfOf` | `BFF/Services/Communication/Models/CommunicationAccount.cs:51-55` — `DeriveAuthMethod()` has **no caller** | — | the account-type choice never changes the identity used |

### Q1.3 Recommended pattern for Model 1 guests, and what would change

Rule: **a Model 1 guest never sends or reads mail through Graph OBO.** Send from the shared/communication mailbox app-only (already
works, path Q1.2) or hand the message to the user's own Outlook client (`displayNewMessageForm`, task 036 — Outlook read mode only).
Read the user's email only through the add-in (Office.js) and send the content with the request (116a — already the server's preferred
input, `OfficeEmailEnricher.cs:40`).

Code that would change:

1. `ADDIN/shared/taskpane/components/views/EmailView.tsx:312` — lock `sendMode="sharedMailbox"` (or offer the From radio with the
   user-mailbox option hidden for guests). Owner: **spaarkeai-word-add-in-r1**.
2. `BFF/Services/Ai/EmailDispositionSender.cs:114` (Daily Briefing) and `BFF/Services/Ai/Nodes/SendEmailNodeExecutor.cs:234-241`
   (playbook node) — send from the default approved sender via the channel sender app-only, with the user as recipient. Owner: **AI**.
3. `BFF/Services/Communication/CommunicationService.cs:1512-1533` — a defined refusal (`USER_MODE_UNAVAILABLE_FOR_GUEST`, 400) or an
   automatic fall-through to the shared mailbox when the caller is a guest. The BFF reads **no** `acct`/`idp` claim anywhere today
   (a06 U2), so the guest test itself is new surface and belongs to the auth contract. Owner: **auth-system-of-record** (contract) /
   **email-communication-intelligence** (implementation).
4. `BFF/Services/Office/OfficeEmailEnricher.cs:144-154` — stop converting a failed Graph fetch into a success; return a defined reason so a
   guest outside the capture gate sees why the save had no body. Owner: **spaarkeai-word-add-in-r1**.
5. `EmailComposer` (`EmailComposer.tsx:527-530`) — accept a host flag that hides the user-mailbox choice. Owner: **communication UI**.

**Q1 summary (plain language).** Everything that sends or reads mail "as the signed-in user" goes through Spaarke's tenant, where a
Model 1 guest has no mailbox, so it fails for them: the Word Email tab (always), the composer's "my mailbox" option, the Daily
Briefing email and the playbook send node (all loud errors), and the Outlook save's server-side fetch (silent — a headers-only file).
The Outlook save is fixed on master by reading the email inside the add-in (task 116/116a, merged in PR #1454), but only in read
mode with Mailbox 1.8; the sends are not fixed anywhere. Everything that uses the shared mailbox app-only already works for guests.
The right pattern is "shared mailbox or the user's own Outlook, never OBO" — five small changes, listed above.

---

## Q2 — Graph mail setup for a NEW environment (Model 1 and Model 2)

### Q2.1 What a stamp needs — as the code reads it

| Piece | Where it is decided (CODE) | Who creates it on a new stamp |
|---|---|---|
| **Mailboxes** = `sprk_communicationaccount` rows: `sprk_emailaddress`, `sprk_sendenabled`, `sprk_receiveenabled`, `sprk_isdefaultsender`, `sprk_monitorfolder`, `sprk_graphsubscriptionid/expiry`, `sprk_accounttype` (Shared / Service / User / Distribution List) | `BFF/Services/Communication/CommunicationAccountService.cs:45-46,58-59`; `docs/data-model/sprk_communicationaccount.md:34-67` | **By hand** in the Matter Management app (entity is in the `SpaarkeMaster` sitemap), then `POST /api/communications/accounts/{id}/verify` (`BFF/Api/CommunicationEndpoints.cs:453`) which tests send/read app-only and creates the Graph subscription (`MailboxVerificationService.cs:14-18`). **L2 creates none** — the only L2/script hit on the table is a data-migration list (`scripts/Migrate-DataverseData.ps1:103`). No guide step says to do this (Q2.3). |
| **Default sender** | `Communication:DefaultMailbox` / `ApprovedSenders` (`BFF/Configuration/CommunicationOptions.cs:26`; `ApprovedSenderValidator.cs:191-200`) | intake `communicationDefaultMailbox` → KV `Communication-DefaultMailbox` (`L2/Handlers/KvSecretsPopulation/H4KvSecretsPopulationHandler.cs:908`; validated at `Sprk.Provisioning.ControlPlane.Api/Api/RunsEndpoints.cs:1308-1311`) |
| **Graph application permissions** — `Mail.Read`, `Mail.ReadWrite`, `Mail.Send`, `MailboxSettings.Read` | BFF catalog `BFF/Infrastructure/Auth/GraphAppRoles.cs:213-220`; comment `:209-212`: on provisioned stamps these are **not** Entra app roles | **H14a** grants `Application Mail.Read/ReadWrite/Send/MailboxSettings.Read` through **Exchange RBAC for Applications**, scoped to the intake group `exchangePolicyScopeGroupId`, to the **stamp managed identity only** (`L2/Handlers/DataverseAppUserGraphParity/IGraphAppRolesRegistry.cs:72-84`; `L2/Handlers/IntegrationWiring/IExchangePolicyApplier.cs:4-13`; `H14aExchangePolicySubHandler.cs:139,182-190`; parent `H14IntegrationWiringHandler.cs:250-251,267-279`). Drift = nothing changed (T4). Needs the one-time-per-tenant `Spaarke Exchange Admin` app (MI-FIC from the L2 Worker, `IntegrationWiringOptions.cs:67-74`). `ApplicationAccessPolicy` is the **legacy** mechanism for environments set up before 2026-10-04, i.e. dev/demo (`docs/guides/COMMUNICATION-DEPLOYMENT-GUIDE.md:820-831` DOC); whether dev actually has one is an open live check (x04 §3 note at `:418`) — NOT VERIFIED. |
| **Graph subscriptions** | created and renewed by the **BFF itself**, app-only, for every receive-enabled account: 30-min tick, renew under 24 h, 3-day lifetime, lifecycle notifications handled (`GraphSubscriptionManager.cs:30-32,125-133,424-435`); also on verify (`MailboxVerificationService.cs:23`) | automatic once an account row is verified. The notification URL is `Communication:WebhookNotificationUrl` = KV `Communication-WebhookUrl` = `{bff}/api/communications/incoming-webhook` written by `infrastructure/bicep/customer.bicep:793` (module call `:797`) and mapped by `scripts/canonical-secret-catalog/generated/Configure-AppServiceSettings.generated.ps1:124`. **In parallel**, H14b registers a second set of subscriptions (Q3) — the operator must supply `communicationGraphResource`/`emailGraphResource`, whose values are nowhere documented (`provisioning-runs/_templates/intake.md:17` is a placeholder; D-073-2 in `notes/task-073-h14-deviations.md` says the targets are "not finalized"). |
| **Polling** | `InboundPollingBackupService` every 5 min, 15-min lookback (`:24,43`); `MailboxDeltaReconciliationService` every 15 min with a persisted delta cursor (`:32`) — both unconditional hosted services (`BFF/Infrastructure/DI/CommunicationModule.cs:616-635`) | automatic |
| **Webhook secrets** | `Communication:WebhookClientState`, `Communication:WebhookSigningKey` (`CommunicationOptions.cs:47-48,65-66`) — bound with `services.Configure<…>` only (`CommunicationModule.cs:45`): the `[Required]` attributes are **not** enforced at boot | H4: `Communication-WebhookClientState` (generated) and `Communication-Webhook-SigningKey` (generated) (`scripts/canonical-secret-catalog/manifest.yaml:368-394`) |
| **Dataverse webhook (H14c)** | nothing in the BFF reads a Dataverse-originated communication webhook (route unmapped — Q3) | registered anyway (dead wiring) |

### Q2.2 Model 1 vs Model 2 — which mailbox a stamp can actually use

- **Model 1 (stamp in Spaarke's tenant).** Every mail identity is Spaarke-tenant: the stamp UAMI, the Exchange role assignments, the
  scope group ("created by the Exchange admin of the stamp's tenant", `L2/Models/IntakeParameterCatalog.cs:182`; "once per tenant —
  Spaarke's, for Model 1", `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md:473`). Therefore the mailboxes a Model 1 stamp can send
  from or watch are **shared/service mailboxes Spaarke creates in its own tenant** and puts in that group. The customer staff's
  mailboxes live in the customer's home tenant; no Spaarke-tenant token — app-only (every app-only credential is pinned to
  `TENANT_ID`, record §6.1 paths A/D) or OBO (Q1) — can open them. By construction, NOT VERIFIED by a live Graph call.
  So on Model 1: inbound intake = only what arrives in the Spaarke-tenant shared mailbox (customers must email or forward there);
  outbound = the shared mailbox app-only; "as the user" = only through the add-in client (capture) or the user's own Outlook (compose).
- **Model 2 (stamp in the customer's tenant).** The design lines up (UAMI, Exchange roles, mailboxes all in one tenant), but nothing
  can run it today: H14b signs in with `new DefaultAzureCredential { TenantId = customer }` as the **L2 Worker UAMI**
  (`L2/Handlers/IntegrationWiring/GraphRestSubscriptionCreator.cs:67`), which only exists in Spaarke's tenant (M-9); the Exchange Admin
  app is "one-time per tenant" with no customer-tenant procedure; and the FIC guard states that a profile pointed at a customer
  tenant is a misconfigured run (`L2/Handlers/EntraAppReg/CrossTenantFicRefusedException.cs:17-28`). Undecided.

### Q2.3 Is it documented, and is it accurate?

- `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` — §4.2.1 Exchange admin app (`:468-505`), intake rows (`:450-452`), H14 row
  (`:561`), §7.7 (`:1231-1236`), §7.9 (`:1288-1290`), T4 (`:1356`): **accurate** for H14a (mechanism, four roles, group scope,
  stamp UAMI only). **Inaccurate**: §7.9(b)/(c) say the Graph and Dataverse webhooks are wired and "fire with correct HMAC" — they
  cannot (Q3). **Missing**: creating and verifying the `sprk_communicationaccount` rows; the meaning/value of
  `communicationGraphResource`/`emailGraphResource`; the fact that H14b's subscriptions duplicate the BFF's own. It defers mailbox
  setup to `COMMUNICATION-DEPLOYMENT-GUIDE.md` (`:1538`), which is the dev-era guide: legacy `ApplicationAccessPolicy` (flagged
  as legacy at `:820-823`), dev host `spe-api-dev-67e2xz` (`:597`), kebab secret names (`:253`).
- `notes/t261-stamp-graph-least-privilege.md` (provisioning worktree, **not on master**) says `MailboxSettings.Read` is dropped, but
  the registry in the same worktree still lists four Exchange-scoped roles (`IGraphAppRolesRegistry.cs:71-73` there) — the note is
  ahead of the code.
- Prior record H-14 (LIVE): the **L2 Worker UAMI** holds tenant-wide `Mail.ReadWrite` + `MailboxSettings.Read`, and `mi-bff-api-dev`
  holds `Mail.Read` + `Mail.Send` as Entra roles (x04 `:415-416`) — dev is on the legacy shape, not the stamp shape.

### Q2.4 Clear statement

| | Works today | Designed but unbuilt | Undecided |
|---|---|---|---|
| Model 1 | shared-mailbox send and polling-based intake once an operator creates + verifies account rows in Spaarke-tenant mailboxes inside the scope group; H14a role grants (live-verified by the provisioning project on 2026-10-04 — DOC, `notes/t251-exchange-sidecar-design.md` header) | account-row creation/verification as a provisioning step; H14b targets; a working webhook receiver (Q3) | which Spaarke-tenant mailbox each customer gets, and how customer staff mail reaches it (forwarding? add-in only?) |
| Model 2 | nothing provisionable | the same handlers, once L2 can act in a customer tenant | whether Spaarke's Exchange Admin / Worker identities are ever federated into customer tenants |

**Q2 summary (plain language).** A new stamp gets mail in three parts: Exchange permissions (built — H14a grants the stamp's identity
the four mail roles, limited to one security group of mailboxes in the stamp's tenant), configuration and secrets (built — bicep + H4),
and the mailbox records in Dataverse (not provisioned — an operator creates and verifies them by hand, and no guide says so). On
Model 1 those mailboxes can only be Spaarke-tenant shared mailboxes; customer employees' own mailboxes are unreachable from Spaarke's
tenant. Model 2 is designed to line up but cannot be provisioned. The deployment guide is right about the Exchange part and wrong
about the webhooks.

---

## Q3 — Webhook routes

### Q3.1 What the stamp registers vs what the BFF serves (CODE, identical on master and the provisioning branch)

| Registration | Registered URL | BFF route | Identity that registers | Can it ever deliver? |
|---|---|---|---|---|
| **H14b** Graph change-notification subscription per module, `changeType=created,updated`, `clientState` = KV `Communication-Webhook-SigningKey` | `{InterStepState.BffApiUrl}/api/webhooks/graph/{communication|email}` (`L2/Handlers/IntegrationWiring/H14bGraphWebhookSubHandler.cs:55,159-163,196-197`; base URL from H9, `H14IntegrationWiringHandler.cs:254-262`) | **unmapped** — the BFF maps only `/api/communications/incoming-webhook` (`BFF/Api/CommunicationEndpoints.cs:470`) and `/api/compose/webhooks/spe-doc-changed` (`BFF/Api/ComposeSyncEndpoints.cs:43`); grep for `/api/webhooks` over the BFF: none | **L2 Worker UAMI** via `DefaultAzureCredential { TenantId }` (`GraphRestSubscriptionCreator.cs:67,106-153`), no lifecycle URL; renewal explicitly out of scope (`IntegrationWiringOptions.cs:94-103`) | No. Graph's create handshake posts a `validationToken` to the URL and needs it echoed; a 404 fails the create, and H14b turns any failure into `HandlerResult.Failure` (`:180-186`) — so H14 should **fail the run**, not pass silently. Whether a run has reached H14 end-to-end is NOT VERIFIED (the only H14 live evidence is H14a's sidecar run). Even with a mapped route the notification would be rejected: `clientState` is the **signing key**, while the BFF compares against `Communication-WebhookClientState` (`Configure-AppServiceSettings.generated.ps1:123`; handler check per a08 §2.7, `CommunicationEndpoints.cs:1307-1335`). |
| **H14c** Dataverse `serviceendpoint` "Spaarke-Communication-Webhook" (contract 8 = WebHook, JSON, auth None, HMAC key in the record) | `{BffApiUrl}/api/webhooks/dataverse/communication` (`H14IntegrationWiringHandler.cs:290`; `H14cDataverseWebhookSubHandler.cs:45,135-139`) | **unmapped** | customer BFF app via `DefaultAzureCredential` (`DataverseWebApiServiceEndpointWebhookRegistrar.cs:6`) | No — and it would never fire anyway: the registrar writes only the `serviceendpoints` row (`:117-147`); **no `sdkmessageprocessingstep` is registered anywhere in L2** (grep), and a Dataverse webhook without a step is inert. Nothing in the BFF expects a Dataverse-sourced communication webhook (the deleted `/api/v1/emails/webhook-trigger` family is the orphan noted in L-4). |
| **BFF's own** Graph subscription per receive-enabled account, `clientState` = `Communication:WebhookClientState` | `Communication:WebhookNotificationUrl` = `{bff}/api/communications/incoming-webhook` on stamps (`customer.bicep:793`) and on dev (LIVE x06 `:140`) | mapped | stamp UAMI (`GraphSubscriptionManager.cs:424`) | The route exists, but the receiver demands an HMAC header Graph never sends: `WebhookSignatureFilter` returns **401 when `X-Hub-Signature-256` is absent** (`BFF/Api/Filters/WebhookSignatureFilter.cs:108-117`), only the `validationToken` handshake bypasses it (`:82-87`), and the BFF's own template states the header is for "a signing relay … since Graph itself does not sign bodies" (`BFF/appsettings.template.json:400`). No relay exists in `infrastructure/`, `scripts/` or `.github/` (grep). So real-time delivery is rejected on **every** environment, dev included (a08 §2.7 recorded the same). |

### Q3.2 What each webhook is for, and what still works without it

| Feature | Webhook | Backstop that keeps it working | What a Model 1 stamp user notices |
|---|---|---|---|
| **Inbound email intake** — a mail arriving in a shared mailbox becomes a `sprk_communication` (+ attachments, filing) | Graph notification → `/api/communications/incoming-webhook` → `IncomingCommunication` job | `InboundPollingBackupService` every 5 min (unconditional, `CommunicationModule.cs:635`; `:24`) and `MailboxDeltaReconciliationService` every 15 min (`:625`; `:32`); both enqueue the same idempotent job (`MailboxDeltaReconciliationService.cs:9-17`). No config flag turns either off. | Nothing breaks; new mail shows up **up to ~5 minutes late** instead of seconds. The subscriptions the BFF keeps creating are wasted, and the `Communication-Webhook-SigningKey` secret protects nothing. |
| **Compose document sync** — detect that an SPE file changed under an open compose session | Graph drive notification → `/api/compose/webhooks/spe-doc-changed` (same filter + `Compose:Webhook:*`) | authenticated poll `POST /api/compose/document/{id}/check-changes` (`ComposeSyncEndpoints.cs:15-16,60-70`) | depends on the client calling check-changes; not a mail path — out of this note's scope beyond the route inventory. H14 does not touch it. |
| **H14c Dataverse webhook** | none in the BFF | n/a | nothing — dead wiring |

### Q3.3 The signing-key name

Three spellings, each internally consistent within one deployment shape, inconsistent across shapes:

| Shape | KV secret the BFF setting points at | Who seeds it | Evidence |
|---|---|---|---|
| **Template / legacy prod script** | `communication-webhook-signing-key` (kebab) for `Communication:WebhookSigningKey`; `communication-webhook-secret` for `WebhookClientState` | `scripts/Seed-ProductionKeyVault.ps1:233` seeds only the client-state secret; the prod settings script sets URL + client state (`scripts/Configure-ProductionAppSettings.ps1:134-135`) and **no signing key** (grep) | `BFF/appsettings.template.json:399-401`; `docs/guides/CONFIGURATION-MATRIX.md:357-358` |
| **Dev (live)** | `Communication-WebhookSigningKey`, `Communication-WebhookClientState` | hand-set | LIVE x06 `:139-141`; `config/spaarke-resources.yaml:348-349,537` |
| **Provisioned stamps** | `Communication-Webhook-SigningKey` (H14b/H14c read the same name, `H14bGraphWebhookSubHandler.cs:55`; `H14cDataverseWebhookSubHandler.cs:48`); `Communication-WebhookClientState` | H4 generates both (`manifest.yaml:368-394`) | `Configure-AppServiceSettings.generated.ps1:123-125` |

So on a **stamp the BFF and H14b/H14c agree** on the name (H-3's "mismatch" is cross-shape, not within a stamp). The real stamp-side
defect is semantic: H14b uses the **signing key** as Graph `clientState`, while the BFF validates `clientState` against the
**client-state** secret — two independently generated values. A template-based redeploy onto dev or a stamp would point at secrets
that do not exist there (the kebab name), and the dev vault holds neither of the other two spellings (x04 §4). `SECRET-ROTATION-PROCEDURES.md:28`
uses the dev spelling and tells the operator to "re-register the sender (Graph subscription) with the new key" — Graph subscriptions
carry no HMAC key, so that instruction has no code behind it.

### Q3.4 Does "provisioned customer environment" mean Model 1 only?

**In practice yes; by design no.** The DAG runs H14 on every run with no profile or model gate
(`L2/Reconciler/DagAdvancer.cs:178`); the intake accepts profile `customer-owned-model2` paired with `Model2`
(`L2/Models/ProvisioningRun.cs:135-137`). But every H14 identity is Spaarke-tenant: the Exchange Admin app is set up "once per tenant
(Spaarke's, for Model 1)" (guide `:473`), H14b/H14c sign in as Spaarke-tenant principals with a `TenantId` override that cannot
reach a foreign tenant (M-9), and the H3 guard treats a Spaarke-hosted profile pointed at a customer tenant as a misconfiguration
(`CrossTenantFicRefusedException.cs:21-26`). No Model 2 run can reach H14 today.

**Q3 summary (plain language).** The provisioning step wires two webhooks to addresses the BFF does not have, so a stamp's
provisioning run should stop at H14b rather than quietly succeed; the Dataverse webhook could never fire anyway because no trigger
step is registered. Separately, the one Graph receiver the BFF does have rejects every real Graph notification because it insists on
a signature Graph never sends and no signing relay exists — on dev as well as stamps. Inbound email still works everywhere because two
polling jobs (5 and 15 minutes) run unconditionally; users just see new mail a few minutes late. The secret-name confusion is three
spellings across three deployment shapes, consistent within each; the stamp-side bug is that H14b echoes the wrong secret.

---

## What needs to change, owned by which project

| # | Change | Owner |
|---|---|---|
| 1 | Point H14b at the real receiver (`/api/communications/incoming-webhook`) with `clientState` = `Communication-WebhookClientState`, or delete H14b in favour of the BFF's own `GraphSubscriptionManager` (which already creates, renews and self-heals subscriptions per account) — `H14bGraphWebhookSubHandler.cs:55,196-197`; `H14IntegrationWiringHandler.cs:254-302` | customer-provisioning-orchestration-r1 |
| 2 | Delete H14c (no consumer, no step) or register the `sdkmessageprocessingstep` + a BFF route — `H14cDataverseWebhookSubHandler.cs`; `DataverseWebApiServiceEndpointWebhookRegistrar.cs:117-147` | customer-provisioning-orchestration-r1 |
| 3 | Decide the receiver's security contract: either deploy a signing relay (none exists) or make the HMAC layer optional for Graph-direct delivery and rely on `clientState` + Graph's validation handshake — `WebhookSignatureFilter.cs:108-117`; `CommunicationEndpoints.cs:462-476`; the same for Compose (`ComposeSyncEndpoints.cs:43-47`) | email-communication-intelligence (communication receiver); compose project (SPE receiver); auth-system-of-record records the decision |
| 4 | Add a provisioning step (or runbook step with a script) that creates and verifies the stamp's `sprk_communicationaccount` rows for the intake mailbox, and document `communicationGraphResource`/`emailGraphResource` (or remove them with #1) | customer-provisioning-orchestration-r1 |
| 5 | Canonicalise the signing-key and client-state names across template, dev and stamp shapes (one name, aliases retired) and fix `SECRET-ROTATION-PROCEDURES.md:28` | customer-provisioning-orchestration-r1 (canonical catalog) + auth-system-of-record (docs) |
| 6 | Guest send paths: lock the Word Email tab to the shared mailbox; Daily Briefing + `sendEmail` node via the shared mailbox; server-side guest refusal in `SendAsUserAsync`; hide "my mailbox" in `EmailComposer` for guests (Q1.3 items 1–5) | spaarkeai-word-add-in-r1; AI; email-communication-intelligence; auth-system-of-record (guest-claim contract) |
| 7 | Guest read path: close the capture gate residual (compose mode / pre-1.8) and stop the silent headers-only save — `OfficeEmailEnricher.cs:144-154`; `OutlookAdapter.ts:645` | spaarkeai-word-add-in-r1 |
| 8 | Correct `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §7.9(b)/(c) and H14 row; replace the `COMMUNICATION-DEPLOYMENT-GUIDE.md` pointer with a stamp-shape mailbox runbook (which mailbox, which group, how customer staff mail reaches it) | customer-provisioning-orchestration-r1 (guide) ; auth-system-of-record (§14 doc disposition) |
| 9 | Record the Model 1 mail model as a decision: Spaarke-tenant shared mailbox per customer; customer staff mailboxes out of reach; Model 2 mail design pending the L2 cross-tenant identity question (M-9) | auth-system-of-record (ADR-028 amendment input) |

## Live checks still open (not provable from the repo)

- The exact Graph error a Model 1 guest receives on `/me/sendMail` and `/me/messages` (x03 E3).
- Whether any provisioning run has completed H14b against a real stamp (and what Graph returned for the unmapped notificationUrl).
- Whether dev has an Exchange `ApplicationAccessPolicy` scoping `mi-bff-api-dev`'s tenant-wide `Mail.Read`/`Mail.Send` (x04 `:418`).
- Whether `/me/memberOf` returns a guest's Spaarke-tenant groups under OBO (expected yes).
