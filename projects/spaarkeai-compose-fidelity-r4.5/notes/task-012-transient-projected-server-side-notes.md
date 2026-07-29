# Task 012 — Open-in-Compose transient drafts projected server-side (FR-02 / WS-1) — implementation notes

**Status**: audit complete; FR-02 found ALREADY SATISFIED by tasks 010+011; a client-level
regression guard was added (no server change needed, no `.csproj` change). This note is written by
the executing subagent, which is NOT permitted to edit `tasks/TASK-INDEX.md` or `current-task.md` —
the orchestrator/human should flip task 012 to ✅ there.

## Outcome: `fr02_outcome = "already-covered-by-010-011+guard-added"`

## Audit (Step 1/2 — re-grep every `mountTransient` dispatch + transient entry path)

`grep -rn "mountTransient"` across `src/` turned up exactly **two** `dispatch({ kind: 'mountTransient', ... })`
call sites in `ComposeWorkspace.tsx` (the reducer/type only define the action once):

| # | Site | Door | Bytes source | Server round-trip | Projection wiring |
|---|---|---|---|---|---|
| 1 | `ComposeWorkspace.tsx:1764` (inside `handleBrowseFileSelected`) | **Browse-local-docx** (empty-state "Browse / open file" CTA) | Local `FileReader` read of the picked `.docx` | `POST /api/compose/project` (task 011) | Already hydrates `projection` — see `:1729-1770`. Best-effort: BFF-unreachable/failed → `projection: null` (documented, intentional degrade — Browse's historical zero-BFF-dependency contract for the *mount* itself is preserved; only render fidelity degrades to `mammoth`). |
| 2 | `ComposeWorkspace.tsx:2073` (inside the `initialUploadRef` effect) | **Assistant-upload "open in Compose"** (chat "open in Compose" on an Assistant-UPLOADED file) | `POST /api/compose/upload` retained bytes from `ITenantCache` | `POST /api/compose/upload` response's `projection` field (task 010) | Already hydrates `projection` via `normalizeProjection(payload.projection)` — see `:2042-2078`. |

Both dispatch sites already pass a resolved `projection` (not a hardcoded `null`) into `mountTransient`,
and the reducer (`ComposeWorkspace.types.ts:322-359`) already does `projection: action.projection ?? null`
(also wired by task 010) — never a hardcoded `null` unconditionally.

**Where does "open in Compose" launch from?** Traced every producer of `composeLaunch.upload` /
`initialUploadRef` (the only prop that feeds site #2): `ComposeDirectWidget.tsx:200-201` reads
`composeLaunch?.upload` from the seed `main.tsx`/`registerComposeWidget.ts` construct, which is populated
ONLY by `ConversationPane.tsx`'s `mountFileInCompose` (`widgetData: { compose: { upload: { sessionId,
sessionFileId, fileName } } }`, line ~271) — the chat "open in Compose" chip/click and the auto-mount-active-doc
effect BOTH funnel through this SAME function. There is no second, distinct "open in Compose"
conduit for a transient/uploaded file — one door, already wired by task 010.

**Is there a third, different "open in Compose" surface?** Checked `DocumentComposeLaunch.ts` (the
`sprk_document` form ribbon "Open in Compose" command) — it resolves `sprk_graphitemid`/`sprk_graphdriveid`
from the STORED record and calls `openSpaarkeAiCompose({ speDriveItemId, speDriveId, ... })`, which
populates `initialDocumentRef` (the **stored-document Load path**, `requestLoad`/`loadSucceeded`), NOT
`mountTransient`. That path already carries `projection` in `loadSucceeded` (pre-existing R4 Shadow
Document wiring, `ComposeWorkspace.types.ts:171-173`/`294-295`) — out of scope for this task (not a
transient mount at all) and not a gap.

**`mountDraftHtml` (DEF-08 AI-drafted seed) — explicitly out of scope, not an escalation candidate.**
There is a THIRD "transient, no-SPE-pointer" mount kind, `mountDraftHtml` (`ComposeWorkspace.types.ts:210`,
dispatched from `ComposeWorkspace.tsx:2135`/Part A ledger-resolve path), which ALSO always sets
`projection: null` (`types.ts:381`, comment: "No server round-trip → no projection; the editor falls
back to the client mammoth convert"). This is NOT the same door FR-02 targets: it seeds the editor from
**HTML** (`seedHtml`), never from `.docx` bytes — there is no OOXML source to run through
`ComposeDocxProjectionBuilder` at all, so it never reached the `mammoth` branch in the first place (the
editor's `projection ? <projection branch> : <mammoth-if-docxBytes-else-seedHtml>` logic at
`ComposeEditor.tsx:1966-1993` only enters `mammoth` when `docxBytes` is non-null; `mountDraftHtml` sets
`docxBytes: null`). F-2 "one reader" governs the docx→editor reader; an AI-drafted HTML seed was never a
`mammoth` consumer, so it is out of F-2's scope by construction — not a "pure client construct with no
server round-trip" in the sense the escalation trigger means (that trigger is about a *docx* transient
draft with no bytes to project), and Part A of the same mount even DOES have a server round-trip (`GET
/compose-outputs`) — it's a ledger-content resolve, not a docx projection candidate. No escalation fired.

## Conclusion

Tasks 010 (assistant-upload door) and 011 (Browse door) together already cover **every** `mountTransient`
dispatch site in the codebase — there is no remaining door that mounts with a hardcoded `projection: null`.
FR-02's acceptance criterion ("`mountTransient` hydrates a real `ComposeServerProjection` — never
`projection: null`... editor takes the projection branch, NOT `mammoth`") is satisfied for BOTH real
docx-bytes doors as of task 011's merge into this branch. Per the POML's Step 3 fallback path B: this is
documented plainly as a legitimate outcome, not a shortcut — no server or reducer code was invented or
duplicated to manufacture "new" work.

## Regression guard added (the actual work product of this task)

The server-side seam tests from 010/011 (`ComposeUploadProjectionSeamTests.cs`,
`ComposeProjectSeamTests.cs`) already prove the TWO BFF ENDPOINTS return a non-null projection for real
docx bytes. They do **not** reach the CLIENT dispatch/reducer/effect layer — a future regression that
silently drops a present server projection back to `null` somewhere in `ComposeWorkspace.tsx`'s
`normalizeProjection` call, the `dispatch({ kind: 'mountTransient', ... })` call, or the reducer's
`action.projection ?? null` line would NOT be caught by those BFF-only seam tests. That is exactly the
class of regression the escalation trigger and FR-02's acceptance criterion #1 care about ("never
`projection: null`"), so a client-level guard closes the real gap:

- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.upload.test.tsx` — extended
  the `ComposeEditor` mock to also capture the `projection` prop (`editorProjection`), and added
  `hydrates a non-null projection from the upload response (FR-02 one-reader regression guard)`: mocks
  `POST /api/compose/upload`'s response with a `projection` field and asserts the editor receives it
  non-null with the exact fields (`status`, `canEdit`, `html`).
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.browse.test.tsx` — same
  pattern for the Browse door: mocks `POST /api/compose/project`'s response and asserts the same.

Both new tests fail loudly (`editorProjection.current` stays `null`/`undefined`, or the wrong shape) if a
future change reintroduces the FR-02 regression on either door — the concrete "regression guard... a
future change can't silently reintroduce `projection: null`" artifact requested by the POML's Step 3
fallback (b).

No new `tests/integration/seam/` file was added — the server-side "does the endpoint return a
projection, not null" proof already exists (010/011's seam tests) and a THIRD copy of that exact proof
would violate root CLAUDE.md §11 (component/test justification — extend, don't fork). The genuinely
missing coverage was at the client boundary, which is where the two new tests were added.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/` → 0 errors (23 pre-existing warnings, unchanged set —
  no `.cs` file was touched by this task).
