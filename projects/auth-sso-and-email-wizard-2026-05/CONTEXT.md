# Active Work — Auth SSO Propagation + Email Documents Wizard

> **Last Updated**: 2026-05-13 (carried over from intensive session 2026-05-12)
> **Status**: In progress — auth fix partially propagated, email wizard built but unverified end-to-end
> **Priority**: User is blocked by login popups on every tab open. Auth propagation is top priority.

---

## Quick Recovery — Where We Are Right Now

**Three parallel tracks were active when we paused:**

1. **AUTH FIX (highest priority)** — Root cause found and fix landed in library. Only 2 of ~30 consumers have been rebuilt to pick it up. User is still seeing popups on every tab open because Daily Briefing was rebuilt but other consumers (loaded on the Corporate Counsel app) likely still bundle the old library.

2. **EMAIL DOCUMENTS WIZARD** — Wired into SemanticSearchControl PCF v1.1.40. Three sub-bugs were fixed (attachment lookup, wrong playbook, single-call combined summary). Demo deploy of the BFF attachment fix is deferred. User has NOT yet verified the wizard works end-to-end.

3. **DocumentRelationshipViewer Phase C** — Pending. Wire the same Email button + multi-select pattern into DRV. Has not been started.

---

## The Auth Bug — Full Story

### Symptom
User opens a new tab → Corporate Counsel app loads → Microsoft "Pick an account" popup appears. Same on every fresh tab. Recurring even with valid AAD session.

### Root Cause (confirmed via console diagnostic)
`@spaarke/auth` library has had `DEFAULT_AUTHORITY = 'https://login.microsoftonline.com/organizations'` since the initial commit (`edb6fdcc`). This multi-tenant authority breaks MSAL.js `ssoSilent` inside iframes — AAD doesn't know which tenant's session cookie to use.

Combined with browser COOP enforcement (`Cross-Origin-Opener-Policy policy would block the window.closed call`), iframe-based silent auth fails entirely → all 6 strategies fail → `MsalPopupStrategy` fires the popup.

The 6-strategy chain in `SpaarkeAuthProvider.ts` (in order):
1. `CacheStrategy` (in-memory, per-instance)
2. `SessionStorageStrategy` (`__spaarke_bff_token_cache__` — shared across same-origin iframes)
3. `BridgeStrategy` (`window.__SPAARKE_BFF_TOKEN__` — parent frame walk)
4. `XrmStrategy` (Xrm.WebApi — but can only get Dataverse-scoped tokens, not BFF)
5. `MsalSilentStrategy` (`acquireTokenSilent` + `ssoSilent` — this is what fails on `/organizations`)
6. `MsalPopupStrategy` (last resort — fires the popup)

### Why this wasn't noticed before
Multiple commits over time tried to *armor around* the issue:
- `9cee57cd` — added loginHint to ssoSilent
- `7780608a` — sessionStorage cache + frame walk
- `923e4122` — stop clearCache from wiping sessionStorage
- `58d4a31b` — JWT tid claim resolution
- `671eeb57` — tenant ID bootstrap race fix

These layers usually hide the popup by ensuring at least ONE strategy succeeds. But repeatedly clearing localStorage/sessionStorage during debugging today wiped the cache armor → underlying issue surfaced.

### The Fix (code-side — already in master)

**File**: `src/client/shared/Spaarke.Auth/src/config.ts`

Added `resolveTenantFromXrm()` that frame-walks to read `Xrm.Utility.getGlobalContext().organizationSettings.tenantId` and constructs `https://login.microsoftonline.com/{tenantId}` as the default authority. Falls back to `/organizations` only when Xrm is unreachable.

**File**: `src/client/shared/Spaarke.Auth/src/SpaarkeAuthProvider.ts`

Changed MSAL config:
```diff
- cacheLocation: 'sessionStorage'   →  cacheLocation: 'localStorage'
- storeAuthStateInCookie: false     →  storeAuthStateInCookie: true
```

