# Can Copilot Cowork get the bytes of a SharePoint Embedded file into a skill sandbox?

Researched 2026-11-26. Builds on researcher notes of 2026-10-30 (`cowork-spe-playbooks-automation`, `cowork-skill-runtime-and-mvp-claims`) and the Spaarke POC SPE test (`poc-2026-10-30-cowork-nda-review.md` section 3.5).

Labels: **Documented** (a Microsoft page says it; URL and page date given) · **Observed** (seen in a Spaarke tenant test) · **Inferred** (reasoned from the above, not stated) · **Unknown** (needs a tenant test).

## Answer

**No, not natively, and not by link. The only workable routes are (a) the user downloads the file from Spaarke and uploads it from the device, or (b) a Spaarke MCP connector that hands the bytes over. Whether (b) can place bytes on the sandbox file system is Unknown and is the one thing to test.**

1. No Cowork or Work IQ page names SharePoint Embedded as a Cowork file source. **Documented.**
2. Cowork's built-in Microsoft Graph access got **403** on both a container list and a direct file read for Spaarke's container type. **Observed** (POC 2026-10-30, container type `8a6ce34c-6055-4681-8f87-2f4f9f921c06`).
3. Spaarke's container type has discoverability **off**, and the consuming tenant cannot override it. **Observed** (`projects/sdap-SPE-admin-app-r2/notes/live-verification-2026-08-24.md`).
4. Even with discoverability on, Microsoft's SPE documentation describes only text-extract grounding (Retrieval API, Work IQ `retrieve`), not file bytes for a Cowork sandbox.

## 1. What Cowork can read, and how a file reaches the sandbox

| Route | What the docs say | Status |
|---|---|---|
| Attach from device ("Upload images and files") | Any listed type, including .docx. Attached files must be under 200 MB. | **Documented** (use-cowork, ms.date 2026-11-25; cowork-faq, ms.date 2026-11-16) |
| "Attach cloud files and folders" | "pick files from OneDrive, SharePoint, or Teams". SPE is not listed. | **Documented** (use-cowork, ms.date 2026-11-25) |
| "Add work context" and `/` picker | "reference files, people, emails, and Teams chats, channels, and meetings from your organization". Sources not enumerated. | **Documented** (use-cowork) |
| Edit existing Office file in place | "stored in OneDrive or SharePoint". | **Documented** (use-cowork, cowork-faq) |
| What Cowork can access | "inherits your permissions... only the files and emails you can already access". "It works with files in OneDrive and SharePoint." | **Documented** (cowork-faq) |
| Pasted link in the chat box | Nothing in the Cowork docs says a pasted file URL is fetched. Copilot Chat (not Cowork) documents "copy and paste or drag and drop" for referencing files. OneNote is the only Cowork case that mentions links ("provide a link to the notebook or page"). | **Unknown** for files in Cowork |
| Side panel "Input folder" | "Files you provided as context for the session." | **Documented**. Whether the Python sandbox sees them at a readable path is not stated; the POC (2026-10-30, Q8) says scripts run in a private temporary Linux workspace and the NDA skill's `scripts/*.py` ran on attached files. **Observed** (mvp-nda/results.md) |
| Business apps (preview) plugin | Dataverse, Dynamics 365, custom Power Apps data under the user's roles; admin installs it. Rows, not SPE files. | **Documented** (cowork-work-iq, ms.date 2026-10-13, prerelease) |
| Work IQ MCP `retrieve` (preview) | Capabilities `People, Meetings, OneDriveAndSharePoint, Email, TeamsMessages, GraphConnectors`. | **Documented** (work-iq/mcp/retrieve, ms.date 2026-11-11) |
| Graph connectors | `GraphConnectors` capability only with strategy `copilot`. They index external items; they do not deliver bytes. | **Documented** (same page) |

Whether Cowork's own Enterprise Search skill uses Work IQ `retrieve` internally is not documented. **Unknown.**

Script egress is still undocumented (cowork-plugin-development, ms.date 2026-11-16, no network text). The POC marks "outbound internet may be restricted [VERIFY]". Do not design on a script downloading a file from the BFF. **Unknown.**

## 2. Can Copilot, Work IQ or the SharePoint connector see or fetch SPE content?

**Visibility (documented, with conflicts):**

