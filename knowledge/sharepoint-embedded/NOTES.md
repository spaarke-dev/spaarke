> ⚠️ STUB — senior engineer review pending

# NOTES — `sharepoint-embedded`

Project-specific commentary on how Spaarke applies the curated SPE patterns, what to modify, and common pitfalls.

Section structure:

- **§1. How this fits Spaarke's architecture** — when to reach for this, role/composition with other surfaces, what it replaces or composes with, preview/cost/licensing implications, decision criteria
- **§2. How we build with it** — manifest/code shape, auth wiring, gotchas, Spaarke divergence from canonical samples, code review checklist

Both sections required for "done"; honest TODOs are fine for what isn't yet known. When annotating, remove the `⚠️ STUB` banner above only after both §1 and §2 have substantive content (or honest TODOs).

---

## 1. How this fits Spaarke's architecture

## Substrate semantic index — what's indexed and what isn't

_TODO: Confirm against `docs/learn-semantic-index.md` whether SPE container content lands in the **tenant-level semantic index** the same way SharePoint Online sites do. Spaarke's working assumption is yes (consuming tenant indexes the container partition), but verify with a test: upload a docx to an SPE container and search via Copilot in that tenant. Also document what the index does **NOT** expose directly to application code — index queries are only available through Copilot Chat and (preview) the Copilot Retrieval API._

---

## Copilot Retrieval API (pay-as-you-go preview)

_TODO: This is the only programmatic way to query the substrate index from application code. Capture: endpoint shape, billing model (per-query metering), how it scopes to a specific container or container type, and whether it respects Spaarke's row-level access (it should — index inherits container permissions). Until this is in GA and budget-approved, Spaarke uses Azure AI Search for application-grounded retrieval (see `knowledge/azure-ai-search/`)._

---

## Spaarke's pattern: one container per client (ADR-005)

_TODO: Reference ADR-005 — Spaarke provisions **one SPE container per client (matter / customer)**, not one container per document collection. Rationale: container = security boundary; per-client isolation makes auditing and offboarding clean. Document the provisioning entry point (the BFF endpoint that creates a container + assigns initial owner permission) and the Dataverse field that pins the `containerId` to a client record._

---

## BFF for upload and operations, Foundry SharePoint knowledge source for agent grounding

_TODO: Split of responsibilities:_
- _**BFF (`Sprk.Bff.Api/Services/SpeFileStore`)** — uploads, downloads, metadata writes, permission grants. App-only or OBO as appropriate. See `knowledge/azure-ai-search/` for the parallel AI Search ingestion path._
- _**Foundry SharePoint knowledge source (preview)** — agent-side grounding for declarative agents and Foundry Agent Service workflows. Configured at the agent level pointing at the SPE container type (see `docs/learn-knowledge-source.md`). Spaarke is not yet wired to this path; AI Search remains primary for now._
- _Document the decision criteria for "when to add Foundry SPE knowledge source vs. continue with AI Search": Foundry SPE wins on managed-substrate-index reuse (no separate ingestion); AI Search wins on chunking control, hybrid + semantic reranking, and permission-filter granularity._

---

## 2. How we build with it

## Container type registration

_TODO: Document the one-time bootstrap — owning-app container type creation in the owning tenant, then `RegisterContainer.ps1`-style registration in each consuming tenant. Capture Spaarke's actual container type IDs (dev / prod) and the `FileStorageContainerType.Manage.All` admin consent flow. Note that **standard container types cannot be deleted today** (only trial), so a botched production registration is permanent — call this out loudly._

---

## Permission scopes — application vs. delegated

_TODO: When does Spaarke use **app-only** (`FileStorageContainer.Selected` as application permission) vs. **delegated** (OBO from user)? Map the BFF call sites: container provisioning is app-only (no user context at scale-out time), end-user operations (upload, grant share, etc.) are delegated/OBO. The boilerplate's `containers.ts` lines 22-33 (auto-fallback CCA → OBO) is **not** the Spaarke pattern — we choose explicitly based on operation, not availability._

---

## webUrl-based opens and the "Document opens in Word with Copilot context" pattern

_TODO: Spaarke opens documents in Office (Word / Excel / PowerPoint Web/Desktop) via the container drive item's `webUrl` — **not** by downloading and re-uploading. Capture:_
- _The Graph call to get the `webUrl` (`GET /drives/{driveId}/items/{itemId}` → `webUrl` property)_
- _Why this matters for Copilot: when the document opens via webUrl in Office Web, the user's Copilot context picks up the SPE-stored file as if it were a regular SharePoint document — same Copilot ribbon, same grounding._
- _Contrast with download-and-open: loses Copilot context, breaks co-authoring, doubles storage._
- _The Spaarke document-record-to-document-open UX: clicking a Dataverse document record triggers `Xrm.Navigation.openUrl(webUrl)` (or a Code Page that does the same). Confirm exact pattern in `src/client/code-pages/SpeDocumentViewer/`._

---

## Common pitfalls

_TODO: As patterns get exercised, capture pitfalls observed in real Spaarke work. Candidates to watch for:_
- _Beta endpoint usage — `/beta/storage/fileStorage/containers` is the working endpoint; some operations are not yet on v1.0._
- _Container "activation" step — newly created containers are inactive until at least one owner permission is granted (see `CreateContainer.ps1`). The boilerplate-aspnet `ActivateContainer` method exists but isn't always called explicitly._
- _SPO token vs. Graph token isolation — `ChatAuthProvider` shows the SP-scoped (`Container.Selected`) token must be acquired separately from Graph tokens (Section 2 of `embedded-chat-react-prompt.md`). Mixing scopes in one request fails._
- _Container type registration must happen in **every consuming tenant** before containers can be created there — easy to forget when adding a new customer environment._
