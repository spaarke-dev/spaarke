# Current Task State — `email-communication-solution-r5`

> **Last Updated**: 2026-07-30 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. This is a **UAT-iteration** session on the
> shipped Email surface — owner-driven rapid iteration, NOT a fresh POML task.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Work** | Email reading-pane + composer UAT iteration + Wave E (templates + AI drafting) + open-email-as-form wiring |
| **Branch** | `work/email-communication-solution-r5` · **HEAD `394c62d6f`** · working tree CLEAN · all pushed to origin |
| **vs master** | **8 commits ahead of `origin/master`** (the UAT batch below). NOT yet merged to master. |
| **Status** | All owner UAT items #1–#13 + list dividers + per-user send + #8 DONE. Wave E in progress: BFF template endpoint DONE; **template picker (client) is the NEXT action**. |
| **Next Action** | Build the **compose-toolbar template picker** — see "NEXT: Wave E template picker" below. Then AI sparkle, then wire Messages "open email" icon. |
| **Harness** | `http://localhost:5175/` (may be stale) — `SPAARKE_REPO_ROOT="c:/code_files/spaarke-wt-email-communication-solution-r5" npm run dev` in `c:/code_files/spaarke-prototype/projects/email-communication-solution-r5-uat`. Native Xrm (lookup/upload/template/nav) NO-OPS in the harness — deploy-validated only. |

### Verify/build commands (per package)
- **UI.Components** engine/composer/launcher: `cd src/client/shared/Spaarke.UI.Components` → `npx tsc --noEmit -p tsconfig.json` · `npx jest EmailComposer SendEmailDialog openEmailRecord`
- **Communication.Components** reading pane/workspace: `cd src/client/shared/Spaarke.Communication.Components` → `npx tsc --noEmit -p tsconfig.json` · `npx jest EmailWorkspace EmailCardList EmailConnectionsReview EmailReadingPaneShell EmailComposeActions`
- **AI.Widgets**: `cd src/client/shared/Spaarke.AI.Widgets` → `npx tsc --noEmit -p tsconfig.json`
- **BFF**: `dotnet build src/server/api/Sprk.Bff.Api/` · `dotnet test ... --filter CommunicationTemplateEndpointTests`
- **CRITICAL cross-package build order**: edits to UI.Components require `npm run build` there BEFORE Communication.Components / AI.Widgets typecheck (they consume the built `dist/` .d.ts, not source). Order: UI.Components → Communication.Components. `dist/` is gitignored (never commit it).
- Tests use **jest** (NOT vitest). Prettier gate is repo-wide (`npx prettier --write` then `--check` your changed files before commit).

---

## DONE this session — 8 commits (all pushed)

