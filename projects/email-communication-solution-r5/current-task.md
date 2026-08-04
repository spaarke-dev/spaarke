# Current Task State — `email-communication-solution-r5`

> **Last Updated**: 2026-08-03 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. This is an **owner-driven UAT-iteration** session on the
> shipped + deployed Email surface — NOT a fresh POML task.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Work** | Outlook-style Email surface — many UAT rounds (R1–R4) + compose launchers + widget/code-page parity |
| **Branch** | `work/email-communication-solution-r5` · **HEAD `4791af3b6`** (== master tip) · working tree CLEAN · pushed |
| **vs master** | **0 ahead / 0 behind.** Everything merged via PRs **#702–#721**. Main repo `c:/code_files/spaarke` master synced. |
| **Deployed** | ALL live on **spaarkedev1**: BFF (`spaarke-bff-dev`), `sprk_emailpage` (display name "Email") + `sprk_spaarkeai` web resources. |
| **Status** | ✅ **Everything done, merged, deployed.** Nothing stranded, nothing pending code-side. |
| **Next Action** | **Owner runs UAT on spaarkedev1** (hard-refresh Ctrl+Shift+R). If issues → iterate on branch → build+deploy (playbook below) → `/merge-to-master`. Else idle. |

### Verify/build commands (per package)
- **UI.Components** (composer engine): `cd src/client/shared/Spaarke.UI.Components` → `npx tsc --noEmit` · `npx jest EmailComposer SendEmailDialog ModalWindowControls openEmailCompose openEmailRecord`
- **Communication.Components** (reading pane/list/widget): `cd src/client/shared/Spaarke.Communication.Components` → `npx tsc --noEmit` · `npx jest EmailWorkspace EmailCardList EmailToolbar EmailReadingPaneShell EmailAssociationsAndTracking EmailComposeActions ensureAssociationColumns useEmailViews`
- **CRITICAL cross-package tsc**: Communication.Components' `tsc` resolves `@spaarke/ui-components` from its **built `dist/`**. After editing UI.Components, run `npm run build` there BEFORE typechecking Communication.Components, or you'll see phantom "stale-dist" errors (this bit us R4 — 4 transient errors from the `spaarke-modal-system` merge cleared after a UI.Components dist rebuild). The code-page **vite builds alias to SOURCE**, so deploys are unaffected by stale dist.
- Tests use **jest** (NOT vitest). Prettier gate repo-wide.

---

## DEPLOY playbook (spaarkedev1) — proven every round

**BFF** (only when server code changed): `pwsh -File scripts/Deploy-BffApi.ps1` (hardened: hash-verify + health). Verify new endpoints return **401** (registered) not 404.