- "Content discoverability is a setting on the container type that governs whether Microsoft 365 Copilot can surface the content... Nothing is auto-exposed. SharePoint Embedded content isn't available to Copilot until discoverability is enabled on the container type." **Documented** (SPE `build/agent-experiences`, ms.date 2026-07-10, updated 2026-09-17; `plan/ground-ai-without-a-vector-db`, ms.date 2026-08-13, updated 2026-08-24).
- The same pages say "Tenant governance controls this setting, so an app can't expose content by changing its own configuration." But `plan/container-types-containers` (ms.date 2026-07-10, updated 2026-09-17) lists `isDiscoverabilityEnabled` as a setting developers can configure after creation. **The pages conflict on who controls it.** The Spaarke type reads `isDiscoverabilityEnabled: false`, and every container-type PATCH returned 400 in testing (live-verification-2026-08-24).
- Graph: `isDiscoverabilityEnabled` = "whether items from containers are surfaced in experiences such as My Activity or Microsoft 365"; `isSearchEnabled` = "whether search is enabled"; both can be listed in `consumingTenantOverridables`. **Documented** (Graph `fileStorageContainerTypeSettings` v1.0, ms.date 2026-09-21, updated 2026-09-30).
- Admin switch: `Set-SPOContainerTypeConfiguration -DiscoverabilityDisabled $false` (SharePoint Embedded Administrator). **Documented** (2026-09-23, from the 2026-10-30 notes). Note the inverted flag.
- SPE content in Microsoft Search for users with access: Graph search over SPE is **preview**, delegated only, and always needs `ContainerTypeId` in the query. "If your application opted out of Microsoft 365 content discoverability, set `includeHiddenContent` to true", so hidden content is excluded from ordinary search when discoverability is off. Search "enforces the signed-in user's access, not your app's authorization"; to open a returned item "your app must have access permissions to the corresponding container type". **Documented** (`build/search-containers-files`, ms.date 2026-07-13, updated 2026-10-28).

**Fetch (the part that matters for bytes):**

- Copilot Retrieval API `dataSource: sharePointEmbedded` (preview since July 2026): delegated token, `FileStorageContainer.Selected`, pay-as-you-go billing on the container type, one Copilot-licensed user to initialise the semantic index. Returns `retrievalHits` with `webUrl` and text `extracts`. **Documented** (agent-experiences; SPE whats-new, July 2026). Text extracts only; it does not return file content.
- Work IQ MCP `retrieve` "supports SharePoint Embedded content that the signed-in user can access", scoped through `OneDriveAndSharePoint` with URL or SharePoint-ID fields. It returns Markdown plus metadata (`driveId`, `itemId`, `entityUrl`). Public preview. **Documented** (work-iq/mcp/retrieve, ms.date 2026-11-11, updated 2026-11-20). This is new since 2026-10-30. It shows Microsoft is wiring SPE into Work IQ, but the output is grounding text, not a file.
- Work IQ MCP `fetch_blob` returns binary content as `base64Content` from a path such as `/drives/{drive-id}/items/{item-id}/content`; default raw-file limit 4 MB. Allowed path prefixes by default are `/me/`, `/users/`, `/sites/`. No SPE statement either way. **Documented** (work-iq/mcp/tool-reference, ms.date 2026-11-16, updated 2026-11-20). Whether `fetch_blob` works on an SPE drive is **Unknown**; given the point below it is likely to fail.
- App authorisation: SPE grants file access only to apps registered on the container type (`container-types-containers`: "A SharePoint Embedded application must be associated with a container type to access containers of that type"). Cowork's Graph access is a Microsoft-owned app that is not registered. **Inferred**, and matches the **Observed** 403. Discoverability changes what search can *surface*, not which app may *open* the file.
- Legacy Copilot Studio SharePoint MCP tools (`readSmallBinaryFile`, 5 MB) are site document libraries by their tool schema (`findSite`, `documentLibraryId`); they are "backward compatibility" only. **Documented** (copilot-studio/mcp-sharepoint-tools, ms.date 2026-11-16). No SPE support stated.

## 3. Attach an SPE file to a Cowork chat by link

- **Picker**: SPE container files are not documented as browsable. **Unknown** in a tenant with discoverability on; expected absent with it off.
- **Pasted webUrl or sharing link**: not documented for Cowork. If Cowork tried to fetch it, it would use Cowork's own Graph identity, which got 403 on the same container. **Inferred**: a link gives the agent a URL, not the bytes, and the sandbox gets nothing.
- Spaarke's `webUrl` values also depend on the container type `urlTemplate`, which Microsoft says to treat as opaque (agent-experiences). A redirecting template may not resolve to a fetchable Office URL.
- **What does work (documented):** the user downloads the .docx from Spaarke and attaches it from the device. That is an ordinary device upload, under 200 MB, and the NDA skill already runs on it (mvp-nda results).

