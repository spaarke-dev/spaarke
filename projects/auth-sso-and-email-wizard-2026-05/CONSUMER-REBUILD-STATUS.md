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
| SpeDocumentViewer | ✅ v1.0.24 | ✅ v1.0.24 | Rebuilt 2026-05-14 to ship new BFF 409 error mapping (no_file_attached etc.). Virtual-control refactor landed in v1.0.22. **Bundle regression UNRESOLVED**: see follow-up below. |

### SpeDocumentViewer bundle regression — investigation hand-off

**Symptom**: clean `npm run build` produces a 6.7 MB `bundle.js` / 1.1 MB packed ZIP. The previous v1.0.22 / v1.0.23 builds (committed in master at SHA `4e875cb0`) were 440 KB / 111 KB ZIP. Source files haven't changed in a way that would touch the bundle composition — only `control/BffClient.ts` was modified between v1.0.23 and v1.0.24, and that change adds new strings in a switch statement (no new imports).

**Reproduction**:
```bash
cd src/client/pcf/SpeDocumentViewer
rm -rf out/ node_modules/.cache
npm run build
ls -la out/controls/control/bundle.js   # observe ~6.7 MB instead of ~440 KB
```

**Verified non-causes** (already checked):
- Shared lib deep imports — `index.ts` and `SpeDocumentViewerHost.tsx` correctly use `@spaarke/ui-components/dist/utils/logger` (not the barrel). Unchanged since 4e875cb0.
- `@fluentui/react-icons` version — installed 2.0.316; SemanticSearchControl uses 2.0.317. Both have `sideEffects: false`. Not version-driven.
- `featureconfig.json` — `pcfReactPlatformLibraries: on` is set. React/Fluent ARE external (`Reactv16`, `FluentUIReactv940` per webpack stats).
- Build cache — `rm -rf out/ node_modules/.cache` followed by full build produces the same 6.7 MB output (deterministic, not cache-driven).

**Webpack stats from current build**:
- `external "Reactv16"` 42 bytes — React platform-loaded ✅
- `external "FluentUIReactv940"` 42 bytes — Fluent platform-loaded ✅
- `modules by path ./node_modules/ 4.03 MiB 55 modules` — this 4 MB is the regression. Babel deoptimization warnings name 9 `@fluentui/react-icons/lib/sizedIcons/chunk-N.js` files (each >500 KB). Tree-shaking is failing to eliminate the unused icons.

**Likely root cause**: subtle webpack/babel config drift between when v1.0.22 was built (May 13) and now (May 14) — possibly in pcf-scripts version or a hoisted dependency. Or maybe the 440 KB number was achieved under specific conditions (e.g., the build that produced it was via custom-webpack-on, while the current build resolves differently).

**Investigation path when picking up**:
1. Run `git worktree add /tmp/sdv-v1022 4e875cb0` and `npm install --legacy-peer-deps && npm run build` there — confirm reproduces 440 KB. If yes → environment difference, not code.
2. If 440 KB reproduces: `diff` the two `node_modules/` trees (focus on `pcf-scripts`, `@fluentui/react-icons`, `webpack`, `babel-loader`).
3. If 6.7 MB reproduces in the worktree too: the 440 KB number was wrong / from a stale build. Accept current size and pin it.
4. Alternative permanent fix: switch icon imports to per-chunk deep paths. Each `@fluentui/react-icons/lib/sizedIcons/chunk-N.js` groups ~200 icons; we'd need to grep each chunk for `LockClosed16Regular`, `Warning24Regular`, `ArrowClockwise24Regular`, etc. and import directly. Brittle (chunk numbers can change across package versions) but bypass-the-tree-shaker certain.

**Impact**: 1.1 MB ZIP is well under Dataverse's 25.6 MB limit. Functionally fine. Cost: extra ~5 MB download per first-load of the control per user. Not blocking.
| EmailProcessingMonitor | ⏳ TBD | ⏳ TBD | Standard control; needs rebuild + P0 virtual-refactor |
| PlaybookBuilderHost | ⏳ TBD | ⏳ TBD | Special architecture (react-flow); needs rebuild verify |
| UniversalDatasetGrid | ⏳ TBD | ⏳ TBD | Standard; needs rebuild + P0 virtual-refactor |
| UniversalQuickCreate | ⚠️ v3.15.3 BLOCKED | ⚠️ v3.15.3 BLOCKED | Version-bumped 2026-05-14 to ship sprk_hasfile fix. Build blocked by 2 pre-existing issues: (1) `@types/react` 16-vs-18 collision with shared lib, (2) 5+ missing hook type exports from `@spaarke/ui-components/src/hooks` (`UseSseStreamOptions` etc.). Form-bound uploads continue to omit `sprk_hasfile` until rebuilt. Backfill script (`scripts/Backfill-DocumentHasFile.ps1`) covers existing records. Workspace + wizard uploads correctly set the flag now. |

## Code Pages (rebuild via vite + `pac webresource update`)

| Solution Path | Web Resource | dev1 | demo | Deploy Script |
|---|---|---|---|---|
| `src/solutions/LegalWorkspace` | `sprk_corporateworkspace` | ✅ 2026-05-14 (hasfile fix) | ✅ 2026-05-14 (hasfile fix) | `Deploy-WizardCodePages.ps1` |
| `src/client/code-pages/DailyBriefing` (or wherever it lives) | `sprk_dailyupdate` | ✅ | ✅ | `scripts/Deploy-DailyBriefing.ps1` |
| `src/solutions/CreateMatterWizard` | `sprk_creatematterwizard` | ✅ 2026-05-14 (hasfile fix) | ✅ 2026-05-14 (hasfile fix) | `Deploy-WizardCodePages.ps1` |
| `src/solutions/CreateProjectWizard` | `sprk_createprojectwizard` | ✅ 2026-05-14 (hasfile fix) | ✅ 2026-05-14 (hasfile fix) | `Deploy-WizardCodePages.ps1` |
| `src/solutions/CreateEventWizard` | `sprk_createeventwizard` | ✅ 2026-05-14 (hasfile fix) | ✅ 2026-05-14 (hasfile fix) | `Deploy-WizardCodePages.ps1` |
| `src/solutions/CreateTodoWizard` | `sprk_createtodowizard` | ✅ 2026-05-14 (hasfile fix) | ✅ 2026-05-14 (hasfile fix) | `Deploy-WizardCodePages.ps1` |
| `src/solutions/CreateWorkAssignmentWizard` | `sprk_createworkassignmentwizard` | ✅ 2026-05-14 (hasfile fix) | ✅ 2026-05-14 (hasfile fix) | `Deploy-WizardCodePages.ps1` |
| `src/solutions/SummarizeFilesWizard` | `sprk_summarizefileswizard` | ⏳ | ⏳ | `Deploy-WizardCodePages.ps1` |
| `src/solutions/FindSimilarCodePage` | `sprk_findsimilar` | ⏳ | ⏳ | `Deploy-WizardCodePages.ps1` |
| `src/solutions/PlaybookLibrary` | `sprk_playbooklibrary` | ⏳ | ⏳ | `Deploy-WizardCodePages.ps1` |
| `src/solutions/DocumentUploadWizard` | `sprk_documentuploadwizard` | ✅ 2026-05-14 (hasfile fix) | ✅ 2026-05-14 (hasfile fix) | `Deploy-WizardCodePages.ps1` |
| `src/solutions/AllDocuments` (if exists) | `sprk_alldocuments` | ⏳ | ⏳ | `Deploy-WizardCodePages.ps1` |
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
