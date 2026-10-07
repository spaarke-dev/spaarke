# Task 093 close-out — create-before-upload comment hygiene + regression pin

**Rescoped 2026-09-21.** The reorder task 093 was originally written to perform (upload → create →
link, migrated to create → upload → link across 7 wizards) already shipped via task 076 and is
compiler-enforced. This task closed out the residue: three stale comments, one regression test, and
two re-homed items. No wizard ordering changed, no Secure Project UI touched,
`EntityCreationService`'s method signatures unchanged (see the "Lane + diff" section below).

## 1 — The four premises, re-verified at HEAD, all FALSE (as the rescope expected)

Re-verified 2026-09-21 against code, not against the plan note, per Step 0's instruction not to trust
a note twice.

1. **"`EntityCreationService.ts` orders upload → create → link."** FALSE. The file's own usage
   example (`:19-31`, unchanged by this task) already reads *"Create the record FIRST — the upload is
   keyed on it, and the server resolves the storage container from that same record (task 076). There
   is no container parameter."*

2. **Create-first is compiler-enforced.** Confirmed: `uploadFilesToSpe(entityLogicalName: string,
   recordId: string, files: IUploadedFile[], onProgress?)` (`:479-484`) has `recordId: string` as a
   required (non-optional) positional parameter. There is no container parameter to get wrong, and
   the old `(containerId, files, onProgress)` shape cannot compile.

3. **"The Secure Project step exists in TWO implementations."** FALSE. `Grep` for
   `SecureProjectSection.tsx` across `src/` returns exactly ONE file:
   `Spaarke.UI.Components/src/components/CreateProjectWizard/SecureProjectSection.tsx`. No second
   implementation exists to sequence against.

