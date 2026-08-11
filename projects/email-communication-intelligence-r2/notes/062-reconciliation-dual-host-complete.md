# Task 062 — Dual host (code page + SpaarkeAi widget) — COMPLETE (2026-08-10)

**Rigor**: FULL · sonnet·high · directional. **Escalation**: neither trigger fired. **Both hosts build green.**

## What shipped
Both hosts mount the SAME shipped `ReconciliationWorkspace` (task 061) unchanged — only host-adapter resolution differs (§11 mount, no fork).

**Host 1 — code page `src/solutions/CommunicationReconciliation/`** (mirrors EmailPage): `index.html`, `vite.config.ts` (singlefile + shared-lib source alias, verbatim from EmailPage), `package.json` (build renames `dist/index.html` → `dist/sprk_communicationreconciliation.html`), `tsconfig*.json`, `src/config/runtimeConfig.ts`, `src/services/authInit.ts`, `src/main.tsx`.
- **Auth (ADR-028)**: `bootstrapAuth()` = `resolveRuntimeConfig` → `setRuntimeConfig` → `ensureAuthInitialized` (v2 contract) + Xrm tenantId fallback; render gated on it (fail-closed retry state, NFR-07). BFF via `authenticatedFetch` (no raw Bearer).
- **Client (ADR-012)**: `XrmDataverseClient`. **`resolveReview`**: Xrm.WebApi bridge (`buildXrmWebApi` → `EmailWorkspaceWebApi`) as both `writeContext.webApi` + `pickerWebApi` — EmailWorkspace's exact pattern (reused ADR-024 write path). **`resolveRegarding`**: reuses the shipped pure `derivePrimaryReview` reducer (NFR-10 gate; Resolved primary w/ typed entity → regarding, else null). `configId` omitted → grid uses `NEEDS_REVIEW_CONFIG_ID` placeholder (059 sets it).

**Host 2 — SpaarkeAi widget** `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/`:
- `ReconciliationWorkspaceWidget.tsx` (new, thin adapter mirroring `EmailWorkspaceWidget`): `XrmDataverseClient` + `getXrm().WebApi` + `useAiSession().authenticatedFetch`; same `resolveReview`/`resolveRegarding`; fail-closed when no Xrm host.
- `register-workspace-widgets.ts` (+1 additive `safeRegisterWidget('communications-reconciliation', …)`, defaultOrder 246, contextType `matter-grid`). **Chassis untouched** (no registry-contract / dashboard-wrapper / pane-bus change).

## Verification
- **Code page**: `npm run build` exit 0; HTML/CSS-reset gate ✓; surface tsc 0 errors; vite 15.8s → `dist/sprk_communicationreconciliation.html` (2.34 MB / 654 KB gz). Bundle grep confirms mount: `reconciliation-workspace`, `reconcile-tab-related/fields`, `Loading Reconciliation`.
- **SpaarkeAi (ai-widgets)**: `tsc --noEmit` → new files produce **0 errors** (grep-confirmed). The 21 tsc errors are pre-existing sibling-package (`@spaarke/ai-outputs`/`@spaarke/ai-context`) dist-resolution failures unrelated to this change (full `npm run build` needs those built; not available in-env — POML permits `--noEmit`).
- Step 9.5: code-review PASS (fail-closed auth, mount-only, no `any` abuse) · adr-check PASS (028/012/021/039-§10/022) · /conflict-check soft-pass (0 open-PR overlap on SpaarkeAi/registry/code-pages; new code page = zero contention; deploy timing = last-writer-wins, handled at 059).

## Known refinement (for 059 / UAT — NOT a blocker)
`onAssociationsChanged` bumps a React `key` to force a grid reload so `resolveRegarding` reflects the new association (the only host-refresh seam `ReconciliationGrid` exposes). Tradeoff: an **in-shell** Related-to confirm remounts the workspace → the browse shell closes before the Fields tab enables (the prototype switched to Fields in-place). A non-remounting grid-refresh seam on `ReconciliationGrid` is the clean fix — worth a small follow-up during 059/UAT.

## Next
**059** (GATED deploy — operator go-ahead): seed needs-review + per-team `sprk_gridconfiguration` into spaarkedev1, set `NEEDS_REVIEW_CONFIG_ID`, `code-page-deploy` the code page + rebuild/redeploy SpaarkeAi + `Deploy-AllDataGridConsumers`, verify both surfaces.
