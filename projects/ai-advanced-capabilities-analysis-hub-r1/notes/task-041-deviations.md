# Task 041 — Wire activeWorkType host prop → getToolsForSurface scoping: deviations

> Documented per task-execute Step 8 ("Document any deviation"). No `<escalation>` trigger fired —
> this is a scoping/interpretation decision, not an ADR conflict.

## 1. `ComposeEditor.tsx` needed NO change — it already fully implements `activeWorkType`

The POML listed `ComposeEditor.tsx` as a "modify (if needed)" target. Investigation
(`ComposeEditor.tsx` lines 638-646, 1543, 2243, 2706, 2968) found the prop is already fully
implemented, documented, and threaded into `getToolsForSurface` at BOTH its call sites (the
right-click/selection popup via `ComposeAiToolbar`, and the `review-note` surface). Its own doc
comment already reads: *"The host passes this so the BubbleMenu + Review-Note ⋮ menu surface
work-type-scoped tools... Defaults to `'*'`."* No change was made to this file.

## 2. The real gap was one layer up: `ComposeWorkspace` did not forward the prop at all

`ComposeWorkspace` (`src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx`)
is the component that actually mounts `<ComposeEditor>` (~line 2365). Its `ComposeWorkspaceProps`
had NO `activeWorkType` field, and the mount site did not pass one — every existing Compose mount
was therefore permanently unscoped (`'*'`) regardless of launch context. This is the concrete,
testable gap this task closes.

## 3. No live "Agreement Review → Compose" dispatch site exists yet — the literal launch path named in the POML doesn't do what the POML implies

The POML's `<relevant-files>` named *"the host launch path that mounts ComposeEditor for Agreement
Review (Spaarke.AI.Widgets launch/wizard wiring from task 040)"* as a modify target. Task 040's
`CreateAnalysisWizardWidget` (`src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/
CreateAnalysisWizardWidget.tsx`) was inspected in full: its `onFinish` dispatches a `widget_load`
for the `document-viewer` widget (`RichFilePreview`), NOT the `compose` widget/`ComposeEditor`. This
is intentional per task 040's OWN documented scoping decision (`notes/task-040-deviations.md` §1):
session binding + AI-execution plumbing that would drive an actual Compose/review launch is
explicitly out of scope for 040, owned by tasks 020-025. There is therefore no concrete, live call
site today that launches an "Agreement Review" into `ComposeEditor` to modify.

Making `CreateAnalysisWizardWidget` dispatch to `compose` instead of/in addition to `document-viewer`
would have been (a) a behavior change to a shipped, tested task-040 deliverable — outside this
task's small, modify-only blast radius — and (b) speculative wiring with no consumer (the resulting
`activeWorkType` field on that dispatch would be dead code, since `DocumentViewerWidget` doesn't
read it), which fails the CLAUDE.md §11 "concrete cost-of-doing-nothing" test.

## 4. What was actually delivered: the full additive plumbing chain, PLUS end-to-end test coverage of the palette scoping

Per the task's own framing ("This task is the PLUMBING... the scoping function already exists"),
the deliverable is threading the value through every EXISTING Compose host, additively (optional
field, no behavior change when omitted), so that whichever launch path is wired up in the future
(a `sprk_analysis`-aware ribbon button, a hub-widget "Review" action, etc.) needs zero further
plumbing work — just supply the value at the call site:

- `ComposeWorkspaceProps.activeWorkType?: string` (Spaarke.Compose.Components/ComposeWorkspace.tsx)
  → forwarded unchanged to `<ComposeEditor activeWorkType>`.
- `ComposeLaunchContextValue.activeWorkType?: string` (composeLaunchContext.ts) — the shared
  cross-solution context both mount doors already use.
- LegalWorkspace's `composeEditor.registration.ts` (`ComposeSectionMount`) — reads
  `composeLaunch?.activeWorkType`, threads to `<ComposeWorkspace>`.
- SpaarkeAi's `ComposeDirectWidget.tsx` (`ComposeDirectMount` + `buildLaunchFromSeed`'s 3
  door-shape branches) — same threading, plus a new `ComposeWidgetSeed.activeWorkType` field
  (composeWidgetData.ts) for the Direct-widget tab-seed path.
- `main.tsx`'s `SpaarkeAiWorkspaceRenderer` seed-translation mirror (the chat-opened-tab layout
  path) — same additive field + 3-branch threading.
- URL-param chain: `main.tsx` parses `?activeWorkType=`, forwards through `App.tsx` →
  `ThreePaneShell.tsx` → the `composeLaunch` memo → `ComposeLaunchContext.Provider`.
- `launch-resolver.ts`: `SpaarkeAiComposeLaunchParams.activeWorkType` + `buildLaunchUrl` encoding —
  the ribbon "Open in Compose" launch point (`DocumentComposeLaunch.ts`) can now pass it once a
  future work-type-aware caller exists.

Every field is optional and additive; omitting it at any layer falls through to `undefined`, which
`ComposeEditor` already treats as `'*'` (unscoped) — no regression for any pre-existing mount.

## 5. Test coverage

- `ComposeEditor.activeWorkType.test.tsx` (NEW, real-mount, mirrors
  `ComposeEditor.aiToolbarTriggers.test.tsx`'s pattern): registers a throwaway
  `workTypes: ['agreement-analysis']` tool via the ALREADY-SHIPPED `registerComposeAiToolbarAction`,
  and proves the REAL right-click popup shows/hides it exactly per the already-tested
  `getToolsForSurface` rule — covering "Agreement Review scopes palette", "default is unscoped",
  "unrecognized work type falls back to `'*'`-only", and ADR-021 dark-mode.
- `ComposeWorkspace.activeWorkType.test.tsx` (NEW, mocked-editor, mirrors
  `ComposeWorkspace.browse.test.tsx`'s mocking convention): proves `ComposeWorkspace` forwards its
  own `activeWorkType` prop to `<ComposeEditor>` unchanged, including the default-omitted case and a
  dark-theme mount.
- `launch-resolver.test.ts`: two new cases (`buildLaunchUrl` + `openSpaarkeAiCompose`) proving
  `activeWorkType` reaches the URL data blob, and is omitted (no regression) when not supplied.

`getToolsForSurface` itself was NEVER modified or reimplemented — every new test consumes it via its
existing public exports (`getToolsForSurface`, `registerComposeAiToolbarAction`,
`__resetComposeAiToolbarActionsForTests`).