`localStorage` survives tab/browser close. `storeAuthStateInCookie: true` lets `ssoSilent` work despite 3rd-party cookie blocking.

### The Propagation Problem (THIS IS THE BLOCKER)

`@spaarke/auth` is a **TypeScript library bundled at build time** into every PCF's `bundle.js` and every Code Page's `index.html`. Changing the library source does NOT auto-update consumers — each must be **rebuilt + redeployed**.

**~30 consumers exist.** Only these have been rebuilt with the fix:
| Component | Web Resource / Solution | dev1 | demo |
|---|---|---|---|
| SemanticSearchControl PCF | `sprk_Sprk.SemanticSearchControl` v1.1.40 | ✅ | ✅ |
| Corporate Workspace | `sprk_corporateworkspace` | ✅ | ✅ |
| Daily Briefing | `sprk_dailyupdate` | ✅ | ✅ |

**The rest still bundle the old library with `/organizations` authority.** If they load on the Corporate Counsel app (or any tab) at any point, they will fire the popup.

---

## Remaining Consumers (NEEDS REBUILD)

Verified via grep — list of files importing `@spaarke/auth`:

### PCFs (rebuild via `/pcf-deploy` skill — each has its own folder + Solution)
1. `src/client/pcf/SpeDocumentViewer` — currently at v1.0.16. Loaded when opening a Document record.
2. `src/client/pcf/RelatedDocumentCount` — Loaded on records that show doc counts.
3. `src/client/pcf/PlaybookBuilderHost` — Loaded on Playbook builder pages.
4. `src/client/pcf/UniversalDatasetGrid` — Loaded on various grid views.
5. `src/client/pcf/EmailProcessingMonitor` — Email processing dashboard.
6. `src/client/pcf/DocumentRelationshipViewer` — PCF wrapper (also has code-page version).

### Code Pages / Solutions (rebuild via vite + Dataverse web resource update)
**Already-rebuilt deploy scripts available:**
- LegalWorkspace → `scripts/Deploy-CorporateWorkspace.ps1` ✅ used
- DailyBriefing → `scripts/Deploy-DailyBriefing.ps1` ✅ created today, used
- BulkWizards → `scripts/Deploy-WizardCodePages.ps1` deploys multiple at once

**Code pages needing rebuild (most can use Deploy-WizardCodePages.ps1):**
| Solution Path | Web Resource Name | Deploy method |
|---|---|---|
| `src/solutions/CreateMatterWizard` | `sprk_creatematterwizard` | Deploy-WizardCodePages.ps1 |
| `src/solutions/CreateProjectWizard` | `sprk_createprojectwizard` | Deploy-WizardCodePages.ps1 |
| `src/solutions/CreateEventWizard` | `sprk_createeventwizard` | Deploy-WizardCodePages.ps1 |
| `src/solutions/CreateTodoWizard` | `sprk_createtodowizard` | Deploy-WizardCodePages.ps1 |
| `src/solutions/CreateWorkAssignmentWizard` | `sprk_createworkassignmentwizard` | Deploy-WizardCodePages.ps1 |
| `src/solutions/SummarizeFilesWizard` | `sprk_summarizefileswizard` | Deploy-WizardCodePages.ps1 |
| `src/solutions/FindSimilarCodePage` | `sprk_findsimilar` | Deploy-WizardCodePages.ps1 |
| `src/solutions/PlaybookLibrary` | `sprk_playbooklibrary` | Deploy-WizardCodePages.ps1 |
| `src/solutions/DocumentUploadWizard` | `sprk_documentuploadwizard` | Deploy-WizardCodePages.ps1 |
| `src/solutions/AllDocuments` | `sprk_alldocuments` | Deploy-WizardCodePages.ps1 |
| `src/solutions/WorkspaceLayoutWizard` | ? — check vite.config.ts | adapt CorporateWorkspace pattern |
| `src/solutions/Reporting` | ? — check | adapt CorporateWorkspace pattern |
| `src/solutions/SpeAdminApp` | ? — check | adapt CorporateWorkspace pattern |
| `src/client/code-pages/DocumentRelationshipViewer` | `sprk_documentrelationshipviewer` | check existing script |
| `src/client/external-spa/*` (External Workspace) | `sprk_externalworkspace` | `/power-page-deploy` skill |

