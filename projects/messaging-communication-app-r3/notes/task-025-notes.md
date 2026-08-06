# Task 025 — Conversation title links to the associated record (FR-12)

**Status**: completed 2026-07-21 · FULL rigor · sonnet/high · Step 9.5 gate RUN — verdict **FIX-FIRST → shipped** (2 Minor fixes applied, 1 accepted with rationale).

## What shipped (ConversationView.tsx only — context-agnostic shared lib)
- **New header** at the top of `ConversationView` showing the conversation `title`. Rendered **only when a `title` prop is supplied** → existing callers (tasks 011/013/014/021/022/023, none of which pass a title) are byte-for-byte unaffected (verified: full CV suite 63/63 green).
- **Title-as-link** when the thread has a `regarding` record **AND** the host wired `onOpenRecord(entityType, id)`: a Fluent v9 `Link as="button" type="button"` whose `onClick` calls `onOpenRecord(regarding.entityType, regarding.id)`. Otherwise (record-less thread, or no `onOpenRecord`) a plain `<Text>` title.
- **Delegation, not implementation (ADR-012 / MODAL-DECISION-CRITERIA)**: the shared component imports **no `Xrm`** and embeds **no iframe** — it hands the open to the injected host callback. The host wrapper (Phase 4 PCF/code page) wires it to the sanctioned OOB record-scoped modal (`Xrm.Navigation.navigateTo({ pageType:'entityrecord', … }, { target:2 })` — Layout 1). Documented in the `onOpenRecord` JSDoc.
- **New prop types** on `ConversationViewProps`: `title?`, `regarding?: IConversationViewRegarding`, `onOpenRecord?`. `IConversationViewRegarding` mirrors the shell's `IConversationWorkspaceRegarding` (entityType/id/name) to ease Phase-4 host wiring.
- **Barrel**: local `ConversationView/index.ts` now also exports `ConversationViewHandle` + `IConversationViewRegarding` (both previously unexported; the PCF task 030 needs the handle for `scrollToMessage`, hosts need the regarding type). Flows up via the existing `export * from './ConversationView'`.

## Step 9.5 gate — findings + resolution
- **Minor-1 (FIXED)** — `Link as="button"` defaulted to `type="submit"`; a host mounting the view inside a `<form>` would submit the form on title click. Added `type="button"` (matches the in-repo `SprkChatMessageRenderer` / `TextareaField` convention). Test added: asserts `type="button"`.
- **Minor-2 (FIXED)** — title had no heading semantics; screen-reader heading navigation skipped it. Wrapped the header row in `role="heading" aria-level={2}` (interactive link/plain text sits inside — valid ARIA). Test added: `getByRole('heading')` for both linked + plain variants.
- **Minor-3 (ACCEPTED)** — `IConversationViewRegarding.name` is unused by the open call. Kept as documented **parity mirror** of the shell's `IConversationWorkspaceRegarding` so hosts pass one regarding shape to both the shell and the view; JSDoc states it. Not scope creep — it removes a host-side type-mapping step.
- Gate ADR verdict: ADR-012 PASS (no Xrm/CF import) · ADR-021 PASS (semantic tokens only; `Link`/`Text` v9; dark-theme test) · ADR-028 PASS (no `@spaarke/auth`) · MODAL-DECISION-CRITERIA PASS (callback delegation, no bespoke modal/iframe) · NFR-05 PASS (keyboard Enter test, heading role, Label-in-Name holds) · §11 PASS (extended existing component via props, no new component/send path).

## Verification
- `npm test -- src/components/ConversationView` → **63 pass** (7 suites), incl. 8 new titleLink tests.
- `tsc --noEmit` → 2 pre-existing unrelated errors only (`EntityCreationService.ts` / `useWizardPageBootstrap.ts` — unbuilt sibling `@spaarke/auth`/`@spaarke/sdap-client` dist). `eslint` clean. `prettier --write` applied.

## Acceptance criteria — all met
Record thread → title is a link, click invokes `onOpenRecord('sprk_matter','M1')` ✅ · record-less / no-callback → plain non-interactive title ✅ · follows MODAL-DECISION-CRITERIA, shared lib imports no `Xrm` + no iframe ✅ · keyboard-operable (Enter) + ARIA-labeled + heading role ✅ · dark mode via host FluentProvider, semantic tokens ✅ · component tests pass ✅.

## Phase 3 status — COMPLETE
020, 021, 022, 023, 024, **025** all done. **Phase 3 finished.** Next: **Phase 4** (030 record right-pane PCF [opus], 031 SpaarkeAI widget, 032 standalone Vite code page, 033 Email&Messages DataGrid tab, 034 deploy) — the goal-eligible Wave 15.

## Phase-4 host-wiring note (carry forward)
The Phase-4 hosts render `ConversationView` into `ConversationWorkspace`'s `renderConversation` seam. To light up the FR-12 link they must pass, for the selected thread: `title` (thread name), `regarding` (the thread's associated record — in **record mode** it's the workspace `regarding`; in **all mode** it comes from the thread-list DTO), and `onOpenRecord` wired to `Xrm.Navigation.navigateTo`. `ConversationWorkspace`/`renderConversation` already forward arbitrary props — no shell change was needed for this task.
