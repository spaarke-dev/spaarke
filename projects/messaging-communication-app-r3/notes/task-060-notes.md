# Task 060 — Send 401 "Denied by the resource provider" — Root-Cause + Remediation

> Status: **ROOT-CAUSED → ENVIRONMENT/CONFIG (escalate).** No code bug. No code changed.
> Date: 2026-07-21 · Environment: spaarkedev1 / subscription `Spaarke Devlopment Environment` (`484bc857-3802-427f-9ea5-ca47b43db0f0`)

## Symptom (UAT §C)
Conversation compose-bar send fails:
```
Unexpected error: Denied by the resource provider. Status: 401 (Unauthorized)
ErrorCode: Denied  Content: {"error":{"code":"Denied","message":"Denied by the resource provider."}}
```
`Denied by the resource provider` is an **ACS-origin** 401 (not client, not Graph, not Dataverse).

## Send path traced (communicationType = `message`, ADR-046)
client `sendTimelineMessage` → BFF `POST /api/communications/send` → `CommunicationService` → `CommunicationChannelDispatcher.ResolveSender(Message)` → **`MessagingChannelSender.SendAsync`**:
1. `IAcsIdentityService.EnsureIdentityAsync(sender)` — reuses `sprk_communicationuserid` if mapped, else **ACS `CreateUserAsync`** (control plane).
2. `IAcsIdentityService.MintChatTokenAsync(...)` — **ACS `GetTokenAsync`** (control plane).
3. `IAcsThreadService.CreateThreadAsync` (if no thread) — ACS data plane.
4. `TransmitAsync` — ACS `SendMessageAsync` (data plane).

Steps 1–2 (`AcsIdentityService`) and the thread client (`AcsServiceChatCredential`) all authenticate the ACS `CommunicationIdentityClient` with the **DI-injected central `TokenCredential`** (`AcsServiceCollectionExtensions.AddAcsIdentityPlane` → `new CommunicationIdentityClient(endpoint, credential)`). That credential is `ManagedIdentityCredentialFactory.Create` → `DefaultAzureCredential` **pinned to the BFF UAMI** (`Graph:ManagedIdentity:ClientId`). This is the ADR-028 / NFR-05 sanctioned path — no connection string, no inline credential.

The first ACS call in the send (CreateUser or IssueToken) is presented as the BFF UAMI. ACS returns 401 `Denied` because that identity has **no RBAC role on the ACS resource**.

## Evidence (live spaarkedev1)
- ACS resource EXISTS: `spaarke-acs-dev` (`rg-spaarke-dev`), created 2026-07-16, provisioning Succeeded, host `spaarke-acs-dev.unitedstates.communication.azure.com`.
- BFF App Service = **`spaarke-bff-dev`** (`rg-spaarke-dev`) — the documented `spe-api-dev-67e2xz` name is stale.
- `Communication__Acs__Endpoint` = `https://spaarke-acs-dev.unitedstates.communication.azure.com` — **configured** (so the code's endpoint check passes; the 401 is a real ACS response, not the "endpoint unconfigured" InvalidOperationException).
- BFF identity = **UserAssigned** UAMI `mi-bff-api-dev`, clientId `5967251e-171c-46fe-a6c2-ef843c90309d`, principalId `9fd47efb-7962-492b-ac44-e5ccd0268ebb`. No system-assigned identity. Credential factory pins to `5967251e...` (verified via `Graph__ManagedIdentity__ClientId` / `ManagedIdentity__ClientId` app settings).
- **Role assignments for principal `9fd47efb-...`: 6 total, NONE on `spaarke-acs-dev`.** (OpenAI User, Cognitive Services User, KV Secrets User, SB Data Sender, SB Data Receiver, Search Index Data Contributor.) ACS scope matches: `[]`.

Conclusion: the presented identity (BFF UAMI) lacks any ACS role → ACS denies the Identity/Chat call → 401 `Denied by the resource provider`. **Root cause = missing RBAC role assignment on the ACS resource.**

## Remediation (operator — env fix, no code change)
Assign the BFF UAMI the ACS built-in role at the ACS resource scope. `Communication and Email Service Owner` (id `09976791-48a7-449e-bb21-39d1a415f350`) is the ACS built-in role that authorizes the Entra-ID caller against `Microsoft.Communication/CommunicationServices/*` (the control-plane permission ACS keys Entra data-plane access off of).

```bash
az role assignment create \
  --assignee-object-id 9fd47efb-7962-492b-ac44-e5ccd0268ebb \
  --assignee-principal-type ServicePrincipal \
  --role "Communication and Email Service Owner" \
  --scope "/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/rg-spaarke-dev/providers/Microsoft.Communication/CommunicationServices/spaarke-acs-dev"
```
- Use `--assignee-object-id` (the principalId) + `--assignee-principal-type ServicePrincipal` to avoid Graph lookup flakiness on the MI.
- Allow a few minutes for RBAC propagation; then retry a send. Restart of `spaarke-bff-dev` is optional (tokens are acquired per-call) but harmless.
- Verify: `az role assignment list --assignee 9fd47efb-7962-492b-ac44-e5ccd0268ebb --all -o table` should now list the ACS scope.

## Not a code bug — no changes made
- ACS wiring is correct per ADR-028/ADR-045/ADR-046; there is exactly ONE send path (ADR-045) — no second path introduced or found.
- No secrets or connection strings printed/committed. No bypass, no fabricated ACS credentials.

## Minor observation (NOT changed — flag only)
In `MessagingChannelSender.SendAsync`, only `TransmitAsync` is wrapped in the `RequestFailedException → SdapProblemException` mapper. The identity/token calls (`EnsureIdentityAsync`, `MintChatTokenAsync`) and `CreateThreadAsync` are outside that try/catch, so an ACS 401 from those surfaces as a raw/`Unexpected error` rather than a mapped `CHANNEL_SEND_FAILED` problem. This is a diagnostics-quality nit only; it does not cause the 401 and fixing it would not restore send. Deferred to avoid masking the real (env) failure. Consider a follow-up to map ACS `RequestFailedException` across the whole send for cleaner error surfacing.
