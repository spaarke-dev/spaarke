# Handoff — Assistant pane viewport clipping in the "Open in Compose" modal (defect D9)

> **From**: `spaarkeai-compose-r6` session (triage + code trace, 2026-08-13)
> **To**: `spaarkeai-assistant-enhancements-r3` (Assistant-surface owner)
> **Canonical register row**: `projects/spaarkeai-compose-r6/notes/phase1-deploy-uat.md` defect **D9**
> (committed `88b02beb0` on `work/spaarkeai-compose-r6`, merged lineage on master)
> **Status**: NOT root-caused to a single element — this doc narrows it to a specific subtree and
> gives the diagnosis recipe + fix pattern. Needs one live-DOM session to name the exact element.

---

## 1. Symptom (operator UAT, 2026-08-13)

Entry path: `sprk_document` form → command bar **"Open in Compose"** → SpaarkeAi code page opens in
an **Xrm dialog** (`navigateTo` web resource, dialog chrome titled "SpaarkeAi Code Page") with
`?composeMode=editor&…`, three-pane shell: Assistant (left) · Workspace/Compose (center-right).

In this modal host, the **Assistant pane's transcript viewport does not fill the pane**:

- The message list / suggestion area ends with a row **clipped mid-content** (a partially visible
  "…suggestions" row cut at the boundary), i.e. content is being clipped by an unbounded/overflowing
  box rather than scrolled inside a bounded one.
- Below the clipped content there is **dead whitespace** before the Model picker + composer input at
  the bottom of the pane.
- The same Assistant on the **standard full-page SpaarkeAi surface** (and in the workspace/widget
  presentation) fills its pane correctly — bounded transcript, internal scroll, composer pinned.

Screenshot on file with the operator (2026-08-13 UAT session). The visual signature is the classic
"flex chain broken → transcript falls back to content-height" failure: clipping + dead space instead
of `flex: 1` + internal scroll.

## 2. Why this lands with r3 (ownership triage)

- **Not a compose-r6 defect**: R6's scope was the Compose save/model pipeline
  (`Services/Compose/**`, `Spaarke.Compose.Components`). R6 never touched `ThreePaneShell`,
  `ConversationPane`, or `SprkChat`.
- **Not a forked layout**: since **compose-r1 task 092** (the "three-pane pivot"), the
  `?composeMode=editor` modal launch mounts the **same `ThreePaneShell`** as the full-page surface —
  `App.tsx` just forwards the compose launch params into the canonical mount. There is no
  modal-specific layout to patch; whatever fixes the chain fixes every host.
- The affected subtree (`ConversationPane` → `SprkChat` slots) is the Assistant surface — r1/r2
  built it, r3 owns it going forward and has the freshest context on these files.

## 3. Code trace (what was verified statically)

All paths relative to repo root; verified on master lineage `cb71cf3fc`+ (post-PR-#748).

### 3.1 Launch path (context, not the bug)

- `src/solutions/SpaarkeAi/src/ribbon/DocumentComposeLaunch.ts` — invocation-only ribbon handler
  (ADR-006); delegates to `openSpaarkeAiCompose` in `src/solutions/SpaarkeAi/src/utils/launch-resolver.ts`,
  which does the `Xrm.Navigation.navigateTo` web-resource dialog open. The dialog hosts the code
  page **in an iframe** — so `100vh` inside the page equals the *iframe* height (the dialog's inner
  height), not the browser window. That is smaller (and settles later) than the full-tab host —
  which is why a latent chain defect shows here and not on the full page.

### 3.2 The height chain, top → down (verified-correct portion)

`src/solutions/SpaarkeAi/src/App.tsx` (~line 69–96):

| Element | Style | Verdict |
|---|---|---|
| `appRoot` | `display:flex; flexDirection:column; width:100vw; height:100vh; overflow:hidden` | ✅ correct (100vh = iframe height in the dialog) |
| `scaleBar` | `flexShrink: 0` | ✅ correct |
| `layoutShell` | `flex: 1; minHeight: 0; overflow: hidden` | ✅ correct — the load-bearing `minHeight: 0` is present |

`src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx` (~line 331–338):

| Element | Style | Verdict |
|---|---|---|
| `shell` | `display:flex; width:100%; height:100%; overflow:hidden` | ✅ correct |

**Conclusion**: the app-level and shell-level chain is sound. The break is **below the shell** — in
the Assistant pane subtree: `ConversationPane`
(`src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx`, a large file; the
transcript is rendered through **`SprkChat`** slots from
`src/client/shared/Spaarke.UI.Components/src/components/SprkChat/`) — including whatever wrappers
ConversationPane inserts between the pane boundary and SprkChat's transcript scroller (note the
`transcriptFooterSlot` usage around ConversationPane ~line 2774–2991: the suggestion/footer content
scrolls WITH the conversation, i.e. it lives inside the transcript viewport — consistent with the
clipped "suggestions" row in the symptom).

