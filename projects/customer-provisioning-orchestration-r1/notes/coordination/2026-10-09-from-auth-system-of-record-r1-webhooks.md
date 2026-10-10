# To customer-provisioning-orchestration-r1, from spaarke-auth-system-of-record-r1 (2026-10-09)

**Subject:** H14 webhook wiring, the stamp mailbox step, and four smaller items in provisioning's area.
**Sent at the owner's request.** Evidence: `projects/spaarke-auth-system-of-record-r1/working/x09a-mail-and-webhooks.md` (Q2–Q3) and `auth-system-of-record.md` §11 on branch `work/spaarke-auth-system-of-record-r1`. Code checked on `origin/master` @ `231c5ab2b` and on your branch (the H14 files are identical). Nothing was changed in provisioning or in any environment.

## 1. H14b and H14c wire webhooks to routes the BFF does not serve (High) — #1560

| Step | What it registers | BFF route? | Can it deliver? |
|---|---|---|---|
| **H14b** | Graph subscriptions, notificationUrl `{stamp}/api/webhooks/graph/{module}`, `clientState` = the `Communication-Webhook-SigningKey` secret (`H14bGraphWebhookSubHandler.cs:55,159-163,196-197`) | **No.** The BFF maps only `POST /api/communications/incoming-webhook` (`CommunicationEndpoints.cs:470`) and `POST /api/compose/webhooks/spe-doc-changed` (`ComposeSyncEndpoints.cs:43`) | No. Graph's create handshake posts `validationToken` to the URL. A 404 fails the create, and H14b turns any failure into `HandlerResult.Failure` (`:180-186`), so **a run should stop at H14b**. Not verified live, because no run has reached H14 end to end. Even with the right URL, the BFF compares `clientState` against the separate `Communication-WebhookClientState` secret. |
| **H14c** | Dataverse `serviceendpoint` for `{stamp}/api/webhooks/dataverse/communication` (`H14IntegrationWiringHandler.cs:290`; `H14cDataverseWebhookSubHandler.cs:45,135-139`) | **No** | Never. The registrar writes only the `serviceendpoints` row (`DataverseWebApiServiceEndpointWebhookRegistrar.cs:117-147`), and no `sdkmessageprocessingstep` is registered anywhere in L2. Nothing in the BFF expects a Dataverse-sourced communication webhook. |

**What users would notice:** nothing breaks for mail. The BFF creates and renews its **own** Graph subscription per receive-enabled mailbox (`GraphSubscriptionManager.cs:424-435`), using `customer.bicep:793`'s correct URL. It also polls every 5 minutes (`InboundPollingBackupService`) and runs delta reconciliation every 15 minutes, both unconditionally. The real risk is a **provisioning run failing at H14b**.

**Suggested change:**
- **Delete H14b.** `GraphSubscriptionManager` already owns subscribe, renew and self-heal per mailbox. Otherwise, point H14b at `/api/communications/incoming-webhook` with `clientState` = `Communication-WebhookClientState`.
- **Delete H14c**, or register the step and agree a BFF route with email-communication.

**Related BFF finding** (not yours; #1561): the mapped receiver returns 401 when `X-Hub-Signature-256` is absent (`WebhookSignatureFilter.cs:108-117`). Graph never sends that header, and no signing relay exists. So real-time delivery is rejected on dev too, and inbound mail runs on the 5-minute polling everywhere.

**Signing-key names:** there are three spellings.
- The template uses `communication-webhook-signing-key`.
- Dev uses `Communication-WebhookSigningKey`.
- Stamps use `Communication-Webhook-SigningKey`.

H14b/H14c and the stamp settings agree with each other, so it is a cross-shape problem, not a within-stamp one. Please make the canonical catalog the single name.

## 2. No provisioning step creates the stamp's mailboxes (High, needs an owner decision) — #1562

A stamp's mail needs three things:

1. **Exchange permissions — built.** H14a grants the stamp identity Mail.Read/ReadWrite/Send/MailboxSettings.Read through Exchange RBAC for Applications, scoped to one group (`IGraphAppRolesRegistry.cs:72-84`).
2. **Settings — built.**
3. **Mailbox records — not built.** These are `sprk_communicationaccount` rows, verified with `POST /api/communications/accounts/{id}/verify`. They are created by hand, and **no guide says so**.

Under Model 1, only Spaarke-tenant shared mailboxes are reachable. A customer employee's own mailbox lives in their home tenant, and no Spaarke-tenant identity can reach it.

The deployment guide is right about H14a and wrong about H14b/H14c (§7.9(b)/(c) says they "fire with correct HMAC"). It also says nothing about the account rows or the `communicationGraphResource` values.

**Asks:**
- Once the owner decides the Model 1 mail model (which Spaarke-tenant shared mailbox each customer gets, and how customer mail reaches it), add a step or scripted runbook that creates and verifies the account rows.
- Correct the guide.

## 3. Other findings in provisioning's area

- **M-17 — standing grants on new stamps (#1565).** H7b does not add the Standing Grant Administrators FLS Read on `contact.sprk_standinggrant`, nor the Access Administrator / Core User privilege edits (`SecureRecordSetupProcedure.cs:221,313,527-540,642-682`). On a new stamp, standing grants read as not held. Dev only works because the BFF identity is System Administrator; the profile has no members live.
- **Missing secrets on stamps (#1570).** `PowerBi__ClientSecret`, `Rag__ApiKey` and `Notifications__SignalR__ConnectionString` are absent from `scripts/canonical-secret-catalog/manifest.yaml`. A stamp starts, but with no SignalR notifications (a null object), RAG enqueue returning 401, and no reporting embed. The owner decides which of these stamps need.
- **L-10 — L2 start-up and the T7 guard.** The L2 hosts refuse to start without `ReservedTenants__*`, and the bicep `ciamTenantIds` has no default. The T7 Spaarke-tenant guard is a silent no-op when `AzureAd__TenantId` is a Key Vault reference (`CustomerIdentityT7Probe.cs:255-272`).
- **Model 1 guests and `CustomerTenantIds`.** Model 1 staff sign in as guests in Spaarke's tenant (tid = Spaarke, acct = 1, verified live 2026-10-08), so the customer-tenant list never admits them on the contact path. The fix sits in the BFF member test (word-add-in G2 / task 126, awaiting UAC-r2; #1563) and needs no provisioning data change. Do **not** add Spaarke's tenant to the list.
- **`Set-ExternalFlagForB2BGuests.ps1` (#1564).** The deployment guide (`:1051-1056, 1284-1296`) tells operators to run it, but under Model 1 it would mark every customer employee external. Please stop recommending it for Model 1 stamps until UAC-r2 decides the rule.

GitHub issues: #1560, #1561, #1562, #1563, #1564, #1565, #1570 (label `spaarke-auth-system-of-record-r1`).
