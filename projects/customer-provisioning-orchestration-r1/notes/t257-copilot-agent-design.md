# T257 — per-customer Microsoft Copilot agent: design note (step 1)

> Researched 2026-10-08 (researcher agent). Sources are Microsoft Learn pages, with their `ms.date` or update date, plus
> the Agents Toolkit schema in `OfficeDev/microsoft-365-agents-toolkit`. Microsoft renamed "Microsoft 365 Copilot" to
> **Microsoft Copilot** and "Copilot Chat" to **Microsoft Copilot Chat** (Learn note, Oct 2026); this note uses "Copilot".
> No live change was made.

## 0. Headline

**Agents published in Spaarke's tenant catalog don't reach customer staff.** Microsoft documents no way for a B2B guest
to use Copilot, or a Copilot agent, in the tenant where they are a guest. So the per-customer agent has to be installed
in the customer's **home** tenant by the customer's IT. That is the same path as the Word/Outlook add-ins (owner
2026-10-07). The agent then signs the user in to **Spaarke's** tenant (OAuth 2.0 + PKCE) and calls only that
customer's BFF. Each customer's package differs in four values. One step per customer can't be automated today: creating
the OAuth "auth config". Microsoft offers no app-only or documented REST API for it.

## 1. Verified facts

**What exists today (repo)**
- `src/solutions/CopilotAgent`:
  - a declarative agent (DA schema v1.2) with one OpenAPI action (plugin v2.2) and 28 functions;
  - the plugin's auth is `OAuthPluginVault` with a hard-coded dev `reference_id`, which base64-decodes to
    `{Spaarke tenant a221a95e}##{guid}`;
  - the OpenAPI file uses the Spaarke tenant's authorize and token URLs and the dev scope
    `api://1e40baad…/access_as_user`;
  - the app manifest is `devPreview` and declares the permissions `identity` and `messageTeamMembers`. It has no
    `webApplicationInfo` and no bot.
- `scripts/Deploy-CopilotAgent.ps1` rewrites the server URL, version and `validDomains`. It does **not** rewrite the
  scope, the authorize/token URLs or `reference_id`. Its steps 1–2 configure Dataverse Copilot glossary/descriptions,
  which is a separate concern.
- H3 already has `EntraAppRegOptions.PreAuthorizedClientAppIds` (T240a), set per entry by Bicep.
- The BFF accepts `api://{appId}` audiences. It logs `azp`/`appid` but doesn't restrict the client app.

