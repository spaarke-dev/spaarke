# Phase 0 Spike — External ID + SPE identity bridge

> **Date**: 2026-07-18
> **Question**: Does an Entra External ID (CIAM) identity give an external user BOTH app login AND SPE document access, or does SPE force a parallel workforce Entra B2B guest per user (reintroducing dual-identity tension)?
> **Verdict**: **GREEN** for a read/download portal — no workforce B2B guest required. **RED** only for direct-Office features (out of scope).
> **Method**: Microsoft Learn research + code trace of the current BFF external path (no live tenant provisioned yet — this resolves the *feasibility/architecture* question that gates the project; a live end-to-end run is deferred into Phase 2 as verification, not a gate).

---

## The answer

The Spaarke external portal is a **pure BFF-broker**. The external user's identity is used **only to authenticate to the BFF**; it never reaches SharePoint Embedded or Graph. Every external-surface SPE + Dataverse call is **app-only / managed identity**. Therefore the external user does not need any Entra identity in the workforce tenant — a **CIAM (External ID) identity is sufficient**, and the migration cleanly removes the corporate-account-vs-guest login conflict (which was itself a symptom of the B2B-guest model).

## Code evidence (current main working tree)

- **No file-content path exists for external users today** — the only document endpoint is metadata-only: `GET /api/v1/external/projects/{id}/documents` → `ExternalDataService.GetDocumentsAsync` queries Dataverse `sprk_documents` (`ExternalDataService.cs:192-200`); the DTO has no downloadUrl/driveId/driveItemId (`ExternalProjectDtos.cs:54-73`).
- **All external SPE/Graph = app-only (`ForApp()`)**: `GrantExternalAccessEndpoint.cs:237`, `RevokeExternalAccessEndpoint.cs:190`, `InviteExternalUserEndpoint.cs:157`, `ContainerOperations.cs:43`. `ForApp()` uses `DefaultAzureCredential` / MI (`GraphClientFactory.cs:109-165`).
- **All external Dataverse reads = app-only token**: `ExternalDataService.cs:580-607`, `ExternalParticipationService.cs:227-251`.
- **The container grant is not a real per-user Entra grant**: it posts a container permission with a **synthetic** `LoginName = "i:0#.f|membership|contact_{contactId}"` (`GrantExternalAccessEndpoint.cs:251`) via app-only Graph, and failure is **non-fatal** (`:135-142`) — the `sprk_externalrecordaccess` Dataverse row is the authorization source of truth.
- **OBO is never used on the external path** — `ExternalCallerAuthorizationFilter` only extracts the email claim (`:63-66`) → resolves a Dataverse Contact app-only → stashes `ExternalCallerContext` on `HttpContext.Items`; the user token is never exchanged downstream.

## Microsoft platform confirmation

- SPE container **delegated** access requires the user be a member/B2B-guest of the **consuming (workforce) tenant** — a CIAM user is not, so cross-tenant delegated SPE access is **not** available. (SPE auth doc, updated 2026-07-15.)
- **But app-only sidesteps this**: `FileStorageContainer.Selected` (application) + container-type **`ReadContent`** permission lets an app download/stream content with **no user identity** — "an app that accesses containers without a user gets the full access defined by its container type application permissions." `GET /drives/{id}/items/{id}/content` is a content read → app-only.
- The **July 2026 mandatory-Entra-B2B** rollout governs *user-delegated* external sharing (SPO/OneDrive links). **App-only container access does not depend on guest accounts or sharing links → untouched.** It actually strengthens the app-only case.

## Boundary — what would force a workforce B2B guest (RED, out of scope)

Direct-Office / user-identity features that need the user's own token reaching SPE, impossible for CIAM:
- Word/Excel/PowerPoint **for Web co-authoring**
- Office **desktop open via `webUrl`**
- **Copilot grounding on the user's identity**
- **Microsoft Search** (delegated `Files.Read.All`)

None are used by the external portal today; all are out of R1 scope. Captured as limitation **E-3** in the ADR-028 amendment.

## Implications for R1

1. **Proceed with CIAM-only** for the external portal identity; do not provision workforce B2B guests. (ADR-028 Amendment A1.)
2. **Add an app-only content-download path** — the external surface currently serves metadata only. Exposing downloads/previews needs `SpeFileStore.DownloadContentAsync(driveId, itemId)` (app-only) with Dataverse authz enforced before the call. Small BFF addition, subject to §10 BFF hygiene. NOTES.md implies the existing user-download path is OBO (`DownloadFileAsUserAsync`) — do NOT reuse that for the external surface.
3. **Drop or no-op the synthetic container grant** — the `contact_{guid}` container permission is vestigial under broker-only; it never mapped to a real Entra identity anyway.
4. **Verification deferred to Phase 2** (not a gate): live end-to-end against a real External ID tenant + SPE container; confirm app-only `/thumbnails` + `ReadContent` cover preview UX (~30 min).

## Sources
- SPE — Configure authentication & authorization (app-only vs delegated, `ReadContent`) — https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/configure-authentication-authorization
- FAQ: Entra B2B integration for external sharing (July 2026 rollout) — https://learn.microsoft.com/en-us/sharepoint/faqs-odspintegrationwithentrab2b
- Researcher deep-dive: `.claude/agent-memory/researcher/spe-ciam-crosstenant-apponly-brokering-2026-07-18.md`
- `knowledge/sharepoint-embedded/NOTES.md` — `SpeFileStore` app-only vs OBO split
