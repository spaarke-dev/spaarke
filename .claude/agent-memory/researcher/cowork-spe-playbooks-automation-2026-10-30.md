---
name: cowork-spe-playbooks-automation-2026-10-30
description: Cowork cannot reach SPE natively (discoverability off; Spaarke dev type off); no default playbooks/library; scripts get no token, egress likely blocked; MCP connector via OAuthPluginVault (not DCR) is the route; no Graph API to install plugins; site skills do not load in Cowork
metadata:
  type: reference
---

# Cowork data access, Legal for Cowork playbooks, deployment automation (research 2026-10-30)

Legend: **[C]** Cited (primary source says it) · **[I]** Inferred (reasoned from cited facts, not stated) · **[U]** Unknown (no primary source found)
Page dates are given as `ms.date / updated_at`.

---

## Summary

- **SPE:** Cowork has no documented SharePoint Embedded path. Its file picker, edit-in-place and grounding are documented only for OneDrive, SharePoint and Teams. Third-party SPE content reaches Copilot only when `isDiscoverabilityEnabled` is on for the container type, which is off by default and **off on Spaarke's live dev container type**. For a T0/T1 kit, the reliable route to Spaarke's SPE is a **plugin `agentConnectors` remote MCP server** that calls the BFF with the user's Entra token.
- **Playbooks:** Microsoft ships **no default playbooks or positions**. Playbooks are user-supplied `.docx` files, uploaded or attached per review. There is no documented org playbook library, admin setting, or persistent or shared playbook object. `create-playbook` drafts a playbook from precedents, but its output format and save location are undocumented.
- **External calls from skills:** Skill scripts get no user token, and a script calling `https://api.spaarke.com` is undocumented and very likely blocked. The supported route is a plugin connector: remote MCP server with `OAuthPluginVault` (static Entra registration; Entra has no DCR), with per-user sign-in. **A user can upload such a plugin and share it with specific people without an admin.** No tenant switch turns off person-to-person sharing; org-wide publishing needs admin approval.
- **Automation:** There is **no documented Graph or admin API that uploads a Cowork plugin or installs it for a user**. Graph covers deployment scope and block/unblock (beta, delegated admin, Agent 365 license) and Teams catalog upload (not documented for Cowork plugins). SharePoint libraries and folders are fully scriptable. **SharePoint site skills do not load in Cowork.**

---

## Q2. Can Cowork read/write files in a SharePoint Embedded container?

**Direct answer.** Not through any documented Cowork surface today.
- Cowork's documented file reach is OneDrive, SharePoint and Teams: the picker, edit-in-place and grounding all name only those three. SPE is not mentioned anywhere in the Cowork docs. **[C]**
- Third-party SPE content can surface in Microsoft 365 Copilot experiences (search, office.com and "intelligent discovery") only when the container type's discoverability is on. It is off by default ("Nothing is auto-exposed"). **[C]**
- Even with discoverability on, nothing documents that Cowork's picker can browse SPE containers or that Cowork can edit SPE files in place. **[U]**
- The routes that work are:
  - a **plugin connector (remote MCP)** that Spaarke hosts. It reads and writes SPE through the BFF with the user's delegated token, and Cowork can pass attached workspace files to it as base64 (supported, documented, GA in Cowork) **[C]/[I]**;
  - the **Copilot Retrieval API `sharePointEmbedded` data source** (preview), called from Spaarke's own MCP server or app. It is delegated-only, requires pay-as-you-go (PAYG) billing on the container type, and needs at least one Copilot-licensed user in the tenant **[C]**;
  - the **Business apps (preview)** plugin. It reaches Spaarke's **Dataverse** rows (not SPE files) under the user's security roles, and an admin must install it **[C]**.
- An MCP connector is not the *only* possible route, but it is the **only GA, documented, file-level read/write route** from Cowork to SPE. **[I]**

**Confidence:** High that no Cowork-native SPE path is documented. Medium on whether enabling discoverability would make SPE files usable in Cowork search or edit (untested; nothing documents it).