| Commit | Items | Notes |
|--------|-------|-------|
| `967f17b30` | **#1** Email tab loads real widget | `WorkspacePane` FIX #10b intercept gated to the compose DRAFT hand-off only (payload has `mode`/`bodyText`/`attachmentFileName`); plain `email` tab falls through to registry → real `EmailWorkspaceWidget`. Stub kept for the draft hand-off. +regression test. SpaarkeAi-only (code page never had the stub). |
| `884e809ae` | **#2,4,5,6,7,9,10** reading polish | pane 20/80 default; subject line above body; "Link another" card matches candidate cards; single-click opens type dropdown; Related-to collapses when 🟢 confirmed; primary green; reading title 18px. Communication.Components. |
| `ca370e3fd` | **#3,10,11,12,13** compose polish | From row above To/Cc; compose title 18px; maximize/restore (fills app container); `modalType="alert"` (no light-dismiss → role `alertdialog`); space above quoted thread. UI.Components. |
| `9d9d59690` | list dividers + search | `EmailCardList` Today/Yesterday/This Week/This Month/Older via pure `bucketEmailsByDate(items, now)`; search box filters sender/subject. Communication.Components. |
| `719199430` | **#3** per-user send | Email surface defaults From to current user (new `defaultSendMode` seed keeps switcher interactive — do NOT use `sendMode` which LOCKS the switcher); resolves signed-in email via `resolveCurrentUserEmail()` (`systemuser.internalemailaddress`) at both mounts → `fromMailbox`. Threaded engine→SendEmailDialog→useEmailComposeActions→EmailWorkspace→mounts. |
| `6daa4ca65` | **#8** compose single-primary | `associations[0]` = primary regarding (BFF `MapAssociationFields` writes `sprk_regardingrecord*` from index 0 — CLIENT-ONLY ordering, no send-contract change). "Link another" → picker → confirm "Set as primary / Will replace current" → `SET_PRIMARY_ASSOCIATION` promotes to index 0. Primary chip green. Inline label+chips+link, font parity. |
| `f42061e9c` | Wave E template endpoint | `POST /api/communications/template/render { templateId, regardingEntityType?, regardingRecordId? } → { subject, body, isHtml }`. Reuses `EmailTemplateService.FetchAndRenderAsync` + `IGenericEntityService`. `CommunicationTemplateEndpoints.cs` + `EndpointMappingExtensions.cs` reg + 6 tests. App-only Dataverse read (OBO variant is a noted follow-up). |
| `394c62d6f` | open-email-as-form | EmailPage resolves record id (Pattern B `data` param / `id` / Pattern A host-form context) → `initialSelectedId` + `hideList`. `hideList` single-record mode in `EmailWorkspace` + `EmailReadingPaneShell`. `openEmailRecord(id,{single})` launcher exported from `@spaarke/ui-components`. Tests 19+5. |

---

## Locked design decisions (do NOT re-litigate)

