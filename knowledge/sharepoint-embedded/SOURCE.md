# SOURCE — `sharepoint-embedded`

> Provenance for curated SPE samples and reference docs. Refreshed on the monthly cadence; do not edit curated files in place — re-pull from upstream.

**Captured**: 2026-05-14

---

## Upstream repositories

| Repo | Commit (HEAD on 2026-05-14) | Purpose |
|---|---|---|
| [microsoft/SharePoint-Embedded-Samples](https://github.com/microsoft/SharePoint-Embedded-Samples) | `7e9ff52885512d901bedf0509d1df0a40dde6516` (2026-04-28) | Official SPE samples — C#/TypeScript boilerplates, PowerShell tools, AI integrations |
| [microsoft/SharePoint-Embedded-VS-Code-Extension](https://github.com/microsoft/SharePoint-Embedded-VS-Code-Extension) | `5918acf9b250d76b960514536b529e7c807abb2d` (2026-04-23) | VS Code extension for trial-tier setup. **Extension source only, no runnable samples** — README captured here for context, no code curated. |

Both repos are MIT-licensed. Curated files retain their original copyright; this knowledge base is a pointer index, not a fork.

---

## Curated samples

All paths below are relative to upstream `SharePoint-Embedded-Samples/`.

### `samples/container-crud-csharp/` — server-side container + permission CRUD (ASP.NET Core)

From `Custom Apps/boilerplate-aspnet-webservice/`. Matches Spaarke's BFF language (C#/.NET) and demonstrates the raw Microsoft Graph HTTP calls that the SDK abstracts away (useful when debugging or when SDK shapes lag behind the beta endpoint).

| Local file | Source path | What it shows |
|---|---|---|
| `ContainersController.cs` | `Controllers/ContainersController.cs` | MVC controller showing `Create`, `CreateAppOnly`, `Delete`, `Index`. Demonstrates **delegated vs. app-only** token acquisition for the same operation. |
| `ContainerPermissionsController.cs` | `Controllers/ContainerPermissionsController.cs` | List / Add / Update / Delete permissions for a container. Uses `FileStorageContainer.Selected` scope throughout. |
| `ContainerModel.cs` | `Models/ContainerModel.cs` | DTO matching the Graph wire shape (note: properties are `lowerCamelCase` — required by the API). |
| `ContainerPermissionModel.cs` | `Models/ContainerPermissionModel.cs` | DTO for `grantedToV2.user` + `roles[]` permission payload. |
| `MSGraphService.excerpt.cs` | `Services/MSGraphService.cs` (lines 25-200, 485-500) | **Curated excerpt** isolating container + permission HTTP calls from the 527-LOC original. Shows exact Graph endpoints: `POST /beta/storage/fileStorage/containers`, `POST /containers/{id}/activate`, `POST /containers/{id}/permissions`. |

### `samples/container-crud-typescript/` — server-side container CRUD (Azure Functions, TypeScript)

From `Custom Apps/boilerplate-typescript-react/function-api/` and `AI/mcp-server/`. Demonstrates the **Microsoft Graph JavaScript SDK** path rather than raw HTTP, plus modern MCP-style permission tools.

| Local file | Source path | What it shows |
|---|---|---|
| `containers.ts` | `function-api/src/functions/containers.ts` | Azure Functions handlers (`POST/GET/PATCH/DELETE /containers`). Notable: lines 22-33 show **auto-fallback from app-only (CCA) to delegated (OBO) auth** when a confidential client is unavailable. |
| `GraphProvider.ts` | `function-api/src/providers/GraphProvider.ts` | Graph SDK wrapper with container CRUD, **`$filter=containerTypeId eq …`** for tenant-scoped listing, drive subscription/webhook helpers, and column management. |
| `mcp-server-permissions.ts` | `AI/mcp-server/src/tools/permissions.ts` | MCP tool registration for `list_permissions`, `grant_permission` (via `/drives/{id}/items/{id}/invite`), `update_permission`, `remove_permission`. Note these are **drive-item permissions**, not container-level (different surface). |

### `samples/powershell/` — bootstrap and registration scripts

From `Tools/powershell/`. Smallest self-contained examples of the full container lifecycle without an app stack.

| Local file | Source path | What it shows |
|---|---|---|
| `CreateContainer.ps1` | `Tools/powershell/CreateContainer.ps1` | Acquire client-credentials token → `POST /v1.0/storage/fileStorage/containers` → `POST /containers/{id}/permissions` to **activate** the container by granting the first owner. Single 118-line file showing the minimal create + permission flow. |
| `RegisterContainer.ps1` | `Tools/powershell/RegisterContainer.ps1` | Container **type** registration in a consuming tenant — JWT-signed client assertion → consumer-tenant token → `PUT /_api/v2.1/storageContainerTypes/{id}/applicationPermissions`. Required step before the consuming tenant can create containers of that type. |

### `samples/embedded-chat/` — embedded Copilot chat control

The SPE embedded chat is a packaged React component published as `@microsoft/sharepointembedded-copilotchat-react` (an out-of-registry `.tgz` from `download.microsoft.com`). The boilerplate-typescript-react sample ships **stubbed** `ChatSidebar` code (real implementation commented out, presumably awaiting the npm package being addressable in CI). The most complete annotated example currently in the repo is in `AI/prompts/`, written as a vibe-code prompt for Lovable/v0/Replit — **the embedded TypeScript inside the prompt is the canonical reference for the `<ChatEmbedded>` props and `ChatLaunchConfig` shape.**

| Local file | Source path | What it shows |
|---|---|---|
| `embedded-chat-react-prompt.md` | `AI/prompts/contoso-legal-agent.md` | Full TypeScript snippet (lines 38-137 of the prompt) showing `ChatEmbedded` component usage: auth provider with `${SP_HOST}/Container.Selected` scope, `ChatLaunchConfig` with theme/zeroQueryPrompts/instruction, `chatApi.openChat()` lifecycle, `containerId` binding. Includes installation note for the out-of-registry npm package URL. |
| `ChatAuthProvider.ts` | `react-client/src/providers/ChatAuthProvider.ts` | MSAL `PublicClientApplication` configured for **SharePoint-scoped tokens** (`${hostname}/Container.Selected`) — kept strictly separate from the Graph token. Uses `acquireTokenSilent` with popup fallback. |
| `ChatController.ts` | `react-client/src/providers/ChatController.ts` | `ChatLaunchConfig` content (`theme`, `zeroQueryPrompts`, `suggestedPrompts`, `instruction`, `locale`) and the **data-source binding pattern** that points the chat at a container's `drive.webUrl`. |

> **Note**: `ChatSidebar.tsx` in the upstream repo has the real implementation **commented out**. The active component body is empty. Treat the embedded-chat-react-prompt.md TS snippet as the canonical wiring example.

---

## Reference documentation snapshots

In `docs/`, captured with WebFetch on 2026-05-14:

| Local file | Source URL | Notes |
|---|---|---|
| `learn-overview.md` | `https://learn.microsoft.com/en-us/sharepoint/dev/embedded/overview` | Stable; light revisions expected |
| `learn-knowledge-source.md` | `https://learn.microsoft.com/en-us/sharepoint/dev/embedded/development/declarative-agent/sharepoint-embedded-knowledge-source` | **Preview status** — re-pull every refresh until GA |
| `learn-containers.md` | _Original URL 404'd_ → fallback `https://learn.microsoft.com/en-us/sharepoint/dev/embedded/development/app-architecture` | The directive's `concepts/app-concepts/containers` URL has been retired/moved; the app-architecture page covers the same container/container-type/owning-app concepts. |
| `learn-containertypes.md` | `https://learn.microsoft.com/en-us/sharepoint/dev/embedded/getting-started/containertypes` | Supplemental — captured because it covers container type creation (the gateway concept) which the missing containers/ page also discussed |
| `learn-semantic-index.md` | `https://learn.microsoft.com/en-us/microsoftsearch/semantic-index-for-copilot` | Stable but content-heavy; re-pull quarterly minimum |

---

## Gaps / known issues

1. **`concepts/app-concepts/containers` (directive URL) is 404 as of 2026-05-14.** Falling back to `development/app-architecture` and `getting-started/containertypes` captures the same concept material. Update the source list in `SPAARKE-KNOWLEDGE-BASE-SETUP.md` on the next refresh.
2. **No full `remoteSharePointParameters` JSON manifest** is in the SPE-Samples repo or on the Foundry knowledge-source Learn page — that schema lives on the linked `agentic-knowledge-source-how-to-sharepoint-remote` page (Azure AI Search docs). Curate that page if/when Foundry SharePoint knowledge source becomes a Spaarke implementation path.
3. **No runnable embedded-chat sample.** The boilerplate-typescript-react `ChatSidebar.tsx` body is commented out (presumably because the `@microsoft/sharepointembedded-copilotchat-react` package is distributed as an out-of-registry `.tgz` and Microsoft's CI can't reliably pin it). The vibe-code prompt is the cleanest annotated example currently available.
4. **VS Code extension repo (`microsoft/SharePoint-Embedded-VS-Code-Extension`) is extension source, not samples.** Per the directive's instruction to skip the bulk and snapshot the README, no code curated from this repo. The extension is the recommended path for spinning up a **trial** container type for development.

---

## Licence

All curated files originate from MIT-licensed Microsoft repositories. Original copyright headers preserved where present; the project's `LICENSE` files are not duplicated here (link from this index instead).
