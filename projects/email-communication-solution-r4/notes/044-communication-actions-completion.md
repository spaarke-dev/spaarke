# Task 044 — Communication Actions PCF + on-demand archive + ribbon retirement — Completion Note

> **Status**: ✅ complete · 2026-07-15 · FULL rigor · Step 9.5 gates run (code-review + adr-check) · absorbs task 062

## What shipped (three parts)

### 044a — `POST /api/communications/{id}/archive` (server)
- **`CommunicationService.ArchiveExistingAsync`** reconstructs a `SendCommunicationRequest`/`Response` from the stored `sprk_communication` and reuses `ArchiveToSpeAsync` (.eml Document) + a dedicated per-attachment Document loop. Idempotent (existing email-archive Document → `AlreadyArchived`); attachments evaluated independently so a file added after a prior archive is still picked up.
- **`ArchiveCommunicationResult`** model; endpoint mirrors `/send` (`CommunicationAuthorizationFilter` + `RequireAuthorization` + ProblemDetails).
- **2 behavior tests** (idempotency, not-found→404).

### 044b — CommunicationActions PCF (`src/client/pcf/CommunicationActions/`)
- Virtual PCF (React 16.14 / Fluent 9.46.2 platform libs, ADR-022) on the OOB form. Bootstraps `@spaarke/auth` (`initAuth`), hosts the shared `<SendEmailPage/>` in a dialog for Reply/Forward/Send, and calls existing `/send` (via composer) + new `/{id}/archive` (via `authenticatedFetch`, ADR-028). Save Draft → Xrm form save.
- Pure `deriveComposerFields` helper (Reply→sender+"Re:", Forward→"Fwd:", Draft→recipients) with **6 tests**. build:prod clean (bundle 2.33 MB incl. msal).

### 044c — Ribbon "Send" retirement (absorbs task 062)
- Removed both `sprk_communication_send.js` copies; emptied `Entities/sprk_communication/RibbonDiff.xml`; removed the send WebResource from `Other/Customizations.xml`.
- **Equivalence verified**: the ribbon's `sendCommunication` and the PCF composer both POST the identical `/api/communications/send`.
- **🔎 "Create To Do" button KEPT** — `createtodo-button.xml` is a LIVE feature (smart-todo-decoupling-r3, launches the real CreateTodoWizard), not a dead-end. It is a separate file, untouched.
- **Deployed-artifact removal happens at task 043** (solution re-import) or by admin removal in the target environment — the source retirement here does not itself remove the deployed button.

## §10 BFF Hygiene (placement justification — per adr-check W1)
- **Placement**: `/{id}/archive` **extends the existing `CommunicationService`** (new method, no new service/interface/package); registered via `MapCommunicationEndpoints` (not `Program.cs`); `CommunicationModule.cs` unchanged. No CRUD→AI dependency.
- **Publish-size**: no NuGet package added → delta ~0 (IL-only). Well under the +5 MB escalation threshold.
- **CVE**: sole HIGH is `Microsoft.Kiota.Abstractions` (transitive via Microsoft.Graph) — **pre-existing**, owned by `trivy-cve-cleanup`, not introduced by 044.
- **Tests**: added in `tests/unit/Sprk.Bff.Api.Tests/`.

## Step 9.5 gate outcomes
- **adr-check: 0 violations** (7 ADRs compliant + §10 + §11). Warnings: placement-justification location (addressed here), correlationId-parity on archive errors (global handler stamps traceId — accepted), unused `tenantId` manifest input (kept for parity with SemanticSearchControl pattern — accepted).
- **code-review: 0 Critical.** 4 Warnings — **all fixed**:
  - **W1** archive Document hardcoded direction "Sent" → now maps `sprk_direction` (Incoming→Received / else Sent) via an optional `ArchiveToSpeAsync` param.
  - **W2** attachment drive id → now uses each attachment's own `sprk_graphdriveid` (falls back to ArchiveContainerId).
  - **W3** dead attachment idempotency-skip → the created Document is now linked back onto `sprk_communicationattachment.sprk_document`, and attachment archival runs independently of the `.eml` gate (so re-archive picks up new attachments). **S5** (partial-failure recoverability) resolved by the same change.
  - **W4** broad catch→404 on retrieve → accepted (consistent with the sibling `GetCommunicationStatusAsync`; Dataverse RetrieveAsync throwing for not-found is the normal case).
  - Suggestions: **S2** dead `!resp.ok` branch → kept intentionally (defensive; safer than relying on `authenticatedFetch` throw behavior). **S3** compose buttons enabled before prefill → **fixed** (gated on prefill load). **S1** .eml AI-analysis-enqueue parity + attachment analysis → attachment analysis added; `.eml` analysis left (the body already lives in Dataverse) — noted.

## Next
- **043** deploys both PCFs (Connections + Actions), packs the "Communications Awaiting Association" view, configures the OOB form (place both PCFs + auth config env vars), and removes the deployed send button/web resource.