**Evidence**
| Claim | Tag | Source (date) |
|---|---|---|
| "Attach cloud files and folders to pick files from OneDrive, SharePoint, or Teams"; Cowork edits existing Office files "stored in OneDrive or SharePoint" | C | use-cowork (2026-09-14 / 2026-10-21) |
| "Cowork can't access or edit files stored locally… It works with files in OneDrive and SharePoint"; can't delete; can't read encrypted files; 200 MB attach limit | C | cowork-faq (2026-09-29 / 2026-10-29) |
| Grounding = "mail, calendar, files in OneDrive and SharePoint, and Teams messages — and from any plugins" | C | cowork-responsible-ai-faq (2026-08-05 / 2026-10-29) |
| "Content discoverability is a setting on the container type that governs whether Microsoft 365 Copilot can surface the content. Tenant governance controls this setting, so an app can't expose content by changing its own configuration." "Nothing is auto-exposed… isn't available to Copilot until discoverability is enabled" | C | SPE agent-experiences (2026-07-10 / 2026-09-17); ground-ai-without-a-vector-db (2026-08-13 / 2026-08-24) |
| `isDiscoverabilityEnabled`: "whether items from containers are surfaced in experiences such as My Activity or Microsoft 365"; `consumingTenantOverridables` may include `isDiscoverabilityEnabled` | C | Graph fileStorageContainerTypeSettings v1.0 (2026-09-21 / 2026-09-30) |
| `Set-SPOContainerTypeConfiguration -DiscoverabilityDisabled $false` makes content "discoverable in the Microsoft 365 experience, including on office.com, onedrive.com, recommended files"; this requires the SharePoint Embedded Administrator role | C | Set-SPOContainerTypeConfiguration (upd 2026-09-23) |
| Developers can configure `isDiscoverabilityEnabled` after creation (Graph Update containerType) | C | container-types-containers (2026-07-10 / 2026-09-17), which contradicts "an app can't expose content…" above, so who actually controls the setting is unresolved |
| **Spaarke dev container type "Spaarke PAYGO 1": `isDiscoverabilityEnabled: false`, `isSearchEnabled: true`, `isOfficeRestricted: false`, `consumingTenantOverridables: "sharingCapability,itemMajorVersionLimit,isOfficeRestricted"`** (the consuming tenant cannot override discoverability). Every container-type PATCH returned 400 in testing | C (repo) | `projects/sdap-SPE-admin-app-r2/notes/live-verification-2026-08-24.md` |
| Retrieval API `sharePointEmbedded`: preview; delegated token; `FileStorageContainer.Selected`; PAYG on container type; one Copilot-licensed user initializes the semantic index; billed on Copilot Studio message meter | C | SPE agent-experiences (2026-09-17); SPE what's-new July 2026 (upd 2026-08-10) |
| SPE agent SDK (ChatEmbedded) deprecated March 2026 → Foundry SharePoint knowledge source | C | SPE what's-new |
| Cowork connector tools accept workspace files via `contentEncoding: base64` (8 files/call, 150 MiB) | C | cowork-plugin-development (2026-10-02 / 2026-10-21) |
| Office integration (Word desktop/web) on SPE files is on by default unless `isOfficeRestricted` | C | SPE open-office-files (2026-07-13 / 2026-10-28) |
| The Word legal plugin works on an SPE-hosted .docx opened in Word desktop (Copilot in Word acts on the open document) | I/U | Not documented for SPE specifically; test it |
| Business apps (preview) plugin: Dataverse, D365 and custom Power Apps data; respects role-based access; **admin must install** | C | cowork-work-iq (2026-10-13 / 2026-10-21) |

---

## Q3. Does Legal for Cowork ship standard playbooks/default positions? What does `create-playbook` produce, and where is it saved?

**Direct answer.**
- **No default playbooks or positions are documented** for Word or Cowork. The user or organization supplies the playbook as a `.docx`. **[C]**
- `create-playbook` is listed as a Legal for Cowork skill and is described only as "**Draft precedent playbook. Create a playbook from a set of precedents.**" **[C]**
- Its output format and save location are **not documented**. **[U]**
- By Cowork's general behaviour, files Cowork creates land in the user's **OneDrive Cowork output folder** (side panel "Output folder"). Because the Word review requires a `.docx` playbook, the output is most likely a `.docx` in that folder. **[I]**

