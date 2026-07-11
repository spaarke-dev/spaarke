# Current Task

**Active task**: VHVU-031 — Deploy VisualHost + pages to dev; UAT "+" via navigateTo (dark+light)
**Status**: ⏸ blocked on owner go (deploy + live Dataverse env)
**Phase**: A3
**Next action**: OWNER-GATED. Phase A is code-complete + build-verified. VHVU-031 (and 021 UAT, optional 004) need a live env + owner observation — not autonomous.

### Quick Recovery
| Field | Value |
|---|---|
| Done | A0 (001-003) + A1 (010-012) + A2 (020) + **A3 code (030)** — 8 tasks + master merge, all committed |
| Next code work | **Phase B** (040-070): scaffold `@spaarke/visuals`, move visuals, reconcile dup, refactor self-fetch, repoint, ADR-012 amendment |
| Gates awaiting owner | VHVU-004 (optional dev deploy), VHVU-021 + VHVU-031 (UAT — need deploy + live env) |

### VHVU-030 outcome (2026-07-10) — COMPLETE
- "+" cut over to `Xrm.Navigation.navigateTo` (webresource dialog 60%×70%, mirrors `sprk_wizard_commands.js`). Local `resolveWizardPage` maps key/entity → `sprk_createeventwizard`/`sprk_createinvoicewizard`/`sprk_createreportcardwizard`; unregistered → toast (FR-03).
- Envelope: `entityType`, `entityId` (`cleanGuid` per ADR-044), `recordName` (awaited), `themeOption`. No auth/token wiring in the PCF anymore (page owns auth, ADR-028) — **auth surface REDUCED**.
- DELETED: inline Dialog + React.lazy mount, `ensureCreateWizardAuthInitialized` lazy `@spaarke/auth` bootstrap, `ICreateWizardAuthContext`, all wizard state, hostAssociation, injected services, resolveSpeContainerId, the wizard React-skew cast, wizard/adapter/AssociateToStep-type imports, 4 Dialog fluentui imports, 2 wizard styles. Removed `@spaarke/auth` from VisualHost `package.json`. Net **−139 lines** in VisualHostRoot.tsx.
- **Build green** (`build:prod`), bundle **746 KiB** (down from >1.5 MiB — msal/wizard code gone). Verified in bundle: `PublicClientApplication`=0, `BrowserAuthError`=0, `resolveRuntimeConfig`=0, `SdapClient`=0; `cleanGuid` body (`trim().toLowerCase()`)=1; footer `v1.4.36`=1.
- Version bumped 1.4.35 → **1.4.36** (all 5 locations).
- Step 9.5 code-review: **CLEAN** (7/7 PASS, one informational async-onClick nit).

**Two documented deviations (both honest, not regressions):**
1. Only 1 of 3 casts deletable (wizard cast). The 2 `AiSummaryPopover` casts persist — empirically confirmed (removing them → TS2786) because `AiSummaryPopover` is still imported from shared-lib `src` (React 18/19 skew), independent of the wizard leak. They retire in **Phase B** when the visual moves to `@spaarke/visuals`.
2. `themeOption` in the navigate envelope is forward-compat; theme actually resolves via the MDA-wide `spaarke-theme` localStorage (same as all other navigateTo wizards). `detectDarkModeFromUrl` reads top-level `flags`, not the `data` envelope — so the envelope value is inert today but harmless. localStorage covers the real case.

### Completed A0–A2 (2026-07-10)
- VHVU-001/002/003 ✅ (build harden, packaging hygiene, v-bump); merged origin/master.
- VHVU-010/011/012 ✅ (shared `useWizardPageBootstrap` factory + Event/Invoice/Report Card code pages).
- VHVU-020 ✅ (initialAssociation/lockAssociation + themeOption token normalization across 3 pages).
- VHVU-004 ⏸ optional (owner-gated interim deploy — superseded by 031 redeploy).

## Progress
- [x] A0–A2 committed
- [x] A3 code (030) committed
- [ ] A3 deploy/UAT (031) — owner-gated
- [ ] Phase B (040–070)
- [ ] Wrap-up (090)

## Notes
- Deploy/UAT tasks (004/021/031/061) are outward-facing → require owner go + live env.
- `.claude/` write boundary: VHVU-070 (ADR-012 amendment) is main-session only.
- Phase B is the next autonomous-eligible block (all code, no deploy until 061).