### Batch Strategy (Recommended)
1. **Run `npm run build` in each solution** (or batch via `Build-AllClientComponents.ps1` if present)
2. **Use `Deploy-WizardCodePages.ps1`** — it covers many wizard pages in one shot
3. **Use individual deploy scripts** for non-wizard ones (CorporateWorkspace, DailyBriefing patterns)
4. **For PCFs**: `/pcf-deploy` skill — each is its own version bump + pack + import cycle

---

## The Binding Auth Requirements (User-Confirmed 2026-05-12)

Saved to memory at `feedback_auth-true-sso-requirement.md`:

1. **True SSO from browser-level AAD account** — if signed into Edge with work account, Spaarke MUST inherit silently. No "Pick an account" popup.
2. **Survives**: tab close, browser close, idle > 60min. Refresh tokens auto-renew in background.
3. **MSAL config (binding)**:
   - `cacheLocation: 'localStorage'`
   - `storeAuthStateInCookie: true`
   - `authority: 'https://login.microsoftonline.com/{tenantId}'` (NEVER `/organizations` or `/common`)
4. **Single auth service shared across ALL components** — every PCF, Code Page, dialog, wizard, tab MUST share the cached token. Zero per-component prompts.
5. **Acceptable prompts only**: first-ever sign-in, AAD CA policy re-auth, "Stay signed in?" once.
6. **Multi-account in Edge**: tenant-specific authority MUST auto-select the work account.

---

## Email Documents Wizard — Status

### What Was Built (in master)
- **`@spaarke/ui-components/src/hooks/useDocumentMultiSelect.ts`** — Set-based selection hook
- **`@spaarke/ui-components/src/services/userLookup.ts`** — `searchUsersAsLookup`, `searchContactsAsLookup`, `searchUsersAndContacts`
- **`@spaarke/ui-components/src/services/communicationApi.ts`** — `sendCommunication()` wrapper
- **`@spaarke/ui-components/src/components/DocumentToolbar/`** — reusable toolbar
- **`@spaarke/ui-components/src/components/DocumentEmailWizard/`** — 3-step wizard:
  - Step 1: Confirm Selection (deselect docs, toggle Send Links / Attach Files)
  - Step 2: **Combined AI Summary** via `/api/ai/analysis/execute` + "Summarize New File(s)" playbook (single multi-doc call, NOT per-doc loop)
  - Step 3: Compose (recipient picker = systemuser + contact, body pre-populated)

### What Was Wired (in SemanticSearchControl v1.1.40)
- Mail icon in toolbar (between + and Open viewer) — visible when results > 0
- Click → opens DocumentEmailWizard with all rendered docs
- `dataService` adapter built from `context.webAPI` for the recipient picker

### What's Broken / Unknown
- **Email send may fail with "Failed to retrieve metadata for document"** — that was the original Issue #2. **Fix has been written and deployed to dev1 BFF only**. Demo BFF deferred. Until user verifies, we don't know if the fix works.
- **AI Summary step may not run** — user reported "documents do not summarize" yesterday. Fix has shipped (single-call + correct playbook), unverified.
- **Wizard's `attachmentDocumentIds` is `sprk_document` GUIDs** — BFF now looks them up to get the actual graphdriveid/graphitemid per doc.

### Combined Summary — Implementation Details
The wizard now does a SINGLE call:
```
POST /api/ai/analysis/execute
{
  documentIds: [<all selected GUIDs>],
  playbookId: <resolved from /api/ai/playbooks/by-name/Summarize%20New%20File(s)>,
  actionId: null,
  additionalContext: null
}
```
Stream SSE, accumulate `chunk.content` into one combined summary, display in ONE card.

---

## BFF Deploy Hardening (Reference)

`scripts/Deploy-BffApi.ps1` was hardened today to handle two silent failure modes:

1. **Windows App Service silent file lock**: `az webapp deploy` returns 200 but DLLs don't replace. **Fix**: SHA-256 hash 6 critical files before deploy, fetch them via Kudu VFS after deploy, compare. Auto-recover via stop→Kudu zipdeploy→start.

2. **Linux App Service rsync exit 123**: `az webapp deploy` returns non-zero. **Fix**: Branch to stop→Kudu zipdeploy→start immediately, then hash-verify.

Documented in `.claude/skills/bff-deploy/SKILL.md` including a manual verification PowerShell snippet for when not using the script. Risk-managed `WEBSITE_RUN_FROM_PACKAGE` migration plan is queued there for future cleanup.

---

## Recent Commits (Today's Work — All on master)

```
658e5944 chore(scripts): add Deploy-DailyBriefing.ps1 for sprk_dailyupdate web resource
9e480d75 fix: 3 critical bugs — tenant-specific MSAL authority, BFF attachment lookup, correct playbook
9f977809 feat(email-wizard): wire DocumentEmailWizard into PCF + AI Summary
2a1a34c1 feat(ui-components): Phase B foundation — multi-select hook, DocumentEmailWizard, DocumentToolbar
fca3a6cd fix(bff/semantic-search): skip MinScore filter when query is empty + Linux deploy fallback
cbb6a64a feat(semantic-search): Phase A — Associated Only default, server-side threshold, top-8, dedup count
3cca264e fix(SpeDocumentViewer): restore PCF source, switch to @spaarke/auth, ship v1.0.16 to dev1 + demo
50fde5c0 fix(bff/DI): make AI-dependent service registrations conditional on DocumentIntelligence:Enabled
```

---

## Critical File Locations