**Confidence:** High on "no defaults documented". Low on the exact `create-playbook` output.

| Claim | Tag | Source (date) |
|---|---|---|
| Legal for Cowork skills list includes `create-playbook`, `review-contract`, `edit-contract`, `draft-new-contract`, etc. (20 total) | C | cowork-available-plugins (2026-10-26 / 2026-10-29) |
| "Draft precedent playbook. Create a playbook from a set of precedents." / "Run structured playbook‑based contract reviews… align with internal standards and approved language" | C | support: Copilot for Legal Transparency Documentation (no page date) |
| "Your playbook must be a .docx file." Upload file or "Attach from Cloud… from M365". Copilot breaks it into topics (rules + example clauses); user can edit rules before Run Review | C | support: get-started-with-copilot-for-legal (no page date; content applies to Word) |
| "Copilot can currently only perform contract reviews from playbooks. Copilot can't yet review contracts from templates" | C | same |
| "Copilot is not jurisdiction-aware by default" | C | transparency doc |
| Files Cowork creates go to the OneDrive **Cowork** folder / "Output folder" | C | use-cowork; cowork-faq |
| Blog: "Approved templates and reusable playbooks help legal teams work from established standards" (marketing, no mechanism) | C (marketing) | techcommunity 4564246 "Introducing Microsoft Copilot for legal" (2026-10-15) |
| MC1509799: skills "review contracts against playbooks"; no admin setup; Legal Agent "being retired" | C | MC1509799 via mc.merill.net (published 2026-10-15) |
| Earlier Legal Agent (Frontier) material reportedly turned a playbook into a reusable, auto-stored skill; that page now redirects to the Copilot for legal page, which says nothing about it | U | support Legal Agent pages (redirected) |

---

## Q4. Where are playbooks stored, and how do Cowork and the Word plugin access them? Persistence or sharing?

**Direct answer.**
- **Word:** the playbook is attached **per review**, by local upload or "Attach from Cloud" from Microsoft 365. Whether rule edits persist, or whether a saved or reusable playbook object exists, is not documented. **[C]/[U]**
- **Cowork:**
  - Files are attached per conversation from the device, OneDrive, SharePoint or Teams, or referenced with `/`. **[C]**
  - **Projects** hold instructions and "Project context" files, but **projects are private to their creator**. **[C]**
  - **Custom instructions** (20 KB) are personal. **[C]**
  - A playbook could also be packed into a **custom skill** as a `references/` companion file. Up to 20 files, 5 MB each, 10 MB total; skills can be shared with specific users. **[C]/[I]**
- There is **no documented org-level playbook library, admin-configured playbook, or tenant setting** for playbooks. The admin page covers only enabling the two plugins. **[C]**
- **Sharing across users** today means ordinary SharePoint or OneDrive file sharing (put the `.docx` in a team library; each user attaches it), or sharing a skill or plugin that embeds or points to it. **[I]**

**Confidence:** High.

| Claim | Tag | Source (date) |
|---|---|---|
| Admin page covers only: Word plugin on by default, Cloud Policy "Legal Skills in M365 Copilot in Word"; Cowork plugin "Legal for Cowork" governed on Agent Tools page; users must add it | C | manage-copilot-for-legal (2026-10-13 / 2026-10-15) |
| "Projects are private to the individual creator"; Project context = reference items, uploaded project files, items from chats; "Listing an attachment here doesn't mean every task in the project can use it" | C | cowork-projects (2026-10-27 / 2026-10-28) |
| Custom instructions personal, up to 20 KB | C | cowork-customize (2026-09-14 / 2026-10-21) |
| Skill companion files: 20 files, 5 MB each, 10 MB total; `references/` loaded on demand | C | cowork-plugin-development |
| Share skill or plugin: "Only you" or "Specific users in your organization"; Re-share pushes updates | C | cowork-customize |
| Whether Legal for Cowork's `review-contract` will pick up a playbook embedded in another skill's `references/` | U | not documented; test it |

