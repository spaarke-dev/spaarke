# Task 041 — `email` widget registration + section shim + layout seed

> FR-01 mount #1 of the dual-use Pattern D surface (SpaarkeAi direct widget).
> Mount #2 (standalone code page) is task 042 — see that task's notes for the
> other mount's own host-adapter resolution.

## Chosen identifiers (binding for 042 / 050 / 051 consistency)

| Item | Value |
|---|---|
| SpaarkeAi direct-widget type string | `email` |
| SpaarkeAi widget `defaultOrder` | `245` (immediately after `communications-list` @ 240, before the metrics dashboards @ 300+) |
| SpaarkeAi widget `category` | `data` |
| SpaarkeAi widget `icon` | `MailRegular` |
| SpaarkeAi widget `allowMultiple` | `true` |
| LegalWorkspace section id | `email` |
| LegalWorkspace section `defaultHeight` | `720px` (`grow`/no `contentSizing` — matches Calendar/Compose, NOT `clamped` like the dense `communications` grid) |
| `sectionMetadataCatalog.ts` entry | `id: 'email'`, `category: 'data'`, `entityName: 'sprk_communication'`, `defaultHeight: '720px'` |
| `system-layouts.json` seed | `name: "Email"`, `sectionId: "email"`, `layoutTemplateId: "single-column"`, `sortOrder: 10` (next after Work Assignments @ 9) |

No collision with `communications-list` (direct widget), `email-compose` (direct widget), or `communications` (section id) — verified by inspection of `register-workspace-widgets.ts` and `sectionRegistry.ts` before adding.

## Deviation from the POML's literal step 1

The POML's step 1 suggested assigning `m.EmailWorkspace` directly as the
widget factory result (mirroring `communications-list`'s
`CommunicationsWorkspaceWidget` cast). That works for
`CommunicationsWorkspaceWidget` because its only prop is an optional
`configId`. `EmailWorkspace` (task 040) is host-agnostic by design and
requires SIX mandatory props (`dataverseClient`, `dataService`,
`navigationService`, `webApi`, `authenticatedFetch`, `bffBaseUrl` — see
`EmailWorkspace.types.ts` docblock: "the assembling mount resolves the
concrete Xrm-backed or BFF-backed adapters ... and hands them in"). Casting
the bare component directly would compile (via `as unknown as
WorkspaceWidgetComponent`) but crash at runtime with all six props
`undefined`.

**Resolution (directional steps mode, task-execute Step 8 note)**: added a
thin host-adapter wrapper per mount, matching the established
`ComposeSectionMount` (`composeEditor.registration.ts`) and
`DataverseEntityViewWidget.tsx` precedents already in this codebase:

- **SpaarkeAi direct widget**: new file
  `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/EmailWorkspaceWidget.tsx`.
  Resolves `authenticatedFetch`/`bffBaseUrl` via `useAiSession()` (this
  widget only ever mounts inside SpaarkeAi's `AiSessionProvider` tree — unlike
  the dual-registered `DataverseEntityViewWidget`, which also mounts
  standalone in LegalWorkspace and therefore uses an optional context read).
  Resolves `dataverseClient`/`dataService`/`navigationService`/`webApi` via
  `@spaarke/ui-components`'s `XrmDataverseClient` /
  `createXrmDataService()` / `createXrmNavigationService()` /
  `getXrm()?.WebApi`.
- **LegalWorkspace section**: the `EmailSectionMount` inner component inside
  `email.registration.ts` itself (single-file shim, matching
  `composeEditor.registration.ts`'s convention). Resolves
  `authenticatedFetch` from the LegalWorkspace-local `services/authInit`
  module (same source `dailyBriefing.registration.ts` uses) and `bffBaseUrl`
  from the `SectionFactoryContext` the factory receives. Same Xrm adapters as
  the widget wrapper.

Both wrappers fail closed (render `null`) if `getXrm()?.WebApi` is
unavailable (non-MDA dev shell) rather than mounting `EmailWorkspace` with
`undefined` required props.

## `sectionMetadataCatalog.ts` (out of the POML's file list, added for correctness)

`sectionRegistry.ts`'s dev-mode guard (`runRegistryDevGuards`) cross-checks
every `SECTION_REGISTRY` id against `SECTION_METADATA_CATALOG` in
`@spaarke/ui-components` and `console.error`s on drift in either direction.
Adding the `email` section to `SECTION_REGISTRY` without a matching catalog
entry would trip that guard on every dev build. Added the `email` entry to
`sectionMetadataCatalog.ts` (mirroring the `communications` entry, but
`defaultHeight: '720px'` / no `contentSizing`/`widthPreference` — Email is a
tall two-pane reading surface, not a dense clamped grid).

## Deploy-SystemWorkspaceLayouts.ps1 — no code change required

The script is fully data-driven: it reads `scripts/system-layouts.json`
generically (`Build-SectionsJson -SectionId $layout.sectionId
-LayoutTemplateId $layout.layoutTemplateId`) and the `single-column` template
was already supported. The seed entry alone (added to `system-layouts.json`)
is sufficient — no script logic change was needed or made.

## Build verification

- `Spaarke.AI.Outputs`, `Spaarke.AI.Context`, `Spaarke.UI.Components`,
  `Spaarke.Communication.Components`, `Spaarke.AI.Widgets` — all `npm run
  build` (tsc) clean. (`Spaarke.AI.Outputs`/`Spaarke.AI.Context` needed a
  first-time `npm install` in this worktree — pre-existing, unrelated to this
  task; done here only to unblock the `Spaarke.AI.Widgets` build chain.)
- `Spaarke.AI.Widgets` existing test suite
  (`register-workspace-widgets.test.ts`): 40/40 passed.
- `LegalWorkspace` `npm run build` (vite): fails on a PRE-EXISTING, unrelated
  broken import — `@spaarke/document-operations` from
  `Spaarke.Compose.Components/src/widgets/ComposeToolbar.tsx` (introduced by
  `feat(compose-r4)` commit `bae44955b`, long before this task). Vite
  transformed all 2868 modules (including every file this task touched)
  successfully before failing on that unrelated module; a scoped `tsc
  --noEmit` also shows zero errors attributable to `email.registration.ts`,
  `sectionRegistry.ts`, or `sections/index.ts` (only the same pre-existing
  repo-wide noise: missing `@types/jest`/`@types/node`, other stale
  `@spaarke/ai-widgets` subpath resolutions, etc.). Out of scope for 041 — not
  touched.
- `Spaarke.UI.Components`'s `sectionMetadataCatalog.test.ts` has one
  PRE-EXISTING failing assertion ("contains exactly the 7 canonical system
  sections") that was already stale before this task — it never accounted
  for `matters`/`projects`/`invoices`/`work-assignments`/`communications`/
  `compose-editor`, all added by earlier tasks. Adding `email` adds one more
  entry to the mismatch but does not newly break a previously-green test. Not
  fixed here (would require reconciling 6 pre-existing entries, out of this
  task's scope) — flagged for `/defer` at task 090 wrap-up.

## For task 042 (code page mount) and task 051 (deploy)

- Reuse the SAME `email` section id / widget type string — do not invent a
  second identifier.
- The code page's own mount will need the SAME six `EmailWorkspace` props
  resolved via its own host context (likely BFF-backed adapters rather than
  Xrm-backed, depending on how the standalone page authenticates) — see
  `EmailWorkspace.types.ts` docblock for the contract.
- Task 051 deploy: `scripts/system-layouts.json`'s new "Email" entry (sortOrder
  10) will be picked up by `Deploy-SystemWorkspaceLayouts.ps1` on the next run
  — no additional deploy-script change needed.
