# `@spaarke/auth` Consumer Rebuild Tracker

> **Purpose**: Live status of which consumers have been rebuilt with the 2026-05-12 SSO binding fix (tenant-specific authority, `localStorage` cache, cookie state).
>
> **Why this matters**: `@spaarke/auth` is bundled at build time into every consumer. A library fix only takes effect after each consumer is rebuilt AND redeployed. Mixed-version drift causes some surfaces to work silently while others fire the "Pick an account" popup.
>
> **Last Updated**: 2026-05-14
> **Source of truth**: `git grep '"@spaarke/auth"' src/**/package.json` — the package.json list. Status columns below are manually tracked.
>
> **Related**: [CONTEXT.md](CONTEXT.md) · [sdap-auth-patterns.md](../../docs/architecture/sdap-auth-patterns.md#library-distribution--bundling-reality) · [.claude/patterns/auth/spaarke-sso-binding.md](../../.claude/patterns/auth/spaarke-sso-binding.md)

---

## How to Verify a Rebuild

In Edge with the consumer loaded, F12 console:
```js
// Inspect MSAL config — should be tenant-specific authority, NOT /organizations
JSON.parse(localStorage.getItem(Object.keys(localStorage).find(k => k.includes('msal.config')))).authority
// Expected: "https://login.microsoftonline.com/{tenant-guid}"
// If "/organizations" — consumer ships the OLD library and needs rebuild.
```

Or check `[SpaarkeAuth]` console logs at page load — rebuilt consumers log the resolved tenant authority on initialization.

---

## PCFs (rebuild via `/pcf-deploy` skill — each has its own folder + Solution)

| PCF | dev1 | demo | Notes |
|---|---|---|---|
| SemanticSearchControl | ✅ v1.1.40 | ✅ v1.1.40 | First rebuilt — reference exemplar (virtual + auth) |
| RelatedDocumentCount | ⏳ TBD | ⏳ TBD | Already virtual; needs `@spaarke/auth` rebuild verify |
| DocumentRelationshipViewer (PCF) | ⏳ TBD | ⏳ TBD | Already virtual; needs rebuild verify |
| SpeDocumentViewer | ✅ v1.0.24 | ✅ v1.0.24 | Rebuilt 2026-05-14 to ship new BFF 409 error mapping (no_file_attached etc.). Virtual-control refactor landed in v1.0.22. **Bundle size regression flagged**: 6.7 MB build / 1.1 MB ZIP (vs. v1.0.22's 440 KB build / 111 KB ZIP) — non-deep `@fluentui/react-icons` imports. Follow-up. |
| EmailProcessingMonitor | ⏳ TBD | ⏳ TBD | Standard control; needs rebuild + P0 virtual-refactor |
| PlaybookBuilderHost | ⏳ TBD | ⏳ TBD | Special architecture (react-flow); needs rebuild verify |
| UniversalDatasetGrid | ⏳ TBD | ⏳ TBD | Standard; needs rebuild + P0 virtual-refactor |

## Code Pages (rebuild via vite + `pac webresource update`)

| Solution Path | Web Resource | dev1 | demo | Deploy Script |
|---|---|---|---|---|
| `src/solutions/LegalWorkspace` | `sprk_corporateworkspace` | ✅ | ✅ | `scripts/Deploy-CorporateWorkspace.ps1` |
| `src/client/code-pages/DailyBriefing` (or wherever it lives) | `sprk_dailyupdate` | ✅ | ✅ | `scripts/Deploy-DailyBriefing.ps1` |
| `src/solutions/CreateMatterWizard` | `sprk_creatematterwizard` | ⏳ | ⏳ | `scripts/Deploy-WizardCodePages.ps1` |
| `src/solutions/CreateProjectWizard` | `sprk_createprojectwizard` | ⏳ | ⏳ | `scripts/Deploy-WizardCodePages.ps1` |
| `src/solutions/CreateEventWizard` | `sprk_createeventwizard` | ⏳ | ⏳ | `scripts/Deploy-WizardCodePages.ps1` |
| `src/solutions/CreateTodoWizard` | `sprk_createtodowizard` | ⏳ | ⏳ | `scripts/Deploy-WizardCodePages.ps1` |
| `src/solutions/CreateWorkAssignmentWizard` | `sprk_createworkassignmentwizard` | ⏳ | ⏳ | `scripts/Deploy-WizardCodePages.ps1` |
| `src/solutions/SummarizeFilesWizard` | `sprk_summarizefileswizard` | ⏳ | ⏳ | `scripts/Deploy-WizardCodePages.ps1` |
| `src/solutions/FindSimilarCodePage` | `sprk_findsimilar` | ⏳ | ⏳ | `scripts/Deploy-WizardCodePages.ps1` |
| `src/solutions/PlaybookLibrary` | `sprk_playbooklibrary` | ⏳ | ⏳ | `scripts/Deploy-WizardCodePages.ps1` |
| `src/solutions/DocumentUploadWizard` | `sprk_documentuploadwizard` | ⏳ | ⏳ | `scripts/Deploy-WizardCodePages.ps1` |
| `src/solutions/AllDocuments` (if exists) | `sprk_alldocuments` | ⏳ | ⏳ | `scripts/Deploy-WizardCodePages.ps1` |
| `src/solutions/WorkspaceLayoutWizard` | ? | ⏳ | ⏳ | check vite.config.ts |
| `src/solutions/Reporting` | ? | ⏳ | ⏳ | check vite.config.ts |
| `src/solutions/SpeAdminApp` | ? | ⏳ | ⏳ | check vite.config.ts |
| `src/solutions/SmartTodo` | ? | ⏳ | ⏳ | check vite.config.ts |
| `src/client/code-pages/DocumentRelationshipViewer` | `sprk_documentrelationshipviewer` | ⏳ | ⏳ | check existing script |
| `src/client/code-pages/AnalysisWorkspace` | ? | ⏳ | ⏳ | check vite.config.ts |
| `src/client/code-pages/PlaybookBuilder` | ? | ⏳ | ⏳ | check vite.config.ts |
| `src/client/code-pages/SemanticSearch` | ? | ⏳ | ⏳ | check vite.config.ts |
| `src/client/external-spa/*` | `sprk_externalworkspace` | ⚠️ EXEMPT | ⚠️ EXEMPT | Different auth model (per-tab sessionStorage); see external-access-spa-architecture.md |

## Documented Exceptions

| Surface | Why exempt |
|---|---|
| Office Add-ins (`src/client/office-addins/`) | Runs in Outlook/Word host — no `Xrm` available, so `resolveTenantFromXrm()` cannot work. Currently uses `@azure/msal-browser` directly. Future work: add an `OfficeStrategy` to `SpaarkeAuthProvider` that reads tenant ID from `Office.context.mailbox.userProfile` or equivalent. |
| External Workspace SPA (`src/client/external-spa/`) | B2B portal for external users. Per-tab `sessionStorage` config is intentional — external users have a different cookie/session model than internal Spaarke surfaces. See [external-access-spa-architecture.md](../../docs/architecture/external-access-spa-architecture.md). |

## Legend

| Symbol | Meaning |
|---|---|
| ✅ | Rebuilt + redeployed with fix; verified at runtime |
| ⏳ TBD | Has not been rebuilt yet — likely still ships old `/organizations` authority |
| ⚠️ EXEMPT | Intentional exception; do not apply this rebuild |

---

## Batch Rebuild Procedure (when resuming propagation)

1. **PCFs** — use `/pcf-deploy` per control. Each requires: version bump (5 locations per `.claude/skills/pcf-deploy/SKILL.md`), build, pack, import.
2. **Wizard Code Pages** — `scripts/Deploy-WizardCodePages.ps1` covers many at once. Verify each `npm install` succeeded with `--legacy-peer-deps`.
3. **Standalone Code Pages** — adapt `scripts/Deploy-CorporateWorkspace.ps1` or `scripts/Deploy-DailyBriefing.ps1` patterns.
4. **Verify each in dev1 first**, then redeploy to demo with the same script run pointing at the demo env.
5. **Update this tracker** after each deploy.

---

## End-State Goal

When all rows show ✅: archive this tracker (move to `projects/auth-sso-and-email-wizard-2026-05/archive/`) and remove the link from [sdap-auth-patterns.md](../../docs/architecture/sdap-auth-patterns.md). Replace with a one-line note that the SSO binding fix has fully propagated.