---

## Q8. Can a skill (SKILL.md + scripts) call an external ISV service?

**(a) Fetching public URLs from the sandbox: Unknown, likely blocked.**
- Cowork docs say only that processing happens "in a temporary, isolated environment inside the Microsoft 365 service boundary" and that script execution runs in the background. They document **no network egress policy** for skill scripts. **[C]/[U]**
- Copilot's documented web access is **Bing web search** (generated query only), governed by the Cloud Policy **"Allow web search in Copilot"**. It is not arbitrary HTTP fetch. **[C]**
- A Microsoft Tech Community thread (2026-07/08) reports Python `requests.get` from a Copilot sandbox returning 403 with `x-ms-block-by: sandbox-proxy` ("URL is not relevant to the conversation"). That source is low-authority and not Cowork-specific. **[I]**
- **Do not design on script egress.** **[I]**

**(b) Authenticated calls with the user's Entra token from a script: No.**
- No mechanism gives a skill script a user token. **[U, i.e. no documented mechanism]**
- The docs say "Don't embed secrets in SKILL.md files. Use agentConnectors with authentication for API credentials." Connector credentials live in the Microsoft Enterprise Token Store and are referenced by ID. **[C]**

**(c) The supported route is a plugin connector (`agentConnectors` → `remoteMcpServer`).**
- **Auth types documented for Cowork:**
  - `None`, `OAuthPluginVault`, and DCR (omit auth, Cowork only). **[C]**
  - `ApiKeyPluginVault`: "not available in Cowork yet". **[C]**
  - **Microsoft Entra ID has no RFC 7591 endpoint**, so an Entra-protected server must use **static OAuth (`OAuthPluginVault`)**. **[C]**
  - Entra SSO is documented for declarative-agent MCP actions. Its use in Cowork `agentConnectors` is not documented. **[U]**
- **Registration:** create the OAuth configuration in the **Teams Developer Portal** (or with Agents Toolkit or wiqd) and set usage to "**Any Microsoft 365 Organization**" for cross-tenant use. Redirect URI: `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect`. Include `offline_access` for refresh. **[C]**
- **Who approves:**
  - **No admin is needed for a user to upload a plugin with connectors and share it with specific people.** Admin approval applies only to org-wide publishing. **[C]**
  - Each user completes the connector's own sign-in or consent; admins "can't sign in on behalf of your users". **[C]**
  - Whether the Entra consent itself succeeds depends on the customer tenant's **user-consent policy** for Spaarke's multitenant app. **[I]**
  - The **Agent 365 MCP registry approval (AI Administrator or Global Administrator plus tenant-wide consent)** applies to *BYO MCP servers registered through the Agent 365 CLI*. That path is preview and targets Copilot Studio, VS Code, Claude Code and GitHub Copilot CLI, **not** Microsoft 365 declarative agents. Nothing says it is required for Cowork plugin connectors. **[C]/[I]**
- **Admin kill switches:**
  - Agents > Settings > **Agent and plugin access**: publisher categories Microsoft / your organization / certified external publishers. **[C]** Clearing "your organization" blocks user-uploaded plugins. **[I]**
  - Per-plugin Block, and MCP server Block. **[C]**
  - Information Barriers tenants: plugin and skill upload or publish is blocked. **[C]**
- **Can a user add a custom MCP connector at T0?** Yes, by uploading a plugin package with `agentConnectors`, or a Claude plugin with `.mcp.json`, which Cowork auto-converts. **[C]** No "add connector URL" UI outside a plugin package is documented. **[U]**
- **Network:** the MCP endpoint must be public HTTPS (TLS 1.2+). Cowork identifies itself with `User-Agent: copilot-cowork/…` and `clientInfo.name = copilot-cowork`, which carry no tenant or user identity. **[C]**
- **Doc conflict on tool descriptions:** the Cowork plugin-dev page says Cowork discovers tools dynamically and ignores `mcpToolDescription`. configure-plugin-mcp-servers (2026-10-28) says "**Copilot Cowork still requires a tool description file**, even with version 1.29 or later." **Ship the file.** **[C]**
- **Alternative authenticated route:** Cowork **local browser** (Edge, on the web only) uses the user's existing sign-ins to drive web apps, which could include the Spaarke web UI. It is **off by default** and an admin enables it ("Allow browser access" in Cowork settings). **[C]**