- `dotnet test --filter "FullyQualifiedName~Compose"` → **622 passed, 1 skipped (pre-existing,
  numbering harness pending WS-3), 0 failed** — identical to task 011's own reported count (expected:
  zero server changes this task).
- `git diff --stat -- '**/*.csproj'` → empty. **0 MB publish-size delta** (no dependency added, no
  server code touched at all).
- Client: `npx jest ComposeWorkspace.upload.test.tsx ComposeWorkspace.browse.test.tsx` (from
  `src/client/shared/Spaarke.Compose.Components/`) → **17 passed** (15 pre-existing + 2 new regression
  guards), 0 failed. Full package suite: `npx jest` → **631 passed, 1 pre-existing failure, 632 total**
  (see "Pre-existing failure" below).
  - This environment's `@spaarke/{auth,ui-components,sdap-client,document-operations}` workspace
    packages had NO `dist/` build output (a pre-existing gap task 010's own notes already flagged —
    "workspace-package resolution... not linked in a standalone install"). Ran `npm install
    --legacy-peer-deps --no-audit --no-fund` + `npm run build` in each of `Spaarke.Auth`,
    `Spaarke.SdapClient` (a transitive dep of `ui-components`), `Spaarke.UI.Components`, and
    `Spaarke.DocumentOperations` (dependency order: auth/sdap-client → ui-components →
    document-operations) so the client Jest suite could actually RUN (not just `tsc --noEmit`, which
    is all 010/011 could achieve). No source files in those packages were modified — only build
    artifacts were produced. This is a one-time local-environment fix, not a code change; not part of
    the diff.