- **Templates**: OOB Dataverse `template` entity (NOT a custom entity/designer). Merge `{!entity.field}` from the **confirmed primary regarding** via the existing BFF `EmailTemplateService`. Template LIST = client-side Xrm on `template`; RENDER = the new BFF endpoint. See memory `email-drafting-templates-and-ai-decisions`.
- **AI drafting**: lightweight **email sparkle** on the compose toolbar → common prompts + "Enter prompt" → **BFF draft endpoint** (NOT the heavy `Spaarke.Compose` doc subsystem). Prompts sourced from the **prompt library** (admin-editable), not hardcoded. 6 starter prompts: *Draft a reply · Summarize the thread · Make it concise · Formal tone · Friendly tone · Fix grammar & tone* + Enter prompt.
- **Per-user send (#3)**: email surface defaults to current-user send. ⚠️ **OPEN**: owner said "we need to test the per user" — confirm the sandbox/prod is configured for per-user (send-as) mail; if only the shared mailbox is provisioned, user-send will fail (fall back to shared default). BFF supports it (`CommunicationSendMode = 'sharedMailbox' | 'user'`).
- **Word templates**: a SEPARATE future effort (OOB Dataverse Document Templates, pairs with the Compose subsystem). Do NOT couple email templates to it now. A unified Spaarke templating concept is a possible larger later project.
- **SPE container** (attachments, item 9b earlier): ONE per deployment, resolved from the owner BU (`businessunit.sprk_containerid`). Memory `spe-single-container-per-deployment`.
- **Form patterns**: BOTH Pattern A (embed as the `sprk_communication` form) AND Pattern B (`openEmailRecord` launcher for icons like the Messages "open email"). Foundation shipped in `394c62d6f`.

---

## NEXT: Wave E template picker (the immediate next action)

Build a `[📄]` template button in the compose body toolbar (`EmailComposer.tsx` toolbarSlot — same area as the paperclip/search/`| [template] [✨AI]` group). Flow:
1. **List templates** client-side via the host: add a handler (mirror `createXrmEmailComposeHandlers` pattern) that queries the OOB `template` entity via Xrm.WebApi (email templates; filter by `templatetypecode`/appropriate). Thread it as an optional prop like `onListEmailTemplates` / a picker callback (additive, like `onLookupRecord`).
2. **Render**: on pick, call `POST {bffBaseUrl}/api/communications/template/render` with `{ templateId, regardingEntityType, regardingRecordId }` = the **primary regarding** (`state.associations[0]`) via `authenticatedFetch`. Response `{ subject, body, isHtml }`.
3. **Apply**: fill the composer `subject` (if empty or confirm-overwrite) + insert/replace `body` (respect `isHtml` → set bodyFormat). Reuse the engine's SET_FIELD / body set.
- Thread any new prop through: `EmailComposer.types.ts` → `SendEmailDialog` (spread) → `useEmailComposeActions` → `EmailWorkspace` (+types) → both mounts (`EmailPage/main.tsx`, `EmailWorkspaceWidget.tsx`). Same threading pattern used for `onUploadLocalAttachment` / `fromMailbox` this session.
- Then AI sparkle (needs a BFF DRAFT endpoint — `EmailDraftService.cs` exists but is matter/agent-specific; likely add a thin generic `POST /api/communications/draft { intent, userInstruction?, currentBody, thread?, regarding?, mode:generate|refine } → { text }` sourcing prompt text from the prompt library). Then a `[✨]` dropdown (6 prompts + Enter prompt) → call it → propose draft into body.
- Then wire the **Messages "open email" icon** → `openEmailRecord(id)` (locate the Messages surface's email-open call site; replace OOB openForm/navigate with the launcher).

### Key anchors for Wave E
- BFF render endpoint: `src/server/api/Sprk.Bff.Api/Api/CommunicationTemplateEndpoints.cs`
- Existing services: `Services/Ai/Delivery/EmailTemplateService.cs` (`FetchAndRenderAsync(templateId, variables, dataverseUrl, accessToken, ct)`); `Api/Agent/EmailDraftService.cs` (matter-specific — DON'T force-fit; add a generic draft endpoint); prompt library `Api/Ai/PromptLibraryEndpoints.cs` / `PlaybookEndpoints.cs`.
- Compose toolbar: `EmailComposer.tsx` `toolbarSlot` (paperclip Menu + record SearchRegular Menu already there; add `| [template] [✨]`).
- Launcher already exported: `openEmailRecord` from `@spaarke/ui-components`.

---

## Verification status (all GREEN at handoff)
- UI.Components / Communication.Components / AI.Widgets typecheck clean against rebuilt dists.
- jest: composer 157–164, reading/workspace/shell 71+, EmailCardList 16, openEmailRecord 5, WorkspacePane email-stub 4. BFF: 6 template-endpoint tests. Prettier clean.
- Pre-existing unrelated failures (documented, verified via git-stash baseline): `useEmailComposeActions` attachment-enum (reading side), some Compose/Timeline/WorkspaceShell suites, EmailPage cross-tree `tsc-surface-gate` reports 66 pre-existing shared-lib errors (canonical package typecheck is clean).

## Memory files written this session (in `~/.claude/projects/.../memory/`)
- `spe-single-container-per-deployment` · `email-drafting-templates-and-ai-decisions` · indexed in `MEMORY.md`.

## Deploy / merge notes
- Deploy is FRONTEND-only for the client waves (Email code page `sprk_emailpage` web resource + `@spaarke/communication-components` widget bundle) PLUS the BFF for the new template endpoint (`f42061e9c` — first BFF change this batch; publish-size negligible, no new deps).
- Branch is 8 ahead of master, pushed but NOT merged. Merge-to-master path: branch protection OFF → direct FF `git push origin HEAD:master` + sync main repo (`c:/code_files/spaarke` → `git fetch && checkout master && merge --ff-only origin/master`). Run Prettier gate first.

## How to continue
Say **"continue"** or **"build the template picker"**. Start at `EmailComposer.tsx` toolbarSlot; thread the new template-picker prop the same way `fromMailbox`/`onUploadLocalAttachment` were threaded this session; call the shipped `POST /api/communications/template/render`.