**Confidence:** High on (b) and (c). Medium on (a), because nothing documents it explicitly.

| Source | Date |
|---|---|
| cowork-plugin-development | 2026-10-02 / 2026-10-21 |
| cowork-manage-plugins | 2026-08-31 / 2026-10-29 |
| cowork-customize | 2026-09-14 / 2026-10-21 |
| plugin-authentication | 2026-10-28 |
| configure-plugin-mcp-servers | 2026-10-28 |
| agent-365 manage-plugins-skills-mcp-servers | 2026-10-14 / 2026-10-23 |
| agent-365 manage-byo-mcp-server | 2026-09-28 / 2026-10-23 |
| cowork-admin-governance | 2026-09-14 / 2026-10-30 |
| manage-public-web-access | 2026-10-29 |
| cowork-local-browser | 2026-10-22 / 2026-10-24 |
| techcommunity discussion 4542482 | posts 2026-07-30, 2026-08-02 |

---

## Q10. Can a script create all of an accelerator's primitives in a customer tenant?

**Direct answer:** Partly.

| Primitive | Scriptable? | API / tool | Permissions / role | Non-admin end user |
|---|---|---|---|---|
| **Personal Cowork skill** (SKILL.md + companions) | Yes. It is just files. | Copy to OneDrive `/Documents/Cowork/skills/<name>/` (sync client, web, or Graph `Files.ReadWrite`, delegated, no admin consent **[C permref]**). The Customize page "Upload skill" (.md/.zip/.skill) is UI only. | User-level | **Can:** copy through OneDrive sync with no script or consent [C/I]. **Script may fail:** needs a Graph client app the user may consent to (tenant user-consent policy), plus device policy (execution policy, AppLocker/WDAC, Constrained Language Mode) [I]. Limit 50 skills, 1 MB SKILL.md [C]. Not supported on mobile [C]. |
| **Cowork plugin install (user)** | No documented API for "install for user X" | UI: Customize > Upload plugin, then Share ("Only you" / "Specific users"). CLI: `atk install --file-path … --scope Personal` (sideload) and `wiqd plugin provision` / `wiqd plugin share --scope users\|tenant` (preview) [C] | Sideloading custom agents needs the Teams setup policy **"Upload custom apps" = On** [C, agent-sideload 2026-03-31]. Whether Cowork's own Upload plugin respects that policy is [U] | **Can:** upload and share with named people [C]. **Blocked if:** IB enabled [C], "your organization" publisher category cleared [I], sideload policy off for atk/wiqd [C/I] |
| **Plugin org publish / deploy** | Partly | Upload: MAC > Manage apps > Upload custom app, or Agents > Tools > Registry > Upload (UI) [C]. Graph `POST /appCatalogs/teamsApps` publishes a Teams app package to the org catalog (delegated `AppCatalog.ReadWrite.All` with admin consent; `AppCatalog.Submit` + `requiresReview=true` lets any user submit for review; no app-only) [C]; that it accepts Cowork plugin packages is **[U]**. Deploy scope: Graph **beta** `PATCH /copilot/admin/catalog/packages/{id}` (`allowedUsersAndGroups`, `acquireUsersAndGroups`), plus block/unblock/reassign. Delegated `CopilotPackages.ReadWrite.All` (admin consent), **no app-only, requires an Agent 365 license**, global cloud only; **no create/upload method** [C] | MAC admin: "tenant administrator, or the Copilot administrator role" [C] | Cannot (request or submit only) |
| **Connector (MCP) auth config** | Yes, ISV side | Teams Developer Portal OAuth config, or `atk` / `wiqd`; done once in Spaarke's tenant with "Any Microsoft 365 Organization" [C] | ISV dev account | Each user signs in once [C] |
| **SharePoint library + folder tree** | Yes | Graph (`Sites.Manage.All` delegated = no admin consent [C permref]) or PnP PowerShell, which needs your own Entra app registration (from prior research 2026-10-29; app registration may be disabled for users) [I] | Site Owner (library); site creation depends on the tenant self-service setting [I] | **Can:** build it in the browser UI as site owner. Script blocked by consent or device policy [I] |
| **SharePoint site skills** `/Agent Assets/Skills/<name>/SKILL.md` | Partly | They are .md files in a product-managed library. The library is "created and managed by the product", is auto-activated when the first site skill is created via Copilot, and can't be deleted [C]. Pre-seeding through Graph before activation is [U] | Edit to create, View to run [C] | **Can** create through Copilot in SharePoint. **Caveat: site skills load only in Copilot in SharePoint, "can't connect to external systems or execute custom code", and are not used by Cowork** [C] |
| **Agent Builder agent** | No API to author Agent Builder agents [U] | Use a declarative agent instead: `wiqd agent provision/share/publish` or `atk` (preview CLI) [C]. SharePoint-built agents can't be sideloaded [C] | Org catalog publish requires admin approval [C] | Can create in the UI and share with users or groups [C, prior research] |
| **Publish to org catalog (Teams admin / Agent 365 / Integrated apps)** | Upload is UI only (MAC); Graph Teams catalog API exists [C]; MCP registry approval is UI only | Agent 365 BYO MCP: register with `a365 develop-mcp register-external-mcp-server`; approval needs **AI Administrator or Global Administrator** plus tenant-wide consent; up to 30 minutes to propagate [C] | Admin | Cannot |
| **SPE discoverability** | Yes (owner side) | Graph Update containerType `settings.isDiscoverabilityEnabled`, or `Set-SPOContainerTypeConfiguration -DiscoverabilityDisabled $false` (SharePoint Embedded Administrator) [C]. Updates take up to 24 hours to replicate [C, knowledge/]. Spaarke dev PATCHes have returned 400 [C repo] | ISV container-type owner; consuming tenant only if overridable | Cannot |
| **Cowork access itself** | No (admin) | Spending policy that selects Cowork (MAC > Copilot > Cost Management) [C] | Admin | Cannot. Can request access if discoverable [C] |

