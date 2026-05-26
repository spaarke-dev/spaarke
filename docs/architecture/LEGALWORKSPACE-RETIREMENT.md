# LegalWorkspace Code-Page Retirement

> **Status**: Adopted (operator decision OC-R4-05, 2026-05-25). Implementation landed via R4 task 041.
> **Date published**: 2026-05-26
> **Scope**: The standalone `sprk_corporateworkspace` Dataverse web resource. **NOT** the LegalWorkspace components, `LegalWorkspaceApp` renderer, or any other library code.
> **Supersedes**: R3 spec FR-25 and NFR-10 ("standalone LegalWorkspace must continue to function identically"). These constraints **no longer apply going forward**.
> **Related**:
> - [`SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](./SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) — the new authoritative two-wrapper framing (W-1)
> - [`LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`](./LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md) — host contract for embedding LegalWorkspace (C-2)
> - [`SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](./SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — current SpaarkeAi pipeline (the host)
> - [`projects/spaarke-ai-platform-unification-r4/notes/lw-consumer-audit-2026-05.md`](../../projects/spaarke-ai-platform-unification-r4/notes/lw-consumer-audit-2026-05.md) — consumer audit results (Risk R-6 mitigation)

---

## 1. Decision

The standalone LegalWorkspace code page — deployed historically as the Dataverse web resource `sprk_corporateworkspace` (HTML built from `src/solutions/LegalWorkspace/dist/corporateworkspace.html`) — **stops being deployed**. Going forward:

- The web resource is no longer in any scheduled deploy.
- The deploy scripts that previously built and pushed it are guarded to skip with a clear log message (preserving script history per the standard deprecation pattern).
- The R3 acceptance criteria FR-25 / NFR-10 ("standalone LegalWorkspace must continue to function identically") are **superseded**. Future authors MUST NOT treat them as forward constraints.

**This is a deploy retirement, not a code deletion.** Everything else continues:

| Asset | Status post-retirement |
|---|---|
| `LegalWorkspaceApp` React component | **Retained as a library** — consumed by SpaarkeAi via embedded mode |
| `src/client/shared/Spaarke.UI.Components/` | **Retained** (unaffected) |
| `src/client/shared/Spaarke.AI.Widgets/` | **Retained** (unaffected) |
| `src/client/shared/Spaarke.Events.Components/` | **Retained** (unaffected) |
| `src/solutions/LegalWorkspace/src/**` (component source) | **Retained** (library only; build scripts retained, no deploy) |
| `src/solutions/LegalWorkspace/dist/corporateworkspace.html` (build artifact) | Will continue to build locally but is no longer deployed |
| `sprk_corporateworkspace` Dataverse web resource | **No new deploys**; existing deployed copy in `spaarkedev1` may remain until operator-driven removal |

## 2. Rationale (OC-R4-05)

The SpaarkeAi code page (`sprk_spaarkeai`) is the unified shell where users now do their legal-operations work. SpaarkeAi:

- Embeds LegalWorkspace components via `WorkspaceLayoutWidget` → `<LegalWorkspaceApp embedded>` for every workspace tab (see [`SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](./SPAARKEAI-WORKSPACE-ARCHITECTURE.md) §2.3).
- Adds the three-pane shell (Assistant + Workspace + Context) that the standalone LW page lacked.
- Inherits the same widgets, sections, and section factories LegalWorkspace was already exposing.
- Will receive R4's mount-source wiring (W-4 Assistant → Workspace; W-5 Context → Workspace), making it the only surface where new behavior is added going forward.

Maintaining a parallel standalone deploy of `sprk_corporateworkspace` therefore creates:

- **Two surfaces to keep in sync** for every R3+ section update (the R3 NFR-10 "identical behavior" commitment was a real cost; build matrices, smoke tests, deploy steps, version bumps).
- **A second surface to test** for every workspace feature, despite SpaarkeAi being the active path.
- **No observable user value** — operators reported using SpaarkeAi for the workflows previously hosted at the standalone page.

The components themselves remain valuable as a library — they implement the dashboard model that SpaarkeAi embeds. So the retirement is scoped narrowly to the deploy, not to the code.

## 3. What's retired vs preserved (precise boundary)

### Retired

- The Dataverse web resource named `sprk_corporateworkspace`.
- Scheduled deploys that pushed the `corporateworkspace.html` artifact to that web resource.
- The "standalone LegalWorkspace continues to function identically" forward constraint (R3 FR-25 / NFR-10).

### Preserved

- `LegalWorkspaceApp` React component (embedded mode entry point).
- All section components (`DailyBriefingSection`, `CalendarSection`, `MyDocumentsSection`, etc.) — used by both LegalWorkspace registrations and direct embedding in SpaarkeAi tabs.
- All shared-library packages (`@spaarke/ui-components`, `@spaarke/ai-widgets`, `@spaarke/events-components`, `@spaarke/auth`).
- The `useWorkspaceLayouts` hook (will be unified by R4 task C-3).
- The `sprk_workspacelayout` Dataverse entity and BFF endpoints serving layouts.
- The build script `npm --workspace src/solutions/LegalWorkspace run build` — components still need to compile so library imports keep resolving in SpaarkeAi.
- The `Deploy-LegalWorkspaceCustomPage.ps1` script — **out of scope of this retirement**. It deploys a separate target: the PCF-based `sprk_LegalOperationsWorkspace` Custom Page from `src/client/pcf/LegalWorkspace/Solution/`, not the `sprk_corporateworkspace` web resource. (See §6 for the disambiguation.)

## 4. Consumer audit (Risk R-6 mitigation)

Per `plan.original.md` §8 Risk R-6, a consumer audit was performed BEFORE the deploy step was guarded. Full results: [`lw-consumer-audit-2026-05.md`](../../projects/spaarke-ai-platform-unification-r4/notes/lw-consumer-audit-2026-05.md).

Summary:

- **4 deploy-script files** reference the web resource — all addressed by this task (see §5).
- **1 source-code self-reference** found at `src/solutions/LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx:432` (the `handleOpenTodoDialog` handler calls `Xrm.Navigation.navigateTo({ pageType: "webresource", webresourceName: "sprk_corporateworkspace", data: "mode=todo" }, ...)` — opens itself in a 90%×90% dialog).
  - **Severity**: medium. After retirement, this navigateTo will fail (404) when the user clicks "Open To Do Dialog" from inside an embedded LegalWorkspace running in SpaarkeAi.
  - **Migration**: deferred to a follow-up task. Options recorded in the audit note §4.1 (Option A: retarget to `sprk_spaarkeai`; Option B: in-page modal; Option C: PaneEventBus `widget_load`; Option D: defer). Operator picks.
- **0 Dataverse solution XML references** found in the tracked repo (`src/dataverse/solutions/**/*.xml` — covers `sprk_Container` and `sprk_Document` entities only).
- **Dataverse Default Solution dependencies** (forms / ribbons / sitemap / model-driven app navigation) — **NOT auditable from the local checkout** (those artifacts are not tracked in the repo). The operator MUST validate this gap in `spaarkedev1` before declaring W-6 fully closed:
  - Default Solution → Web Resources → `sprk_corporateworkspace` → "Show Dependencies" — expect empty.
  - Model-driven app sitemap exports — grep for `sprk_corporateworkspace`.
  - `pac solution check` against the post-retirement Default Solution — expect no missing-dependency warnings.

## 5. Migration guidance

### For future hosts of LegalWorkspace components

There IS NO active "standalone LegalWorkspace page" going forward. If you find yourself wanting one (e.g., for embedding outside the Power Apps shell), do NOT resurrect the `sprk_corporateworkspace` deploy. Instead:

1. **Read [`LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`](./LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md)** (R4 task C-2 output) — the host contract: config init, theme ownership, sessionStorage sentinels, `webApi` shim, mount semantics.
2. **Build a thin host page** that satisfies the contract and embeds `<LegalWorkspaceApp embedded>` (or its `WorkspaceLayoutWidget` wrapper).
3. **Register the host page as its own Dataverse asset** with a distinct logical name. Do NOT reuse `sprk_corporateworkspace`.
4. **Justify the new surface against `LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md` §requirements** in your project's design doc.

### For consumers of the retired web resource navigateTo call

If you (like `WorkspaceGrid.tsx:432`) called `Xrm.Navigation.navigateTo({ pageType: "webresource", webresourceName: "sprk_corporateworkspace", ... })`, migrate to one of:

- **In-page modal** using the existing modal infrastructure (no Xrm navigation, lives inside the active SpaarkeAi tab).
- **`PaneEventBus.workspace` `widget_load` dispatch** (mounts the target as a new workspace tab — aligns with the W-4 / W-5 mount-source model).
- **Retarget to `sprk_spaarkeai`** with a query-string deep link IF SpaarkeAi gains a matching `?mode=todo` (or similar) entry point — confirm with operator before relying on this.

Do NOT retarget to a new standalone web resource unless you can justify it against the embedded-mode contract.

### For R3 acceptance criteria authors

R3 spec **FR-25 / NFR-10** ("standalone LegalWorkspace continues to function identically") are **superseded by this retirement**. If you are reading R3's spec.md and tempted to chase a regression because the standalone page behaves differently than R3 committed it would: STOP. The retirement reframes the commitment. SpaarkeAi (the embedded host) is now the active surface, and its behavior is governed by R4 acceptance criteria (FR-01 through FR-14 in R4 spec.md).

There is no code-level constraint to remove from the codebase. The R3 code paths that supported standalone LegalWorkspace continue to compile and run when the module is embedded — they just are no longer deployed as a standalone Dataverse web resource. Authors MUST NOT delete LegalWorkspace component code on the grounds of "R3 FR-25 is superseded"; the components are the dashboard engine.

## 6. Disambiguation: `sprk_corporateworkspace` vs `sprk_LegalOperationsWorkspace`

Two LegalWorkspace-adjacent Dataverse assets exist in the repo; only ONE is retired here.

| Asset | What it is | Status |
|---|---|---|
| **`sprk_corporateworkspace`** (web resource) | HTML build of `src/solutions/LegalWorkspace/dist/corporateworkspace.html`. Vite + React 19 + Fluent v9. Deployed by `Deploy-CorporateWorkspace.ps1`. | **Retired** by this doc + R4 task 041 |
| **`sprk_LegalOperationsWorkspace`** (Custom Page) | Power Apps Custom Page hosting the PCF control `sprk_Spaarke.Controls.LegalWorkspace` from `src/client/pcf/LegalWorkspace/`. Deployed by `Deploy-LegalWorkspaceCustomPage.ps1` (PAC CLI + solution import). | **NOT retired** by this doc — separate target; out of W-6 scope |

If a future task retires the Custom Page too, that requires a separate retirement doc + consumer audit. Until then, treat `Deploy-LegalWorkspaceCustomPage.ps1` as active.

## 7. Deploy-script changes (R4 task 041 implementation)

The following deploy scripts were updated to skip the LW web-resource deploy:

| Script | Change |
|---|---|
| `scripts/Deploy-CorporateWorkspace.ps1` | Added early-exit guard at the top of the script. The guard logs the retirement notice and exits 0 (success — i.e., orchestrator does not treat the skip as a failure). The deploy body is left in place after the guard (preserves history). |
| `scripts/Deploy-WizardCodePages.ps1` | The `sprk_corporateworkspace` entry in the deploy-loop array was commented out with a retirement notice referencing this doc. |
| `scripts/Deploy-AllWebResources.ps1` | The `CorporateWorkspace` entry in the `$components` array was commented out with a retirement notice. (The standalone `Deploy-CorporateWorkspace.ps1` early-exit guard is the belt-and-braces backup if the comment is accidentally reverted.) |
| `scripts/README.md` | The deploy-sequence table was annotated to mark CorporateWorkspace as retired with a link to this doc. |

The scripts were NOT deleted. The guard approach was preferred over rename-to-deprecated so that:

- The script-history audit trail in PR diffs remains intact.
- Any external automation (CI, runbooks) that calls the scripts by name fails gracefully (clean log + exit 0) rather than failing on missing file.
- Reverting the retirement, if needed in an emergency, requires only removing the guard — no script-rename surgery.

## 8. Open follow-ups (operator)

1. **Dataverse-side dependency validation** in `spaarkedev1` (and any other deployed environment) — see §4 last bullet.
2. **Migration of `WorkspaceGrid.tsx:432` navigateTo** — pick Option A/B/C/D per audit note §4.1.
3. **Eventual `sprk_corporateworkspace` web-resource deletion** in Dataverse — currently only the deploy is retired; the deployed copy persists in the environment until operator removes it. Recommend leaving it deployed until the §8.2 migration ships, so the navigateTo doesn't 404 silently while the migration is in flight.
4. **Bundle size budget** — once the deploy is gone, the LegalWorkspace bundle measurement at `~589 KB gzip` becomes informational only. Track it during R4 transition, then drop from the budget table in plan.original.md once stable.

## 9. Cross-references

- Operator decision record: [`projects/spaarke-ai-platform-unification-r4/backlog.md`](../../projects/spaarke-ai-platform-unification-r4/backlog.md) §Scoping Decisions row OC-R4-05.
- Risk register: [`projects/spaarke-ai-platform-unification-r4/plan.original.md`](../../projects/spaarke-ai-platform-unification-r4/plan.original.md) §8 Risk R-6.
- R4 acceptance criterion DR-03 ([`spec.md`](../../projects/spaarke-ai-platform-unification-r4/spec.md)) covers this doc + audit + deploy-script changes.
- Root `CLAUDE.md` §16 references this doc in its pointer table.

---

*Maintainer note: this doc is the operator-binding source of truth for the retirement. Updates require an OC-R4-* style operator decision OR a follow-up project's superseding decision.*