## 4. Documented SPE-in-Copilot limits, with dates

- SPE agent SDK (`ChatEmbedded`) deprecated **March 2026**, replaced by Foundry Agent Service with a SharePoint knowledge source (preview). **Documented** (SPE whats-new; agent-experiences).
- Retrieval API `sharePointEmbedded` source: preview, July 2026. **Documented.**
- Graph search over SPE: preview, delegated only, beta for hidden content. **Documented** (2026-10-28 update).
- Work IQ `retrieve`: public preview; SPE support stated 2026-11-11. **Documented.**
- Discoverability default off; control ownership unclear (see section 2). **Documented, conflicting.**
- Nothing in any SPE or Cowork page says Microsoft 365 Copilot or Cowork can read SPE file bytes into an agent sandbox. **No documentation either way.**
- Loop "My workspace" SPE content is reported not discoverable by Copilot or search (community post, not official). Low authority.

## The gap in the current design

`design.md` section 10 says `spaarke_get_document` "returns the file to Cowork as base64 (≤ 8 files and 150 MiB per call [Cited])". Those limits belong to the **input** direction. Microsoft documents `contentEncoding: base64` only for a tool *parameter*: Cowork reads a workspace file and base64-encodes it **before calling the server** (cowork-plugin-development, ms.date 2026-11-16, updated 2026-11-17; "Accept files from the Cowork workspace"). The same page warns that pasting a base64 blob into model context "appears to work on small test files and fails on real ones". The docs say nothing about a tool *result* that carries a file being saved to the workspace.

Consequences:

- **Sandbox to SPE (saving a redline back): documented route.** A tool such as `spaarke_save_version` with a `contentEncoding: base64` parameter receives the workspace file (8 files, 150 MiB).
- **SPE to sandbox (reading the .docx): no documented route.** A tool result that returns base64 puts the bytes into the model's context, not onto disk. The only inline limit documented anywhere is 64 KiB for an MCP App result (mcp-apps-support, from the 2026-10-30 notes). A real NDA .docx is typically larger.
- The deliberate "metadata and open links only" shape of `spaarke_get_document` is therefore not the cause of the gap. Returning base64 from it may not help, and may fill the context.

## Routes to get the .docx bytes into the sandbox (ranked)

1. **Device upload (works today, documented).** User downloads from the Spaarke UI and attaches. Zero platform risk; adds one manual step; the file leaves the container (already true for any download).
2. **Stage to OneDrive via the connector (Inferred to work).** A connector tool (for example `spaarke_stage_document`) has the BFF copy the SPE file into a folder in the signed-in user's OneDrive and returns the OneDrive link. The user picks it with "Attach cloud files" or `/`, both documented for OneDrive. Edited files can go back through `spaarke_save_version`. Cost: a copy of client content in personal OneDrive (retention, labels, DLP, clean-up), which is a posture decision for the owner, and a BFF write path that needs delegated OneDrive access (Files.ReadWrite through OBO). Needs an ADR-028 check before design.
3. **Tool result materialised as a file (Unknown).** MCP `resource` or `resource_link` content in a tool result might be saved by Cowork as a workspace file. No Cowork page documents it. Test it (below).
4. **Short-lived signed download URL fetched by the script (Unknown, likely blocked).** Depends on sandbox egress, which is undocumented, and adds a public download endpoint on the BFF (security review). Do not rely on it.
5. **Turn on discoverability (not recommended).** Surfaces matter documents tenant-wide in Microsoft 365 search and Copilot (subject to permissions), and still would not give a script the bytes. Spaarke's PATCH has returned 400, and the consuming tenant cannot override it.
6. **Ask Microsoft to register Cowork's app on the container type (POC option 3).** Unconfirmed support; the POC says verify before pursuing. Nothing in the docs offers it.

## Smallest tenant tests

Test environment: the Spaarke DEV tenant, a user who is a member of a container with a small .docx, Cowork open, discoverability off (current state).