**Information Barriers:** "aren't currently supported for plugin or skill management and sharing… embedded knowledge file uploads are blocked at the tenant level," which blocks plugin and skill upload and publishing. **[C]** (cowork-plugin-development, cowork-manage-plugins)

---

## Corrections to our assumptions

1. **SharePoint site skills (`/Agent Assets/Skills/`) are not a Cowork distribution channel.** They run only in Copilot in SharePoint, cannot call external systems or run code, and are not loaded by Cowork. Cowork's custom skills come only from the user's own OneDrive `/Documents/Cowork/skills/`, plugins, or skills shared with the user. T1 "SharePoint library" can be a *distribution/download* point, not a live skill source. **[C]**
2. **SPE content is not available to Cowork or Copilot by default, and Spaarke's own dev container type has discoverability OFF**, with the consuming tenant unable to override it. Any "Cowork reads matter files in SPE" story needs either (a) an MCP connector to the BFF, or (b) a deliberate discoverability decision. Option (b) exposes matter files across office.com, OneDrive recommendations and Copilot search tenant-wide (subject to permissions), which is a security posture change. **[C]/[I]**
3. **No Microsoft default playbooks, no playbook library, no persistent playbook object.** A practice accelerator that ships curated playbook `.docx` files and positions fills a real gap. **[C]**
4. **Plugin connector ≠ Agent 365 MCP registry.** The AI Admin / Global Admin MCP approval flow is for BYO MCP servers registered in Agent 365 (preview; Copilot Studio and dev tools), not for Cowork plugin connectors. A user-shared plugin with a Spaarke MCP connector needs no admin approval. It does need each user's OAuth or Entra consent, and admins can still block it. **[C]/[I]**
5. **Entra cannot use DCR.** Use `OAuthPluginVault` with a static Teams Developer Portal OAuth config registered for "Any Microsoft 365 Organization". **[C]**
6. **Tool description file:** ship `mcpToolDescription` even with manifest 1.29+, because the newest page says Cowork still requires it. **[C]**
7. **No Graph "install plugin for user" API.** The Package Management API (beta) changes availability and deployment scope and blocks packages, but cannot upload. It needs an Agent 365 license and delegated admin; app-only is not available. **[C]**
8. **"Allow agents and plugins built by your organization" now appears as** Agents > Settings > **Agent and plugin access** publisher categories (Microsoft / your organization / certified external publishers). **[C]**