4. **"There are exactly three such flows."** FALSE — there are TWO. `ripgrep` for
   `uploadFilesWithoutRecord` across `src/**/*.{ts,tsx}` returns exactly three files: the service
   definition itself (`EntityCreationService.ts`) plus its two callers —
   `createXrmEmailComposeHandlers.ts:262` (EmailComposer local attachment) and
   `CreateAnalysisWizardWidget.tsx:783` (Analysis wizard standalone document). The third
   (DocumentUploadWizard's "skip associate") was cut over to `uploadFilesToSpe` by task 076.

None of the four premises held, so the escalation trigger ("if any premise turns out TRUE, STOP and
report") did not fire. The rescope's own reading of the code was correct.

## 2 — Classification resolution (flagged for owner confirmation, not decided unilaterally)

`EntityCreationService.ts:511-513` (pre-fix) called the two survivors debt: *"reordering them so the
record exists first is task 093, after which this method should lose callers."* Task 076's own notes
(`notes/task-076-client-cutover-and-supplier-classification.md:54-55`) classify BOTH as legitimately
**"parentless"** in its 8-call-site table:

| Row | Call site | Classification |
|---|---|---|
| 7 | `EmailComposer/createXrmEmailComposeHandlers.ts` | `uploadFilesWithoutRecord(…)` — parentless |
| 8 | `Spaarke.AI.Widgets/.../CreateAnalysisWizardWidget.tsx` | `uploadFilesWithoutRecord(…)` — parentless |

Both readings cannot be right. This task resolved in favor of 076's "parentless" reading, for three
reasons: (a) 076's classification is chronologically later than the comment it contradicts; (b) 076
is the project that actually performed the cutover and made the call-site-by-call-site decision;
(c) the comment being replaced was *demonstrably wrong on its own terms* — it asserted three flows in
the same sentence where the count is two, which is independent evidence it had gone stale rather than
being a deliberate, current decision.

🔔 **Flagged for owner confirmation, not decided on this task's own authority** (per plan D-8). If the
owner instead confirms the comment's "debt" reading — i.e., that these two callers should be migrated
away and `uploadFilesWithoutRecord` retired — that is NEW small work. It is not this task, and per the
plan constraint it must not be improvised in here. Candidate shape, if confirmed: EmailComposer would
need to persist a minimal draft record before attaching a file, and CreateAnalysisWizardWidget would
need to create `sprk_analysis` before its document upload — both are real product changes with UX
implications, not comment fixes.

## 3 — Comments corrected (3 files, comment-only diffs)

All three sites previously said the reorder "is task 093" (implying pending work) and/or (in
`EntityCreationService.ts`) that the method "should lose callers." Both phrasings are retired. Each
comment now:
- States the correct count (TWO callers, not three).
- Cites 076's classification with a file:line reference
  (`notes/task-076-client-cutover-and-supplier-classification.md:54-55`).
- Keeps the load-bearing warning: routing a record-bearing upload through the record-less path stores
  bytes in the caller's business-unit container instead of the record's, which is wrong for a secure
  record and not reversible.

Files changed:
- `src/client/shared/Spaarke.UI.Components/src/services/EntityCreationService.ts` (`uploadFilesWithoutRecord` doc comment, ~:502-517)
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/CreateAnalysisWizardWidget.tsx` (~:764-783; also dropped the stale "(2 of 3)" counter)
- `src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/createXrmEmailComposeHandlers.ts` (~:238-248; also dropped the stale "(1 of 3)" counter)

## 4 — Regression pin: what was pinned, what was tried and abandoned, and why

**New file**: `src/client/shared/Spaarke.UI.Components/src/services/__tests__/EntityCreationService.createBeforeUpload.test.ts`.

**What it pins (runtime, proven by perturbation).** `uploadFilesToSpe` must route a record-keyed
upload to the record-KEYED BFF route (`/api/obo/records/{entity}/{recordId}/files/{name}`), never the
record-less one (`/api/obo/me/files/{name}`), and the supplied `recordId` must reach that URL
verbatim. Asserted on the URL produced by a real `SdapApiClient` over a fake `authenticatedFetch`
(mirrors the pattern in `document-upload/__tests__/FileUploadService.sharedClient.test.ts`), not on a
mocked SDAP method — so a regression inside the routing decision itself cannot hide behind a double
that already assumes the right route was picked.

**Perturbation evidence:**

| Run | Change | Result |
|---|---|---|
| Baseline | none | `Test Suites: 1 passed, 1 total` / `Tests: 1 passed, 1 total` |
| Perturbed | `uploadFilesToSpe`'s upload call swapped from `uploadFileForRecord(entityLogicalName, cleanRecordId, file)` to `uploadFileWithoutRecord(file)` | `Test Suites: 1 failed, 1 total` / `Tests: 1 failed, 1 total` — failure shows `Expected substring: ".../api/obo/records/sprk_matter/11111111-.../files/brief.docx"` vs `Received string: "https://bff.example.com/api/obo/me/files/brief.docx"` |
| Reverted | change undone | `Test Suites: 1 passed, 1 total` / `Tests: 1 passed, 1 total` — `git diff --stat` on `EntityCreationService.ts` confirmed only the doc-comment region differs from HEAD |

**What was tried and abandoned for pinning "`recordId` is a required parameter" specifically, and
why — a genuine finding, not a shortcut.** The task-093 escalation trigger anticipated needing a
compile-fail fixture (`@ts-expect-error`) if the runtime form of "recordId lost its required-ness"
were unrepresentable. Two approaches were tried and both proved unusable in THIS package's actual
toolchain (verified empirically, not assumed):

1. **`@ts-expect-error` compile-fail fixture.** `jest.config.js`'s `ts-jest` transform resolves this
   package's `tsconfig.json`, which sets `"isolatedModules": true`. Probe: placed
   `// @ts-expect-error` above a line with NO type error (which must fail to compile under normal
   TypeScript — "Unused '@ts-expect-error' directive", TS2578) in a throwaway test file and ran
   `npx jest`. It **passed**. ts-jest's isolatedModules mode transpiles each file independently
   (Babel-like) without cross-checked type diagnostics, so a compile-fail fixture inside a `.test.ts`
   file is silently inert here — it would pass whether the pinned contract holds or not. Separately,
   this package's `tsconfig.json` `exclude`s the `__tests__` directory and every `.test.ts` /
   `.test.tsx` file from its own `include`, so even the real `tsc` build (`npm run build`) never
   type-checks this directory either. **No tool in this package's pipeline would ever evaluate a
   type-only assertion placed in a `__tests__` file** — shipping the fixture would have been exactly
   the decorative-test failure mode (passes both before and after the break) this task exists to
   retire, not reintroduce.
2. **`Function.prototype.length` arity pin.** Tried as a pure-runtime alternative. Measured
   empirically at **3** for both the real 4-parameter signature
   (`entityLogicalName, recordId, files, onProgress?`) and a perturbed 3-parameter one with
   `recordId` removed (`entityLogicalName, files, onProgress?`) — identical value in both cases, so
   arity cannot distinguish the two states in this toolchain and cannot serve as a pin.

Per the escalation trigger's own instruction ("say so… do NOT ship a test that passes both before and
after the break"), the `recordId`-required-parameter contract is therefore pinned **indirectly** by
the runtime routing test above (a `recordId` that never reached the request would not appear in the
asserted URL) rather than by a dedicated compile-time fixture, because no dedicated compile-time
mechanism exists in this repo's tooling for a `__tests__`-scoped file.

**Final test counts** (whole-package, unrelated to this change): `npx jest --no-coverage` →
`Test Suites: 8 failed, 227 passed, 235 total` / `Tests: 13 failed, 3309 passed, 3322 total`. All 8
failing suites (`buildDynamicWorkspaceConfig`, `surfaceLaunchRegistry`, `todoScoreMappings`,
`configResolution`, `RichFilePreview`, `TimelineComposeBox`, `ConversationView.emailInFlow`,
`ConversationView.forward`, `AccessGrantModal.userShare`) were confirmed pre-existing and unrelated —
none references `EntityCreationService` or `createXrmEmailComposeHandlers` (grep-verified), and the
new/modified files are not among them. `npx tsc --noEmit` on the package is clean (exit 0). All 3
`EntityCreationService*.test.ts` suites: 23/23 passed.

## 5 — Re-homing (per Step 4)

**(a) The live "files land in the project's own container" assertion → task 047.** Task 047
(`047-validate-secure-project-provisioning-live.poml`) already depends on 093 (`<deps>045, 046,
093</deps>`) and its own background section already states plainly what it will NOT prove: *"Nothing
yet READS the project's container — document isolation needs the three container-resolution
strategies special-cased… So a passing run proves provisioning RECORDS the right container; it does
NOT prove documents land in it."* That is exactly this residual item's subject. No POML edit was made
here (out of this task's lane) — this note records the re-homing target; 047 is the task with the
live dev environment to actually run it.

**(b) Secure MATTER provisioning → owner-deferred, needs its own future task.** Confirmed by grep: the
BFF has `ProvisionProjectEndpoint.cs` (`src/server/api/Sprk.Bff.Api/Api/ExternalAccess/`) with no
`ProvisionMatterEndpoint` counterpart anywhere under `src/server`. `provision-project` is
project-only. Per the POML notes, the owner has set secure-matter provisioning as a later add-on; no
task exists for it yet. Recorded here so it is not silently lost — a future task should be filed
against the owner's decision to pursue it, scoped analogously to task 021 (project provisioning) but
for `sprk_matter`.

## 6 — Additional residual found in passing — FIXED by the main session (was out of the agent's lane)

`src/client/shared/Spaarke.SdapClient/src/operations/UploadOperation.ts:84-86`
(`uploadSmallWithoutRecord`'s doc comment) also says *"For the three flows where the bytes genuinely
move before any record exists — an EmailComposer local attachment, the Analysis wizard's standalone
document, and DocumentUploadWizard's 'skip associate'."* This is the SAME stale "three flows" claim
this task retired in the three named files, but `Spaarke.SdapClient` is outside this task's strict
lane (only `EntityCreationService.ts`, `CreateAnalysisWizardWidget.tsx`,
`createXrmEmailComposeHandlers.ts`, the new test file, and this notes file were in scope). The agent
flagged it rather than touching it, which was correct lane discipline.

**The MAIN SESSION then fixed it (2026-09-21)**, because the owner's standing directive is that
nothing is knowingly deferred: a fourth file asserting the retired premise is the same defect, and
leaving it is exactly the premise rot this task exists to kill.

🔴 **Fixing it surfaced something the two-caller count does NOT cover.** The first attempt simply
rewrote "three flows" to "TWO flows" — and that would have been **a new wrong claim in a comment
whose whole purpose is retiring a wrong claim**. `uploadFileWithoutRecord` has a **third** production
consumer at the SdapClient layer:

| Consumer | Layer | Notes |
|---|---|---|
| `EntityCreationService.ts:526` (`uploadFilesWithoutRecord`) | UI.Components service | itself has exactly **two** callers — the count §1 verified |
| `document-upload/FileUploadService.ts:93` | UI.Components service | **a generic dispatcher**, not a flow: it branches on `request.target.kind === 'record'` and serves whatever any caller passes with a non-record target |

So **`EntityCreationService`'s corrected comment is accurate as scoped** — it counts callers of *its
own* method, and that is genuinely two. But a flow count is **unknowable at the `UploadOperation`
layer**, because `FileUploadService` will route anything a consumer hands it.

**Resolution**: `UploadOperation.ts`'s comment no longer asserts a caller count at all. It states the
parentless-content contract, records that the old "three flows" claim was wrong and why, and points
here for the verified caller set. A low-level client method enumerating its consumers' flows is the
coupling that made the comment go stale in the first place — replacing one count with a fresher count
would have reset the clock on the same defect rather than removing it.

## 7 — Lane discipline verification

`git diff --name-only` at close-out (see final report) shows exactly: the three named source files
(comment-only diffs), the new test file, and this notes file. No `.cs` file, nothing under
`src/server/**`, no POML `<status>`, `TASK-INDEX.md`, or `current-task.md` touched. `EntityCreationService.ts`'s
method signatures are unchanged from HEAD (verified via `git diff --stat`: only the doc-comment hunk
differs — the perturbation edits used to prove the regression pin were fully reverted before this
close-out).
