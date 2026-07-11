# Current Task

**Active task**: VHVU-030 — Cut over VisualHost "+" to navigateTo; delete inline embedding + casts (Phase A3)
**Status**: not-started
**Phase**: A3
**Next action**: Begin VHVU-030 — the big cutover (heavy edits to VisualHostRoot.tsx, ~1172 lines). Recommend fresh context.

### Quick Recovery
| Field | Value |
|---|---|
| Done | A0 (001-003) + A1 (010-012) + A2 (020) — 7 tasks + master merge, all committed |
| Next | **VHVU-030** navigateTo cutover (the decoupling task — removes leak + React casts) |
| Gates awaiting owner | VHVU-004 (optional dev deploy), VHVU-021 + VHVU-031 (UAT, need deploy) |
| Then | Phase B (040-070) `@spaarke/visuals` extraction + ADR-012 amendment; 090 wrap-up |

### VHVU-030 starting notes (for whoever picks it up)
- Edit `src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx`: replace the inline `React.lazy` wizard Dialog mount with `Xrm.Navigation.navigateTo` (webresource dialog 60%×70%, mirror `src/client/webresources/js/sprk_wizard_commands.js`).
- Resolve target page from wizard key: event→`sprk_createeventwizard`, invoice→`sprk_createinvoicewizard`, report-card→`sprk_createreportcardwizard`.
- Build the `data` envelope: `entityType`, `entityId` (**cleanGuid it — ADR-044**), `recordName`, `themeOption=dark|light` (matches the tolerant reader from VHVU-020). bffBaseUrl also as the pages expect.
- DELETE: inline embedding, `ensureCreateWizardAuthInitialized()` lazy `@spaarke/auth` bootstrap, the 3 `as unknown as React.ComponentType` casts (CardChrome + VisualHostRoot ×2), and the now-unused shared-lib-`src` wizard imports (wizardRegistry, adapters, AssociateToStep types).
- Verify: `@spaarke/sdap-client` + `@spaarke/auth` gone from VisualHost's build graph; `build:prod` green; bundle size DROPS (msal/wizard code removed).

### Completed A0 (2026-07-10)
- **VHVU-001 ✅** — declared `@spaarke/auth` on ui-components; extended `ensure-dist-fresh.js` for sibling dists. Deterministic green build.
- **VHVU-002 ✅** — removed 2 `.tgz` artifacts + `files:["dist"]` allow-list (npm-pack validated); removed committed `storybook-static/` (92 files) + gitignored.
- **VHVU-003 ✅** — bumped v1.4.35 (5 locations); build green; v1.4.35 + `trim().toLowerCase()` both confirmed in bundle.
- **VHVU-004 ⏸ optional** — dev deploy decoupled (owner call); 010 now depends on 003.
- Merged origin/master (0 behind); ADR-044 folded in.

## Progress
- [x] design.md, spec.md authored + reviewed
- [x] A0 groundwork committed (ui-components repointed to directory dep; `VisualHostRoot.tsx:505` implicit-any fixed) — commit `1c319c66e`
- [x] Green build recipe confirmed (cleanGuid in bundle) — see spec FR-01/FR-02
- [x] Project pipeline: plan/README/CLAUDE/tasks generated
- [ ] Task 001 onward

## Notes
- Worktree 3 commits behind origin/master (sync when convenient).
- Coordinate shared-surface edits with PR #508 (Events/SmartTodo package boundary).
- A0 is independently shippable — deploy to dev + UAT before Phase A1.