## Implications for a T0 / T1 / T2 design

- **T0 (attorney, no IT):**
  - Ship a skills-only `.zip` (SKILL.md + `references/` with playbook positions + `assets/` playbook `.docx`) that the user uploads from Customize > Upload skill, or copies into OneDrive `/Documents/Cowork/skills/`. Keep each skill ≤ 20 companions and ≤ 10 MB.
  - Optional Spaarke connector: a plugin `.zip` with an `agentConnectors` remote MCP (`OAuthPluginVault`, Entra, multitenant) that the user uploads and shares with "Only you".
  - Prerequisites the attorney cannot fix: Cowork spending policy, IB off, "your organization" plugins allowed, and their own consent to Spaarke's Entra app.
  - Do **not** rely on script egress or tokens inside skills.
- **T1 (practice team):**
  - A SharePoint library holds versioned playbook `.docx` files (attached via "Attach cloud files" in Cowork, or "Attach from Cloud" in Word) and the downloadable skill and plugin zips.
  - Live sharing goes through Cowork **Share → Specific users** (Re-share pushes updates). Site skills are **not** the vehicle for Cowork.
  - Projects are private, so they are no team vehicle.
- **T2 (IT tenant-wide):**
  - An admin uploads the plugin (MAC Upload custom app, or Agents > Tools > Registry) and deploys it to groups. Optionally script deploy scope through the beta Package Management API (Agent 365 license).
  - Optionally install the Business apps plugin for Dataverse access, and decide on SPE discoverability.
  - Long term: AppSource or Partner Center listing (4–6 weeks; prior research).
- **Spaarke MCP server design:**
  - Expose SPE read and write as tools (list/search matter files, fetch extract, save output).
  - Accept Cowork workspace files through `contentEncoding: base64`, and annotate tools with `readOnlyHint` / `destructiveHint`.
  - Gate requests on the `copilot-cowork` User-Agent prefix if needed.
  - All writes go through the BFF write path (ADR-002/007).

## Caveats

- `create-playbook` internals, Word playbook-edit persistence, and the Cowork sandbox egress policy are undocumented. Verify all three with a live tenant test.
- The SPE docs conflict on who controls discoverability (developer Graph PATCH vs "tenant governance controls this setting").
- Retrieval API `sharePointEmbedded`, Business apps plugin, BYO MCP, wiqd, and the Package Management API PATCH/block are all **preview or beta**.
- Cowork docs churn weekly; several pages were updated 2026-10-28 to 10-30.

## Recommended follow-ups (live-tenant tests)

1. Run `create-playbook` in Cowork and record the output format and location.
2. Paste an SPE file `webUrl` into Cowork and try a `/` reference; record the result.
3. Turn on discoverability on a *trial* container type in a test tenant, then check Cowork Enterprise Search and the picker.
4. Upload a plugin with an Entra `OAuthPluginVault` connector as a non-admin in a tenant that has user consent restricted.
5. Run `curl` from a Cowork skill script against a public URL and against `api.spaarke.com`.
6. Find out why container-type settings PATCHes return 400 before any discoverability change.

## Reference links