### 3.3 Root-cause hypotheses (in likelihood order)

1. **An element in the ConversationPane/SprkChat chain is missing `flex: 1; minHeight: 0`** (or is
   `display:block` with no height), so the transcript region sizes to its content instead of its
   container. In the tall full-tab host the content happens to fit (or the extra height masks it);
   in the shorter dialog iframe it overflows → clip + dead space. This is by far the most common
   cause of exactly this signature, and the `minHeight: 0` omission is the classic culprit (flex
   children default to `min-height: auto`, which refuses to shrink below content height).
2. **A measured/captured height** (ResizeObserver / `getBoundingClientRect` / explicit px height)
   taken **before the Xrm dialog settles its final size**. Dialog iframes commonly report a
   transient height during open animation; if any wrapper freezes that number, the pane is
   permanently wrong in a way `flex` would never be. Grep note: no `100vh`/fixed heights were found
   in `ConversationPane.tsx` itself (`App.tsx` has the only `100vh`, correctly at the root), so if
   this is the cause it lives in SprkChat or a hook.
3. (Less likely) A **scroll container nested one level too deep** — the `overflow-y: auto` sits on
   an element that is itself unbounded, so the scrollbar exists (one is visible in the screenshot)
   but bounds only part of the content, clipping the rest.

## 4. Diagnosis recipe (~minutes, needs live DOM)

Static reading cannot name the exact element — do this once in the failing host:

1. Repro: any `sprk_document` with SPE pointers → "Open in Compose" → wait for the Assistant to
   render suggestions (content must exceed the pane height to trigger the symptom).
2. DevTools (F12 targets the code-page iframe; or `/ui-test` with Chrome integration): inspect the
   clipped row, then **walk UP the ancestor chain** from SprkChat's transcript scroller toward
   `appRoot`. At each element compare `offsetHeight` vs its parent's `clientHeight`.
3. **The first ancestor whose rendered height EXCEEDS its parent's is the culprit.** Check its
   computed style for the missing `min-height: 0` / `flex: 1`, or an inline px height (→ hypothesis 2).
4. Confirm by toggling the fix live in DevTools (add `flex:1; min-height:0` or delete the px
   height) — the transcript should immediately bound itself and scroll internally.

## 5. Fix pattern (what "done" looks like)

- Every intermediate wrapper between the pane boundary and the transcript scroller:
  `flex: 1; min-height: 0; overflow: hidden` (Fluent v9 `makeStyles`, tokens-only per ADR-021).
- The transcript scroll region itself: `flex: 1; min-height: 0; overflow-y: auto`.
- Fixed elements (pane header, Model row, composer): `flex-shrink: 0`.
- **No fixed or measured heights anywhere in the chain** — that is what makes the pane host-proof
  (Xrm dialog, full tab, widget presentation, future hosts) with zero per-host branching.
- If hypothesis 2 is confirmed instead: delete the measurement, replace with the flex chain above
  (do NOT re-measure on resize — the chain makes measurement unnecessary).

**Where the fix lands**: if the culprit is inside `SprkChat` → fix in
`Spaarke.UI.Components/src/components/SprkChat/` (benefits every SprkChat consumer); if it's a
ConversationPane wrapper → fix in `ConversationPane.tsx`. Either way it is **client-only**: ships
with a `sprk_spaarkeai` rebuild + `Deploy-SpaarkeAi.ps1` — no BFF involvement, no atomic-window
coordination needed.

## 6. Verification checklist

- [ ] Modal host ("Open in Compose" from a document): transcript bounded, internal scroll, no
      clipped rows, no dead space, composer pinned — light + dark theme.
- [ ] Full-page SpaarkeAi surface: unchanged (regression check).
- [ ] Workspace/widget presentation of the Assistant: unchanged.
- [ ] Resize the dialog (platform Expand/full-screen toggle on the dialog chrome): pane re-bounds
      correctly — this specifically guards against a reintroduced measured height.
- [ ] Long conversation (content ≫ pane height) and near-empty conversation both lay out correctly.
- [ ] If fixed in SprkChat: spot-check one other SprkChat consumer surface.

## 7. Related defects in the same register (context for batching)

`projects/spaarkeai-compose-r6/notes/phase1-deploy-uat.md`:

- **D8** — Compose tab "Blank page" mounts non-editable (empty-seed-specific; same `mountBornInEditor`
  path as Template which works; suspects at `ComposeEditor.tsx:2252`/`2155`). Compose-surface, R7 batch.
- **D6/D7** — save-progress indicator; Add Comment affordance (Compose-surface UX batch).
- **D1** — Save split-button fork UX + filename uniquify (Compose-surface).

D9 is the only Assistant-surface item in the set — hence this handoff rather than the R7 batch.
If r3 prefers to decline it, it falls back to the R7 Compose-UX batch; the register row + this doc
carry everything either team needs.
