---
description: Integrate with SharePoint Embedded — containers, permissions, agent grounding, document opens
tags: [spe, sharepoint-embedded, storage, graph, container]
techStack: [aspnet-core, csharp, microsoft-graph, sharepoint]
appliesTo: ["**/SpeFileStore*.cs", "**/Spe*.cs", "**/Graph*.cs", "**/containers/**"]
alwaysApply: false
---

# spe-integration

> **Category**: Development
> **Last Updated**: 2026-05-14

---

## Purpose

Anchor SharePoint Embedded (SPE) integration work in current Microsoft idioms — container CRUD, permissions, container type registration, SPE-as-Foundry-knowledge-source, the substrate semantic index, and the webUrl-based "open document in Word with Copilot context" pattern. Without this skill, generated code tends to inject `GraphServiceClient` directly into controllers (breaks ADR-007), download-and-reopen files (instead of using webUrl), or assume SPE behaves like classic SharePoint.

---

## Applies When

- Implementing container CRUD or permission management
- Building or modifying anything that touches `SpeFileStore` or its Graph dependencies
- Registering a new container type in a consuming tenant
- Wiring SPE as a knowledge source for a declarative agent or Foundry agent
- Implementing "open in Word/Excel/PowerPoint with context" flows
- Querying the substrate semantic index (via Copilot Retrieval API) over SPE content
- **NOT applicable** for: declarative agent manifest authoring (use `declarative-agent`), Foundry IQ KB over SP remote (use `foundry-agent`)

---

## Workflow

### Step 1: Load knowledge context (mandatory)

Read in this order:

1. **`knowledge/sharepoint-embedded/NOTES.md`** — Spaarke's pattern: one container per client (ADR-005), webUrl-based opens, BFF facade.
2. **`knowledge/sharepoint-embedded/samples/container-crud-csharp/`** — Container + permission CRUD via Microsoft Graph, isolated extract.
3. **`knowledge/sharepoint-embedded/samples/container-crud-typescript/`** — Same operations from a Functions handler + MCP tool style.
4. **`knowledge/sharepoint-embedded/samples/powershell/`** — Container type registration (`RegisterContainer.ps1`) and minimal create flow (`CreateContainer.ps1`).
5. **`knowledge/sharepoint-embedded/samples/embedded-chat/`** — `<ChatEmbedded>` reference (boilerplate is non-runnable upstream — annotated snippet is the canonical reference per `SOURCE.md`).
6. **`knowledge/sharepoint-embedded/docs/learn-overview.md`** + **`docs/learn-containers.md`** + **`docs/learn-containertypes.md`** + **`docs/learn-knowledge-source.md`** + **`docs/learn-semantic-index.md`** — Microsoft Learn snapshots (only read the ones relevant to your specific work pattern).

### Step 2: Apply Spaarke contracts (ADR-007, ADR-005, ADR-001)

- **ADR-007 (SpeFileStore facade)**: **No Graph SDK types in controllers or handlers above the facade.** All Graph calls go through `SpeFileStore` or its helpers in `Sprk.Bff.Api/Infrastructure/Graph/`. If you need a Graph capability the facade doesn't expose, extend the facade — don't bypass it.
- **ADR-005 (Flat storage in SPE)**: **No folder hierarchies in SPE containers.** Represent hierarchy via Dataverse metadata (`sprk_document` + `sprk_documentassociation`). Evaluate permissions via UAC (not SPE native ACLs). MUST access SPE only via `SpeFileStore`.
- **ADR-001 (Minimal API)**: SPE operations are exposed via endpoints in `Sprk.Bff.Api`. No separate SPE microservice.
- **Operational policy** (not yet ADR): one container per client. Scope new SPE-backed operations to the relevant client's container. Cross-client operations require explicit BFF-level authorization — never assume.

### Step 3: Choose the auth pattern

| Operation | Auth pattern | Where to look |
|---|---|---|
| User-initiated read/write of a file | OBO (On-Behalf-Of) — user token | `Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` |
| Background indexing, system-level container ops | App-only (client credentials) | Same factory; app-only path |
| Container type registration in a new tenant | App-only via PowerShell during onboarding | `RegisterContainer.ps1` |

**Default**: OBO for user-facing endpoints; app-only only when the operation has no acting user.

### Step 4: webUrl-based document opens (don't download-and-reopen)

The "Document record opens in Word with Copilot context" pattern is **not** a download flow. Per `knowledge/sharepoint-embedded/NOTES.md` (Spaarke pattern):

1. From the document record, get the SPE driveItem's `webUrl`
2. Pass `webUrl` to `Xrm.Navigation.openUrl` or equivalent
3. Word/Excel opens the file in SPE context — Copilot Chat side panel automatically grounds on the document
4. **Do NOT** download the file, save to OneDrive, and reopen. That breaks the Copilot grounding and adds a lifecycle problem.

### Step 5: Container type registration

When onboarding a new tenant or environment:

- Use `RegisterContainer.ps1` — registers the container type with the consuming tenant's Entra AD
- Requires global admin consent at first run
- The registration must happen BEFORE creating containers in that tenant
- Document the registered container type ID in `infrastructure/` or environment config

### Step 6: SPE as Foundry knowledge source

- For agent grounding over matter documents: use the **SharePoint knowledge source** in a Foundry agent or declarative agent
- The knowledge source points at a SharePoint Online URL or, for SPE, the SPE container's SharePoint-equivalent endpoint
- The substrate semantic index automatically indexes SPE content — no manual indexing needed
- For application-code queries (not agent grounding), use the **Copilot Retrieval API** (pay-as-you-go preview)
- See `knowledge/foundry-iq/samples/kb-with-sharepoint-remote/` for the remote SP knowledge source pattern (cross-reference)

### Step 7: Code review checklist

- [ ] No `GraphServiceClient` injected above `SpeFileStore` (ADR-007)
- [ ] No folder hierarchies created in SPE (ADR-005); hierarchy lives in Dataverse via `sprk_documentassociation`
- [ ] Container scoping respects operational policy (one per client; no cross-client leakage)
- [ ] Auth pattern matches the operation (OBO for user-initiated, app-only for system)
- [ ] webUrl pattern used for document opens (not download-and-reopen)
- [ ] Permission filtering applied at the right layer (container ACLs, not just BFF-level)
- [ ] OTel tracing on the Graph call layer

---

## Conventions

- SPE file ops live behind `SpeFileStore` in `src/server/api/Sprk.Bff.Api/Infrastructure/SpeFileStore/`
- Graph client factory: `Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs`
- Container type IDs: stored in environment config, never hardcoded
- PowerShell registration scripts: `infrastructure/spe/Register-*.ps1`

## Resources

| Resource | Purpose |
|----------|---------|
| `knowledge/sharepoint-embedded/NOTES.md` | Spaarke pattern: one-container-per-client, webUrl opens, BFF facade |
| `knowledge/sharepoint-embedded/samples/container-crud-csharp/` | Container + permission CRUD in C# |
| `knowledge/sharepoint-embedded/samples/powershell/RegisterContainer.ps1` | Container type tenant registration |
| `knowledge/sharepoint-embedded/docs/learn-overview.md` | Microsoft Learn SPE overview (also: `learn-containers.md`, `learn-containertypes.md`, `learn-knowledge-source.md`, `learn-semantic-index.md`) |

## Output

When this skill completes, expect:
- SPE work landed through `SpeFileStore` (or facade extension) — no Graph types leaked
- Container scoping enforced
- webUrl pattern used for any "open in Office" flow
- Reference updates in `knowledge/sharepoint-embedded/NOTES.md` if a new Spaarke-specific pattern emerges