- https://learn.microsoft.com/en-us/copilot/cowork/cowork-faq (2026-09-29 / 2026-10-29)
- https://learn.microsoft.com/en-us/copilot/cowork/use-cowork (2026-09-14 / 2026-10-21)
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-customize (2026-09-14 / 2026-10-21)
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-plugin-development (2026-10-02 / 2026-10-21)
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-manage-plugins (2026-08-31 / 2026-10-29)
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-admin-governance (2026-09-14 / 2026-10-30)
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-available-plugins (2026-10-26 / 2026-10-29)
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-projects (2026-10-27 / 2026-10-28)
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-work-iq (2026-10-13 / 2026-10-21, preview)
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-local-browser (2026-10-22 / 2026-10-24)
- https://learn.microsoft.com/en-us/copilot/cowork/responsible-ai/copilot-cowork-application-card (2026-09-22 / 2026-10-29)
- https://learn.microsoft.com/en-us/copilot/cowork/responsible-ai/cowork-responsible-ai-faq (2026-08-05 / 2026-10-29)
- https://learn.microsoft.com/en-us/microsoft-365/admin/manage/manage-copilot-for-legal (2026-10-13 / 2026-10-15)
- https://support.microsoft.com/en-us/word/get-started-with-copilot-for-legal (undated)
- https://support.microsoft.com/en-us/word/copilot-for-legal-transparency-documentation (undated)
- https://techcommunity.microsoft.com/blog/microsoft365copilotblog/introducing-microsoft-copilot-for-legal/4564246 (2026-10-15)
- https://mc.merill.net/message/MC1509799 (published 2026-10-15)
- https://learn.microsoft.com/en-us/microsoft-agent-365/admin/manage-plugins-skills-mcp-servers (2026-10-14 / 2026-10-23)
- https://learn.microsoft.com/en-us/microsoft-agent-365/admin/manage-byo-mcp-server (2026-09-28 / 2026-10-23, preview)
- https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-authentication (2026-10-28)
- https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/configure-plugin-mcp-servers (2026-10-28)
- https://learn.microsoft.com/en-us/microsoft-365/copilot/manage-public-web-access (2026-10-29)
- https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/admin-settings/package/overview (2026-08-27 / 2026-09-28)
- https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/admin-settings/package/copilotpackagedetail-update (2026-05-01 / 2026-10-02)
- https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/admin-settings/package/resources/copilotpackage (2026-08-27 / 2026-10-21)
- https://learn.microsoft.com/en-us/microsoft-365/copilot/agent-essentials/agent-lifecycle/agent-upload-agents (2026-02-23 / 2026-09-09)
- https://learn.microsoft.com/en-us/microsoft-365/copilot/agent-essentials/agent-policies/agent-sideload (2026-03-31 / 2026-08-18)
- https://learn.microsoft.com/en-us/graph/api/teamsapp-publish?view=graph-rest-1.0 (2024-09-27 / 2026-06-19)
- https://learn.microsoft.com/en-us/sharepoint/copilot-in-sharepoint-skills (2026-09-28 / 2026-09-29)
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/agent-experiences (2026-07-10 / 2026-09-17)
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/plan/ground-ai-without-a-vector-db (2026-08-13 / 2026-08-24)
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/plan/container-types-containers (2026-07-10 / 2026-09-17)
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/plan/security-compliance-governance (2026-07-13 / 2026-10-29)
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/open-office-files (2026-07-13 / 2026-10-28)
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/whats-new (upd 2026-08-10)
- https://learn.microsoft.com/en-us/graph/api/resources/filestoragecontainertypesettings?view=graph-rest-1.0 (2026-09-21 / 2026-09-30)
- https://learn.microsoft.com/en-us/powershell/module/microsoft.online.sharepoint.powershell/set-spocontainertypeconfiguration (upd 2026-09-23)
- https://microsoft.github.io/wiqd/cli/reference/ (v0.22.0 preview, undated)
- https://github.com/microsoftgraph/microsoft-graph-docs-contrib/blob/main/concepts/permissions-reference.md (fetched 2026-10-30)
- https://techcommunity.microsoft.com/discussions/microsoft-copilot/urls-not-accessible-when-running-python-script-in-sandbox/4542482 (2026-07-30 / 2026-08-02; community, low authority)
- Repo: `projects/sdap-SPE-admin-app-r2/notes/live-verification-2026-08-24.md`; `knowledge/sharepoint-embedded/docs/learn-containertypes.md`
