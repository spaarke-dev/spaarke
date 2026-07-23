# UAT Round 5 — Communications conversation UI (2026-07-23)

Direct UAT iteration (not a POML task). Three surfaces share `@spaarke/ui-components`:
workspace widget · `sprk_communicationconversationpage` modal code page · `CommunicationConversationPanel` PCF.

## Feedback → Resolution

### Workspace widget
1. **Widget still not filling the full tab height (vertical).**
   - **Root cause (found via container-chain trace):** the Communications section
     registration `src/solutions/LegalWorkspace/src/sections/communications.registration.ts`
     still carried `contentSizing: "clamped"` + `defaultHeight: "480px"` from its old
     dense-DataGrid body. `buildDynamicWorkspaceConfig` translates `clamped` into
     `maxHeight:480px + overflow:hidden` on the SectionPanel card — a hard ceiling that
     capped the two-pane conversation shell at ~480px regardless of the widget root's
     `minHeight: calc(100vh - 200px)` floor.
   - **Fix:** switched to the **grow** pattern — dropped `contentSizing: "clamped"`, set
     `defaultHeight: "560px"`. With no `contentSizing`, `defaultHeight` becomes a
     `min-height` FLOOR (not a ceiling), so the widget's own `calc(100vh - 200px)` floor
     drives the true fill. This is exactly how SmartTodo/Calendar fill their tabs.
   - Deploy surface: **SpaarkeAi** code page (embeds LegalWorkspaceApp).

2. **Message pane missing the scroll arrow.**
   - Downstream symptom of #1: the jump-to-latest circle-down arrow (`ConversationView`,
     already implemented) only appears when the list overflows AND the user is scrolled
     up. With the pane capped at 480px and auto-scrolled to bottom, it stayed hidden.
     Once the pane fills, the affordance behaves. **No code change** in ConversationView.

### New Thread modal (`NewThreadModal` + shared `AssociateToStep`)
3. **Add padding between the modal title ("New conversation") and the "New Thread" section.**
   - Added a `title` style (`paddingBottom: spacingVerticalL`) on the `DialogTitle`.
4. **"Associate To" too big; remove the description + the "You can always link records later" hint.**
   - Added a `variant?: 'default' | 'compact'` prop to the shared `AssociateToStep`
     (`types.ts` + component). `compact`: heading renders at `size=300` (matches the
     "Name" `<Field>` label) and BOTH the subtitle ("Link this record…") and the skip
     hint are suppressed. Wizard callers keep the default variant (untouched).
   - `NewThreadModal` now passes `variant="compact"`.
   - Deploy surfaces: **conversationpage** + **SpaarkeAi** (+ PCF, all read shared dist).

### PCF (`CommunicationConversationPanel` / `ConversationModal.tsx`)
5. **Messages modal still anchored to the top of the screen.**
   - The round-4 `createPortal(modal, document.body)` fix did NOT hold. Investigation
     (two Explore agents) confirmed: Fluent's `<Dialog>` already portals its
     `DialogSurface` to `document.body`, and the top-anchoring is caused by a CSS
     `transform` on an app-shell ancestor (`html`/`body`/wrapper) that redefines the
     `position:fixed` containing block — portaling to `body` cannot escape a transform
     sitting AT/above `body`. No repo recipe exists to viewport-center a fixed-size
     Fluent Dialog in that context.
   - **Fix:** dropped the Fluent `<Dialog>` envelope; adopted the transform-ROBUST
     pattern the `DocumentRelationshipViewer` PCF already ships — a full-viewport
     `position:fixed; inset:0` flex-centered overlay (`overlay` + `surface` styles) that
     centers the 1040×72vh surface. A near-full-viewport overlay renders the same
     regardless of a residual ancestor offset. Kept `createPortal(..., document.body)` +
     re-wrapped `FluentProvider` for THEMING only (fluent-v9-portal-gotcha), and
     implemented Esc + backdrop-click dismiss ourselves (the Dialog used to provide them).
   - PCF version bumped **1.5.0 → 1.6.0** (5 locations: `index.ts` CONTROL_VERSION,
     `ControlManifest.Input.xml`, `Solution/pack.ps1`, `Solution/solution.xml`,
     `Solution/Controls/.../ControlManifest.xml`).

## Build / deploy status
- `@spaarke/ui-components` dist rebuilt clean (tsc, 0 errors).
- PCF `build:prod` succeeded; bundle copied into Solution; packed:
  `src/client/pcf/CommunicationConversationPanel/Solution/bin/CommunicationConversationPanelSolution_v1.6.0.zip`
  (user uploads to Dataverse manually).
- Code pages (conversationpage + SpaarkeAi) — build verification in progress; deploy on go-ahead.
- No BFF change this round.

## Investigation artifacts (this round)
- Widget container chain: clamp injected at `buildDynamicWorkspaceConfig.ts:353-361`;
  SmartTodo grow-pattern contrast at `todo.registration.ts:301`.
- Modal centering: only transform-immune in-DOM pattern is `RelationshipViewerModal.tsx:53-80`
  (`position:fixed; inset:0` flex overlay); `RecordNavigationModalShell` has NO positioning CSS.