### Source
- `src/client/shared/Spaarke.Auth/src/config.ts` — **resolveTenantFromXrm() lives here**
- `src/client/shared/Spaarke.Auth/src/SpaarkeAuthProvider.ts` — MSAL config + 6-strategy chain
- `src/client/shared/Spaarke.UI.Components/src/components/DocumentEmailWizard/DocumentEmailWizard.tsx` — combined summary logic
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs` — per-doc graphdriveid lookup in `DownloadAndBuildAttachmentsAsync`
- `src/server/api/Sprk.Bff.Api/Services/Ai/SemanticSearch/SemanticSearchService.cs` — AssociatedOnly + MinScore + dedup count

### Deploy Scripts
- `scripts/Deploy-BffApi.ps1` — hardened with hash verify + auto-recover
- `scripts/Deploy-CorporateWorkspace.ps1`
- `scripts/Deploy-DailyBriefing.ps1` (new today)
- `scripts/Deploy-WizardCodePages.ps1` — bulk web resource deploy
- `scripts/Deploy-WizardCommandsJs.ps1` — focused JS deploy

### Memory (cross-session)
- `feedback_auth-true-sso-requirement.md` — binding auth requirements
- `feedback_shared-doc-component-library.md` — share doc UI via @spaarke/ui-components
- `feedback_bff-deployment-safety.md`
- `feedback_bff-url-normalization.md`

### Docs
- `docs/architecture/sdap-auth-patterns.md` — 9-pattern taxonomy (**NEEDS UPDATE** with binding requirements + bundling reality)
- `.claude/patterns/auth/spaarke-auth-initialization.md`
- `.claude/patterns/auth/xrm-webapi-vs-bff-auth.md`
- `.claude/constraints/auth.md`
- `.claude/skills/bff-deploy/SKILL.md` (updated today with file-lock failure + manual verification)

---

## Pending Verification (User Side)

Before charging forward on rebuilds, the user needs to verify the existing fixes:

### Auth fix verification (highest priority)
```javascript
// In Edge browser F12 console:
localStorage.clear();
sessionStorage.clear();
document.cookie.split(';').forEach(c => { document.cookie = c.split('=')[0].trim() + '=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/'; });
// Then CLOSE EDGE COMPLETELY, reopen, navigate to Corporate Counsel app
// Expected: no popup. Daily Briefing + Workspace load silently.
// Diagnostic if popup appears: paste the [SpaarkeAuth] All 6 token strategies failed line
//   - authority value tells us whether the rebuilt component or an OLD un-rebuilt one is firing
```

### Email wizard verification
1. Open SemanticSearchControl on a matter form (PCF v1.1.40)
2. Click the mail icon in toolbar
3. Verify Step 1 lists docs + has the toggles
4. Verify Step 2 shows ONE combined summary streaming in
5. Verify Step 3 lets you search users + contacts in the To picker
6. Try sending with "Attach Files" enabled → verify no "Failed to retrieve metadata" error

---

## Next Steps After Verification

### If auth fix works on Corporate Counsel app:
1. **Batch-rebuild remaining consumers** (~30 components). Order by likelihood of user impact:
   - SpeDocumentViewer PCF (Document open flow)
   - DocumentUploadWizard, FindSimilar (used frequently)
   - Create wizards (Matter/Project/Event/Todo/WorkAssignment)
   - SummarizeFiles, PlaybookLibrary
   - RelatedDocumentCount, UniversalDatasetGrid PCFs
   - Reporting, WorkspaceLayoutWizard, SpeAdmin
   - External Workspace SPA (Power Pages)
   - DocumentRelationshipViewer (both PCF + Code Page versions)
2. **Update `docs/architecture/sdap-auth-patterns.md`** with:
   - New "Authority resolution" section
   - "Library distribution" section (bundling reality + rebuild requirement)
   - Strategy chain visualization
   - Long-term refactor target: convert to runtime-loaded Dataverse web resource

### If auth fix doesn't work on Corporate Counsel app:
- Get the actual `authority:` value from the console
- If still `/organizations`: my `resolveTenantFromXrm()` is returning undefined. Need to debug: is Xrm reachable from the iframe? Try inspecting `window.parent.Xrm.Utility.getGlobalContext().organizationSettings.tenantId` directly in console.
- If specific tenant ID: ssoSilent is failing for a different reason. Check Conditional Access policies, or whether the user's AAD session cookie is valid.

### Phase C — DocumentRelationshipViewer (not yet started)
- Wire the same Email button + DocumentEmailWizard into DRV (`src/client/code-pages/DocumentRelationshipViewer`)
- Same shared components from `@spaarke/ui-components` — no new components needed
- ~30 min of integration work

### Demo BFF deploy of email attachment fix
- Was deferred at user's request
- Run `powershell -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1 -SkipBuild -Environment "staging" -ResourceGroupName "rg-spaarke-demo" -AppServiceName "spaarke-bff-demo"`
- The hardened script's auto-recover handles demo's Linux rsync issue

---

## Active Environments

| Environment | Dataverse URL | BFF URL | Sub |
|---|---|---|---|
| dev1 | https://spaarkedev1.crm.dynamics.com | spe-api-dev-67e2xz.azurewebsites.net | Spaarke Devlopment Environment |
| demo | https://spaarke-demo.crm.dynamics.com | spaarke-bff-demo.azurewebsites.net | Spaarke Demo Environment |

`pac auth list` shows both as available profiles. Use `pac auth select --index 3` for dev1, `--index 2` for demo.

---

## Conversation Style Preferences (carried over)

- User wants honest assessments, not jumping to fixes without verification
- User wants short, direct responses (per CLAUDE.md and user feedback)
- User wants to verify before committing to large rebuilds
- User is OK with parallel agents but expects clear file-ownership separation
- Keep skill invocations (pcf-deploy, ribbon-edit) when applicable

---

## If You're Reading This After Compaction

**Do this first**:
1. Read this file completely
2. Read `C:\Users\RalphSchroeder\.claude\projects\c--code-files-spaarke\memory\MEMORY.md` for the memory pointers
3. Read `feedback_auth-true-sso-requirement.md` for the binding requirements
4. Check `git log --oneline -10` for recent state
5. Ask the user to confirm test results from the verification list above before making changes