- **Pre-existing failure (unrelated, not touched by this task)**:
  `ComposeEditor.advisoryComments.test.tsx` → `placed` expected `1`, received `2` (task 031 advisory-comment
  target-resolution test). `git status --porcelain` on both the test file and `ComposeEditor.tsx`
  confirms neither was modified by tasks 010, 011, or this task — pre-existing, unrelated to FR-02.

## Placement Justification (root CLAUDE.md §10 / `.claude/constraints/bff-extensions.md`)

**Existing**: `POST /api/compose/upload` (task 010) and `POST /api/compose/project` (task 011) already
cover 100% of the transient-mount surface (see audit above) — no new server capability is needed.
**Extension**: N/A — no server code was added or modified by this task. **Cost-of-doing-nothing**: had
this task invented a THIRD server endpoint or a new `Services/Compose/` reader "to be safe," it would
have violated root CLAUDE.md §11 (component justification / default-to-reuse) by forking the exact same
`ComposeDocxProjectionBuilder` reader task 010 already exposed via `IComposeService.ProjectDocument` —
the audit is the load-bearing evidence that no such fork is warranted. `Services/Compose/` remains
untouched and pure (no AI-internal type, no `Microsoft.Graph` above `SpeFileStore`, per ADR-007/013) —
trivially true since zero `.cs` files were touched.

## Files changed

- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.upload.test.tsx` (extended)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.browse.test.tsx` (extended)

No `.cs`, `.csproj`, or non-test `.tsx`/`.ts` file was modified.

## Deviation from the POML for the orchestrator/reviewer

The POML's `<outputs>` section listed `ComposeEditor.tsx`/`ComposeWorkspace.tsx` as `role="modify"` and a
new `tests/integration/seam/` file as the expected test artifact, on the premise that `mountTransient`
"sets `projection: null` today." The re-grep in Step 1/2 (mandated by the POML itself: "RE-GREP the
anchors... Line numbers indicative") found that premise was already false as of task 011's landing — both
real `mountTransient` dispatch sites already hydrate a projection. Per the POML's own Step 3 escalation
guidance path ("IF the audit shows 010+011 ALREADY cover every transient-mount path... document... and
add a regression guard... this is a legitimate result, not a shortcut"), no `.tsx`/`.cs` production code
was changed; a client-level regression-guard test pair was added instead of a redundant BFF seam test.