**Code pages** (both alias shared libs to SOURCE — no dist rebuild needed, just clear vite cache):
- `cd src/solutions/EmailPage && rm -rf dist node_modules/.vite .vite && npm run build` → `dist/sprk_emailpage.html`
- `cd src/solutions/SpaarkeAi && rm -rf dist node_modules/.vite .vite && npm run build` → `dist/spaarkeai.html`
- **SpaarkeAi deploy**: `pwsh -File scripts/Deploy-SpaarkeAi.ps1` (defaults to spaarkedev1; upserts `sprk_spaarkeai` + publishes).
- **EmailPage deploy**: NO dedicated script — Web-API upsert (find `sprk_emailpage` by name → PATCH base64 content → PublishXml). Reusable script written this session at `C:/Users/RALPHS~1/AppData/Local/Temp/claude/deploy-emailpage.ps1` (regenerate if temp cleared — it's a ~15-line inline PS).
- **ALWAYS verify server-side** after: GET webresourceset content, base64-decode, grep a marker string.
- **`sprk_spaarkeai` is SHARED + clobber-prone** — BUT now **benign for merged work**: other projects redeploy it from master, which contains all my merges, so my markers survive (verified 2026-08-01: it was redeployed by another project and all my markers were intact). The durable protection = everything is in master.
- **Users must hard-refresh** (Ctrl+Shift+R) — Dataverse caches web resources aggressively.

---

## What shipped (session inventory — all in master + deployed)

- **R1/R2 + Wave E** (PR #702/#704): reading-pane + composer redesign, templates (`[document]` → `POST /api/communications/template/render`), AI sparkle (`POST /api/communications/draft` via `IEmailDraftAi` PublicContracts facade), per-user send, single-primary resolver, SPE anonymous share links at send (`POST /api/documents/{id}/share-link`).
- **R3 (#705–#708)**: resolver type-label ("Matter:" not "Record:") via `sprk_regardingrecordtype`; **true delete** of a confirmed denorm-only primary (`clearPrimaryRegarding`); link-card font (`fontFamily: inherit`) + equal-size cards; shared **`ModalWindowControls`** (maximize + close X, upper-right); streamlined resolver match wording + one-line clamp; state-driven resolver cards (confirmed → link-only; matches → no blank fillers); **compose "Related to" = in-memory MULTI-association list** (`ADD_ASSOCIATION` append + chip × remove; removed the single-primary replace-confirm).
- **R3.4 (#709)**: widget/code-page **compose-handler parity** — the LegalWorkspace `email` SECTION (`email.registration.ts`, which the SpaarkeAi "Email" tab actually renders) now wires all compose handlers.
- **R3.5 (#710)**: single-record email open hides ALL list chrome (list pane + view selector); renamed web-resource display name "Email Workspace" → **"Email"**.
- **openEmailCompose (#711)**: standalone compose launcher (new/reply/replyAll/forward) → `sprk_emailpage` compose mode (`EmailComposeStandalone`), closes its own window on Cancel/Send (`useEmailComposeActions` gained an `onClose` hook).
- **R4 (#721)**: **Send button → From row** (Outlook-style; new `ComposerSendButton`; bottom bar = Cancel + Save Draft); **"New" → list pane** (removed from reading toolbar; added "New email" + to `EmailCardList` via `onCreateNew` → `actions.onNew`); **restore left-list association dot** (`ensureAssociationColumns` injects association attrs into the view's FetchXML before running it).

---

## Locked design decisions (do NOT re-litigate)
- **Three email mounts, all must wire compose handlers** — see memory `email-surface-three-mounts-parity`. SpaarkeAi "Email" tab = LegalWorkspace `email` SECTION, NOT the direct widget.
- **Two launchers** (`@spaarke/ui-components`): `openEmailRecord(id, {single:true})` (read one email, clean single-record) + `openEmailCompose({mode, sourceCommunicationId})` (compose). Callers (Messages) import + call these — never `openForm`. `msg.id` MUST be the `sprk_communicationid` GUID.
- **Compose "Related to"** = in-memory multi-association (append/remove), `associations[0]` = primary the BFF maps.
- **Send** lives in the From row (item 1). **New** lives in the list pane (item 2). **Association dots** work regardless of view columns (item 3 injection).
- **AI drafting** = lightweight `IEmailDraftAi` facade (NOT the heavy Compose subsystem). Memory `email-drafting-templates-and-ai-decisions`.
- **SPE container** = ONE per deployment, from owner BU. Memory `spe-single-container-per-deployment`.

---

## Key file anchors
- **Composer engine**: `src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/` — `EmailComposer.tsx`, `subcomponents/{ComposerActionBar,ComposerSendButton}.tsx`, `createXrmEmailComposeHandlers.ts`, `wrappers/SendEmailDialog.tsx`, `openEmailRecord.ts`, `openEmailCompose.ts`, `ModalWindowControls/`.
- **Reading pane/list/widget**: `src/client/shared/Spaarke.Communication.Components/src/components/` — `EmailWorkspace/` (+ `.mapping.ts` dot tone), `EmailReadingPaneShell/{EmailReadingPaneShell,EmailToolbar}.tsx`, `EmailCardList/`, `EmailViewSelector/{useEmailViews,ensureAssociationColumns}.ts`, `EmailAssociationsAndTracking/` (resolver), `EmailComposeActions/useEmailComposeActions.tsx`, `logic/connections/` (`ConnectionsWriteHandler` clearPrimaryRegarding, `provenance` derivePrimaryReview/typeLabel).
- **Mounts**: code page `src/solutions/EmailPage/src/main.tsx` (list + single + compose modes); SpaarkeAi widget `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/EmailWorkspaceWidget.tsx`; **LegalWorkspace section `src/solutions/LegalWorkspace/src/sections/email.registration.ts`** (what SpaarkeAi's Email tab renders).
- **BFF**: `Api/{CommunicationDraftEndpoints,CommunicationTemplateEndpoints,FileAccessEndpoints}.cs`, `Services/Ai/PublicContracts/{IEmailDraftAi,EmailDraftAi,NullEmailDraftAi}.cs`, `Infrastructure/Graph/{DriveItemOperations,SpeFileStore}.cs`.

---

## How to continue
- Owner UAT clean → project effectively complete; idle.
- Owner UAT finds issues → iterate on this branch (file anchors + `/conflict-check` first, since many active projects share `@spaarke/communication-components` / `sprk_spaarkeai`), verify per commands, build+deploy (playbook), `/merge-to-master`, then `git merge --ff-only origin/master` in the main repo. Prefer parallel package-partitioned agents for multi-item batches (composer=UI.Components, reading/list=Communication.Components) — proven this session.
- **Messages-project follow-on (offered, not built)**: wire `openEmailCompose()` (New) + `openEmailCompose({mode, sourceCommunicationId})` (reply/forward) into `CommunicationsWorkspaceWidget` as a reference impl.
