# To customer-provisioning-orchestration-r1, from spaarke-auth-system-of-record-r1 (2026-10-09)

The code-level auth investigation (master @ `8a9ecaac1`) found a defect in provisioning that stops communication
ingest on every provisioned customer stamp. It holds on today's master, and two separate passes checked it against the code.

## What is wrong

Provisioning registers the stamp's webhooks at routes the BFF does not serve.

| Registered by provisioning | Route |
|---|---|
| Dataverse service-endpoint webhook (`H14IntegrationWiringHandler.cs:290`, H14c) | `{stamp}/api/webhooks/dataverse/communication` |
| Graph change-notification subscriptions (`H14bGraphWebhookSubHandler.cs:197`) | `{stamp}/api/webhooks/graph/{module}` |

| Served by the BFF | Route |
|---|---|
| Communication webhook (`CommunicationEndpoints.cs:470`) | `POST /api/communications/incoming-webhook` |
| Compose SPE change notifications (`ComposeSyncEndpoints.cs:43`) | `POST /api/compose/webhooks/spe-doc-changed` |

The BFF has no `/api/webhooks/...` route. On a provisioned stamp, Dataverse and Graph post to an endpoint that
does not exist, so no inbound communication is processed. Dev works because its webhook URL was set by hand to
`/api/communications/incoming-webhook` (live app setting `Communication__WebhookNotificationUrl`).

The signing-key secret name that provisioning writes also differs from the one the BFF reads
(`appsettings.template.json` ~line 401; see the record's defect H-3).

## Other findings in provisioning's area (for your triage)

- **H7b does not grant Standing Grant Administrators field-security Read** on `contact.sprk_standinggrant`. On a provisioned
  environment, standing grants therefore read as not held, even though H7b and H13 pass (record M-17).
- **The L2 hosts refuse to boot without `ReservedTenants__*`.** The bicep `ciamTenantIds` parameter has no default, so
  deploying the T255 binaries onto an App Service that predates T255 fails until the platform is redeployed (record L-10; a08 D-17).
- **The T7 "Spaarke tenant" guard silently does nothing** when the slot's `AzureAd__TenantId` is a Key Vault reference,
  because `Guid.TryParse` fails and the guard skips (a09 D-21).
- **`CustomerWorkforceTenantsRule` forbids Spaarke's own tenant** as a customer workforce tenant, while the dev BFF lists it
  (`WorkforceIdentity__CustomerTenantIds__0` = Spaarke tenant). How Model 1 guest stamps should be configured is still open (a08 D-18).
- **There are two app-registration creators.** The L2 provisioner and the customer deployment guide both call
  `scripts/Register-EntraAppRegistrations.ps1`, which is described as retired (a09 D-19).

Details and evidence: `projects/spaarke-auth-system-of-record-r1/auth-system-of-record.md` §11, and the working evidence
files `a07`, `a08`, `a09` and `x03` in the same project. Nothing has been changed in provisioning or in any environment.