**Packaging and schemas**
- A DA ships inside a Microsoft 365 app package (`copilotAgents.declarativeAgents`, manifest ≥ v1.19).
  - The latest numbered app manifest is **v1.30** (Aug 2026). Source: [schema index](https://learn.microsoft.com/en-us/microsoft-365/extensibility/schema/), checked 2026-10-08.
  - The latest DA schema is **v1.8**: instructions ≤ 8,000 chars, 1–10 actions, ≤ 12 conversation starters.
    Source: [DA schema 1.8](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/declarative-agent-manifest), ms.date 2026-09-30.
  - The plugin schema is v2.4.
- `devPreview`-only features fail validation in a package that targets a numbered schema.
  Source: [OAuth config](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-authentication-oauth), ms.date 2026-08-28.
- Tooling: Microsoft 365 Agents Toolkit (`atk`). Work IQ Dev Tools (`wiqd`) is a newer **preview** route that adds
  validation (`wiqd agent validate`).
  Source: [prerequisites](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/prerequisites), ms.date 2026-09-30.

**Licensing (who can run the agent)**
- Copilot Chat is free for users with an eligible Microsoft 365 plan, including Business Basic.
  Source: [Manage Copilot Chat](https://learn.microsoft.com/en-us/copilot/manage), ms.date 2026-10-01.
- The capability table shows **"Custom actions" available in Copilot Chat with no Copilot licence and no
  usage-based billing**. SharePoint, Dataverse and connector knowledge need metering or a licence. Email, People and
  Teams knowledge need a licence. Source: [prerequisites](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/prerequisites).
- Agents grounded only in instructions or public data cost nothing extra; tenant-data grounding is metered.
  Source: [cost considerations](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/cost-considerations), ms.date 2026-09-30.
- **Consequence:** an agent that has only instructions plus our OpenAPI action works for any staff member with an
  eligible licence **in their home tenant**. Nobody needs a Copilot add-on and no metering is involved. This holds only
  while the DA declares no knowledge capabilities.

**Guests (the blocker for Spaarke's catalog)**
- Copilot licences can't be assigned to B2B guests, and guests get a "no license" error on agents. The recommended
  workaround is to use Copilot from the user's own tenant. This is a Microsoft Q&A moderator answer, not a Learn page.
  Source: [Q&A 5847621](https://learn.microsoft.com/en-us/answers/questions/5847621/can-external-b2b-guest-accounts-be-assigned-a-copi), 2026-04-02.
- The only documented cross-tenant Copilot access is "B2B member access" in Teams **meetings/channels**:
  - it applies to multitenant-organization (MTO) **members** using their home licence;
  - "It isn't available to external or federated users";
  - it doesn't cover agents.
  Source: [copilot-mto](https://learn.microsoft.com/en-us/microsoftteams/copilot-mto), updated 2026-02-04.
- The Copilot requirements page names no guest path. It requires an Entra account plus a qualifying licence.
  Source: [requirements](https://learn.microsoft.com/en-us/microsoft-365/copilot/microsoft-365-copilot-requirements), ms.date 2026-10-05.

**Authentication for the API action**
- Supported schemes for API plugins: Entra SSO, OAuth 2.0 auth code, API key and none. Each uses an "auth config" stored
  in the Microsoft Enterprise token store; the manifest's `reference_id` points to it.
  Source: [plugin auth](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api-plugin-authentication), ms.date 2026-09-30.
- **OAuth 2.0** ([OAuth config](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-authentication-oauth)):
  - PKCE is on by default. "To avoid client secrets entirely, register a public client … single-page application
    platform … and let PKCE secure the code exchange."
  - Redirect URI: `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect`.
  - "Restrict usage by org": choose **Any Microsoft 365 organization** "when the plugin must work across tenants".
  - "Restrict usage by app": use **Any Teams app**.
  - Include `offline_access` in the scope to get refresh.
  - In Agents Toolkit the action is `oauth/register` with `targetAudience: HomeTenant|AnyTenant`,
    `applicableToApps: AnyApp`, `isPKCEEnabled`, and an optional `clientSecret`
    ([yaml schema v1.11](https://raw.githubusercontent.com/OfficeDev/microsoft-365-agents-toolkit/dev/packages/fx-core/resource/yaml-schema/v1.11/yaml.schema.json)).
  - `oauth/register` never rewrites an existing record. Use `oauth/update` to change one. Delete only in the Teams
    developer portal.
- **Entra SSO** ([SSO config](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-authentication-entra-sso), ms.date 2026-07-14):
  - add an extra identifier URI (`api://auth-…`) to the API's app;
  - pre-authorize the token store client `ab3be6b7-f5df-413d-ac2d-abf1e3fd9c0b`;
  - the API must accept the new audience.
  - The token comes from the tenant where Copilot runs. For a customer's staff that is their **home** tenant, not their
    guest identity in Spaarke's tenant. That is wrong for our BFFs, which are single-tenant and keyed on the guest `oid`.
- The scope and base URL belong to the auth config. So **one auth config per customer BFF** is required, because the
  scope (`api://{customerBffAppId}/user_impersonation`; corrected in §5.2) and the host differ.

**Publishing and assignment APIs**
- Graph `POST /appCatalogs/teamsApps` (publish) and `POST …/{id}/appDefinitions` (update) are **delegated only**.
  "Application: Not supported." The caller needs `AppCatalog.ReadWrite.All` and Teams admin.
  Sources: [publish](https://learn.microsoft.com/en-us/graph/api/teamsapp-publish?view=graph-rest-1.0) and [update](https://learn.microsoft.com/en-us/graph/api/teamsapp-update?view=graph-rest-1.0), both updated 2026-06-19.
  Each catalog app needs a unique manifest id.
- Package Management API (`/beta/copilot/admin/catalog/packages`) can list, block, reassign and PATCH
  `allowedUsersAndGroups` / `acquireUsersAndGroups`. It **can't upload**. It is delegated only
  (`CopilotPackages.ReadWrite.All`), beta, and needs a **Microsoft Agent 365 licence**.
  Sources: [overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/admin-settings/package/overview), ms.date 2026-08-27; [update](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/admin-settings/package/copilotpackagedetail-update), updated 2026-10-02.
- The Agents Toolkit CI template signs in to **Azure** with a service principal, but M365 actions need a signed-in M365
  account. Source: [CI/CD templates](https://learn.microsoft.com/en-us/microsoftteams/platform/toolkit/use-cicd-template).
  No documented REST API creates auth configs; only `atk` (delegated), the `wiqd` skill (MCP only) and the developer
  portal UI do.
- Admin upload: Microsoft 365 admin center → Agents → **Upload custom agent**, then assign users or groups.
  Source: [upload agents](https://learn.microsoft.com/en-us/microsoft-365/copilot/agent-essentials/agent-lifecycle/agent-upload-agents), updated 2026-09-09.
  Built agents are managed in **Integrated apps**.
  Source: [govern agents](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/manage), ms.date 2026-09-30.
- Updates: the admin uploads the new version.
  - It auto-updates for users unless permissions, `webApplicationInfo`, a bot or a messaging extension change; then each
    user must consent.
  - Existing assignment policies carry over.
  Source: [apps update experience](https://learn.microsoft.com/en-us/microsoftteams/apps-update-experience), ms.date 2026-06-01.
- Catalog size: **no documented limit** on custom apps or agents per catalog. None was found in any page above.

## 2. Blockers and unknowns, stated plainly

1. **BLOCKER for the task's premise:** customer staff are guests in Spaarke's tenant, so an agent published only in
   Spaarke's catalog is unusable for them. Microsoft documents no path, and its staff answer says no. The Copilot app
   signs in to the user's home tenant. That it has no guest "switch organization" path is my inference, not a
   documented statement.
2. **Not automatable app-only:** creating the per-customer OAuth auth config. It needs a delegated M365 sign-in (`atk`
   or the developer portal). Publishing into a customer's tenant is the customer admin's act in any case.
3. **Unknown, settled by the live test:**
   - **(a)** Does the Teams token store redeem a code for a **SPA-platform** public client? Microsoft's doc says yes. The
     risk is Entra's cross-origin rule for SPA redemption (AADSTS9002327). The fallback is a "Mobile and desktop" public
     client with the same redirect URI. Note that SPA refresh tokens last 24 h, so users would sign in again daily.
   - **(b)** Does an "Any Microsoft 365 organization" auth config created in Spaarke's tenant work when the agent is
     installed in Dewey Cheatham's tenant?
   - **(c)** Is the guest's sign-in to Spaarke's authority from inside home-tenant Copilot clean? It may be affected by
     Conditional Access and cross-tenant outbound settings.
4. **Side effect to accept:** the existing guide's "Copilot side pane in the model-driven app" path runs in Spaarke's
   tenant. By the same licensing logic, guests probably can't use it. Internal licensed users still can.
5. The current package is `devPreview` and has unneeded `permissions`. It must move to manifest v1.30 + DA v1.8 +
   plugin v2.4 before any customer uploads it.

## 3. Recommended design (one)

**Where it runs.** In each customer's **home** tenant, installed by the customer's IT. This mirrors the add-ins (T240,
owner 2026-10-07). Spaarke's catalog holds only agents for Spaarke's own environments (dev/demo/internal), scoped to
Spaarke staff. No customer's agent goes into Spaarke's catalog.

**One package template, rendered per customer.**
- CI builds a template package from `src/solutions/CopilotAgent` and publishes it as a provisioning artifact. It is
  never hand-built (the SpaarkeMaster rule, applied by analogy).
- The template contains:
  - manifest v1.30, with no `permissions`, no `webApplicationInfo` and no bot (avoids the AADSTS700016 trap seen with
    the add-in on 2026-10-08);
  - DA v1.8 with **no knowledge capabilities**, which keeps it free for Copilot Chat users;
  - plugin v2.4.
- Four values differ per customer and are all read from the registry row (the BFF app id needed a new column, §5.3):

  | Value | Source |
  |---|---|
  | Manifest `id` | Deterministic UUIDv5 of `customerId`, so it is stable across versions |
  | OpenAPI `servers[0].url` | Customer BFF base URL |
  | OpenAPI scope | `api://{customerBffAppId}/user_impersonation` (corrected from `access_as_user`, §5.2) |
  | Plugin `auth.reference_id` | The customer's auth config id |

- The authorize and token URLs are Spaarke's tenant for all customers (Model 1).

**How it binds to that customer's BFF only.**
- **One shared client app**, created once by the operator in Spaarke's tenant: "Spaarke Copilot Agent".
  - It is a public client with the redirect `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect`, PKCE and
    **no secret**.
  - It has a one-time admin consent for `openid profile offline_access`.
- **H3** pre-authorizes this client on every customer BFF app. This is config only: add the client's id to the existing
  `PreAuthorizedClientAppIds` list. So no user ever sees a consent prompt for the BFF scope.
- **One auth config per customer:**
  - OAuth 2.0, PKCE, Any tenant, Any app;
  - base URL = that customer's BFF;
  - scope = that BFF's `user_impersonation` plus `offline_access` (corrected from `access_as_user`, §5.2).
  - Its token can only carry that BFF's audience, so a package can't reach another customer's BFF.
- No Entra SSO, because SSO tokens come from the home tenant (§1).
- Sign-in lands on Spaarke's authority. The guest gets `tid` = Spaarke and `acct` = 1, the same identity the add-ins use.
  Users who are not guests in Spaarke's tenant fail at sign-in.

**Who can see it, and who can use it.**
- **Visibility** is the customer IT's assignment in their admin center (users or groups in **their** tenant).
  `sprk-{customerId}-users` lives in Spaarke's tenant and can't be used there.
- **Use** is enforced by Spaarke:
  - only invited guests can get a token;
  - the stamp's Dataverse security group and roles decide the data, as for every other client.
- The onboarding guide tells customer IT to assign the agent to the same staff they had invited.

**Updates.**
- A release that changes the agent bumps the template version. The operator re-renders the packages for all active
  customers (a script loop over the registry) and sends them to each customer's IT, who upload them.
- Users auto-update because no permission or `webApplicationInfo` change is involved.
- Keep the package thin: tool behaviour lives in the BFF. Re-issue the package only when instructions or the OpenAPI
  surface change.
- Decommission: customer IT removes the app, and the operator deletes the auth config in the developer portal (the only
  delete path).

**Automated vs operator steps.**

| Step | Who | When |
|---|---|---|
| Create the "Spaarke Copilot Agent" client app + admin consent; add its id to `PreAuthorizedClientAppIds` (Bicep) | Operator | Once per platform |
| Pre-authorize the client on the customer BFF app | **H3, automated** | Every run |
| Create the customer's auth config (`atk oauth/register`, delegated, idempotent by name `spaarke-copilot-{customerId}`); store its id in the registry | Operator, in a `/provision-environment` manual gate after Ready | Once per customer |
| Render `spaarke-copilot-{customerId}-{version}.zip` from the CI template + registry | **Script in the skill, automated** | Every release that changes the agent |
| Upload + assign in the customer tenant | Customer IT | Once, then per update |

There is no new L2 handler. The render is pure templating, done operator-side right after the delegated step that
produces its last input, so a handler would only add a resume mechanism. The one new piece of data is the auth config
id. It goes in the registry as a column, or on the run record if the owner prefers (Q5).

**Live test that proves it.** Use Dewey Cheatham (Business Basic means Copilot Chat is eligible), the guest
`ralph@deweycheatham`, and the dev or demo BFF.
1. Render the package for that BFF. The Dewey admin uploads it via Agents → Upload custom agent, assigned to Ralph only.
2. Ralph opens Copilot in his home tenant → Spaarke AI → "What are my overdue tasks?" → signs in.
   **Pass:** the BFF log shows `tid` = Spaarke, `acct` = 1, `oid` = `bc596ecd…` and `azp` = the shared client, and real
   data comes back.
3. **Negative test 1:** `admin@deweycheatham`, who is not a guest, can't get a token (AADSTS50020-class failure).
4. **Negative test 2:** a guest without a Dataverse user on that stamp gets 403.
5. **Negative test 3:** the token's `aud` is only that BFF's app, which the BFF log shows.
6. **Update test:** bump the version and re-upload. Ralph gets it with no prompt.
7. Record which public-client platform worked (SPA or Mobile/desktop) and how long the refresh lasts.
8. Hardening check: register the auth config restricted to the customer's derived manifest id (`applicableToApps`
   set to that app, not `AnyApp`). If sign-in and calls still work, make it the default in the gate (§5.2 K4).

## 4. Questions for the owner (with recommended answers)

1. **Install location.** May the per-customer agent be installed by customer IT in their home tenant (like the add-ins),
   instead of in Spaarke's catalog? *Recommended: yes. It is the only documented path that works for guests.*
2. **Auth shape.** May every customer use OAuth 2.0 + PKCE through one shared, secret-free public client pre-authorized
   by H3, with one auth config per customer, and no Entra SSO? *Recommended: yes.*
3. **One human step per customer.** May "create the auth config" be a delegated operator gate after Ready? It is an
   exception to the no-human-interaction end state until Microsoft ships an API. *Recommended: yes. Re-check `wiqd` and
   Graph at each refresh.*
4. **Spaarke's own environments.** Should Spaarke's catalog carry only the dev/demo/internal agents, published by an
   operator script with delegated Graph `AppCatalog.ReadWrite.All` and scoped to a Spaarke staff group?
   *Recommended: yes. No customer agents go into Spaarke's catalog.*
5. **Where to keep the auth config id.** *Recommended: a registry column on `sprk_dataverseenvironment`, because updates
   and decommission need it and no documented API can look it up. The manifest id is derived, so it needs no column.*
6. **Release cadence.** Is it acceptable that agent changes reach customers only when their IT re-uploads?
   *Recommended: yes. Keep the package thin and re-issue only when instructions or the OpenAPI surface change.
   Marketplace listing can't carry per-customer values.*
7. **MDA side pane.** Do we accept that guests probably can't use the agent in the model-driven app's Copilot pane
   (Spaarke's tenant), and that only internal users can? *Recommended: yes. Document it, and test it once in the live
   test as an optional check.*

## 5. Owner acceptance and alignment check (step 2, 2026-10-09)

### 5.1 Acceptance

On 2026-10-09 the owner accepted the §3 design and all seven §4 recommended answers: *"if these align with Copilot
use and our access then ok"*. The condition is checked in §5.2. It holds, with one corrected value. The task's
dependency on T240c is waived for the code: nothing in §3 needs the T240c directory.

### 5.2 Alignment with ADR-028 and `.claude/constraints/auth.md`

| Rule | What §3 does | Result |
|---|---|---|
| A4 / project rule: no secret or certificate on a confidential client; none on `bfac7f6e` | The "Spaarke Copilot Agent" client is a **public** client using PKCE, with no secret and no certificate. Nothing is added to `bfac7f6e` or to any customer BFF app. The BFF's own OBO keeps MI-FIC. | Aligned |
| MUST use a tenant-specific authority, never `common`/`organizations` | The auth config's authorize/token URLs are `login.microsoftonline.com/{Spaarke tenant}/oauth2/v2.0/…`. The renderer refuses any tenant value that isn't a GUID. "Any Microsoft 365 organization" is the Teams token store's setting for which organizations may use the auth config. It is not the Entra authority. | Aligned |
| auth.md MUST: BFF scope `api://{APP_ID}/user_impersonation`; MUST NOT: friendly scope names | §1/§3 said `access_as_user`, copied from the dev package (dev app `1e40baad`). H3 exposes **only** `user_impersonation` on a customer BFF app (`GraphAppRegistrationProvisioner`). H3 pre-authorizes clients **on that scope** (T240a). The BFF's `/api/config` publishes `api://{id}/user_impersonation`. With `access_as_user` every customer sign-in would fail with an unknown scope. | **Corrected** to `api://{customerBffAppId}/user_impersonation`, with `offline_access` in the auth config. Path C (comply). This changes a value, not any owner decision. |
| Inbound validation (Microsoft.Identity.Web, `aud`), audit enrichment (`oid`, `appid`/`azp`, `tid`) | The token is issued by Spaarke's tenant to the guest, with `aud` = that customer's BFF app. The BFF is unchanged. The live test reads `azp` from the existing enrichment. | Aligned |
| Client-side MUSTs (`@spaarke/auth`, `PublicClientApplication` only inside it, D-AUTH-7) | These govern Spaarke client code. The agent has none: Microsoft's token store holds the tokens and Copilot calls the API. The dev agent already works this way. | Not applicable (no deviation) |
| A1/A3: CIAM external users never reach user-identity Copilot features (E-3 boundary) | The agent serves customer **staff**, who are Model 1 workforce B2B guests (D2). External contacts never get it. | Aligned |
| "Our access": Model 1 users are guests in `sprk-{customerId}-users`; the BFF is single-tenant (Spaarke) | Sign-in lands on Spaarke's authority (`tid` = Spaarke, `acct` = 1), the same identity as the Word/Outlook add-ins (owner 2026-10-07). Dataverse roles on the stamp decide the data. The UAC-r2 workforce member test (`WorkforceIdentity`, `acct`) belongs to the external-access contact-binding plane. The agent calls only core endpoints (`/api/ai/*`, `/api/v1/documents`, `/api/v1/events`, `/api/workspace/*`, `/api/agent/*`, `/api/me`), so the open cross-project question about that test does not touch the agent. | Aligned |
| Consent | H3 pre-authorizes the client on `user_impersonation` (the existing `PreAuthorizedClientAppIds`). The operator grants a one-time admin consent for `openid profile offline_access` on the client in Spaarke's tenant. No user sees a prompt. | Aligned |

`AgentToken:*` / `AgentTokenService` are not on this path. No endpoint consumes them, so a stamp needs no `AgentToken`
settings. Their comment says `AgentAppId` validates incoming tokens, but no code does. That is filed as ISS-017.

**Known limit (K4), with a test step.** An auth config set to "Any Teams app" can be referenced by another app's
package. Such an app could only send the signed-in guest's own token to that customer's BFF, because the base URL is
bound in the auth config. The user would also have to install that app, and customer IT controls installs. The live
test (§3, new step 8) tries restricting the auth config to the customer's derived manifest id. If that works, it
becomes the registration default.

### 5.3 Facts found while building (2026-10-09)

- **Schema versions re-checked** against `developer.microsoft.com/json-schemas`:
  - Teams manifest v1.30 exists; v1.31 returns 404.
  - Plugin v2.4 exists; v2.5 returns 404.
  - A **DA v1.9 schema file exists** (it adds `agent_skills`), but Learn still documents v1.8 as current
    (`declarative-agent-manifest` → 1.8, ms.date 2026-09-30). Pinned: **v1.30 / v1.8 / v2.4**. Move to v1.9 when Learn
    documents it.
- **The registry had no BFF app id.** The run record (`InterStepState.BffAppRegId`) holds it, but the registry row did
  not, so "all read from the registry row" was not yet true. **Needed → built**:
  - a new column `sprk_bffappid`, which H13 promotes from `BffAppRegId` with the other Ready-state columns;
  - the Q5 column `sprk_copilotauthconfigid`.
  Both are in `scripts/Extend-DataverseEnvironmentSchema-v3.3.ps1`, and PRQ-E-14 checks them.
  - **Order:** run the script on the admin environment **before** deploying an L2 build that contains this H13.
    Otherwise H13's promoted-columns PATCH fails and the registry goes stale, although the run still completes.
  - The BFF base URL comes from `sprk_appservicename` (`https://{name}.azurewebsites.net`, the same URL H9 deploys
    and records).
- **What was built** (no new L2 handler):
  - the template source `src/solutions/CopilotAgent` (tokens in place of per-customer values);
  - the module `scripts/copilot-agent/CopilotAgentPackage.psm1`;
  - the template build `New-CopilotAgentTemplate` (module function, called by the workflow) and the script
    `Render-CopilotAgentPackage.ps1`;
  - the CI publish workflow `publish-copilot-agent-template.yml`;
  - the Bicep parameter `copilotAgentClientAppId`;
  - the two registry columns and the H13 promotion;
  - tests and docs.
  `scripts/Deploy-CopilotAgent.ps1` (Spaarke's own environments, Q4) now packages through the same renderer and keeps
  its catalog id and dev values.

### 5.4 Review (2026-10-09, one adversarial pass)

**Fixed:**
- **F4: stamps from before T257 have no `sprk_bffappid`.**
  - Guide §7.12 step 7 now sets it with the auth config id.
  - `-AllActive` skips an incomplete row, renders the rest and exits non-zero.
  - Step 6a mirrors the column (proposed `.claude` edit E1).
- **F2: the guide's step order** now puts the registry columns before the control-plane deploy.
- **F2: PowerShell version.** `#Requires -Version 7.3` is on the module and both scripts.
- **F2: a wrong script name** in §5.3.
- **F2: the render script's SHA-256/version checks and the release-loop filter were untested.**
  - Both are now tested.
  - The filter also requires Model 1 (`sprk_tenancymodel eq 0`).
- **K2: template checks threw on a malformed template.** They now report the problem.
- **K2: a relative output path** now resolves against PowerShell's location.

**Known limits:**
- **K2: `Deploy-CopilotAgent.ps1` keeps dev defaults** for the auth config, catalog id and scope.
  - Pointing it at another environment without overriding them gives a package that fails at sign-in.
  - It serves only Spaarke's own environments; customer packages never use it.
- **K1: the template publish can overwrite its rollback pointer.**
  - On a same-commit re-run after `latest` already moved, `latest.previous` ends up pointing at the new version.
  - This is inherited unchanged from the SpaarkeMaster workflow. Fix both together if it matters.
