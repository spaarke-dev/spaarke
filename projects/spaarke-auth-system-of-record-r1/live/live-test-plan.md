# Spaarke auth system of record — live verification test plan

> Written 2026-10-09. Code root `C:/code_files/spaarke-wt-spaarke-auth-system-of-record-r1` = master @ `8a9ecaac1`. Paths below are relative to that root; `BFF/` = `src/server/api/Sprk.Bff.Api/`, `LIB/` = `src/client/shared/Spaarke.Auth/src/`, `L2/` = `src/server/services/Sprk.Provisioning.ControlPlane.Core/`.
>
> **Inputs.** `working/x03-matrix-and-adr.md` §3 (the 75-row consolidated live-verification list), the §7 "Needs live verification" tables of `working/a01`–`a09`, `working/x01-contradictions.md` (its "truth" column overrides the area files), and the two live read-outs `working/x04-live-entra-readout.md` (Entra / App Service / Key Vault, captured 2026-10-09T15:26Z) and `working/x05-live-dataverse-readout.md` (dev Dataverse env vars + application users). `working/x02-docs-reconciliation.md` is a document-verdict table; it adds no live item of its own. Nothing in this plan was run; the read-outs are the only live evidence it uses.
>
> **No secret values appear in this file.** x04 §2 already redacts the one value its script printed in clear (`Compose__Webhook__ClientState`); this plan refers to that setting by name only.

## Run log

| Date | Batch | Tests | Outcome | Evidence |
|---|---|---|---|---|
| 2026-10-09 | A (read-only, owner-approved) | T-P1-10 | **FAIL** — Model 1 test guest `bc596ecd…` has `sprk_isexternal = No` | x07 |
| 2026-10-09 | A | T-P1-12 steps 1–3 | **PASS** — active BFF app user holds `prvActOnBehalfOfAnotherUser` (via System Administrator); writer FLS profiles = exactly the two BFF app users. Findings: all BFF app users are System Administrator; Standing Grant Administrators has no members | x07 |
| 2026-10-09 | A | T-P1-12 step 4 | **Inconclusive** — wrong App Insights component matched; re-run against the BFF's own component | x06 |
| 2026-10-09 | A | T-P2-09 steps 1–2 | Answered — control-plane API app `70ba7b19…`, one role holder | x06 |
| 2026-10-09 | A | T-P2-09 steps 3–4 | **Not run** — hosts not in the current subscription; stamp query error | x06 |
| 2026-10-09 | A | T-P2-10 (Spaarke tenant parts, prod SWA) | Answered — grants recorded; prod SPA site exists at `external.spaarke.com` | x06 |
| 2026-10-09 | A | T-P3-01, T-P3-18 | Answered — four secrets stored as plain app settings; **no deployment slots** on `spaarke-bff-dev` | x06 |
| 2026-10-09 | A | T-P3-04 steps 1–3 | Answered — registration ribbon runs the old packaged copy against a retired host; Archive Email and matter insight call unmapped routes; KPI scripts send no token; `sprk_DocumentDelete.js` unwired | x07 |
| 2026-10-09 | A | T-P3-05 (FLS part) | Answered — only System Administrator and an empty Standing Grant Administrators profile can read `sprk_standinggrant` | x07 |
| 2026-10-09 | A | T-P3-07 steps 1, 3, 5, 6 | Answered — broad requested permissions; ACS Owner role on the BFF identity; FIC audiences correct | x06 |
| 2026-10-09 | A | T-P3-09 | Answered — unconsumed live secret on the dev BFF app; expired secrets on two apps; CIAM cert exportable | x06 |
| 2026-10-09 | A | T-P3-12, T-P3-16 | Answered | x06 |

Still to run: every guest-account test (T-P1-01…-05, -07, -08), every test flagged LIVE CHANGE (owner approval each), T-P2-09 steps 3–4 (name the control-plane subscription), T-P1-12 step 4 (correct App Insights component), and items needing Exchange PowerShell, the CIAM tenant sign-in, or the Teams admin center.

---

## 0. How to read this plan

### 0.1 Provenance labels

Every fact carries one of four labels. Where a fact is load-bearing for a test's pass/fail rule, the code line was re-opened for this file and is marked *(checked)*; other code citations are inherited from the evidence file named.

| Label | Meaning |
|---|---|
| **VERIFIED IN CODE** `[CODE path:line]` | Read in source at `8a9ecaac1`. |
| **VERIFIED LIVE** `[LIVE x04 §n]` / `[LIVE x05 §n]` | Read from Entra, the dev App Service, Key Vault or dev Dataverse by the 2026-10-09 read-only scripts. True for the dev environment at capture time only. |
| **NEEDS LIVE TEST** `[TEST T-…]` | The repo cannot show it and the read-outs did not cover it; a test below is scheduled. |
| **DOC CLAIM ONLY** `[DOC path:line]` | Stated in a document or a comment; not verified anywhere. |

### 0.2 Terms

- **User types** (x03 §1): **U1** Spaarke staff (member of tenant `a221a95e…`); **U2** Model 1 customer staff — a B2B guest in Spaarke's tenant whose home account lives in another tenant; **U3** licence-free workforce user — a workforce account with no Dataverse `systemuser`; **U4** CIAM contact — an external user in the Entra External ID tenant `7052feba…`; **U5** Model 2 customer staff (dedicated stamp in the customer's tenant; not provisionable today, x03 M-9); **U6** service identities (managed identities, app registrations, API keys).
- **Surfaces** (x03 §1): **S1** Dataverse-hosted clients (PCF, code pages, classic JS web resources); **S2** Office add-ins; **S3** Teams tab (external SPA inside Teams); **S4** external SPA in a browser, workforce plane; **S5** external SPA, CIAM plane; **S6** M365 Copilot agent; **S7** everything else (L2 control plane, keyless proof, webhooks, background jobs).
- **Member test**: `WorkforceMembershipTest.Evaluate` — the BFF's decision, for a workforce token with no matching `systemuser`, whether the caller may be bound to a contact. Order is fixed: caller kind → tenant list empty → `tid` ∈ `WorkforceIdentity:CustomerTenantIds` → `acct` (`0` member, `1` guest) `[CODE BFF/Infrastructure/ExternalAccess/WorkforceIdentityOptions.cs:199-230]` *(checked)*. Deny codes `[CODE …/WorkforceIdentityOptions.cs:176-191]` *(checked)*: `…workforce_tenant_list_empty`, `…workforce_tenant_not_customer`, `…workforce_acct_claim_missing`, `…workforce_guest`.
- **OBO**: on-behalf-of — the BFF exchanges the caller's token for a Graph or Dataverse token in the caller's name. **App-only**: the BFF acts as its own identity (the UAMI `mi-bff-api-dev`).
- **NAA**: nested app authentication — MSAL brokered through the host (Office or Teams) rather than a popup.
- **Plane**: the BFF's choice between the workforce scheme and the CIAM scheme, made only from the validated token's `iss` / `tid` `[CODE BFF/Infrastructure/ExternalAccess/CallerPrincipalResolver.cs:386-406]` *(checked)*.

### 0.3 Safety classes

| Class | Meaning | Approval |
|---|---|---|
| **READ-ONLY** | Observes: decodes a token the client already holds, reads App Insights, runs `GET`s, runs `az … show/list`, Dataverse `SELECT`. May produce log lines and 4xx responses; creates no rows, files, consents or settings. | None beyond the test account itself. |
| **LIVE CHANGE (benign)** | Creates test data that is easy to delete (a chat session, a to-do, a notification ping) or sends one e-mail to a test mailbox. | **Owner approval before running.** |
| **LIVE CHANGE** | Alters tenant, app-registration, App Service or Dataverse configuration, sideloads a package, uses a stored credential, or runs a provisioning handler. | **Owner approval before running; record the rollback.** |

