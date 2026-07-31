# Current Task State — `email-communication-solution-r5`

> **Last Updated**: 2026-07-31 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. This is an **owner-driven UAT-iteration** session on the
> shipped + deployed Email surface — NOT a fresh POML task.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Work** | Email reading-pane + composer UAT iteration (2 batches) + Wave E (templates/AI sparkle) + SPE sharing links + open-email-as-form |
| **Branch** | `work/email-communication-solution-r5` · **HEAD `5e5286d51`** · working tree CLEAN · pushed |
| **vs master** | **0 ahead / merged.** All work merged to master via **PR #702** + **PR #704** (merge commit `43a1b8f86`). Main repo `c:/code_files/spaarke` master synced. |
| **Deployed** | ALL of it is live on **spaarkedev1**: BFF (`spaarke-bff-dev`), `sprk_emailpage` + `sprk_spaarkeai` web resources. |
| **Status** | ✅ **Everything done, merged, deployed.** Nothing stranded, nothing pending code-side. |
| **Next Action** | **Owner is running a UAT pass on spaarkedev1** (hard-refresh Ctrl+Shift+R first). If UAT finds issues → iterate on this branch → re-run `/merge-to-master`. Otherwise idle. |

### Verify/build commands (per package)
- **UI.Components** (composer engine): `cd src/client/shared/Spaarke.UI.Components` → `npx tsc --noEmit` · `npx jest EmailComposer SendEmailDialog shareLinks templatePicker aiSparkle uatR2Chrome`
- **Communication.Components** (reading pane/list/widget): `cd src/client/shared/Spaarke.Communication.Components` → `npx tsc --noEmit` · `npx jest EmailWorkspace EmailCardList EmailReadingPaneShell EmailConnectionsReview EmailRecipients EmailComposeActions EmailAssociations`
- **AI.Widgets**: `cd src/client/shared/Spaarke.AI.Widgets` → `npx tsc --noEmit`
- **BFF**: `dotnet build src/server/api/Sprk.Bff.Api/` · `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~CommunicationDraftEndpointTests|FullyQualifiedName~CommunicationTemplateEndpointTests"`
- **CRITICAL build order** (cross-package dist): edits to UI.Components require `npm run build` there BEFORE Communication.Components / AI.Widgets typecheck (they consume built `dist/`). Order: UI.Components → Communication.Components → AI.Widgets. `dist/` is gitignored.
- Tests use **jest** (NOT vitest). Prettier gate repo-wide.

---

## DEPLOY playbook (spaarkedev1) — proven this session

**BFF**: `pwsh -File scripts/Deploy-BffApi.ps1` (hardened: hash-verify + health). Target `spaarke-bff-dev` / `rg-spaarke-dev`. Verify new endpoints return **401** (registered) not 404.

**Code pages** (both alias shared libs to SOURCE — no dist rebuild needed, just clear vite cache):
- `cd src/solutions/EmailPage && rm -rf dist node_modules/.vite .vite && npm run build` → `dist/sprk_emailpage.html`
- `cd src/solutions/SpaarkeAi && rm -rf dist node_modules/.vite .vite && npm run build` → `dist/spaarkeai.html`
- **SpaarkeAi deploy**: `pwsh -File scripts/Deploy-SpaarkeAi.ps1` (defaults to `https://spaarkedev1.crm.dynamics.com`; upserts `sprk_spaarkeai` webresource + publishes).
- **EmailPage deploy**: NO dedicated script — same Web-API upsert pattern (find `sprk_emailpage` by name → PATCH base64 content → PublishXml). Inline PowerShell used this session (see transcript).
- **ALWAYS verify server-side** after: GET webresourceset content, decode base64, grep for a marker. **`sprk_spaarkeai` is a SHARED resource** — other active worktrees deploy it too and CAN CLOBBER your deploy (happened once at "coming soon"). Re-verify + redeploy if it reverts; durable fix = deploy from master.
- **Users must hard-refresh** (Ctrl+Shift+R) — Dataverse caches web resources aggressively even after publish.

---

## What shipped (full session inventory — all in master + deployed)

### Batch 1 + Wave E (PR #702, merge `42ba5758b`)
- UAT #1–#13 + list Today/Yesterday dividers + search + per-user send (#3) + single-primary Related-to (#8).
- **Wave E templates**: `[document]` compose toolbar button → lists OOB `template` (Xrm) → `POST /api/communications/template/render` (reuses `IEmailTemplateService`, `{!entity.field}` merge from primary regarding) → fills subject/body.
- **Wave E AI sparkle**: `[✨]` button, 6 presets + "Enter prompt" → `POST /api/communications/draft` via NEW `IEmailDraftAi` PublicContracts facade (real wraps `IChatClient`; `NullEmailDraftAi` mirror on the AzureOpenAI gate, ADR-032) → replaces body.
- **Open-email-as-form**: Pattern A (embed on `sprk_communication` form) + Pattern B (`openEmailRecord` launcher); Messages "open email" icon → new Email surface.