**Test 1: native read (10 minutes; expected: fails).**
1. In Cowork choose Attach cloud files and folders. Look for the Spaarke container or file under OneDrive, SharePoint and Teams.
2. Type `/` and search the file name.
3. Paste the file's `webUrl` in a prompt: "Open this file and tell me how many paragraphs it has."
- Not listed, and a "can't access" answer = confirmed no native path (matches the 403 in the POC).
- A correct answer with real content = Cowork can read SPE through its index. Then repeat from a skill: "copy the file to your working folder and run `python -c "import zipfile,sys;print(zipfile.ZipFile(sys.argv[1]).namelist())" <path>`". A listing proves the bytes reach the sandbox.

**Test 2: tool result to file (the decisive one; 1 hour).**
1. Add one throwaway tool `probe_return_file` to a dev MCP connector, with three variants returning a 20 KB .docx: (a) base64 in a text content block; (b) an embedded `resource` with `blob` and docx `mimeType`; (c) a `resource_link` to a dev HTTPS URL.
2. Prompt: "Call probe_return_file variant X, then run python to open the file you received and print its zip part names." Add a second step: "run `python -c "import urllib.request;print(urllib.request.urlopen('<dev https url>').status)"`" to test egress.
- Python opens a file on disk = a tool result can materialise a file; keep the base64 `spaarke_get_document` design and cap sizes.
- Python can only see text in the context, or truncates = bytes cannot be delivered that way; use route 1 or 2.
- The `urlopen` call succeeds = route 4 is feasible (still needs a security review); fails = script egress is blocked.

**Test 3 (only if route 2 is chosen): OneDrive staging (30 minutes).** Manually copy the .docx into the user's OneDrive `Cowork-inbox/` folder, attach it with the picker, and run `nda-review`. A pass confirms the skill side; the remaining work is the BFF copy.

## Caveats

- Cowork and Work IQ docs changed on 2026-11-11, 11-16, 11-20 and 11-25. Re-check before deciding.
- Work IQ `retrieve` and the SPE Retrieval API source are preview; Graph search over SPE is preview.
- The "no SPE in Cowork" conclusion rests on absence from the docs plus one Observed 403 in the Spaarke tenant. A different tenant with a different container-type configuration could behave differently. Test 1 in a tenant with discoverability on (a trial container type, up to 5 containers, 30 days, own tenant only) would settle that.
- The POC note describes "tool scripts" that call Microsoft 365 tools from Python with user permissions. That is not in any Learn page I found. Treat it as unverified.

## Sources

Spaarke repo:
- `projects/spaarke-legal-ai-accelerator-r1/notes/poc-2026-10-30-cowork-nda-review.md` (section 3.5, Q8)
- `projects/spaarke-legal-ai-accelerator-r1/design.md` (section 10)
- `projects/sdap-SPE-admin-app-r2/notes/live-verification-2026-08-24.md`
- `docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md` (no Copilot or discoverability content in it)
- `.claude/agent-memory/researcher/cowork-spe-playbooks-automation-2026-10-30.md`

Microsoft Learn (page dates are ms.date / updated_at):
- https://learn.microsoft.com/en-us/copilot/cowork/use-cowork (2026-11-25 / 2026-11-26)
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-faq (2026-11-16 / 2026-11-26)
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-plugin-development (2026-11-16 / 2026-11-17)
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-work-iq (2026-10-13 / 2026-11-26)
- https://learn.microsoft.com/en-us/copilot/cowork/whats-new (2026-11-25 / 2026-11-26)
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/agent-experiences (2026-07-10 / 2026-09-17)
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/plan/ground-ai-without-a-vector-db (2026-08-13 / 2026-08-24)
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/plan/container-types-containers (2026-07-10 / 2026-09-17)
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/search-containers-files (2026-07-13 / 2026-10-28)
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/plan/security-compliance-governance (2026-07-13 / 2026-10-29; no Copilot statements)
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/whats-new (updated 2026-08-10)
- https://learn.microsoft.com/en-us/graph/api/resources/filestoragecontainertypesettings?view=graph-rest-1.0 (2026-09-21 / 2026-09-30)
- https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq/mcp/retrieve (2026-11-11 / 2026-11-20)
- https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq/mcp/tool-reference (2026-11-16 / 2026-11-20)
- https://learn.microsoft.com/en-us/microsoft-copilot-studio/mcp-sharepoint-tools (2026-11-16 / 2026-11-19)
- https://support.microsoft.com/en-us/microsoft-365-copilot/refer-to-specific-files-and-more-in-microsoft-365-copilot (Copilot Chat paste/reference; not Cowork)