Observation-only methods are preferred throughout: the add-in Diagnostics view (`src/client/office-addins/shared/taskpane/services/tokenDiagnostics.ts:15-23,62-70` *(checked)*, deployed to the dev site by `ADDIN_DIAGNOSTICS_ENABLED: "true"` `[CODE .github/workflows/deploy-office-addins.yml:83-87]` *(checked)*), browser devtools (MSAL console lines, the network tab), and the BFF's existing per-request claim logger (logger category `CopilotAuth`), which already writes every `/api*` request's `aud` / `iss` / `appid` / `scp` to App Insights at Warning level `[CODE BFF/Infrastructure/DI/MiddlewarePipelineExtensions.cs:121-158; the log call is at :140-146]` *(checked)* [CORRECTED by check: was `:88-94`, which is the exception handler's CORS re-apply (a05 §8 item 2), not the logger] (x03 M-5). Decode tokens in the browser console or with a local `jwt` decoder; never paste a token into a file, ticket or chat.

---

## 1. Settled by live reads

The 2026-10-09 read-outs answer the items below. No test is scheduled for a row marked **settled**; a row marked **partly** names the residual test.

### 1.1 Settled items

| x03 §3 ID | Item | Answer | Read-out | What it changes in the record |
|---|---|---|---|---|
| B1 | `signInAudience` of `1e40baad…` (dev BFF) | **AzureADMultipleOrgs** | `[LIVE x04 §1 A1]` | x01 #8.1 → VERIFIED LIVE. ADR-028 A2 R24 "multitenant registration" → HOLDS (live). The `/organizations` authority of S3/S4 is valid against this app. |
| B1 | Exposed scopes on `1e40baad…` | `access_as_user`, `access_as_external_user`, `SDAP.Access`, `user_impersonation` — all four, enabled | `[LIVE x04 §1 A1]` | x01 #8.9 → VERIFIED LIVE: Entra will mint tokens for every scope name a client requests today, including the packaged ribbons' `SDAP.Access` (a02 D-7). The BFF checks no `scp` value on the default scheme — no `RequiredScope` / `RequireScope` call exists in `Sprk.Bff.Api` *(re-checked by grep)*; the only readers of `scp` are the per-request `CopilotAuth` logger `[CODE BFF/Infrastructure/DI/MiddlewarePipelineExtensions.cs:146]` [CORRECTED by check: was "the auth-failure logger `:148`"] and the keyless-proof filter, which requires its absence `[CODE BFF/Api/Filters/KeylessProofAuthorizationFilter.cs:49]` *(checked)* — so the scope name does not decide acceptance. [ADDED by check] a05 §7 r10 expected `signInAudience` to "match the script (`AzureADMyOrg`)": refuted for dev (`AzureADMultipleOrgs`); it holds for the prod app `spaarke-bff-api-prod` `92ecc702…` = `AzureADMyOrg` `[LIVE x04 §1, "Per-customer BFF apps and prod BFF app" table]` (see §1.3 F-8). `requiredResourceAccess` was printed as counts only (Graph 25, Exchange 1, SharePoint 2) — the ids (a09 §7 r6) → T-P3-07 step 3. |
| B1 | `identifierUris` on `1e40baad…` | `api://green-dune-…/1e40baad…`, `api://auth-3e04ab58-…/1e40baad…`, `api://1e40baad…` | `[LIVE x04 §1 A1]` | Teams-SSO and Copilot audience URIs both exist. Matches `AgentToken__CopilotAudience` (C1). |
| B1 | SPA redirect URIs on `1e40baad…` | `brk-multihub://green-dune…`, `brk-1fec8e78…://green-dune…` (Teams desktop), `brk-5e3ce6c0…://green-dune…` (Teams web), `https://green-dune…`, `https://spaarkedev1.crm.dynamics.com`, `https://spaarkedev1.crm.dynamics.com/webresources/sprk_spaarkeai`. **No `http://localhost:3000`.** | `[LIVE x04 §1 A1]` | a04 §7 r4 answered. Teams NAA broker redirects are registered (input to T-P1-06). |
| B1 | Pre-authorized clients and `knownClientApplications` | 9 pre-authorized: `170c98e1` (SDAP-PCF-CLIENT), `c1258e2d` (dev add-in), `f257a0a9` (Copilot bot), `f306885a` (spaarke-external-access-SPA), `ab3be6b7` (Teams Graph Service), `1fec8e78` + `5e3ce6c0` (Teams desktop/web, no SP in tenant), `04b07795` (Azure CLI), `29d9ed98` (unidentified, no SP). `knownClientApplications` = `[170c98e1]`. **`1958aec2` (production add-in) is NOT pre-authorized.** | `[LIVE x04 §1 A1, §6]` | a09 §7 r5 answered. New fact: the production add-in cannot obtain a `1e40baad` token without separate consent (see §1.3 F-4). |
| B1 | App roles on `1e40baad…` | **`Admin` only.** No `SystemAdmin`, no `sprk_ReportingAccess/Author/Admin`, no `Provisioning.KeylessProof`. | `[LIVE x04 §1 A1]` | x01 #8.10 → VERIFIED LIVE: x03 H-8 is live on dev (every Reporting caller gets 403). The `SystemAdmin` policy `[CODE BFF/Infrastructure/DI/AuthorizationModule.cs:375-383]` can be satisfied on dev only through `Admin`. `/api/platform/keyless-proof` cannot be satisfied by any token minted for the dev app (role undefined) — consistent with "first live run pending" (x03 R54). |
| B1 / I6 / C8 | `acct` optional claim on `1e40baad…` | Present in `optionalClaims.accessToken` (`email`, `preferred_username`, `upn`, `acct`) | `[LIVE x04 §1 A1]` | x01 #8.4 → VERIFIED LIVE for the dev app. The residual question is what Entra emits for a guest (T-P1-01, T-P1-03, T-P1-04). |
| B1 | `groupMembershipClaims` | None | `[LIVE x04 §1 A1]` | a08 §7 r7 answered: no `groups`/`wids` inflation. |
| B1 / G5 | Federated credentials on `1e40baad…` | `mi-bff-api-dev-assertion` (issuer `…/a221a95e…/v2.0`, subject `9fd47efb…`) and `github-actions-deploy-staging` (subject `repo:spaarke-dev/spaarke:environment:staging`). Count 2. | `[LIVE x04 §1 A1, §3]` | G5 settled for dev: subject = `mi-bff-api-dev` **principalId** `9fd47efb…` `[LIVE x04 §3]`, as the MI-FIC design requires (x03 R40). Audience not printed by the script (expected `api://AzureADTokenExchange`; DOC CLAIM ONLY). |
| B2 (part) | Dev add-in `c1258e2d…` | `AzureADMyOrg`; SPA redirects include `brk-multihub://icy-desert-…` and `https://icy-desert-…/auth-callback.html` (plus localhost and the BFF `/office/auth-callback`); public-client `…/common/oauth2/nativeclient`; pre-authorized on `1e40baad`; requires 2 permissions on `1e40baad` + 3 on Graph | `[LIVE x04 §1 A6, A1]` | a03 §7 r1, a01 §7 r7 answered. Single-tenant client: a guest signs in to it only as the Spaarke-tenant guest identity (input to T-P1-03). [CORRECTED by check: B2 also asks "pre-authorized **for `user_impersonation`**" — x04 prints the pre-authorized client ids, not the scope ids (`delegatedPermissionIds`), and the "2 permissions on `1e40baad`" are a count. Which scopes → T-P2-10 step 1.] |
| B3 (part) | Production add-in `1958aec2…` | Exists (`Spaarke Office Add-in (Production)`), `AzureADMyOrg`, redirects `https://addins.spaarke.com/auth-callback.html` + `brk-multihub://addins.spaarke.com`; requires Graph (3) only — **no permission on `1e40baad`**; not pre-authorized on `1e40baad` | `[LIVE x04 §1 A7, A1]` | x01 #8.11 → VERIFIED LIVE (exists). See §1.3 F-4. [CORRECTED by check: B3's "Graph `email profile User.Read` **consent**" is not settled — x04 shows a count of 3 required Graph permissions, not their names or an `oauth2PermissionGrants` row. → T-P2-10 step 1.] |
| B5 | `170c98e1…` (SDAP-PCF-CLIENT), `5175798e…`, `b36e9b91…` | `170c98e1`: `AzureADMultipleOrgs`, SPA redirects `spaarkedev1` + `spaarke-demo` (+ `.api.` variants, localhost), **2 secrets + 1 certificate**, no exposed scopes; `5175798e`: **not found** (deleted); `b36e9b91` (SPE File Viewer PCF): exists, `AzureADMyOrg`, SPA redirects `spaarkedev1`/`spaarke-demo`, requires `1e40baad` (1) + Graph (1) | `[LIVE x04 §1 A2, A3, A4]` | a09 D-2/D-7 answered. The packaged ribbons' hard-coded client `b36e9b91` still resolves on dev (x03 H-5). `170c98e1` is both a public client and the SPE owning app with live secrets (x03 M-8) — confirmed. [ADDED by check] a01 §7 r8 ("every Dataverse org origin is a registered SPA redirect on `sprk_MsalClientId`") is settled for the dev org (`spaarkedev1` and `spaarke-demo` are on `170c98e1`); other orgs → T-P3-03. |
| B6 | Orphan ids in `sprk_DocumentDelete.js` | Tenant `e2e89f3a…`: **AADSTS90002 tenant not found**; apps `ed44fb14…` and `8c60b44e…`: not found in Spaarke's tenant, no service principal | `[LIVE x04 §1 A21, A22, §5]` | a09 D-18 answered: the script cannot sign in anywhere reachable; it is dead code, not a foreign dependency. Residual (P3, low value): the CIAM tenant lookup, T-P3-17. |
| B8 | Exchange Admin `46670ee2…`, SPE Model 1 Owner `bfac7f6e…`, demo BFF `da03fe1a…` | Both FICs `sprk-controlplane-dev-uami-assertion` with subject `38f7693f…` = L2 UAMI principalId; `da03fe1a`: `AzureADMyOrg`, scopes `SDAP.Access` + `user_impersonation`, app role `Admin`, pre-authorizes Azure CLI, 2 secrets | `[LIVE x04 §1 A16, A17, A14, §3]` | a09 §7 r6, a08 §7 r14 answered. |
| C1 | Dev BFF `AzureAd__*`, `ValidAudiences`, `AgentToken__*` | `Instance=https://login.microsoftonline.com/`, `TenantId=a221a95e…`, `ClientId=1e40baad…`, `Audience=api://1e40baad…`; `ValidAudiences__0=api://1e40baad…`, `__1=1e40baad…` (bare GUID), `__2=api://green-dune-…/1e40baad…`; `AgentToken__CopilotAudience=api://auth-3e04ab58-…/1e40baad…`; `AgentToken__ClientId=1e40baad…`; `AgentAppId` masked | `[LIVE x04 §2]` | a04 D-8 answered: the Teams-SSO fallback audience **is** configured. a05 §7 r2 (bare-GUID v2 audience) is configured, so acceptance now depends only on Microsoft.Identity.Web honouring `ValidAudiences` (T-P1-07 observes it). Staging slot unread (T-P3-18). [CORRECTED by check: row is **C1 (part)** — `AgentToken__AgentAppId` is masked, so its value (a08 §7 r3: is it `f257a0a9…`?) is not settled → T-P3-01 records its shape; and `api.requestedAccessTokenVersion` on `1e40baad…` was not printed, so whether a v2 (bare-GUID) `aud` is ever minted is T-P1-07 step 0.] |
| C2 | Dev BFF `Ciam__*` | `Instance=https://spaarkeextid.ciamlogin.com`, `TenantId=7052feba…`, `ClientId=Audience=4a4d5126…` (GUID), `Domain=spaarkeextid.onmicrosoft.com`, `GraphProvisioner__ClientId=e63e6eb1…`, `CertificateName=ciam-graph-provisioner-cert` | `[LIVE x04 §2]` | x01 #8.2: the BFF side is settled — it expects a v2 (GUID) `aud`. Whether the CIAM app issues v2 tokens is T-P2-01. |
| C3 (part) | Dev BFF `WorkforceIdentity__CustomerTenantIds__N` | **`__0 = a221a95e…` — Spaarke's own tenant.** No `TenantRouting__*` key in the read-out. [CORRECTED by check: C3 also lists `IdentityLink__Reconciliation__WritesEnabled` and `ExternalAccess__*WritesEnabled`; neither is in the read-out and the read-out's key filter is undocumented, so they stay open → T-P3-01 (already listed there).] | `[LIVE x04 §2]` | a08 D-18 answered: the dev stamp is hand-configured in exactly the way `CustomerWorkforceTenantsRule` forbids for L2 stamps (x01 #7). Consequence on dev (deterministic from `[CODE …/WorkforceIdentityOptions.cs:199-230]`): a token with `tid=a221a95e` and no `systemuser` match is **`Member`** when `acct=0` (→ contact bind) and **`Guest`** → `…workforce_guest` when `acct=1`; a token with a home-tenant `tid` is **`ForeignTenant`** → `…workforce_tenant_not_customer`. This is the branch table T-P1-04 / T-P1-05 / T-P2-05 observe. |
| C4 | Dev BFF outbound identity settings | `Graph__ManagedIdentity__Enabled=true`; `Graph__ManagedIdentity__ClientId` = `ManagedIdentity__ClientId` = `5967251e…` (`mi-bff-api-dev`); `Graph__Credentials__Order__0=ManagedIdentityFederated` (sole entry); `Graph__Scopes__0=https://graph.microsoft.com/.default`; `Dataverse__ClientId=Graph__ClientId=1e40baad…`; no `*ClientSecret*` / `API_CLIENT_SECRET` key in the read-out; `RequireSecretFreeIdentity` present (value masked) | `[LIVE x04 §2, §3]` | a06 §7 r1, a08 §7 r8 answered for dev: app-only Dataverse/Graph runs as the UAMI; the active Dataverse application user is therefore `mi-bff-api-dev` (`[LIVE x05 §B]`, enabled). x03 R7/R41 HOLD live (no secret in the order). `AZURE_CLIENT_ID` not in the read-out (T-P3-01). |
| C5 (part) | `Cors__AllowedOrigins__*`, `PublicConfig__*`, `SpeAdmin__PlatformOperatorEnvironment`, webhook keys | CORS: `spaarkedev1.crm`, `spaarkedev1.api.crm`, `spaarke.powerappsportals.com`, `icy-desert…`, `green-dune…`; `PublicConfig__BffUrl/MsalClientId/TenantId` = dev BFF / `1e40baad` / `a221a95e`; `SpeAdmin__PlatformOperatorEnvironment=true`; `Communication__WebhookSigningKey` + `WebhookClientState` = Key Vault references (names not printed); `Compose__Webhook__ClientState` = **plain app setting** (redacted); `Customer__Id=spaarke` | `[LIVE x04 §2]` | a02 §7 r6 (CORS for the Dataverse origin) answered YES. `PublicConfig__MsalClientId=1e40baad` while Dataverse's `sprk_MsalClientId=170c98e1` (`[LIVE x05 §A]`) — the x03 M-8 contradiction is live. See §1.3 F-2. Remaining C5 keys → T-P3-01. |
| D1 | Dev `sprk_*` environment-variable values | `sprk_TenantId=a221a95e…`; `sprk_CustomerTenantId=a221a95e…`; `sprk_BffApiAppId=1e40baad…`; **`sprk_MsalClientId=170c98e1…`** (overrides the solution default `1e40baad…`); **`sprk_BffApiBaseUrl=https://spaarke-bff-dev.azurewebsites.net/api`** (trailing `/api`); `sprk_ReportingModuleEnabled=yes`; `sprk_SharePointEmbeddedContainerId` set (differs from default); secret-typed variables have no value rows | `[LIVE x05 §A]` | a02 §7 r3 answered for dev. a02 D-29 is **live**: the three KPI scripts that concatenate `/api/matters` onto this value call `/api/api/…` → 401 (x03 §1.7). The Dataverse-hosted MSAL client is SDAP-PCF-CLIENT (`AzureADMultipleOrgs`, pre-authorized + known client on `1e40baad`), which is the client every S1 guest test below signs into. Other environments → T-P3-03. |
| D2 (part) / x01 #8.6 | Dataverse application users (dev) | Present and enabled: `SDAP-BFF-SPE-API` (`1e40baad`, oid `d93c832e…`), `mi-bff-api-dev` (`5967251e`, oid `9fd47efb…`), `sprk-controlplane-dev-uami`, `github-actions-spe-infrastructure` (`8c85a481`), `spe-api-dev-67e2xz`, `mi-ontology-writer-dev`, **`spaarke-bff-api-prod` (`92ecc702`)**; `Spaarke DMS-SPE Dev 1` (`fd1325aa`) **disabled** (its app object no longer exists, `[LIVE x04 §1 A5]`) | `[LIVE x05 §B]` | x01 #8.6 → VERIFIED LIVE for dev (the UAMI is an application user). a06 Flow E item 6 settled. Roles, `prvActOnBehalfOfAnotherUser` and FLS membership were not read → T-P1-12. See §1.3 F-3. |
| G1 (part) | Graph app roles held by the UAMI service principals | `mi-bff-api-dev` (`9fd47efb…`): `User.ReadWrite.All`, `SecurityEvents.Read.All`, `Group.ReadWrite.All`, `FileStorageContainerTypeReg.Selected`, `Mail.Read`, `FileStorageContainer.Selected`, `Mail.Send` (7). L2 UAMI (`38f7693f…`): 15 roles incl. `Directory.ReadWrite.All`, `User.Invite.All`, `Sites.ReadWrite.All`, `Files.ReadWrite.All`, `Mail.ReadWrite`, `GroupMember.ReadWrite.All` | `[LIVE x04 §3, §6]` | The grant SET is settled; `FileStorageContainer.Selected` (x03 R20 "NOT CHECKABLE") is held. Comparison against `GraphAppRoles.All` (15) and the Exchange scoping of `Mail.*` → T-P3-07. |
| G7 (part) | Key Vault `spaarke-spekvcert` | Active: `spe-owning-app-secret`, `MANAGED-IDENTITY-CLIENT-ID`, `UAMI-ClientId`, `office-addin-client-id`, `Redis-ConnectionString`, AI/OpenAI keys, webhook keys, …; **deleted**: `BFF-API-ClientSecret`, `bff-api-client-secret`, `Graph-API-ClientSecret`, `ServiceBus-ConnectionString`, `AiSearch--AdminKey`, `ai-search-key`, `spe-app-cert`, `spe-app-cert-pass`; **`Dataverse-ClientSecret` in neither list**; certificate `ciam-graph-provisioner-cert` present | `[LIVE x04 §4]` | a09 D-3 confirmed: `ServiceBus-ConnectionString` and `AiSearch--AdminKey` are soft-deleted — any template reference to them resolves to a literal string (x03 M-10). x03 R59 (E-3 closed) consistent. Exportability of the CIAM cert, Power BI SP secret, stamp vaults → T-P3-09. |
| G10 (part) | Insights UAMIs | `insights-spaarkedev-uami` (`583ca2d8…`) and `insights-search-deploy-uami` (`4ceca18e…`) exist in `spe-infrastructure-westus2` | `[LIVE x04 §3]` | a09 D-17 partly confirmed (the identities are real). Function App, storage, RBAC → T-P3-12. |

### 1.2 Partly settled items (residual scheduled)

| x03 §3 ID | Settled part | Residual → test |
|---|---|---|
| B7 | `965a4a01…` is the L2 UAMI's clientId (service principal of type `ManagedIdentity`, no app object) `[LIVE x04 §1 A15, §3]` — confirms a09 D-14 ("placeholder"). | Which app registration exposes `api://spaarke.com/provisioning-controlplane-dev` and holds `Operator`/`Reader` → T-P2-09. |
| B9 | `8c85a481…` holds four GitHub FICs (`ref:refs/heads/master`, `environment:dev/staging/production`); **no `pull_request` credential remains**; `1e40baad…` ALSO holds `github-actions-deploy-staging` `[LIVE x04 §1 A20, A1]`. The yaml-vs-guide disagreement (x01 #8.8) resolves to "both apps hold GitHub FICs". | Which app `secrets.AZURE_CLIENT_ID` names; RBAC on each; `NIGHTLY_*` secrets → T-P3-16. |
| B10 | `appRoleAssignmentRequired=False` on the service principals of `1e40baad`, `170c98e1`, `b36e9b91`, `c1258e2d`, `1958aec2`, `da03fe1a` `[LIVE x04 §1]`. | `oauth2PermissionGrants` (delegated consent) and the CIAM SP → T-P2-10. |
| D2 | Application users exist (above). | Security roles, `prvActOnBehalfOfAnotherUser`, FLS profile membership → T-P1-12. |
| C5 / C7 / C8 | CORS, PublicConfig, SpeAdmin, webhook-key shapes (above). `Onboarding__Enabled` and `ReservedTenants__*` are **absent from the read-out**; the read-out's key filter is not documented, so absence is not proof. | → T-P3-01 (dev), T-P2-09 (L2), T-P3-18 (staging slot: "could not read settings"). |
| G1 | Grant sets (above). | Exchange `ApplicationAccessPolicy` / RBAC for Applications scoping of `Mail.*`; least-privilege use (x04 §6 open checks) → T-P3-07. |
| B2 [ADDED by check] | Pre-authorized client id present (above). | Which scope ids `c1258e2d…` is pre-authorized for / which 2 permissions it requires on `1e40baad` → T-P2-10 step 1. |
| B3 [ADDED by check] | App exists, redirects, no `1e40baad` dependency (above). | Which 3 Graph permissions and whether they are consented (`oauth2PermissionGrants` for its SP) → T-P2-10 step 1. |
| C1 [ADDED by check] | Audiences and Copilot audience (above). | `AgentToken__AgentAppId` value shape (masked in x04) → T-P3-01; `api.requestedAccessTokenVersion` on `1e40baad…` → T-P1-07 step 0. |
| C3 [ADDED by check] | Tenant list value, no `TenantRouting__*` (above). | `IdentityLink__Reconciliation__WritesEnabled`, `ExternalAccess__*WritesEnabled` presence → T-P3-01. |
| B1 [ADDED by check] | Everything printed (above). | `requiredResourceAccess` ids (x04 prints counts) → T-P3-07 step 3. |

### 1.3 New facts surfaced by the live reads (not tests — record items)

| # | Fact | Evidence | Record impact |
|---|---|---|---|
| F-1 | The dev BFF app registration `1e40baad…` holds **one password credential** (a client secret). The BFF's live credential order contains no `ClientSecret` kind and every BFF-identity consumer was removed (x03 R59). The secret's consumer, if any, is unknown. | `[LIVE x04 §1 A1]` (`passwordCredentials: 1`), `[LIVE x04 §2]` (`Graph__Credentials__Order__0` sole) | Add to x03 M-10 (credential hygiene). T-P3-09 enumerates it (name + expiry only) and looks for a consumer. |
| F-2 | `Compose__Webhook__ClientState` is a **plain App Service setting**, not a Key Vault reference (the sibling `Communication__WebhookClientState` is a KV reference). | `[LIVE x04 §2]` (value redacted) | ADR-028 R17 (no plaintext secrets in `appsettings*`) is about the repo files and still HOLDS; this is a live-config hygiene gap for x03 M-10. The value must be rotated if it was ever displayed. |
| F-3 | The **production** BFF app registration `spaarke-bff-api-prod` (`92ecc702…`) is an enabled application user in the **dev** Dataverse environment. | `[LIVE x05 §B]` | Tenant/environment hygiene; add to x03 §4 (L tier) pending an owner reason. |
| F-4 | The production add-in `1958aec2…` has no permission on, and is not pre-authorized by, `1e40baad…`; it requires Graph only. | `[LIVE x04 §1 A7, A1]` | x03 H-6 sharpens: a production add-in build would need the per-customer BFF app (x03 R45) to pre-authorize it — which H3 does by default `[CONFIG infrastructure/bicep/platform-controlplane.bicep:187-189]` (x01 #8.11) — and cannot reach the dev BFF at all. |
| F-5 | The dev BFF lists Spaarke's own tenant as the customer workforce tenant (`WorkforceIdentity__CustomerTenantIds__0=a221a95e…`). | `[LIVE x04 §2]` | On dev the `Guest` and `Member` branches of the member test are reachable for Spaarke-tenant tokens; on any L2-provisioned stamp they are not (x01 #7). Every U2/U3 result from dev must be re-read against a stamp before it is generalised (x03 M-2). |
| F-6 | `sprk_MsalClientId` (dev) is `170c98e1…`, while both anonymous config endpoints and the solution default say `1e40baad…`. | `[LIVE x05 §A]`, `[LIVE x04 §2]` `PublicConfig__MsalClientId` | x03 M-8 is live: a Dataverse-hosted client that reads the env var signs into SDAP-PCF-CLIENT; one that falls back to `/api/config/client` signs into the BFF app itself. T-P1-01 records `appid` to show which happened. |
| F-7 | `mi-bff-api-dev` holds 7 Graph application roles, not the 15 in `GraphAppRoles.All`; the L2 UAMI holds 15. | `[LIVE x04 §6]` | Input to T-P3-07; no `Mail.ReadWrite`, `Sites.*`, `Files.*`, `User.Invite.All` on the BFF identity. |
| F-8 [ADDED by check] | The production BFF app registration `spaarke-bff-api-prod` (`92ecc702…`) **exists** and is `AzureADMyOrg`. The same display-name query returned **no `spaarke-bff-api-<customerId>` registration** (a09 A18 per-customer apps) in Spaarke's tenant at capture time. | `[LIVE x04 §1, "Per-customer BFF apps and prod BFF app (by display name)"]` | a09 A19 "whether it exists is [LIVE?]" → VERIFIED LIVE (exists; single-tenant, as `Register-EntraAppRegistrations.ps1:1611,1645` writes, x01 #8.1). The empty per-customer result means every "existing stamp" item (C8 stamp half, I8, G5 `AADSTS70021`, D8 H7b outcomes) may have no live subject yet; T-P2-09 step 4 re-runs the query by prefix before assuming so. Its `prvActOnBehalfOfAnotherUser` / roles on dev (F-3) → T-P1-12 step 1 includes it. |

---

## 2. Tests

Each test: **ID · priority · user type · surface · safety**, then preconditions, steps, what to record, pass/fail, and the record section it confirms or changes. Tests are grouped by flow inside each priority. "Observe" means read; nothing in a READ-ONLY test writes.

Shared preconditions for every P1 test: the dev environment as read on 2026-10-09 — `spaarkedev1` Dataverse, `spaarke-bff-dev` production slot with the settings in `[LIVE x04 §2]`, the dev add-in site `icy-desert-…` built by `deploy-office-addins.yml` (Diagnostics ON), the external SPA / Teams tab at `green-dune-…`. If any of these has been changed since, re-run the x04/x05 read-outs first.

### 2.1 P1 — Model 1 guest (U2) and the Model 1 client sign-in design

#### T-P1-11 — U1 baseline token on S1 (control for every U2 comparison)

- **Priority** P1 · **User** U1 · **Surface** S1 · **Safety** READ-ONLY
- **Preconditions**: a Spaarke staff member with a licensed `systemuser` in `spaarkedev1`; Edge with devtools; `sprk_MsalClientId=170c98e1` and `sprk_TenantId=a221a95e` as read `[LIVE x05 §A]`.
- **Steps**:
  1. Open a Matter in the Spaarke MDA; open a form with a `@spaarke/auth` PCF (e.g. `DocumentRelationshipViewer`, which spreads `tenantId` `[CODE src/client/pcf/DocumentRelationshipViewer/DocumentRelationshipViewer/authInit.ts:57]`).
  2. In the console, note every `[BrowserMsalStrategy]` and `[SpaarkeAuth]` line (silent / `ssoSilent` / popup; the `hint=` value).
  3. In the network tab, take the `Authorization` header of one BFF call and decode the payload in the console (`JSON.parse(atob(token.split('.')[1]…))`).
  4. In App Insights for `spaarke-bff-dev`, find the same request's Warning line from the request logger (`aud`, `iss`, `appid`, `scp`) `[CODE BFF/Infrastructure/DI/MiddlewarePipelineExtensions.cs:140-146]` *(checked)* [CORRECTED by check: was `:88-94`].
- **Record**: `tid`, `oid`, `aud` (`api://1e40baad…` vs bare GUID), `scp`, `roles`, `acct`, `idtyp`, `appid`/`azp`, `iss`, `upn`/`preferred_username`; `wids` present Y/N [ADDED by check: a08 §7 r7 asks for its absence — `groupMembershipClaims=None` is settled `[LIVE x04 §1 A1]`, the emitted token is not]; the MSAL cache location in use (`localStorage` keys `msal.*`); whether a popup appeared.
- **Pass**: `tid=a221a95e…`, `acct=0`, no `idp`, `appid=170c98e1…` (env var honoured, F-6) or `1e40baad…` (fallback path), `scp=user_impersonation`, BFF call 200.
- **Confirms / changes**: x03 §3 A1 (closes it); x03 §1.1 S1/U1 Sign in stays WORKS with live claim shapes attached; x03 §1 global dependency G1 (real token claims) for U1 on S1 [CORRECTED by check: "G1" here is the §1 global-dependency label, not §3 row G1 (Graph app roles)]; a08 §7 r7 (`wids`).

#### T-P1-01 — U2 guest sign-in on S1 (Dataverse-hosted, tenant-pinned authority): which tenant mints the token

- **Priority** P1 · **User** U2 · **Surface** S1 · **Safety** READ-ONLY
- **Preconditions**: a B2B guest in `a221a95e…` whose home account is in a different, real tenant; a licensed, enabled `systemuser` in `spaarkedev1` for that guest; a **fresh Edge profile** signed in only to the home tenant (no Spaarke-tenant session); a second profile with both sessions. The guest's `systemuser.azureactivedirectoryobjectid` value, read beforehand by a read-only query (Dataverse MCP `read_query` or `GET …/systemusers?$select=azureactivedirectoryobjectid&$filter=domainname eq '…'`).
- **Steps** (repeat for both profiles):
  1. Sign the guest into `spaarkedev1.crm.dynamics.com` (the Dataverse session is the precondition, not the test).
  2. Open the same PCF form as T-P1-11. Capture every `[BrowserMsalStrategy]` line: which account `_pickAccount` chose (`AccountInfo.tenantId` is what it filters on `[CODE LIB/strategies/BrowserMsalStrategy.ts:285-289]`), the `ssoSilent` `loginHint`, whether a popup opened and what the popup showed (account picker / "pick an account" / silent).
  3. In the console log `Xrm.Utility.getGlobalContext().organizationSettings.tenantId` and `userSettings.userPrincipalName` (home UPN, `#EXT#` alias, or empty — it is the `loginHint` `[CODE LIB/strategies/BrowserMsalStrategy.ts:55-61]`).
  4. Decode the BFF bearer; list the `msal.*` `localStorage` account entries (tenant ids only).
  5. Note the response of the first BFF call the PCF makes.
- **Record**: `tid`, `oid`, `acct`, `idp`, `iss`, `aud`, `appid`, `upn`; MSAL `AccountInfo.tenantId`; the two Xrm values; popup Y/N; HTTP status of the first BFF call; whether `oid` equals the `azureactivedirectoryobjectid` read in the precondition.
- **Pass** (the design's working assumption): `tid=a221a95e…`, `acct=1`, `idp=` the home issuer, `oid=` the Spaarke-tenant guest object id = `systemuser.azureactivedirectoryobjectid`, BFF 200. **Fail modes to name explicitly**: popup required on every load (silent redemption from a home-only session does not work); `tid=` home tenant (authority pin not honoured — unexpected for a pinned authority); `AADSTS50058` / `interaction_required`; `oid ≠ azureactivedirectoryobjectid` (home `oid` stored in Dataverse).
- **Confirms / changes**: x03 §1.1 S1/U2 Sign in (UNTESTED → WORKS or BLOCKED); x03 §3 A2 (the fresh-profile and two-profile runs; A2's third run, two same-tenant accounts, is T-P3-13 item 2) [ADDED by check]; x03 §1 global dependency G9 (guest `oid` ≠ home `oid`; tenant-pinned authority mints a resource-tenant token) [CORRECTED by check: "G9" is the §1 label, not §3 G9 (ACS)]; x01 #1 for the tenant-pinned client ("a B2B guest's token carries `tid` = Spaarke's tenant" moves from DOC CLAIM ONLY to VERIFIED LIVE or is refuted); a01 §7 r4, r10; a02 §7 r4, r5.

#### T-P1-02 — U2 on S1: BFF acceptance, `oid`→`systemuser`, Dataverse OBO

- **Priority** P1 · **User** U2 · **Surface** S1 · **Safety** READ-ONLY
- **Preconditions**: T-P1-01 completed in the same browser session (the guest holds a BFF token). A `sprk_document` id the guest can Read via Dataverse security (confirm with `Xrm.WebApi.retrieveRecord` first — read-only).
- **Steps**:
  1. From the console, `fetch` with the held bearer: `GET {sprk_BffApiBaseUrl}/users/me/memberships/sprk_matter` (Dataverse OBO + `oid` cross-ref `[CODE BFF/Api/Membership/MembershipEndpoints.cs:435-459]` *(checked)* — note the `isdisabled eq false` filter at `:450-453`).
  2. `GET …/documents/{id}/preview-url` (route behind `DocumentAuthorizationFilter("read")` → OBO `RetrievePrincipalAccess`, then app-only SPE `[CODE BFF/Api/FileAccessEndpoints.cs:23-32,190-198]`).
  3. In App Insights find `[UAC-DIAG] GetUserAccessAsync START … UsingOBO=` `[CODE src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs:284]` *(checked)* for the request, and any `obo_failed` 401 `[CODE BFF/Infrastructure/DI/MiddlewarePipelineExtensions.cs:44]` *(checked)* or `caller_not_a_dataverse_user` `[CODE …/DataverseAccessDataSource.cs:464]` *(checked)*.
- **Record**: both HTTP statuses and bodies' `error`/`reason` fields; `UsingOBO` value; any OBO error (`AADSTS…` code only).
- **Pass**: step 1 → 200 with a membership list (even empty); step 2 → 200 (or 403 with a rights reason, not `obo_failed`); `UsingOBO=True`. **Fail**: 401 `obo_failed` (OBO does not work for a guest in the resource tenant) or 401 from step 1 with no membership (the `oid` does not match, or the row is disabled).
- **Confirms / changes**: x03 §1.1 S1/U2 "Read Dataverse records" and "Read/write SPE documents" (UNTESTED → WORKS/BLOCKED); x03 §3 D3 (first half), E1 (U2 half); a05 §7 r8; a06 §7 r7; a07 §7 r2.

#### T-P1-10 — U2 `sprk_isexternal` state (the "guest counts as internal" defect, x03 H-2)

- **Priority** P1 · **User** U2 · **Surface** S1/S2/S3 · **Safety** READ-ONLY
- **Preconditions**: operator Dataverse access to `spaarkedev1`.
- **Steps**:
  1. Run `scripts/Set-ExternalFlagForB2BGuests.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify` (verify mode reads only; exit 0 = every guest flagged `[CONFIG scripts/Set-ExternalFlagForB2BGuests.ps1:9-14,31-36]` per x03 H-2).
  2. Read-only query: `systemusers?$select=domainname,sprk_isexternal,azureactivedirectoryobjectid,isdisabled&$filter=contains(domainname,'#EXT#')`.
- **Record**: exit code; per guest: `sprk_isexternal` (blank / true), `isdisabled`.
- **Pass**: every guest `true`. **Fail**: any blank — then on dev every Restricted-record membership, `/share-user` and standing-writer decision treats that guest as internal `[CODE BFF/Services/Identity/SystemUserIdentityResolver.cs:302-316]`.
- **Confirms / changes**: x03 H-2 (live state on dev); x03 §3 D3 (second half); a07 §7 r6. Decides the precondition for T-P1-08 (run it once blank and once `true` if the owner approves the flag change — that change is a LIVE CHANGE and is out of this test).

#### T-P1-12 — Active Dataverse application user: roles, `prvActOnBehalfOfAnotherUser`, FLS membership

- **Priority** P1 · **User** U6 (decides U1/U2 impersonated reads) · **Surface** S1, S2 · **Safety** READ-ONLY
- **Preconditions**: `Graph__ManagedIdentity__Enabled=true` `[LIVE x04 §2]`, so the active application user is `mi-bff-api-dev` (`systemuser` oid `9fd47efb…`, `[LIVE x05 §B]`). Impersonation (`MSCRMCallerID`) fails closed without the privilege `[CODE src/server/shared/Spaarke.Dataverse/DataverseImpersonation.cs:44-47]` (x01 #8.5).
- **Steps** (all read-only queries):
  1. `systemuserroles_association` for the `mi-bff-api-dev` and `SDAP-BFF-SPE-API` application users → role names; [ADDED by check] also for `spaarke-bff-api-prod` (`92ecc702…`, enabled on dev — §1.3 F-3) so the owner can decide whether a prod identity holding roles on dev is intended.
  2. For each role: `roleprivileges` → does any carry `prvActOnBehalfOfAnotherUser`?
  3. `fieldsecurityprofiles?$expand=systemuserprofiles_association` → which profiles hold the two BFF application users (writer profiles must hold only those two, x03 §3 D8).
  4. App Insights: count `[WF-STANDING] … secured attribute … absent` and `RPA-FALLBACK` over the last 7 days.
- **Record**: role list per app user; privilege present Y/N per app user; FLS profile membership; both counts.
- **Pass**: the ACTIVE app user (`mi-bff-api-dev`) holds the privilege; writer FLS profiles contain exactly the two app users; `RPA-FALLBACK` count 0.
- **Confirms / changes**: x01 #8.5 (VERIFIED LIVE either way); x03 §1.2 S2/U1 "Read Dataverse records" (Office entity search impersonation) — conditional WORKS becomes WORKS or BLOCKED; x03 §3 D2 (residual), D8 (FLS part), D6 (`RPA-FALLBACK` part); a06 §7 r3; a07 §7 r4, r5; a08 §7 r9.

#### T-P1-03 — U2 in HOME-tenant Outlook and Word (S2): NAA broker and popup paths via the Diagnostics view

- **Priority** P1 · **User** U2 · **Surface** S2 · **Safety** READ-ONLY
- **Preconditions**: the guest's home tenant has Outlook/Word with the dev unified add-in package sideloaded for that user (sideloading is a precondition the owner arranges; it is not part of this test). Dev add-in client `c1258e2d…` is `AzureADMyOrg` `[LIVE x04 §1 A6]`; authority is the Spaarke tenant baked at build `[CODE .github/workflows/deploy-office-addins.yml:66-69]` *(checked)*; Diagnostics view deployed `[CODE …/deploy-office-addins.yml:83-87]` *(checked)*; it shows exactly `tid, oid, acct, idp, iss, aud` and the sign-in path `[CODE src/client/office-addins/shared/taskpane/services/tokenDiagnostics.ts:15-23]` *(checked)*.
- **Steps** (four runs: Outlook desktop, Word desktop, Outlook on the web, Word on the web):
  1. Open the task pane; "⋮ → Diagnostics"; press Copy; paste the six rows and the sign-in path into the test record (the view never shows the token).
  2. In the pane's console note which `[OfficeNaaStrategy]` init line fired (`initialized via NAA broker` vs popup PCA) and the `platform=… version=…` line.
  3. Note whether the broker/popup offered the Spaarke guest identity, the home identity, or an account picker; count popups.
  4. `GET /api/office/search/entities?q=xx` from the pane (type two letters in the search box) → status; 403 body `errorCode` `OFFICE_SEARCH_FORBIDDEN` `[CODE BFF/Api/Office/OfficeEndpoints.cs:1210]` *(checked)* means no `systemuser` resolved; this route is impersonated (`MSCRMCallerID`), so it also depends on T-P1-12.
- **Record**: per host: the six claims, sign-in path, init line, popup count, search status.
- **Pass**: `tid=a221a95e…`, `acct=1`, `idp=` home issuer, `aud ∈ {api://1e40baad…, 1e40baad…}`, search 200. **Fail modes**: broker offers only the home identity (no token for a single-tenant Spaarke client → sign-in error); `tid=` home; 403 `OFFICE_SEARCH_FORBIDDEN` with a correct `oid` (then T-P1-12 is the cause).
- **Confirms / changes**: x03 §1.2 S2/U2 Sign in + Read Dataverse (UNTESTED → WORKS/BLOCKED); x03 §3 A3, E5; a03 §7 r3, r6; a01 §7 r5, r6 (desktop NAA thresholds for the guest's hosts).

#### T-P1-04 — U2 in the Teams tab (S3): NAA token, Teams-SSO fallback token, and the BFF's branch

- **Priority** P1 · **User** U2 · **Surface** S3 · **Safety** READ-ONLY
- **Preconditions**: the Teams app installed for the guest in the **home** tenant's Teams (owner-arranged; see T-P1-06 for the package variant). Client path: NAA primary (`acquireTokenSilent` → `ssoSilent` → `acquireTokenPopup`), then `teamsAuthentication.getAuthToken()` `[CODE src/client/external-spa/src/auth/msal-auth.ts:321-346,353-359,372-399]` *(checked)*; authority `/organizations` (no tenant pin) `[CODE src/client/external-spa/src/auth/msal-config.ts:147-159]`; fallback token `aud = api://green-dune…/1e40baad…`, which the dev BFF accepts (`ValidAudiences__2`, `[LIVE x04 §2]`). Server: default scheme → `/api/v1/external` → Workforce plane → (a) `oid`→`systemuser`, (b) member test, (c) deny `[CODE BFF/Infrastructure/ExternalAccess/WorkforcePrincipalResolver.cs:111-170]`; dev list = `{a221a95e…}` `[LIVE x04 §2]`.
- **Deterministic branch table (what the BFF does once the claims are known)** `[CODE …/WorkforceIdentityOptions.cs:199-230; …/WorkforcePrincipalResolver.cs:163,199,206]` *(checked)*:

  | Token shape | BFF outcome on dev | Log line |
  |---|---|---|
  | `iss`/`tid` = home tenant | **unknown at the scheme** (x01 #1 — Microsoft.Identity.Web issuer validation): 401 if rejected; if accepted → `ForeignTenant` → 403 `sdap.access.deny.workforce_tenant_not_customer` | `[WF-AUTH] Denying request: …` `[CODE …/CallerPrincipalResolver.cs:623,633]` |
  | `tid=a221a95e`, `oid` = guest `systemuser` oid | branch (a) → systemuser principal → 200 | `[WF-AUTH] Resolved workforce caller oid=… to systemuser …` `:163` |
  | `tid=a221a95e`, `acct=1`, no `systemuser` | `Guest` → 403 `sdap.access.deny.workforce_guest` | `:206` |
  | `tid=a221a95e`, `acct` absent | 403 `…workforce_acct_claim_missing` | `:206` |

- **Steps** (two sessions: home-only browser/Teams session; Teams session where the guest has also signed in to the Spaarke tenant):
  1. Open the tab; in devtools decode the token the SPA sends to `GET /api/v1/external/me` (the `Authorization` header) — note whether it came from NAA (MSAL console lines) or from `getAuthToken` (`aud` = `api://green-dune…`).
  2. Record the response status and body; find the `[WF-AUTH]` line in App Insights.
  3. Note `fetchMeEntitlements` masking: if the UI shows "Sam Rivera" / "Dana Okafor" the call was 401/403 and the mock replaced it (a04 D-4) — record the real status from the network tab, not the UI.
  4. To force the Teams-SSO fallback for one run, use a client without an NAA bridge (Teams web in a browser profile where the broker is unavailable; record which client) — do not change the manifest for this test.
- **Record**: per session and per path (NAA / SSO): `tid`, `oid`, `acct`, `idp`, `iss`, `aud`, `appid`; `/me` status; deny code; `[WF-AUTH]` text; `extractAuthDiagnostics` output on failure.
- **Pass** (for the design): at least one path yields `tid=a221a95e…` with `oid` = the guest `systemuser` oid and `/me` 200. **Decisive negative**: both paths mint home-tenant tokens → the tab cannot bind a Model 1 guest without a tenant pin or `domainHint`. Either way the issuer-acceptance question (x01 #1) is answered by the status of the home-tenant run.
- **Confirms / changes**: x03 §1.3 S3/U2 Sign in (UNTESTED → one of the branch-table rows); x01 #1 (home-tenant issuer acceptance: VERIFIED LIVE); x03 §3 A4 (U2 part), J1 (foreign-tenant half); a04 §7 r2, r8; a05 §7 r1; x03 M-2.

#### T-P1-05 — U2 browser sign-in to the external SPA, workforce realm ("Sign in with your work account"), against Spaarke's tenant

- **Priority** P1 · **User** U2 (and U1 as control) · **Surface** S4 · **Safety** READ-ONLY
- **Preconditions**: `https://green-dune-…` reachable; `RealmChooser` → `setStoredRealm('workforce')` → fresh PCA on `/organizations`, `sessionStorage`, `loginRedirect` `[CODE src/client/external-spa/src/auth/standalone-plane.ts:51-74; src/client/external-spa/src/components/AuthGuard.tsx:66-94]`; no `domainHint`/`loginHint` passed `[CODE src/client/external-spa/src/auth/msal-config.ts:157]`.
- **Steps** (three runs): (i) fresh profile, home-tenant session only; (ii) profile with a Spaarke-tenant session (sign in to `spaarkedev1` first); (iii) profile with both, and pick the Spaarke-tenant entry if an account picker appears.
  1. Choose the workforce realm; complete the redirect; decode the token the SPA sends to `/api/v1/external/me` (from `sessionStorage` `msal.*` entries or the network tab).
  2. Record `/me` status, deny code, `[WF-AUTH]` line (same table as T-P1-04).
  3. Sign out (`App.tsx:225-229`) and confirm the next load shows the realm chooser again.
- **Record**: per run: which tenant minted (`tid`, `iss`), `acct`, `idp`, `oid`, `aud` (`api://…` vs GUID), `appid`; whether Entra showed a tenant/account picker; `/me` status and deny code.
- **Pass** (for the design): run (ii) or (iii) yields `tid=a221a95e…` and 200. Run (i) is expected to mint a home-tenant token — its status answers x01 #1 a second time. If NO run can mint a Spaarke-tenant token without a picker, the SPA needs an authority pin / `domainHint` for Model 1 (a design input; a pinned-authority dev build would be a LIVE CHANGE — T-P1-06 note).
- **Confirms / changes**: x03 §1.4 S4/U2 (UNTESTED → row of the branch table); x03 §3 A4 (browser realm), J1; a04 §7 r2 (browser plane), §4 U2.

#### T-P1-07 — Default-scheme acceptance of a foreign-tenant token and of a v2 bare-GUID audience (Microsoft.Identity.Web behaviour, x01 #1 / J1)

- **Priority** P1 · **User** U2 (home-tenant token), U1 · **Surface** S1/S3 · **Safety** READ-ONLY
- **Preconditions**: a home-tenant-issued token already held from T-P1-04/T-P1-05 run (i) — if none was minted there, this test has no input and is closed as "unreachable"; a U1 token whose `aud` is the bare GUID (request `1e40baad-…/.default` from Azure CLI, which is pre-authorized `[LIVE x04 §1 A1, §6]`: `az account get-access-token --resource 1e40baad-e065-4aea-a8d4-4b7ab273458c` — read-only, prints a token to the operator's terminal; do not store it). No code sets `ValidIssuers`/`IssuerValidator` for the default scheme `[CODE BFF/Infrastructure/DI/AuthorizationModule.cs:47-49]` *(checked)*; audience merge `:111-126` *(checked)*; live `ValidAudiences` includes the bare GUID `[LIVE x04 §2]`.
- **Steps**:
  0. [ADDED by check] READ-ONLY: `az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --query api.requestedAccessTokenVersion` — x04 did not print it; `null`/`1` means the CLI token of step 2 will carry `aud=api://1e40baad…` and the bare-GUID case is reachable only through a client that requests v2 (record which `aud` the CLI token actually carries before drawing a conclusion).
  1. `GET /api/me` with the home-tenant token → status; App Insights `OnAuthenticationFailed` text (issuer / signature / audience).
  2. `GET /api/me` with the bare-GUID U1 token → status (record the token's actual `aud`).
  3. From the same App Insights entries, note the claim-type form the BFF saw (`oid`/`tid` short names vs `http://schemas.microsoft.com/identity/claims/...`) — the request logger prints what `HttpContext.User` holds.
- **Record**: statuses; failure reason strings; claim-type form.
- **Pass**: step 1 → 401 with an issuer-mismatch reason (single-tenant `AzureAd:TenantId` enforced) **or** 200 (not enforced — then every U2 token shape reaches the member test); step 2 → 200. Both are "pass" as observations; the record wording differs.
- **Confirms / changes**: x01 #1 (VERIFIED LIVE); x03 §3 J1 (closes both halves), A1 claim-type form; a05 §7 r1–r3; x03 G5.

#### T-P1-08 — U2 OBO outcome per downstream: Graph, Dataverse, SPE (app-only vs OBO surfaces)

- **Priority** P1 · **User** U2 · **Surface** S1 (and S2) · **Safety** READ-ONLY (core); one optional step is LIVE CHANGE (benign)
- **Preconditions**: T-P1-01/02 passed (the guest holds a valid BFF token and OBO to Dataverse works); a document the guest can Read; the guest has **no** SPE container role (confirm via the owner; do not grant one). The user token reaches SPE only on six surfaces (`/api/me*`, Office URL `/shares`, Compose Path B, Word export, chat persist, AI context items) `[CODE BFF/Infrastructure/Graph/DriveItemOperations.cs:1008-1013; BFF/Services/Compose/ComposeSpeAccess.cs:37-81; BFF/Api/Ai/ChatWordExportEndpoints.cs:157-184]`; preview/download/open-links are app-only behind the ownership guard (x03 §1.1 S1/U1 SPE row).
- **Steps**:
  1. Graph OBO: `GET /api/me` → status; on failure the Graph/Entra error code from App Insights (expected shapes: success, or `AADSTS50020`/`AADSTS500011`-class, or Graph 404 for a user with no mailbox — code only, no payload).
  2. Dataverse OBO: already covered by T-P1-02; repeat `GET …/users/me/memberships/sprk_matter` here for the same session.
  3. SPE app-only: `GET /api/documents/{id}/preview-url` and `GET /api/documents/{id}/download` (HEAD or abort the body) → expect 200 with no container role.
  4. SPE OBO: `POST /api/documents/resolve-identity` with an SPE URL of that document → expect 200 `{resolved:false, reason:not_resolvable}` for a caller without a container role (the code's measured note `[CODE BFF/Services/Documents/DocumentUrlIdentityResolution.cs:229-239]`).
  5. *(Optional, LIVE CHANGE benign — writes one Word file to SPE; **owner approval required before running** [ADDED by check])*: `POST /api/ai/chat/export` for a chat session the guest owns → expect 403 at SPE (container role required).
- **Record**: per step: status, `reason`/`error` field, Entra/Graph error code; the `obo` and `oid` fields of the audit scope (`AuditEnrichmentMiddleware` pushes `oid`, `appid`, `obo`, `tenantId`, `correlationId` `[CODE BFF/Infrastructure/Logging/AuditEnrichmentMiddleware.cs:80-116]`).
- **Pass**: steps 3 and 4 behave as stated (app-only path does not need a container role; OBO path reports not-resolvable); step 1's result is recorded as the Graph-OBO fact for guests; step 5, if run, 403.
- **Confirms / changes**: x03 §1.1 S1/U2 "Read/write SPE documents"; x03 §3 E2, A7 (U2 part); a06 §7 last two rows, r6; x03 §4 H-7 root cause (Graph OBO for a guest).

#### T-P1-09 — U2 e-mail paths: Quick Save without `canGetAttachments`, and Word Email-tab send

- **Priority** P1 · **User** U2 · **Surface** S2 · **Safety** **LIVE CHANGE** (creates a document row + `.eml` in SPE; sends one e-mail from the guest's identity) — owner approval required; use a test mailbox as the recipient.
- **Preconditions**: T-P1-03 passed on at least one host; a test Matter to file into; an Outlook host where `canGetAttachments` is false (compose mode, or a host below Mailbox 1.8 — record which) so the server path is Graph OBO `Me.Messages` in the Spaarke tenant `[CODE src/client/office-addins/shared/adapters/OutlookAdapter.ts:639-645; BFF/Services/Office/OfficeEmailEnricher.cs:139-149]`; the Email tab is ON in the dev build `[CODE .github/workflows/deploy-office-addins.yml:76-82]` *(checked)*.
- **Steps**:
  1. Quick Save an e-mail from the guest's home mailbox in that host. Record the pane's result (success / error), then read the created `sprk_document` row and open the `.eml` (read-only after the save): is it headers-only?
  2. App Insights: the enricher's swallowed Graph error (code only).
  3. In Word, Email tab: send a one-line message to the test mailbox with a small attachment the guest can Read. Record status of `POST /api/communications/send` and the Graph error code in `SendMode.User` `[CODE BFF/Services/Communication/Channels/EmailChannelSender.cs:57; BFF/Infrastructure/Graph/GraphClientFactory.cs:286-293]`.
  4. Rollback: delete the test document row and `.eml` (note ids in the record); the e-mail, if sent, stays in the test mailbox.
- **Record**: pane result vs file content (headers-only Y/N); Graph error codes; send status; ids created.
- **Pass**: either (a) the guest has a usable mailbox in the resource tenant and both work, or (b) both fail with a recorded code — but a **"success" that produced a headers-only `.eml` is a FAIL** (x03 H-7 confirmed live).
- **Confirms / changes**: x03 H-7; x03 §1.2 S2/U2 "Read/write SPE documents" and "Send communication"; x03 §3 E3; a03 §7 r7; a06 §7 r6.

#### T-P1-06 — Teams NAA with `webApplicationInfo` removed from the manifest

- **Priority** P1 · **User** U1 first, then U2 · **Surface** S3 · **Safety** **LIVE CHANGE** (sideloads a modified Teams app package for a test user in the dev tenant; a second package for the guest's home tenant if the owner wants the U2 variant) — owner approval required; rollback = remove the sideloaded app.
- **Why it decides the design**: the Teams-SSO fallback needs the manifest's `webApplicationInfo.resource` (`api://green-dune…/1e40baad…`) `[CODE src/client/external-spa/appPackage/manifest.json:41-44]` *(checked)*; NAA does not — it needs the `brk-{teams-client-id}://origin` redirects and the Teams client ids pre-authorized on the app, both present on `1e40baad` `[LIVE x04 §1 A1]`. A per-customer manifest without `webApplicationInfo` would remove the per-customer BFF-app dependency from the package.
- **Preconditions**: a copy of `src/client/external-spa/appPackage/` with the `webApplicationInfo` block deleted and a distinct `id`/name ("Spaarke Collab — NAA test"); sideload rights for the test user; the unchanged `green-dune` site (no code change).
- **Steps**:
  1. Sideload the modified package for the test user; open the tab in Teams desktop and Teams web.
  2. In devtools confirm the NAA path (`acquireTokenSilent`/`ssoSilent`/`acquireTokenPopup` lines; no `getAuthToken` call succeeded) and decode the token sent to `/api/v1/external/me` (`aud`, `tid`, `appid`).
  3. Force the fallback once (client without a bridge, as in T-P1-04 step 4) and record the `getAuthToken` error — it is expected to fail without `webApplicationInfo`.
  4. If approved, repeat 1–2 with the guest (U2) in the home tenant.
  5. Remove the sideloaded app.
- **Record**: NAA success Y/N per client; token `aud`; fallback error text; any consent prompt and which app it named.
- **Pass**: NAA yields a BFF-accepted token (`/me` 200 for U1) in both desktop and web with no `webApplicationInfo`; the fallback fails as predicted. **Fail**: NAA also fails → the package still needs `webApplicationInfo` (per-customer app id in the manifest).
- **Confirms / changes**: x03 §1.3 S3 Sign in (NAA bridge availability per client, a04 §7 r6); x03 H-6 (per-customer packaging); x03 §3 F3 (NAA part); design input for the pending Model 1 client sign-in.

### 2.2 P2 — CIAM, Teams (U1/U3), add-ins (U1), Copilot, control plane

#### T-P2-01 — U4 CIAM token claims, plane selection and contact binding; CIAM tenant app state

- **Priority** P2 · **User** U4 · **Surface** S5 · **Safety** READ-ONLY
- **Preconditions**: a test partner account in `7052feba…` with a `contact` row whose `sprk_externalobjectid` = that account's `oid`; `Ciam__Audience=4a4d5126…` (GUID) `[LIVE x04 §2]`; plane rule `[CODE …/CallerPrincipalResolver.cs:386-406]` *(checked)*; binder `EmailBindOnly`, never creates `[CODE BFF/Infrastructure/ExternalAccess/ContactIdentityBinder.cs:190-202]`. Operator: `az login --tenant 7052feba-… --allow-no-subscriptions`.
- **Steps**:
  1. "Continue as Partner" → sign in; decode the token in `sessionStorage` → `iss`, `aud`, `tid`, `oid`, `preferred_username`/`email`, `scp`.
  2. `GET /api/v1/external/me` → status; `MeEntitlementsEndpoint` reports the plane in its body (`MeEntitlementsEndpoint.cs:48`); `/me/entitlements` → expect blanket `["assigned-work"]` (x03 M-1).
  3. Read-only Entra (CIAM tenant): `az ad app show --id 4a4d5126-…` → `api.requestedAccessTokenVersion`, `identifierUris`; `az ad app show --id bd57e54e-…` → SPA redirects (incl. `http://localhost:3000`?), `requiredResourceAccess` to `4a4d5126`; `az ad app show --id e63e6eb1-…` → `keyCredentials` thumbprint (compare `938A0ECE…` DOC-ONLY per a04/a09); `az rest … /beta/identity/authenticationEventsFlows/046a0529-…?$expand=conditions` → `isSignUpAllowed`.
- **Record**: the claims; `aud` = GUID or `api://…`; `iss` = `https://spaarkeextid.ciamlogin.com/7052feba…/v2.0`?; `/me` status and plane; the four app facts.
- **Pass**: `aud=4a4d5126…` (GUID, matches `Ciam__Audience`), `iss` contains `ciamlogin.com`, `/me` 200 with plane `CiamContact`, `isSignUpAllowed=false`. **Fail**: `aud=api://4a4d5126…` (v1 token vs GUID audience → 401 on every CIAM call).
- **Confirms / changes**: x03 §1.5 S5/U4 Sign in (WORKS confirmed live); x01 #8.2 (closed); x03 §3 A5, B4; x03 R21 (NOT CHECKABLE → VERIFIED LIVE); a04 §7 r1, r5; a09 §7 r17.

#### T-P2-02 — U4 blocked surfaces and multi-scheme logging noise

- **Priority** P2 · **User** U4, anonymous · **Surface** S5 · **Safety** READ-ONLY (POSTs to routes that are unmapped or reject before any handler)
- **Preconditions**: T-P2-01 token in hand.
- **Steps**:
  1. With the CIAM token: `POST /api/ai/search` (expect 401 — default scheme only `[CODE BFF/Api/Ai/SemanticSearchEndpoints.cs:75-77]`); `POST /api/v1/external/ai/playbook` (expect 404 — unmapped, a04 D-1); `POST /api/dataverse/fetch` and `POST /api/emails/convert-to-document` (expect 401 — unmatched route + fallback policy, x01 #5); [ADDED by check] `GET /api/office/search/entities?q=xx` (expect 401 — a03 §7 r7: the CIAM scheme is not applied to `/api/office/*`).
  1b. [ADDED by check] Same `POST /api/dataverse/fetch`, `POST /api/emails/convert-to-document`, `POST /api/v1/external/ai/playbook` with a **U1 workforce** token → expect 404 each (x03 §3 E7's workforce half; the authenticated → 404 half is pinned only on the test host `[CODE tests/integration/Sprk.Bff.Api.IntegrationTests/Api/Dataverse/DataverseProxyRoutesRemovedTests.cs:40-71]`, inherited from a05 §7).
  2. Same four with **no** token (expect 401 each — J2, the framework's fallback-policy-on-no-endpoint behaviour).
  3. Open `DocumentUploadPage` and `PlaybookLibraryPage` in the SPA as U4; record the console error (`uninitialized_public_client_application` vs redirect) and the request status (a04 D-2/D-3).
  4. App Insights: on the successful `/me` call of T-P2-01, is there a "JWT auth failed" warning with a `ciamlogin.com` issuer from the default scheme (a05 §7 r11)?
  5. [ADDED by check] a06 §7 r7 ("Entra error when a `*.ciamlogin.com` token is used as `UserAssertion` against `login.microsoftonline.com/{tid}`") is a **lab call to `AcquireTokenOnBehalfOf`** that needs the BFF's own client credential — that is **credential use → LIVE CHANGE, owner approval**; it is unreachable by any route (defence-in-depth only). Default: **NOT RUN**; record "not run — unreachable by route" unless the owner asks for it.
- **Record**: twelve statuses (incl. the office-search 401 and the three workforce 404s); the two page errors; the warning Y/N and its text; step 5 run/not-run.
- **Pass**: statuses as expected; the warning finding is recorded either way.
- **Confirms / changes**: x03 §1.5 S5/U4 AI row (BLOCKED confirmed); x03 §3 E7 (both halves), E8, J2; x03 H-9; a05 §7 last row + r11; a02 §7 r11, last row; a03 §7 r7 (CIAM scheme on `/api/office/*`); a06 §7 r7 (recorded as not run unless approved).

#### T-P2-03 — Workforce user choosing "Continue as Partner"

- **Priority** P2 · **User** U1 · **Surface** S5 · **Safety** READ-ONLY
- **Preconditions** [ADDED by check]: a U1 account; `https://green-dune-…` reachable; the "Continue as Partner" realm goes to the CIAM authority `https://spaarkeextid.ciamlogin.com/7052feba…` `[LIVE x04 §2 Ciam__Instance/TenantId]`; operator read access to `contacts` in `spaarkedev1` for the after-check.
- **Steps**: choose the CIAM realm with a Spaarke staff account; record the exact IdP error page / `AADSTS` code; confirm no contact was created (read-only `contacts` query by e-mail afterwards).
- **Pass**: sign-in refused at the CIAM IdP; no row created. **Confirms**: x03 §1.5 S5/U1–U3 Sign in (NOT SUPPORTED, exact error recorded); x03 §3 F4; a04 §4 U1.

#### T-P2-04 — U1 Teams tab and browser realm: NAA per client, Tier-1 roles, the OBO-route leak

- **Priority** P2 · **User** U1 · **Surface** S3, S4 · **Safety** READ-ONLY
- **Preconditions**: the published dev Teams app; `sprk_approlemodulemap` rows read beforehand (read-only query).
- **Steps**:
  1. Teams desktop (Windows, Mac if available), Teams web, Teams mobile: open the tab; record NAA vs `getAuthToken` path, popup Y/N, `extractAuthDiagnostics` on failure, whether the realm chooser appeared inside Teams (`detectTeamsHost(750 ms)` timeout) and `sessionStorage` isolation (devtools → Application).
  2. Decode the token: `roles` present? `GET /api/v1/external/me/entitlements` → body; App Insights `0 internal modules` line `[CODE BFF/Infrastructure/ExternalAccess/ModuleEntitlementResolver.cs:106]` means no app roles reached Tier-1.
  3. From the SPA's Semantic Search, one query → `POST /api/ai/search` status (a default-scheme OBO route reached from the broker-only surface, x03 H-9 / R26).
  4. Open `DocumentUploadPage` as U1 — record whether it mints a token at all (never-initialised CIAM singleton) and the status of `PUT /api/obo/records/…` if reached (do not complete an upload).
- **Record**: per client: path, popup count, chooser shown Y/N; `roles`; entitlements body; search status; upload-page behaviour.
- **Pass**: a token per client via NAA or fallback; entitlements reflect `roles` ∩ `sprk_approlemodulemap` (expected: none → empty shell, since `1e40baad` defines only `Admin` `[LIVE x04 §1 A1]`); search 200 (confirms the leak) or 401.
- **Confirms / changes**: x03 §1.3 S3/U1 Sign in, Read Dataverse, AI rows (UNTESTED → WORKS with observed path); x03 §3 A4 (U1), D4 (Tier-1 part), F3; x03 H-9; a04 §7 r6, r7, r12; a07 §7 r9 (roles).

#### T-P2-05 — U3 licence-free workforce user: OBO on internal routes, contact binding, context-free chat

- **Priority** P2 · **User** U3 · **Surface** S1 (BFF routes), S3/S4, S7 · **Safety** steps 1–2 READ-ONLY; step 3 **LIVE CHANGE** (binds/creates a `contact`) — **owner approval required before running; record the rollback** [ADDED by check]; step 4 LIVE CHANGE (benign — creates a chat session) — **owner approval required** [ADDED by check]
- **Preconditions**: a Spaarke-tenant member with no `systemuser` and no licence (`acct=0`, `tid=a221a95e…`). On dev the tenant list contains `a221a95e…` `[LIVE x04 §2]`, so the member test returns `Member` and the binder runs `EmailBindAndCreate` `[CODE BFF/Infrastructure/ExternalAccess/ContactIdentityBinder.cs:163-186]` — this is why step 3 writes.
- **Steps**:
  1. Obtain a BFF token as U3 via the SPA browser realm (T-P1-05 mechanics); decode (`acct=0` expected).
  2. READ-ONLY: `GET /api/me` → expect 401 `obo_failed` or 403; `GET /api/documents/{id}/preview-url` → expect 403 `caller_not_a_dataverse_user` `[CODE …/DataverseAccessDataSource.cs:464]` *(checked)* — [ADDED by check] also record, from App Insights, the `CallerRecordAccessProbe` log line and the HTTP status Dataverse returned to the U3 OBO token (`WhoAmI` / `systemusers?$filter=…`), which is a06 §7 r8's observation; `GET /api/office/search/entities?q=xx` → 403 `OFFICE_SEARCH_FORBIDDEN`; `GET /api/v1/external/me/entitlements` → 401/403 before binding? (no — binding happens on `/me`; call `/me/entitlements` only after step 3 if approved).
  3. **LIVE CHANGE**: `GET /api/v1/external/me` → expect 200 with a contact-only principal and `[WF-AUTH] Resolved workforce caller oid=… to contact-only principal … via EmailBindAndCreate` `[CODE …/WorkforcePrincipalResolver.cs:199]` *(checked)*; read the created/linked `contact` row (id, `sprk_externalobjectid`); rollback = delete or unlink it per owner instruction.
  4. **LIVE CHANGE (benign)**: `POST /api/ai/chat/sessions` with an empty body, then one `POST …/messages` ("hello") → record whether the runtime completes after the filter chain (x03 §3 E4); delete the session afterwards if a route exists, else note the session id.
- **Record**: claims; four statuses with reason strings; the `[WF-AUTH]` line; contact id; chat outcome.
- **Pass**: step 2 all fail closed with the named reasons; step 3 binds (on dev) with the exact log line; step 4 recorded either way.
- **Confirms / changes**: x03 §1.1 S1/U3 (NOT APPLICABLE confirmed fail-closed), §1.3 S3/U3 Sign in (UNTESTED → WORKS on dev's hand-configured list; still unreachable on a stamp without the intake list, x03 M-2), S7 "SpaarkeAi standalone"; x03 §3 E1 (U3), E4, I6 (dev half); a05 §7 r9; a06 §7 r8 (U3 Dataverse status) [ADDED by check], a06 §7 (U3 row); a07 §7 r9 (chat).

#### T-P2-06 — U1 Office add-in host behaviour and admin-center state

- **Priority** P2 · **User** U1 · **Surface** S2 · **Safety** READ-ONLY
- **Steps**:
  1. On each host (Outlook/Word desktop PC and Mac, Outlook/Word web): read `[OfficeNaaStrategy] platform=… version=…` and the init line that follows (NAA thresholds PC 13530 / Mac 16.44 per a01 §7 r6).
  2. Clear `sessionStorage`; click Quick Save from a **cold** ribbon command (no pane open): does a popup/broker prompt appear (a03 §7 r4)?
  3. Sign in via the pane, then Quick Save: `acquireTokenSilent` vs `acquireTokenPopup` lines (is `sessionStorage` shared between pane and command runtimes, a03 §7 r5)?
  4. Outlook on the web: does the fallback popup get blocked at `auth-callback.html` (a03 §7 r9)?
  5. Does the pane's eager startup `getAccessToken()` pop up on a cold cache without a user gesture (x03 M-3)?
  6. M365 admin center → Integrated apps: unified package installed Y/N; legacy XML add-ins removed Y/N; `alternates.hide` honoured Y/N (a03 §7 r8).
- **Record**: per host: version line, init path, popup count per step; admin-center state.
- **Pass**: observations recorded; any unprompted popup on startup is a confirmed x03 M-3 instance.
- **Confirms / changes**: x03 §1.2 S2/U1 Sign in (WORKS with observed host matrix); x03 §3 F2; a03 §7 r4, r5, r8, r9; a01 §7 r6.

#### T-P2-07 — M365 Copilot agent sign-in (S6) and the Bot Service shell

- **Priority** P2 · **User** U1, then U2 · **Surface** S6 · **Safety** READ-ONLY
- **Preconditions**: the declarative agent installed for the tester (Developer Portal registration `7fac3a6f…`, `[CONFIG src/solutions/CopilotAgent/spaarke-bff-openapi.yaml:1295-1308]`); `AgentToken__CopilotAudience` matches `identifierUris[1]` `[LIVE x04 §1 A1, §2]`; the filter checks only `oid`+`tid` presence `[CODE BFF/Api/Agent/AgentAuthorizationFilter.cs:54-81]`.
- **Steps**:
  1. Developer Portal → the OAuth client registration: client id, redirect, scope (read-only view).
  2. Run the agent as U1: one prompt that calls `GET /api/agent/playbooks`; in App Insights read the request logger's `aud`/`scp`/`appid` and any `CopilotAuth` failure text; decode nothing client-side (the vault holds the token).
  3. If the owner can install the agent in the guest's home tenant, repeat as U2 and record whether the vault's sign-in offers the Spaarke guest identity and the resulting `tid`.
  4. `az bot show -n spaarke-bot-dev -g <rg>` (expect not found or no MI); App Insights: any requests to `/api/agent/message` with 401 in the last 30 days.
- **Record**: registration facts; `aud` (`api://auth-…` expected), `scp` (`access_as_user`?), `tid`, `idp`; playbooks status; bot state; 401 count.
- **Pass**: U1 playbooks 200 with `aud` = CopilotAudience; U2 result recorded; bot absent or unused.
- **Confirms / changes**: x03 §1.6 S6/U1 (UNTESTED → WORKS/BLOCKED), U2 row; x03 §3 A6, G11; x03 H-10 (deployed or not); a08 §7 r1, r4.

#### T-P2-08 — Reporting and admin gates against the live app-role catalogue

- **Priority** P2 · **User** U1 · **Surface** S1 (Reporting code page, `/api/admin/*`) · **Safety** READ-ONLY
- **Preconditions**: `1e40baad` defines only `Admin` `[LIVE x04 §1 A1]`; `sprk_ReportingModuleEnabled=yes` `[LIVE x05 §A]`; filter requires `roles` ∋ `sprk_ReportingAccess…` `[CODE BFF/Api/Reporting/ReportingAuthorizationFilter.cs:154-169]`.
- **Steps**: `GET /api/reporting/status` as U1 → expect 403 (role cannot exist); decode a U1 token for `roles`; if the tester holds `Admin`, `GET /api/admin/jobs` (list) → 200 and note that no Dataverse identity was needed (x03 M-4; a08 D-5). Power BI: `Reporting__ModuleEnabled` / `PowerBi__*` live values go to T-P3-01. [ADDED by check] If `PowerBi__ClientId` is set (T-P3-01): read-only Power BI admin-portal view — tenant settings that admit the SP, the SP's membership in the Power BI security group, and the `BusinessUnitFilter` RLS role on the datasets (a08 §7 r6); do not generate an embed token (the Reporting gate returns 403 on dev anyway).
- **Record**: statuses; `roles` claim content; the three Power BI facts (or "PowerBi__* absent").
- **Pass**: Reporting 403 (x03 H-8 VERIFIED LIVE end to end); admin route behaviour recorded.
- **Confirms / changes**: x03 §1.7 "Reporting code page" (UNTESTED → BLOCKED on dev); x03 H-8, M-4; a08 §7 r5, r6.

#### T-P2-09 — L2 control plane: identifier URI owner, Operator/Reader, `ReservedTenants__*`

- **Priority** P2 · **User** operator · **Surface** S7 · **Safety** READ-ONLY
- **Steps**:
  1. `az ad app list --filter "identifierUris/any(u:u eq 'api://spaarke.com/provisioning-controlplane-dev')" --query "[].{appId:appId,name:displayName,roles:appRoles[].value}"`.
  2. Graph `servicePrincipals/{sp}/appRoleAssignedTo` on that app → who holds `Operator`/`Reader`.
  3. `az webapp config appsettings list` on `sprk-controlplane-dev-api` (production + `--slot staging`) and `-worker`, names and shape only, filtered to `ReservedTenants__`, `AzureAd__`, `ManagedIdentity__`, `AZURE_CLIENT_ID`; `/healthz` on both hosts (they refuse to start without the pair `[CODE L2/Models/ReservedTenantsOptions.cs:33-86,96-109]`).
  4. [ADDED by check — x03 §3 C8 stamp half and I8] Existing stamps: `az ad app list --filter "startswith(displayName,'spaarke-bff-api-')" --query "[].{appId:appId,name:displayName,acct:optionalClaims.accessToken[].name,fics:length(federatedIdentityCredentials)}"` (x04's display-name query returned only `spaarke-bff-api-prod`, §1.3 F-8 — the set may be empty). For each per-customer app found: `acct` present Y/N (pre-T255 apps lack it, a09 §7 r7 / a08 §7 r18). For each matching stamp App Service `sprk-<customerId>-<env>-api`, both slots: `WorkforceIdentity__CustomerTenantIds__*`, `PublicConfig__*`, `Graph__Scopes__0` presence (a09 §7 r9), and the **shape** of `AzureAd__ClientId` / `AzureAd__TenantId` (plain value vs `@Microsoft.KeyVault(...)` reference — the T7 guard silently skips a KV-reference string `[CODE L2/Handlers/E2EAcceptance/CustomerIdentityT7Probe.cs:255-272]`, x03 I8 / a09 D-21). Names + shape only; no values printed.
- **Record**: app id/name; assignments; setting names present; healthz status; per-stamp: app `acct` Y/N, the three setting families per slot, the `AzureAd__*` shape.
- **Pass**: exactly one app exposes the URI and holds both roles; both L2 hosts carry `ReservedTenants__SpaarkeTenantId` + `__CiamTenantIds__0` and are healthy; every stamp found carries `acct` and the H4b-written settings on both slots with plain `AzureAd__*` values (a missing one is a pre-T255/T258 stamp — x03 C3(b) / C8).
- **Confirms / changes**: x03 §1.7 "Provisioning L2 REST API" (UNTESTED → WORKS/BLOCKED); x03 §3 B7 (residual), C8 (L2 part and stamp part), I2, I8 [ADDED by check]; a09 D-14, D-21; a08 D-17 / x03 L-10; a08 §7 r18–r19, a09 §7 r7, r9 [ADDED by check].

#### T-P2-10 — Delegated consent grants, CIAM SP, and Teams/SPA publication state

- **Priority** P2 · **User** U1, U4 · **Surface** S2, S3–S5 · **Safety** READ-ONLY
- **Steps**:
  1. `az rest … /v1.0/oauth2PermissionGrants?$filter=clientId eq '<sp of c1258e2d>'` and the same for `<sp of 170c98e1>`, `<sp of f306885a>` → which scopes on `1e40baad` are admin-consented (what `.default` will return on OBO — also feeds G2). [ADDED by check] Also `<sp of 1958aec2>` (`6fb59227…`, `[LIVE x04 §1 A7]`) → which Graph scopes the production add-in has consented (x03 B3's `email profile User.Read`, a03 §7 r2); and `az ad app show --id 1e40baad-… --query "api.preAuthorizedApplications[?appId=='c1258e2d-1688-49d2-ac99-a7485ebd9995'].delegatedPermissionIds"` + `--query "api.oauth2PermissionScopes[].{id:id,v:value}"` → whether the dev add-in is pre-authorized for `user_impersonation` specifically (x03 B2 residual, a03 §7 r1).
  2. CIAM tenant: `az ad sp show --id 4a4d5126-…` → `appRoleAssignmentRequired`; grants for `bd57e54e` → `SDAP.Access`.
  3. Teams admin center: org-catalog entry for the Spaarke tab; per-customer admin-consent state recorded by the owner.
  4. `az staticwebapp show -n swa-spaarke-external-spa-prod` (exists? custom domain `external.spaarke.com`?); is that origin a redirect on `bd57e54e` and `1e40baad`? (`[LIVE x04 §1 A1]` already shows it is **not** on `1e40baad`.)
- **Record**: grant lists; CIAM SP flag; catalog state; prod SWA facts.
- **Pass**: facts recorded; any missing grant for a shipped scope is a finding.
- **Confirms / changes**: x03 §3 B10 (residual), B2 (scope residual), B3 (consent residual) [ADDED by check], G2 (consent half), I7; x03 H-6 (no prod SPA path); a09 §7 r8, r10; a03 §7 r1, r2 [ADDED by check]; a04 §7 r11.

### 2.3 P3 — Inventory and hygiene

#### T-P3-01 — Remaining dev App Service settings (names and shapes only)

- **Priority** P3 · **User** U6 · **Safety** READ-ONLY
- **Steps**: `az webapp config appsettings list -g rg-spaarke-dev -n spaarke-bff-dev --query "[?…].{n:name,isKv:starts_with(value,'@Microsoft.KeyVault')}"` for: `Reporting__ModuleEnabled`, `PowerBi__*`, `Rag__ApiKey`, `ServiceBus__FullyQualifiedNamespace` vs `ServiceBus__ConnectionString`, `Redis__Endpoint`, `AiSearch__ManagedIdentity__Enabled`, `Notifications__SignalR__*`, `Communication__Acs__Endpoint`, `Communication__Acs__EventGridIngress__ValidationKey`, `Onboarding__Enabled`, `Onboarding__HmacSigningKey`, `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `IdentityLink__Reconciliation__WritesEnabled`, `ExternalAccess__*WritesEnabled`, `TenantRouting__*`, `Graph__Credentials__RequireSecretFreeIdentity` (shape: `true`/`false` is not a secret — record it), the exact KV secret **name** inside the `Communication__WebhookSigningKey` reference (compare `communication-webhook-signing-key` vs `Communication-Webhook-SigningKey`, x03 H-3); [ADDED by check] `AgentToken__AgentAppId` (shape only: is it a GUID, and does it equal `f257a0a9…` — masked in x04, a08 §7 r3), `IdentityLink__Reconciliation__WritesEnabled` and `ExternalAccess__*WritesEnabled` (C3 residual). Repeat for the demo BFF (`spaarke-bff-demo`; also `az identity show -g rg-spaarke-demo -n mi-bff-api-demo --query "{clientId:clientId,principalId:principalId}"`, a09 §7 r11 — not in x04 §3) and any stamp the owner names (T-P2-09 step 4 lists them).
  [ADDED by check — x03 §3 G8 / x01 #6] Which `DefaultAzureCredential` member resolves on each host and whether the host honours `AZURE_CLIENT_ID` / `AZURE_TENANT_ID`: read the `Azure.Identity` information-level log lines on the dev slot (App Insights traces, category `Azure.Identity`; enable the category's level on the slot only if the owner approves — a **LIVE CHANGE** to logging config; otherwise record "not observable at current log level"), and `AZURE_CLIENT_ID` / `ManagedIdentity__ClientId` presence on `sprk-controlplane-dev-api` / `-worker` (from T-P2-09 step 3). The BFF validator's rule 2 already logs an error when `AZURE_CLIENT_ID` holds the UAMI id while an app registration is required `[CODE BFF/Configuration/IdentityConfigurationValidator.cs:60-65,88-95]` *(checked)* — search App Insights for that error text on dev.
- **Record**: presence + shape per key per environment; the startup log line "Connecting Dataverse ServiceClient via …" from App Insights; the `Azure.Identity` credential-selection line (or "not observable"); the validator rule-2 error present Y/N.
- **Pass**: no `*ClientSecret*` key anywhere; the webhook secret name matches an existing KV secret; `AZURE_CLIENT_ID`, if present, equals the UAMI (the validator logs an error when it differs from the app registration — a09 D-6).
- **Confirms / changes**: x03 §3 C1 (`AgentAppId` residual), C3 (residual), C4 (residual), C5 (residual), C7, G8 [ADDED by check]; x01 #6 [ADDED by check]; x03 H-3, M-10, L-11; a05 §7 r7, last row; a06 §7 r1, r9 (`DefaultAzureCredential`), r12, r13; a08 §7 r3, r7, r11, r12; a09 §7 r11 [ADDED by check].

#### T-P3-18 — Staging slot settings (x04 could not read them)

- **Priority** P3 · **Safety** READ-ONLY · **Steps**: `az webapp config appsettings list … --slot staging` for the same keys as C1–C4 and T-P3-01; diff against the production slot. **Pass**: identical identity settings (any drift is a finding; H4b writes both slots on stamps, dev is hand-set). **Confirms**: x03 §3 C1–C5 for the slot; a09 §7 r12.

#### T-P3-02 — `GET /api/config/client` and `GET /api/config` from the Dataverse origin

- **Priority** P3 · **User** U1 · **Safety** READ-ONLY · **Steps**: from a code-page iframe on `spaarkedev1`, `fetch` both; also `curl -i`. [ADDED by check — x03 §3 D7 / a01 §7 r1] In the same code-page iframe and again in a new window (`navigateTo` dialog), `fetch` `…/api/data/v9.2/environmentvariabledefinitions?$filter=schemaname eq 'sprk_TenantId'` and `…/environmentvariablevalues` with `credentials:'include'` and **no** `Authorization` header → record both statuses. **Record**: JSON casing (`tenantId`), `tenantId` value (expect `a221a95e…`, not `common`), `msalClientId` (expect `1e40baad…` per `PublicConfig__MsalClientId` — which differs from `sprk_MsalClientId`, F-6), `Access-Control-Allow-Origin` header, the 429 behaviour after 10 requests/min from one IP, and the two cookie-only Dataverse GET statuses per context. **Pass**: camelCase, real tenant, CORS header present; both cookie-only GETs 200 in both contexts (a 401 in the new-window context is the a01 §7 r1 finding). **Confirms**: x03 §3 C6, D7 [ADDED by check], J3; a01 §7 r1 [ADDED by check], r9; a02 §7 r6, r9; a05 §7 r13.

#### T-P3-03 — `sprk_*` values in every other environment; the KPI `/api/api` defect

- **Priority** P3 · **User** U1 · **Safety** values READ-ONLY; the KPI save is **LIVE CHANGE (benign)**
- **Steps**: the x05 §A query against uat / prod / demo / each customer environment; compare to the `SpaarkeMaster` defaults; note the trailing `/api`. [ADDED by check] In each of those environments also run the x05 §B query (`systemusers?$filter=applicationid ne null`) and record whether the BFF UAMI / BFF app / GitHub SP of that environment are enabled application users (a08 §7 r9 — "registered in the environments they write to"), and `az ad app show --id <that environment's sprk_MsalClientId> --query spa.redirectUris` → is that org's origin listed (a01 §7 r8). Optional (approved — **LIVE CHANGE (benign), owner approval required**): save one test KPI assessment on dev and capture the `POST …/recalculate-grades` URL → expect `/api/api/…` → 401 (a02 D-29; live on dev per `[LIVE x05 §A]`); delete the assessment afterwards (note the id).
- **Record** [ADDED by check]: per environment: the four `sprk_*` identity values vs defaults, trailing `/api` Y/N, application users present/enabled, origin registered Y/N; the KPI URL + status if run.
- **Pass** [ADDED by check]: observations recorded; any environment whose `sprk_BffApiBaseUrl` ends in `/api` is a live instance of a02 D-29; any environment whose origin is missing from its `sprk_MsalClientId` SPA redirects is a sign-in blocker (AADSTS50011 class) to report.
- **Confirms**: x03 §3 D1 (other environments), D2 (other-environment app users) [ADDED by check]; a02 D-11/D-12/D-29; a01 §7 r8, a08 §7 r9 [ADDED by check]; x03 §1.7 classic web resources row.

#### T-P3-04 — Which web-resource copies, ribbons and bundles are live (x03 §1 global dependency G8) [CORRECTED by check: "G8" here is the §1 label (which web-resource copy is live), not §3 row G8 (`DefaultAzureCredential`), which T-P3-01 now covers]

- **Priority** P3 · **User** U1 · **Safety** READ-ONLY (the "Archive Email" click POSTs to an unmapped route and gets 404)
- **Steps** (Dataverse MCP `read_query` / Web API, read-only):
  1. `webresourceset?$filter=name eq '<n>'&$select=name,modifiedon,content` for `sprk_registrationribbon`, `sprk_aichatcontextmap_ribbon`, `sprk_emailactions`, `sprk_DocumentOperations`, `sprk_DocumentDelete.js`, `sprk_communication_send`, the KPI/rollup scripts → decode `content` and grep for `SDAP.Access`, `b36e9b91`, `user_impersonation`, `sprk_BffApiAppId` (source vs packaged copy, a02 D-27).
  2. `systemforms?$filter=objecttypecode eq 'sprk_document'&$select=name,formxml` → `<Library name="sprk_DocumentDelete.js"`; same for `sprk_matter` (insight card scripts) and `sprk_email`/communication forms (`sprk_communication_send`).
  3. Export the unmanaged `sprk_document` / email ribbons; grep for the same names.
  4. Open a Completed e-mail: is "Archive Email" shown; click → `POST …/api/emails/convert-to-document` status (expect 404) and the UI message.
  5. Open a Matter: `POST /api/insights/ask` to the Dataverse origin (expect 404) — present Y/N.
  6. The deployed `SemanticSearchControl` bundle version string (1.1.82 checked-in vs 1.1.84 source, a02 §12).
- **Record**: per resource: which copy; per form/ribbon: loaders; button presence; statuses; bundle version.
- **Pass**: observations recorded; each packaged (pre-#1453) copy found live is an instance of x03 H-5 (D-27) on that environment.
- **Confirms / changes**: x03 §3 D5; x03 §1 G8; x03 H-4, H-5, L-1; a02 §7 r1, r2, r7, r10, gap-fill rows; a09 §7 last row.

#### T-P3-05 — Security roles, FLS profiles, effective-access and RPA fallback rates

- **Priority** P3 · **Safety** READ-ONLY
- **Steps**: `scripts/Set-*RolePrivileges.ps1 -Verify` for `Spaarke Basic User`, `Spaarke Core User`, `Spaarke Access Administrator`; `Secure Record Owner` compare against `config/secure-record-owner-role.json` (H7b codifies it only on provisioned environments, a07 §8-26); `fieldsecurityprofiles` membership (already in T-P1-12 — reuse); App Insights: `[EFFECTIVE-ACCESS] … Failing CLOSED`, `[WF-AUTHZ] Deny veto … could not be read`, `RPA-FALLBACK` counts over 30 days; the Standing Grant Administrators FLS Read on `contact.sprk_standinggrant` (x03 M-17).
- **Pass**: verify scripts exit 0; all three App Insights counts 0; the standing-grant FLS Read present.
- **Confirms**: x03 §3 D4, D6 (fallback part), D8; x03 M-17, L-9; a07 §7 r3, r7, two [ADDED] rows.

#### T-P3-06 — NFR-04 impersonation negative canary against dev

- **Priority** P3 · **Safety** READ-ONLY (the canary reads only) · **Steps**: run `tests/integration/auth/UnifiedAccessControl/ImpersonationNegativeCanary*` per `tests/integration/auth/README.md:52-77` against dev; record all four invariants PASS/FAIL and the date (last recorded run 2026-10-03 is DOC CLAIM ONLY, `[DOC tests/integration/auth/README.md:182]`). **Confirms**: x03 R50 live result; x03 §3 D6; a07 §7 r3; x03 H-1 context (A5 not consumed — the canary proves narrowing, not consumption).

#### T-P3-07 — Graph grants, Exchange scoping, delegated `.default`, SPE container-type grants, Azure RBAC

- **Priority** P3 · **User** U6 · **Safety** READ-ONLY except the SPE grant read (requires an app-only token as the owning app `170c98e1`, i.e. use of its stored secret/cert — **LIVE CHANGE: credential use**, owner approval)
- **Steps**:
  1. Diff `[LIVE x04 §6]` (7 roles on `mi-bff-api-dev`) against `GraphAppRoles.All` (15) `[CODE BFF/Infrastructure/Auth/GraphAppRoles.cs]` and record which code paths need the 8 missing roles (least privilege) — grep is enough.
  2. Exchange: `Get-ApplicationAccessPolicy` (legacy) and `Get-ManagementRoleAssignment -App 5967251e-…` (RBAC for Applications) → which mechanism scopes `Mail.Read`/`Mail.Send` on dev `[CODE …/GraphAppRoles.cs:209-212]`.
  3. `az ad app permission list-grants --id 1e40baad-…`; [ADDED by check] `az ad app show --id 1e40baad-… --query requiredResourceAccess` and resolve the ids against `az ad sp show --id 00000003-0000-0000-c000-000000000000 --query "oauth2PermissionScopes[].{id:id,v:value}"` / `appRoles[].{id:id,v:value}` (x04 printed counts only — Graph 25, Exchange 1, SharePoint 2; a09 §7 r6); decode the `scp` of one Graph OBO result from a dev-slot log (x03 §3 A7 — the OBO result is server-side; use App Insights only if the scope is logged; otherwise record "not observable without a code change"); [ADDED by check] the same for one **Dataverse** OBO result — does it preserve `upn`/`oid` (a06 §7 r6, ADR-028 A4's "proven 2026-08-20" claim is DOC CLAIM ONLY) — same observability caveat.
  4. [CORRECTED by check] Preferred READ-ONLY path first: as a U1 holding the `Admin` app role, `GET /api/spe/containertypes/{id}/permissions` (the BFF admin surface a07 §7 r8 names; `[CODE BFF/Api/SpeAdmin/ContainerTypePermissionEndpoints.cs:37]` *(checked — `MapGet("/containertypes/{typeId}/permissions", …)`, delegated, `WithSpeAdminContainerTypeScope()`)*) → the grants for `1e40baad` and `5967251e`. Only if that route is unavailable or returns 403: *(approved — credential use, LIVE CHANGE)* `GET …/containerTypeRegistrations/8a6ce34c-…/applicationPermissionGrants` as `170c98e1`.
  5. `az role assignment list --assignee 9fd47efb-… --all` → KV Secrets User, Cosmos Built-in Data Contributor, Storage Blob Data Contributor, Search roles, Service Bus Data Sender/Receiver (scope), Cognitive Services User, any ACS role (template grants none, a06 #16); Redis access policy.
  6. `az ad app federated-credential list` on `1e40baad`, `46670ee2`, `bfac7f6e` → `audiences` (expect `api://AzureADTokenExchange`) — the one field x04 did not print.
- **Record**: role diffs; Exchange mechanism; grants; RBAC list; audiences.
- **Pass**: facts recorded; a Graph role with no code consumer is a least-privilege finding.
- **Confirms**: x03 §3 G1 (residual), G2, G3, G4, G5 (audience), A7 (both OBO results), B1 (`requiredResourceAccess` residual) [ADDED by check]; x03 R20 (`FileStorageContainer.Selected` held → VERIFIED LIVE), R53 ACS gap; a06 §7 r2, r4, r5, r6, r10; a07 §7 r8 [ADDED by check]; a09 §7 r6, r16.

#### T-P3-08 — Azure OpenAI MI path, Service Bus mode, SignalR, Redis/token cache

- **Priority** P3 · **User** U6 · **Safety**: Service Bus / SignalR / Redis checks READ-ONLY; the OpenAI host-alias test **clears a key on a slot — LIVE CHANGE**, owner approval
- **Steps**: `az servicebus namespace show` + `/healthz` Service Bus check (namespace+MI vs SAS; template ships SAS, a08 D-7); `az signalr key list` dates (rotation); `CacheMetrics` + decoded cached-token `exp` vs the fixed 55-min TTL (from a dev-slot log, no token value); *(approved)* E-2 measurement per ADR-028 :474-479 on `spaarke-openai-dev` (`…cognitiveservices.azure.com` alias + cleared key → 401 or 200), then restore the key.
- **Record** [ADDED by check]: Service Bus mode (namespace+MI vs SAS) per `/healthz` and the app settings; SignalR key dates; `CacheMetrics` hit/miss + cached-token `exp` minus issue time vs 55 min; the E-2 result (401 vs 200) if run, and confirmation the key was restored.
- **Pass** [ADDED by check]: facts recorded; SAS mode on any environment is a08 D-7 confirmed live; a cached-token real lifetime under 55 min is a06 §8 item 6 confirmed live.
- **Confirms**: x03 §3 G6, G12; x03 R58, M-10; a06 §7 r9, r12, r13.

#### T-P3-09 — Credential inventory: the BFF app's password credential, the owning app's secrets, Key Vault residuals

- **Priority** P3 · **User** U6 · **Safety** READ-ONLY (names, hints, expiry dates only — never values)
- **Steps**: `az ad app credential list --id 1e40baad-…` (1 secret, F-1), `--id 170c98e1-…` (2 secrets + 1 cert, E-1), `--id 8c85a481-…` (1 secret on an OIDC-only app), `--id da03fe1a-…` (2); for each, grep the repo and the live settings (T-P3-01) for a consumer; `az keyvault certificate show --vault-name spaarke-spekvcert -n ciam-graph-provisioner-cert --query "policy.keyProperties.exportable"`; Power BI SP (`PowerBi__ClientId`, if set) `az ad app credential list`; stamp vaults `az keyvault secret list` → no `*ClientSecret*`; [ADDED by check] `Rag:ApiKey` holder(s) and rotation (a08 §7 r12): `az keyvault secret list-versions --vault-name spaarke-spekvcert -n <rag key secret name from T-P3-01>` → version **dates only**, and which app settings reference it (T-P3-01 output).
- **Record**: per app: credential count, display names, expiry; consumer found Y/N; cert exportability; Power BI secret live Y/N; `Rag:ApiKey` version dates + referencing hosts.
- **Pass**: every live credential has a named consumer; any without one is a removal candidate (x03 M-10 / F-1).
- **Confirms**: x03 §3 G7 (residual); F-1; a06 §7 r11; a09 §7 r15.

#### T-P3-10 — Webhook receivers, signing relay, Event Grid, stamp wiring

- **Priority** P3 · **User** U6 · **Safety** READ-ONLY (an unsigned POST is rejected before any handler)
- **Steps**: resource-group inspection for a Logic App / Function / APIM in front of `/api/communications/incoming-webhook` and `/api/compose/webhooks/spe-doc-changed` (x01 #8.7); `curl -X POST` each with an empty body and no signature → expect 401 (`WebhookSignatureFilter` fail-closed `[CODE BFF/Api/Filters/WebhookSignatureFilter.cs:86-105]`); `POST /api/onboarding/consent-callback` unsigned → 400 if mapped (`Onboarding__Enabled=true`), 401 if not (T-P3-01 tells which); `az eventgrid event-subscription show` → endpoint carries `?sig=`; on a stamp: H14b/H14c run logs for the `/api/webhooks/graph/{module}` validation handshake (expected to fail — x03 H-3) and the KV secret name `Communication:WebhookSigningKey` resolves.
- **Record** [ADDED by check]: relay resource found Y/N (name/type only); the four unsigned-POST statuses; Event Grid endpoint carries `?sig=` Y/N; stamp H14b/H14c outcome lines; the KV secret name the stamp resolves.
- **Pass** [ADDED by check]: unsigned POSTs to the two webhook receivers → 401; consent-callback status matches `Onboarding__Enabled` (400 if on, 401 if off); `?sig=` present. No relay found = x01 #8.7 answered "none" (not a failure, a record fact); an H14b handshake failure on a stamp = x03 H-3 / a08 D-15 confirmed live.
- **Confirms**: x03 §3 H1–H4, C7 (status half); x03 H-3, L-11, R8 (ACS exception); a05 §7; a08 §7 r10, r11.

#### T-P3-11 — L2 UAMI roles, Exchange Admin sidecar, Model 2 foreign-tenant probe, CI secrets

- **Priority** P3 · **User** U6 · **Safety** READ-ONLY (the Model 2 probe is a token request expected to fail; the Exchange sidecar `/healthz` is a GET)
- **Steps**: `az role assignment list --assignee 38f7693f-… --all`; Dataverse admin env: `systemusers?$filter=azureactivedirectoryobjectid eq 38f7693f-…` + roles (`Spaarke Provisioning Registry`); Exchange Admin: `Exchange.ManageAsApp` on `46670ee2` (`requiredResourceAccess` shows 1 Exchange permission `[LIVE x04 §1 A16]` — resolve the id) and the sidecar `GET /healthz` (`degraded` if `SIDECAR_SHARED_SECRET` does not resolve); Model 2 probe: with `az login` as the operator, `az account get-access-token --tenant <customer-tenant-id> --resource https://graph.microsoft.com` is NOT the same identity — record instead the L2 Worker's own log for any `AADSTS700016`/`AADSTS500011` on a past Model 2 attempt (observation only; do not run a provisioning run); GitHub: `gh secret list` (names), latest `deploy-*` run logs for `AADSTS700213` (sidecar push leg); prod Dataverse: the GitHub SP `8c85a481` as an application user (it is one on dev, `[LIVE x05 §B]`).
  [ADDED by check — x03 §3 D8 and G5 residuals, stamp-only] For each stamp found by T-P2-09 step 4 (may be none, §1.3 F-8): (a) READ-ONLY: the most recent H7b / H13 run record in the L2 run store (`GET /api/runs/{id}` as the operator) → gate `h7b-secure-record-setup` outcome, any refusal code (`NoAccessEntryMissing`, replica role, users-in-unit), T7 result, and the `ADR-028 E-2 measurement` line; the stamp Dataverse: `businessunits` / `teams` / `roles` for the "Secure Record" set and `fieldsecurityprofiles(…)/systemuserprofiles_association` → the writer profiles hold only the two BFF application users (a08 §7 r20, a07 §7 r2). (b) `az ad app federated-credential list --id <stamp app>` → count vs the 20-per-app cap, and the L2 Worker log for `AADSTS70021` after that stamp's H3 (x03 G5 residual). (c) Running H7b with `secureRecordSetupDryRun = "true"` (a07 §7 r2's suggested test) is a **provisioning-handler run → LIVE CHANGE, owner-scheduled**; not part of this plan's run order — record "not run" unless the owner schedules it.
- **Record** [ADDED by check]: L2 UAMI RBAC list; admin-env app user + roles; Exchange permission id resolved + sidecar healthz body; Model 2 error code(s) from past logs (or "none attempted"); `gh secret list` names; `AADSTS700213` present Y/N; prod GitHub SP app user Y/N; per stamp: gate outcomes, FLS membership, FIC count, `AADSTS70021` Y/N.
- **Pass** [ADDED by check]: L2 UAMI holds the roles I1 lists and is an admin-env app user with `Spaarke Provisioning Registry`; sidecar healthz not `degraded`; no `AADSTS700213` in the latest run; prod GitHub SP is an app user; stamp writer FLS profiles hold exactly the two BFF app users. Everything else is recorded as a fact.
- **Confirms**: x03 §3 I1, I3, I4, I5, B9 (RBAC residual), D8 (stamp outcomes), G5 (cap + `AADSTS70021`) [ADDED by check]; x03 M-9, L-10; a08 §7 r13, r15, r16, r17, r20 [ADDED by check]; a07 §7 r2 [ADDED by check].

#### T-P3-12 — Insights Function shell inventory

- **Priority** P3 · **User** U6 · **Safety** READ-ONLY · **Steps**: the four `az` commands in a09 §7 gap-fill rows (`az functionapp show`, `az storage account show … allowSharedKeyAccess`, `az functionapp config appsettings list` names + shape, `az role assignment list` for both insights UAMIs, `az deployment-scripts list`). **Pass**: facts recorded; a Function App with no code holding KV Secrets User / Storage Blob Data Owner is x03 M-11 confirmed live. **Confirms**: x03 §3 G10 (residual); a09 D-17.

#### T-P3-13 — Host and MSAL runtime bundle on S1 (a01 §7 rows not covered by P1)

- **Priority** P3 · **User** U1 · **Surface** S1 · **Safety** READ-ONLY (logout ends the tester's own Entra session; re-sign-in afterwards)
- **Steps** (one item each, same PCF form as T-P1-11): (1) Edge with third-party cookies blocked, storage cleared → `ssoSilent` outcome (`storeAuthStateInCookie` effect); (2) two same-tenant accounts cached → `_pickAccount → null`, `loginHint: undefined` → popup or silent; (3) `useAuth().logout()` → reload → popup required? and `Received logout broadcast` in a sibling PCF iframe / `navigateTo` dialog (`BroadcastChannel('spaarke-auth-events')`); (4) nested code-page iframe: `organizationSettings.tenantId` empty on first load?; (5) count `[MSAL]` console lines at `logLevel: 3`; (6) a document form with `sprk_DocumentOperations.js` + a PCF: popup count and `msal.interaction.status` (`interaction_in_progress`?); (7) `[SpaarkeAuth] v2.0.0 initialized` + MSAL version per surface (3.x PCFs vs 4/5.x solutions), and whether deployed bundles contain the #1453 strings (`Duplicate init coalesced`, `tenant_unresolved`) — INV-8; (8) force a BFF 401 (**revoking the test user's sessions is a LIVE CHANGE — owner approval; prefer waiting for natural expiry** [CORRECTED by check]) → do the three retries send identical bearers (`authenticatedFetch.ts:55-60`); (9) a test page with an explicit CIAM authority and no `knownAuthorities` → `untrusted_authority`?; (10) `initAuth({clientId})` with no scope → `scopes: ['']` error?; (11) [ADDED by check — x03 §3 F1 last item, a02 D-24] host enforcement of `<external-service-usage enabled="false">`: open a form carrying one of the three PCFs that call the BFF/Entra but declare no domains (`CommunicationConnections`, `RegardingResolver`, `SpeDocumentViewer` `[CONFIG src/client/pcf/CommunicationConnections/CommunicationConnections/ControlManifest.Input.xml:4-5]`, inherited from a02 §3) → do the MSAL and BFF calls complete (network tab) or does the host block them?
- **Record** [ADDED by check]: one line per item (1)–(11): outcome, popup count where relevant, the console lines quoted, bundle strings found Y/N, MSAL version per surface.
- **Pass** [ADDED by check]: items recorded; a popup on (1)/(2)/(5) without a user gesture is x03 M-3 class; identical bearers on (8) confirms a01 D1 live; a bundle without the #1453 strings on (7) is INV-8 drift (a01 D7); a blocked call on (11) is a02 D-24 confirmed live.
- **Confirms**: x03 §3 F1 (all items incl. the host-enforcement item), J4; x03 M-14, R2, R4 (INV-1/INV-8); a01 §7 r2, r3, r11–r16; a02 §7 r8, r9; a02 D-24 [ADDED by check].

#### T-P3-14 — Service-identity edge cases: app-only token on user routes, keyless proof, SignalR isolation, healthz

- **Priority** P3 · **User** U6, U1 · **Safety**: healthz READ-ONLY; the app-only token test requires **minting a client-credentials token** (credential use → LIVE CHANGE, owner approval; prefer `az account get-access-token` as the operator's identity for the "no `oid`-less" variant — that is a delegated token and does not answer the question); the SignalR ping is LIVE CHANGE (benign); H13 first run is a **provisioning run — LIVE CHANGE**, owner-scheduled, not part of this plan's run order
- **Steps**: `GET /healthz/dataverse*` on dev (UAMI probe output); *(approved)* present an app-only token for `1e40baad…/.default` to a bare `RequireAuthorization()` route (`GET /api/me`) and to `POST /api/platform/keyless-proof` → expect 401/`no_caller_token`-class refusal on the user route (never a 403-with-rights) and 401 on keyless proof (role `Provisioning.KeylessProof` does not exist on the dev app, `[LIVE x04 §1 A1]`); *(approved)* negotiate SignalR as user A, connect, have a producer ping user B's `systemuserid` → A receives nothing.
- **Record** [ADDED by check]: healthz bodies; the app-only token's statuses on `/api/me` and keyless-proof with the `reason`/deny code; the SignalR run: A's received messages (expect none).
- **Pass** [ADDED by check]: `/healthz/dataverse*` healthy under the UAMI; app-only token → 401 / `no_caller_token`-class on `/api/me` (a 403 carrying rights is a FAIL — it would mean the app user's own rights were evaluated for an app-only caller, a07 §7 last row) and 401 `sdap.access.deny.keyless_proof_role`-class on keyless proof `[CODE BFF/Api/Filters/KeylessProofAuthorizationFilter.cs:46]` *(checked)*; A receives nothing.
- **Confirms**: x03 §3 A8, E6, E9, I6 (stamp half remains with H13); x03 §1.7 keyless-proof row; a05 §4 U6, §7 r12; a07 §7 r8, last row.

#### T-P3-15 — (merged into T-P2-02) anonymous / unmatched-route statuses — no separate test.

#### T-P3-16 — GitHub OIDC residuals

- **Priority** P3 · **Safety** READ-ONLY · **Steps**: `az role assignment list --assignee <sp of 8c85a481> --all` and the same for `<sp of 1e40baad>` (Contributor, Storage Blob Data Contributor, AcrPush); `gh secret list` (names only) for `AZURE_CLIENT_ID`, `NIGHTLY_*`; which app id the `deploy-bff-api.yml:199-204` login actually used in the last successful run (the run log prints the client id). **Pass**: one app named consistently; the yaml/guide disagreement (x01 #8.8) closed with the live answer. **Confirms**: x03 §3 B9 (residual); a08 §7 r16; a09 §7 r7, D-4.

#### T-P3-17 — Orphan app ids in the CIAM tenant

- **Priority** P3 · **Safety** READ-ONLY · **Steps**: after `az login --tenant 7052feba-… --allow-no-subscriptions`: `az ad app show --id ed44fb14-…`, `--id 8c60b44e-…`, `az ad sp list --filter "appId eq '…' or appId eq '…'"`. **Pass**: not found (then `sprk_DocumentDelete.js` references nothing that exists anywhere Spaarke controls — delete candidate, x03 H-5). **Confirms**: x03 §3 B6 (closes it); a09 D-18.

#### T-P3-19 — ACS (Azure Communication Services) auth state [ADDED by check — x03 §3 G9 had no test]

- **Priority** P3 · **User** U6 · **Safety** READ-ONLY (resource `show`/`list` only; **do not run `az communication list-key`** — it prints key material; `disableLocalAuth` answers the same question)
- **Preconditions**: operator `az login` in `a221a95e…` with Reader on every Spaarke subscription; T-P3-01 output for `Communication__Acs__Endpoint` per environment.
- **Steps**:
  1. `az communication list --query "[].{name:name,rg:resourceGroup,id:id}"` across the dev and demo subscriptions → does any ACS resource exist (the a06 gap-fill rows say code comments claim none)?
  2. For each: `az communication show --ids <id> --query "{disableLocalAuth:properties.disableLocalAuth,dataLocation:properties.dataLocation}"`; `az role assignment list --scope <id> --query "[].{p:principalId,role:roleDefinitionName}"` → any role for `9fd47efb…` (the template grants none, a06 #16).
  3. `Communication__Acs__Endpoint` present on any environment (from T-P3-01).
  4. If a resource exists: ACS diagnostic logs / Event Grid `ParticipantAddedToThread` payloads over 30 days → count of distinct service ACS user ids (one per BFF process per a06 gap-fill r5) — observation only.
  5. Offline (not live): the SDK overload check `new ChatClient(endpoint, TokenCredential)` is a compile-time check against the installed `Azure.Communication.Chat` package — record the constructor set from the package's reference assembly; this is a lab item, not a tenant read.
- **Record**: resource count; per resource `disableLocalAuth`, RBAC for the BFF UAMI; endpoint setting presence; service-user count; the overload set.
- **Pass**: facts recorded. "No ACS resource and no endpoint setting anywhere" closes G9 as "not deployed" (x03 R53 stays a template gap). A deployed resource with `disableLocalAuth=false` and no MI role is a06 §7 gap-fill rows 1–3 confirmed live.
- **Confirms / changes**: x03 §3 G9 (closes it); x03 R53; a06 §7 gap-fill rows 1–5.

#### T-P3-20 — Disabled `systemuser` with a still-valid token: thread read and membership lookup [ADDED by check — x03 §3 D3 third observation had no test]

- **Priority** P3 · **User** U1 (a disposable test staff account) · **Surface** S1 (BFF routes) · **Safety** **LIVE CHANGE** (disables, then re-enables, a test `systemuser` in `spaarkedev1`) — **owner approval required before running; rollback = re-enable the user and confirm `isdisabled=false`**
- **Preconditions**: a licensed test `systemuser` X that the owner is willing to disable for ~10 minutes; X is a participant on one `sprk_communication` thread; X holds a BFF bearer obtained before the disable (the token stays valid until `exp`). Code facts: the membership cross-ref excludes disabled rows `[CODE BFF/Api/Membership/MembershipEndpoints.cs:450-453]` *(checked)*; `CallerSystemUserResolver` (AI chat context) matches `azureactivedirectoryobjectid` with **no** `isdisabled` condition `[CODE BFF/Services/Ai/Context/CallerSystemUserResolver.cs:101-107]` *(checked)*; `SystemUserIdentityResolver` does filter `isdisabled eq false` `[CODE BFF/Services/Identity/SystemUserIdentityResolver.cs:223]` *(checked)*; Microsoft does not document what Dataverse returns for a disabled `MSCRMCallerID` `[CODE src/server/shared/Spaarke.Dataverse/DataverseImpersonation.cs:33-35]` *(checked)*.
- **Steps**:
  1. As X, obtain a BFF token (T-P1-11 mechanics) and note `exp`.
  2. Owner disables X (`PATCH systemusers(X) {isdisabled:true}` or the admin centre) — record the time.
  3. With the held token: `GET /api/users/me/memberships/sprk_matter` → expect 401 (disabled row excluded); `GET /api/communications/threads/{id}/messages` for X's thread → status and row count (the a07 §7 gap-fill observation: a Dataverse refusal or empty rows, NOT rows); `GET /api/office/search/entities?q=xx` → status (impersonated route).
  4. App Insights: the impersonation error code (`0x8004A110` vs a different Dataverse error) and any `[WF-AUTH]` / `CallerSystemUserResolver` line.
  5. Owner re-enables X; confirm with a read-only `systemusers(X)?$select=isdisabled` → `false`.
- **Record**: three statuses, row count, error codes, disable/enable timestamps.
- **Pass**: membership 401; thread read returns **no** rows (refusal or empty); search 403. **Fail**: thread rows returned to a disabled user's token — then `CallerSystemUserResolver`'s missing `isdisabled` filter is a live defect (a07 T96), to add to x03 §4.
- **Confirms / changes**: x03 §3 D3 (third observation: "disabled users still resolving via `CallerSystemUserResolver`"); a07 §7 gap-fill row "DISABLED systemuser"; a07 §7 r10 / a04 §7 r8 (thread read).

---

## 3. Run order

Run in this order; stop and record at the first P1 result that contradicts the design assumption (a home-tenant `tid` on every surface, or an OBO failure for the guest), because it changes the preconditions of everything after it.

| Step | Test | Safety | Why here |
|---|---|---|---|
| 1 | T-P1-11 | READ-ONLY | U1 control claims; confirms the harness (console, App Insights logger) before any guest run. |
| 2 | T-P1-10 | READ-ONLY | Records the `sprk_isexternal` precondition for every later U2 observation. |
| 3 | T-P1-12 | READ-ONLY | Decides whether impersonated routes (Office search) can work at all. |
| 4 | T-P1-01 | READ-ONLY | The first guest token — settles the S1 tenant/`acct`/`idp` shape. |
| 5 | T-P1-02 | READ-ONLY | BFF acceptance + Dataverse OBO for the same session. |
| 6 | T-P1-08 | READ-ONLY (core) | Graph / SPE OBO vs app-only for the same session. |
| 7 | T-P1-03 | READ-ONLY | Add-in Diagnostics on four hosts. |
| 8 | T-P1-04 | READ-ONLY | Teams tab, both sessions; produces the home-tenant token for step 10. |
| 9 | T-P1-05 | READ-ONLY | Browser workforce realm, three sessions. |
| 10 | T-P1-07 | READ-ONLY | Issuer / audience behaviour using the tokens from 8–9. |
| 11 | **T-P1-06** | **LIVE CHANGE** | Only after 8 has shown which path works; needs approval + sideload. |
| 12 | **T-P1-09** | **LIVE CHANGE** | Only after 7; creates test data and sends one e-mail. |
| 13–22 | T-P2-01 … T-P2-10 | READ-ONLY except T-P2-05 steps 3–4 | CIAM first (independent of U2 results), then Teams/U1, U3, add-ins, Copilot, Reporting, L2, consent. |
| 23–41 | T-P3-01, -18, -02, -03, -04, -05, -06, -07, -08, -09, -10, -11, -12, -13, -14, -16, -17, -19, **-20** | READ-ONLY except the flagged steps; **T-P3-20 is a LIVE CHANGE** (disables a test user) and runs last, with approval | Inventory; T-P3-01 first because several later tests read its output; T-P2-09 step 4 before T-P3-11 (stamp list). [CORRECTED by check: was 23–38 / 17 tests] |

After the P1 block, re-read x03 §1.1–§1.4 column U2 and rewrite each UNTESTED cell with the observed status; after the full run, re-read x01 #1, #8.5, #8.8 and x03 §3's "Total" paragraph.

## 4. Accounts, environments and builds needed

| Need | Detail | Used by |
|---|---|---|
| **U1** | A Spaarke staff member with a licensed `systemuser` in `spaarkedev1`; ideally one who holds the `Admin` app role on `1e40baad` (the only app role that exists, `[LIVE x04 §1 A1]`) and one who does not. | T-P1-11, -05, -06, -07; all P2/P3 user tests. |
| **U2** | A B2B guest in `a221a95e…` whose home account is in a real second tenant (owner-controlled), with: a licensed enabled `systemuser` in `spaarkedev1`; `azureactivedirectoryobjectid` known; `sprk_isexternal` state known (T-P1-10); Read on one `sprk_document`; **no** SPE container role; the dev add-in sideloaded in the home tenant's Outlook/Word; the Teams tab installed in the home tenant's Teams. | T-P1-01 … T-P1-09. |
| **U3** | A member of `a221a95e…` with no `systemuser` and no licence (`acct=0`). Because dev lists its own tenant, this account WILL bind a contact on first `/me` call (T-P2-05 step 3 is a live change). | T-P2-05. |
| **U1 (disposable)** [ADDED by check] | A licensed test staff `systemuser` the owner will disable for ~10 minutes and re-enable; participant on one `sprk_communication` thread. | T-P3-20. |
| **U4** | A test partner in the CIAM tenant `7052feba…` with a `contact` whose `sprk_externalobjectid` = its `oid`. | T-P2-01, -02. |
| **Operator** | `az login` as the operator's own account in `a221a95e…` (read scopes on app registrations, App Service, Key Vault, role assignments); `az login --tenant 7052feba-… --allow-no-subscriptions` for the CIAM reads; Dataverse MCP / Web API read access to `spaarkedev1` (and the admin env for T-P3-11); App Insights reader on `spaarke-bff-dev`; Exchange Online PowerShell read for T-P3-07; `gh` with repo read. No service-principal credentials are used except where a step is marked LIVE CHANGE (credential use). | all `az`/query steps. |
| **Environments** | `spaarkedev1.crm.dynamics.com`; `spaarke-bff-dev` (production slot as read; staging slot unread); `icy-desert-0bfdbb61e.6.azurestaticapps.net` (dev add-in with Diagnostics ON); `green-dune-0c4f1221e.7.azurestaticapps.net` (external SPA / Teams tab); CIAM tenant `spaarkeextid.ciamlogin.com`; the guest's home tenant (Outlook, Word, Teams); optionally the demo BFF and one L2-provisioned stamp for the "on a stamp" re-reads (x03 M-2). | — |
| **Builds** | No new build for P1 except T-P1-06 (a manifest-only Teams package variant). If T-P1-05 shows the SPA cannot mint a Spaarke-tenant token without a picker, a dev build with the workforce authority pinned to `https://login.microsoftonline.com/a221a95e-…` (seam `src/client/external-spa/src/auth/msal-config.ts:147-159`) becomes a follow-up — that is a deploy to `green-dune`, a LIVE CHANGE outside this plan. | T-P1-06; follow-up. |
| **Browser profiles** | Edge: (a) fresh, home-tenant session only; (b) Spaarke-tenant session only; (c) both; (d) third-party cookies blocked. | T-P1-01, -05, T-P3-13. |

## 5. Counts

- **Settled by live reads** [CORRECTED by check]: 8 x03 §3 items fully settled (B5, B6 [CIAM-tenant lookup is a P3 residual], B8, C2, C4 [`AZURE_CLIENT_ID` residual], D1 [dev], G5 [audience residual], plus x01 #8.1, #8.6, #8.9, #8.10, #8.11 and #8.4 [dev app]) and 14 partly settled (B1 [`requiredResourceAccess` ids], B2 [scope ids], B3 [Graph consent], B7, B9, B10, C1 [`AgentAppId`, `requestedAccessTokenVersion`], C3 [two `*WritesEnabled` keys], C5/C7/C8, D2, G1, G7, G10); 8 new record facts (§1.3 F-1 … F-8). Was "21 fully settled, 6 partly" — the check moved B1, B2, B3, C1 and C3 to "partly" because x04 prints counts/ids/masked values for the residual sub-items.
- **Tests**: P1 = 12 (T-P1-01 … T-P1-12), P2 = 10 (T-P2-01 … T-P2-10), P3 = 19 (T-P3-01 … T-P3-14, -16, -17, -18, **-19, -20** [ADDED by check]; T-P3-15 merged into T-P2-02). Total **41** [CORRECTED by check: was 39].
- **Tests that require a live change (owner approval)** [CORRECTED by check — complete list]: T-P1-06 (sideload), T-P1-08 step 5 (optional chat export, benign), T-P1-09 (document + e-mail), T-P2-02 step 5 (OBO lab with the BFF credential — NOT RUN by default), T-P2-05 steps 3–4 (contact bind, chat session), T-P3-01 `Azure.Identity` log-level change (only if needed), T-P3-03 optional KPI save, T-P3-07 step 4 fallback (owning-app credential use — the BFF admin route is tried first), T-P3-08 OpenAI key clear, T-P3-11 (c) H7b dry-run (owner-scheduled, not in run order), T-P3-13 item 8 (session revoke — prefer natural expiry), T-P3-14 (app-only token mint, SignalR ping; H13 run is owner-scheduled), **T-P3-20** (disable/re-enable a test user). Everything else is read-only.
- **Deliberately not scheduled**: a07 §7 "Dataverse behaviour for `CallerObjectId` with a foreign-tenant oid" — the area file itself says "not needed while all call sites use `ApplyAsSystemUser`"; recorded here so the omission is visible [ADDED by check].

---

## Check log

> Independent check, 2026-10-09, against the same evidence set (a01–a09, x01–x05) and the code root at `8a9ecaac1`. Every change above is marked `[CORRECTED by check]` or `[ADDED by check]`. Nothing was run live; no secret value was read or written. `Compose__Webhook__ClientState` stays redacted.

### 1. "Settled by live reads" — rows moved from settled to partly settled

| Row | Why it was not fully settled by x04/x05 | Where the residual went |
|---|---|---|
| B1 | x04 prints `requiredResourceAccess` as counts (Graph 25, Exchange 1, SharePoint 2), not ids. Everything else in B1 is printed. | T-P3-07 step 3 |
| B2 | x04 prints pre-authorized client ids, not the scope ids (`delegatedPermissionIds`); "2 permissions on `1e40baad`" is a count. B2 asked "for `user_impersonation`". | T-P2-10 step 1 |
| B3 | Graph permission names and consent (`oauth2PermissionGrants`) are not in x04; only a count of 3. | T-P2-10 step 1 |
| C1 | `AgentToken__AgentAppId` is masked; `api.requestedAccessTokenVersion` on `1e40baad…` was not printed. | T-P3-01; T-P1-07 step 0 |
| C3 | `IdentityLink__Reconciliation__WritesEnabled` and `ExternalAccess__*WritesEnabled` are not in the read-out and its key filter is undocumented. | T-P3-01 |

All other settled rows (B5, B6, B8, C2, C4, D1, G5, D2-part, G1-part, G7-part, G10-part, C5-part) were re-read against x04 §1/§2/§3/§4/§5/§6 and x05 §A/§B line by line and hold. One new live fact was added (§1.3 F-8): `spaarke-bff-api-prod` exists and is `AzureADMyOrg`; no `spaarke-bff-api-<customerId>` registration matched by display name at capture time.

### 2. Coverage — x03 §3 rows (75) and area §7 items

- **Every x03 §3 row now has a test or a settled row.** Three had none: **G9** (ACS) → new T-P3-19; **D7** (session-cookie env-var GETs) → T-P3-02; **I8** (stamp `AzureAd__*` shape / T7 KV-reference skip) → T-P2-09 step 4. **G8** (`DefaultAzureCredential` member, x01 #6) was only nominally covered — T-P3-04's "(G8)" is the x03 §1 global-dependency label, not §3 row G8 — so G8 was added to T-P3-01 and the three ambiguous labels (T-P1-11 "G1", T-P1-01 "G9", T-P3-04 "G8") were disambiguated to "x03 §1".
- **Half-rows filled**: E7 workforce-token 404 half → T-P2-02 step 1b; C8 stamp half (`acct` on pre-T255 apps, settings on existing stamps) → T-P2-09 step 4; D3 third observation (disabled user still resolving) → new T-P3-20; D8 stamp run outcomes → T-P3-11; G5 cap + `AADSTS70021` → T-P3-11; A2 cross-reference added to T-P1-01; A7 Dataverse-OBO half → T-P3-07 step 3; F1 host-enforcement item (a02 D-24) → T-P3-13 item 11.
- **Area §7 items not reachable through an x03 row** were added where found: a03 §7 r7 (CIAM scheme not applied to `/api/office/*`) → T-P2-02; a06 §7 r7 (CIAM token as `UserAssertion` lab) → T-P2-02 step 5, flagged credential use / NOT RUN by default; a06 §7 r8 (U3 Dataverse status + `CallerRecordAccessProbe`) → T-P2-05; a08 §7 r6 Power BI admin-portal facts → T-P2-08; a08 §7 r7 `wids` → T-P1-11; a08 §7 r9 app users per environment and a01 §7 r8 origin-per-org → T-P3-03; a08 §7 r12 `Rag:ApiKey` versions → T-P3-09; a09 §7 r11 `mi-bff-api-demo` → T-P3-01; a07 §7 r8's read-only BFF admin route for SPE grants → T-P3-07 step 4 (now tried before the credential-use fallback). a07 §7 "`CallerObjectId` with a foreign-tenant oid" is deliberately unscheduled (the area file says not needed) and is now stated in §5.

### 3. Executability

- Added missing **Record / Pass** lines to T-P3-03, -08, -10, -11, -13, -14 and **Preconditions** to T-P2-03. Every test now names accounts, concrete steps, what to record and a pass/fail rule.
- T-P1-07 gained step 0 because the bare-GUID `aud` case depends on `requestedAccessTokenVersion`, which x04 did not print; the test now records the CLI token's actual `aud` instead of assuming it.

### 4. Live-change flags

Explicit "owner approval" wording added where the §0.3 class implied it but the test did not say it: T-P1-08 step 5, T-P2-05 steps 3–4, T-P3-03 KPI save, T-P3-13 item 8 (session revoke; prefer natural expiry), T-P3-01 log-level change (conditional), T-P3-11 (c) H7b dry-run (owner-scheduled), T-P2-02 step 5 (NOT RUN by default). New T-P3-20 is a LIVE CHANGE with rollback stated. §5's live-change list was rewritten to be complete.

### 5. Code citations re-opened for this check (path:line at `8a9ecaac1`)

Confirmed as cited: `WorkforceIdentityOptions.cs:176-191,199-230`; `CallerPrincipalResolver.cs:386-406`; `WorkforcePrincipalResolver.cs:163,199,206`; `MembershipEndpoints.cs:435-459` (`isdisabled` at `:450-453`); `AuthorizationModule.cs:47-49,111-126,375-383`; `KeylessProofAuthorizationFilter.cs:46,49`; `OfficeEndpoints.cs:1210`; `DataverseAccessDataSource.cs:284,464`; `DataverseImpersonation.cs:33-35,44-47`; `ContactIdentityBinder.cs:163-186,190-202`; `tokenDiagnostics.ts:15-23,62-70`; `deploy-office-addins.yml:66-69,76-82,83-87`; `manifest.json:41-44`; `Set-ExternalFlagForB2BGuests.ps1:9-14,31-36`; `ContainerTypePermissionEndpoints.cs:37`; `IdentityConfigurationValidator.cs:60-65,88-95`; `CallerSystemUserResolver.cs:101-107` (no `isdisabled` condition — a07 T96 holds); `SystemUserIdentityResolver.cs:223`; grep for `RequiredScope|RequireScope` in `Sprk.Bff.Api` → none.
**One citation was wrong and is corrected in three places**: the per-request claim logger is `MiddlewarePipelineExtensions.cs:121-158` (log call `:140-146`, `scp` read `:146`), not `:88-94` (the exception handler's CORS re-apply, a05 §8 item 2) and not an "auth-failure logger".

### 6. Counts after the check

Fully settled 8 · partly settled 14 · record facts 8 · tests 41 (P1 12, P2 10, P3 19) · tests or steps needing owner approval 13 (listed in §5).