### Batch 2 (UAT round 2) — PR #704, merge `43a1b8f86`
- **`991db15d5`** — 11 UX items: (#1) list-pane 280px min-width anchor; (#2) elevated widget view header; (#3) auto-load first email; (#4) collapsible right-aligned search icon; (#5) **restored review status dots** — root cause: `hasAssociationData` gate ignored denorm `sprk_regardingrecordname/number`; (#6) reading `From:` plain text; (#7) Related-to card alignment; (#8) removed compose modal X (kept maximize); (#9) compose `From:` plain text + switcher kept; (#10/#11) Related-to "Link another record" inline + in New modal.
- **`63a339336`** — **#12 SPE sharing links**: emailed document "Link" now a recipient-openable file link. NEW `POST /api/documents/{id}/share-link` (OBO `DriveItemOperations.CreateSharingLinkAsUserAsync` → Graph createLink **view/anonymous**, on `SpeFileStore` facade). Client `onResolveShareLink` prop resolves at send (best-effort, NON-BLOCKING — failure keeps prior URL, never blocks send). Record links stay record links.

---

## Locked design decisions (do NOT re-litigate)
- **Templates**: OOB Dataverse `template` entity; merge from confirmed primary regarding (`associations[0]`). Memory `email-drafting-templates-and-ai-decisions`.
- **AI drafting**: lightweight sparkle → `IEmailDraftAi` facade → `IChatClient` (NOT the heavy Compose subsystem). Intent→instruction map server-side in `EmailDraftAi.cs` (prompt-library sourcing = future growth). NOT `IChatClient` injected into comms code (§10/ADR-013).
- **Per-user send (#3)**: CONFIRMED working in tenant (owner sent real email from ralph.schroeder@spaarke.com). `defaultSendMode='user'` correct. Graph OBO `/me/sendMail`, not server-side sync. `EmailChannelSender.cs`.
- **#12 sharing links**: owner chose **anonymous view** scope (external-recipient flexibility). ⚠️ Requires tenant SPE/SharePoint external-sharing policy to permit "Anyone" links; if disabled, createLink refused → send falls back to prior URL (never blocks). Switch to `organization` scope = one line in `FileAccessEndpoints.CreateShareLink`.
- **SPE container** (attachments): ONE per deployment, from owner BU. Memory `spe-single-container-per-deployment`.

---

## Key file anchors (for future iteration)
- **Composer engine**: `src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/EmailComposer.tsx` (+ `.types.ts`, `.reducer.ts`, `createXrmEmailComposeHandlers.ts`, `wrappers/SendEmailDialog.tsx`). Send-time share-link resolve: `resolveAttachmentShareLinks` (exported). Toolbar (`[attach][search]|[template][✨]`): `toolbarSlot`.
- **Reading pane/list/widget**: `src/client/shared/Spaarke.Communication.Components/src/components/` — `EmailWorkspace/` (composition root + `.mapping.ts` review-tone gate), `EmailReadingPaneShell/` (pane min-width + first-email adopt), `EmailCardList/` (search icon + dots + dividers), `EmailRecipients/` (From:), `EmailAssociationsAndTracking/EmailConnectionsReview` (Related-to cards), `EmailComposeActions/useEmailComposeActions.tsx` (compose dialog host + prop threading).
- **Widget mount**: `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/EmailWorkspaceWidget.tsx`. **Code-page mount**: `src/solutions/EmailPage/src/main.tsx`. Both build `createXrmEmailComposeHandlers` + pass all `on*` props.
- **BFF**: `Api/CommunicationTemplateEndpoints.cs`, `Api/CommunicationDraftEndpoints.cs`, `Api/FileAccessEndpoints.cs` (`CreateShareLink`), `Services/Ai/PublicContracts/{IEmailDraftAi,EmailDraftAi,NullEmailDraftAi}.cs`, `Infrastructure/Graph/{DriveItemOperations,SpeFileStore}.cs` (`CreateSharingLinkAsUserAsync`), `Infrastructure/DI/AiModule.cs` (IEmailDraftAi gate), `Infrastructure/DI/EndpointMappingExtensions.cs`.
- **Prop-threading pattern** (for any new compose capability): engine `IEmailComposerProps` → `SendEmailDialog` (spread) → `useEmailComposeActions` (deps type + destructure + pass) → `EmailWorkspace` (+ `.types.ts`) → both mounts. Xrm impl in `createXrmEmailComposeHandlers` (present only with `authenticatedFetch` + `bffBaseUrl`).

---

## How to continue
- If owner UAT is **clean** → project effectively complete; nothing to do.
- If owner UAT finds **issues** → iterate on this branch (use the file anchors + threading pattern above), verify per the commands, commit, redeploy to spaarkedev1 (deploy playbook), then `/merge-to-master` (update from master → PR → auto-merge; sync main repo). For parallel multi-item batches, partition by package (composer=UI.Components, reading=Communication.Components) to avoid file collisions — proven twice this session.
- Harness for local dev (native Xrm NO-OPS there — deploy-validated only): `http://localhost:5175/`.
