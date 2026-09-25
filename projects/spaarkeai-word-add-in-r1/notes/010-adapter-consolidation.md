# Task 010 — FR-04 adapter consolidation (Word)

> **Completed**: 2026-09-09 · **Rigor**: FULL · **Model tier**: opus @ xhigh · **Step mode**: PRESCRIPTIVE
> **Finding addressed**: plan.md §3 **F-e** — "FR-04 as written would regress the `.docx` save."
> **Risk addressed**: plan.md §7 **R-4** — "HostAdapterFactory activation surfaces latent breakage in Outlook."

---

## 1. What the task actually had to avoid

FR-04 reads: *"Route Word through `HostAdapterFactory` onto the tested `shared/adapters/WordAdapter.ts` … delete the duplicate."* Executed literally and in that order, it ships the **broken** extraction:

| | `word/WordHostAdapter.ts` (FR-04 says delete) | `shared/adapters/WordAdapter.ts` (FR-04 says keep) |
|---|---|---|
| `.docx` extraction | `getFileAsync(Office.FileType.Compressed)` slice assembly — **the UAT-correct path** | `body.getOoxml()` → flat OOXML XML — **the 2026-09-03 UAT defect** |
| Reached by the pane? | Yes (`new WordHostAdapter()`) | No |

`body.getOoxml()` returns flat OOXML *text*, not a zip container. Downstream that means: SPE cannot preview it, Word cannot open it, and AI profiling reports "unsupported file type."

`HostAdapterFactory` was **dead infrastructure**: `registerAdapter()` had zero call sites, so the registry was permanently empty and `create()` always threw `INVALID_HOST`. Both panes bypassed it.

So the order was inverted and made prescriptive: **port the extraction → register → route → verify Outlook → delete.**

---

## 2. BYTE-IDENTICALITY EVIDENCE (the constraint check)

> Constraint: *"the `.docx` byte payload produced by the consolidated `WordAdapter` after this task MUST be byte-identical to what `word/WordHostAdapter.getCompressedFile()` (`:171-220`) produces today for the same open document … do not accept 'it compiles and returns a buffer' as verification."*

This was **proven by execution, not asserted.** A throwaway differential harness
(`shared/adapters/__tests__/zz-task010-parity.test.ts`) ran **both implementations in the same process,
against the same mocked `getFileAsync`, over the same byte source**, and compared outputs. It was written
and run **while the reference implementation still existed** (POML step 5, before the step-6 deletion),
then deleted together with the reference. **36/36 assertions passed.**

Per case it asserted: byte length equal to the reference AND to the source; `Buffer.equals(reference) === true`
(a full memcmp, not a sample); SHA-256 equal; leading bytes `50 4b 03 04`; identical ascending slice order;
identical `getFileAsync` arguments (`'compressed'`, `{ sliceSize: 65536 }`); file handle closed exactly once.

### 2a. 26 real `.docx` files

Byte sources only — no Compose code imported (ADR-049). Every file: `equalToRef=true`, `equalToDisk=true`, head `50 4b 03 04`.

| File | Bytes | Slices | SHA-256 (first 16) |
|---|---|---|---|
| 01 - Test Matter Create Fields Only.docx | 16264 | 1 | `02c6e7c97b007877` |
| AppligentNDA_Signed.docx | 27986 | 1 | `e94081390378a8fa` |
| Engagement Letter.docx | 17036 | 1 | `829183f644f226b9` |
| PAT 109270W-1 - CLAIMS track changes…docx | 27417 | 1 | `d0c35d21d22acebd` |
| alternate-content-duplicate-paraid.docx | 1567 | 1 | `f123a02ae7d05a72` |
| char-formatting-mixed-runs.docx | 1453 | 1 | `87343fad82839dd2` |
| chart-embedded.docx | 2171 | 1 | `ff54d23b1932ba0d` |
| comment-ranges-multiparagraph.docx | 2273 | 1 | `d9bf56013199e9d7` |
| content-controls-sdt.docx | 1383 | 1 | `a65434d5a8ba98e4` |
| court-filing-spacing.docx | 1492 | 1 | `694178f162f615ea` |
| embedded-font.docx | 2419 | 1 | `8d9b12ff283570e8` |
| endnote-references.docx | 2113 | 1 | `989370c1effa4886` |
| footnote-references.docx | 2267 | 1 | `3842878c630439b2` |
| heading-style-numbering.docx | 3998 | 1 | `e3453f3b7088e08e` |
| inline-image.docx | 2019 | 1 | `7d8e104a80818092` |
| interior-text-boxes.docx | 1332 | 1 | `37a7cd4b6b940be1` |
| line-numbered-pleading.docx | 5096 | 1 | `43c0376920e82bf5` |
| multi-author-redline-synthetic.docx | 2666 | 1 | `d65ba33a03e28376` |
| multilevel-1-1-1.docx | 3686 | 1 | `5f64b47cc682263a` |
| multipart-paraid-collision.docx | 2682 | 1 | `218a5f49d26fc367` |
| nda-interrupted-clauses.docx | 4414 | 1 | `2195ce8a57073969` |
| nested-merge-fields.docx | 1479 | 1 | `092b76be3f823491` |
| ole-embedded-object.docx | 2116 | 1 | `680ba95986629b15` |
| ref-cross-references.docx | 1396 | 1 | `b8eb8ae01872fc81` |
| style-inherited-numbering.docx | 21192 | 1 | `c6c8144be14710bf` |
| symbol-section-mark.docx | 3848 | 1 | `a6fcbf739b27e8f4` |

### 2b. The gap that finding exposed, and how it was closed

**Every one of the 26 corpus files is under 65536 bytes, so all of them assemble in a SINGLE slice.**
They prove content fidelity but leave the actual risk — **multi-slice ordering and concatenation** —
completely untested. Stopping at 2a would have been a misleading proof. Assembly is content-agnostic, so
the same code path was driven with deterministic PK-prefixed synthetic payloads across the boundaries:

| Case | Bytes | Slices | SHA-256 (first 16) | equalToRef | equalToSource |
|---|---|---|---|---|---|
| exactly 1 slice boundary | 65536 | 1 | `02f9054dbad37236` | true | true |
| 1 byte over the boundary | 65537 | 2 | `b2ce0e3f503aa609` | true | true |
| 1 byte under the boundary | 65535 | 1 | `717d7358ed89d74b` | true | true |
| 2 whole slices | 131072 | 2 | `403ec52810c2962e` | true | true |
| 4.37 slices, partial tail | 286331 | 5 | `5a58c06aa9789a6d` | true | true |
| ~4 MB | 4194304 | 64 | `f40a27f37501e739` | true | true |
| ~4 MB + 1 byte | 4194305 | 65 | `d6c7013ee1d22144` | true | true |

### 2c. Negative control on the proof itself

A separate assertion took the reference output for a 3-slice payload, swapped slice 0 with slice 2, and
asserted that the **length is still identical** while `Buffer.equals` is `false`. This records that
**byte length alone is not a proof of assembly correctness** — it is the full byte compare that carries
the weight. Any future re-verification that only checks length is insufficient.

### 2d. Structural argument (why the executed proof is what one would expect)

The byte-producing statements were ported **character-for-character**: the `sliceSize: 65536` request, the
sequential `readSlice(index + 1)` recursion, `slices[index] = data instanceof Uint8Array ? data : Uint8Array.from(...)`,
the `reduce` total, the `out.set(s, offset); offset += s.length` concatenation, and the `closeAsync` inside
`finish()`. **The single intentional divergence is the rejection value**: the consolidated adapter rejects
with a typed `CONTENT_RETRIEVAL_FAILED` `HostAdapterError` instead of a bare `Error` (POML acceptance
criterion 7). That affects the failure path only, never the bytes.

### 2f. Mechanical diff of the ported method against the deleted original (the decisive artifact)

Reproduce with:

```bash
git show <pre-010-commit>:src/client/office-addins/word/WordHostAdapter.ts | sed -n '171,220p' > /tmp/ref.txt
sed -n '290,349p' src/client/office-addins/shared/adapters/WordAdapter.ts        > /tmp/new.txt
diff <(sed 's/$//; s/^[[:space:]]*//' /tmp/ref.txt) <(sed 's/$//; s/^[[:space:]]*//' /tmp/new.txt)
```

Result — **exactly three hunks, all of them the error-type change**:

```diff
5c5,10
< reject(new Error(result.error?.message || 'Failed to read the Word document (getFileAsync).'));
---
> reject(
>   this.createError(
>     'CONTENT_RETRIEVAL_FAILED',
>     result.error?.message || 'Failed to read the Word document (getFileAsync).'
>   )
> );

13c18
< const finish = (err?: Error) => {
---
> const finish = (err?: HostAdapterError) => {

38c43,48
< finish(new Error(sliceResult.error?.message || ));
---
> finish(
>   this.createError(
>     'CONTENT_RETRIEVAL_FAILED',
>     sliceResult.error?.message || 
>   )
> );
```

**Nothing else differs.** Every byte-producing statement is character-identical: the
`getFileAsync(Office.FileType.Compressed, { sliceSize: 65536 })` call, `file.sliceCount`, the
`slices` array allocation, `closeAsync`, the `reduce` total, the
`out.set(s, offset); offset += s.length` concatenation loop, `resolve(out.buffer)`, the
`readSlice(index + 1)` recursion, and the `data instanceof Uint8Array ? data : Uint8Array.from(...)`
coercion. The three hunks all sit on the FAILURE path and none of them can affect a successful
extraction’s bytes.

This structural diff and the executed differential run in §2a–§2c are independent lines of evidence
that agree. Together they are conclusive.

### 2e. How to re-run this proof if it is ever doubted

The harness is gone with the reference implementation, so an exact re-run is not possible. What survives is
the permanent coverage in `shared/adapters/__tests__/WordAdapter.test.ts`
(`describe('getDocumentContent — .docx binary path (FR-04)')`), which asserts multi-slice ordering, the PK
signature, the exact `getFileAsync` arguments, single-slice handling, handle closure on both success and
failure, and that `body.getOoxml()` is never called for the default format. To reconstruct the differential
proof, restore the reference with
`git show <pre-task-010-commit>:src/client/office-addins/word/WordHostAdapter.ts` and diff
`getCompressedFile()` against the current private method in `WordAdapter.ts`.

---

## 3. Outlook verification (escalation trigger 1) — gate PASSED

Verification was performed at POML step 5, **before** the deletion. Live sideload was not available (the
sideload path is gone; verification is via deploy — commit `12c1c3b74`), so the evidence is static plus
executable:

1. **`shared/adapters/OutlookAdapter.ts` and `OutlookAdapter.test.ts` are byte-untouched** by this task
   (`git diff --stat` on both is empty). The Outlook adapter's own behavior cannot have changed.
2. **The Outlook adapter suite result is bit-identical before and after factory activation**:
   18 failed / 11 passed / 29 total, measured immediately before and after the entry-point rewire.
3. **Executable equivalence guard** — `HostAdapterFactory.test.ts` contains
   *"Outlook: factory bootstrap is observably EQUIVALENT to the pre-task-010 direct construction"*, which
   constructs an `OutlookAdapter` the OLD way (`new` + `await initialize()`) and the NEW way
   (`registerAdapter` + `createAndInitialize`) in the same test and asserts equal constructor, host type,
   item type, initialized state, and capability object. This is the standing regression guard.
4. **The diff of `outlook/taskpane/index.tsx` touches only the Stage-4 construction lines.** Auth bootstrap
   (Stage 2), API client config (Stage 3), `Office.onReady` (Stage 1) and the React mount (Stage 5) are
   unchanged, so ADR-028's ordering is preserved.

**Note on (2):** those 18 Outlook failures are **pre-existing and were previously invisible.** Before this
task the suite crashed at import with `TypeError: Cannot read properties of undefined (reading 'Importance')`
— `Office.MailboxEnums` was absent from `jest.setup.js` — and contributed **0 tests**. That gap had to be
closed for step 5's "run the full adapter test suite" to mean anything; with it closed, the suite runs and
its long-standing mock-contract failures ("Unable to determine item mode") became visible. They are **not**
this task's regressions and were deliberately **not** chased (out of scope). See §7.

---

## 4. What changed

| File | Change |
|---|---|
| `shared/adapters/WordAdapter.ts` | `getDocumentContent()` default/`'ooxml'` now delegates to a new private `getCompressedFile()` (ported verbatim); the `body.getOoxml()` branch is **removed**; the `:126-134` header comment recording *why* was carried over. `html`/`text`/`pdf` behavior unchanged. |
| `word/taskpane/index.tsx` | Imports `HostAdapterFactory` + `WordAdapter`; Stage 4 registers `'word'` then `await createAndInitialize()`. Adapter typed `IHostAdapter`. |
| `outlook/taskpane/index.tsx` | Same shape for `'outlook'` + `OutlookAdapter`. |
| `shared/adapters/__tests__/WordAdapter.test.ts` | +8 `.docx`-path tests; the obsolete `getOoxml`-asserting test replaced; two harness defects fixed (§7). |
| `shared/adapters/__tests__/HostAdapterFactory.test.ts` | **NEW** — 14 tests: registry, `detectHostType`, `create`, `createAndInitialize`, the Outlook equivalence guard, `isHostAdapterError`. |
| `shared/__mocks__/office-js.ts` | **NEW** `setupWordCompressedFile()` + `createMockDocxBytes()`. |
| `jest.setup.js` | Added `Office.FileType` and `Office.MailboxEnums` to the global stub. |
| `word/WordHostAdapter.ts` | **DELETED.** |
| `src/client/office-addins/CLAUDE.md` | "Host abstraction" row rewritten. |
| architecture doc · knowledge NOTES · 2 e2e page objects | Stale pointers to the deleted file corrected. |

**Nothing was orphaned by the deletion.** `WordHostAdapter` carried three methods `WordAdapter` lacks
(`getCurrentContext`, `getContentToSave`, `supportsFeature`), all marked `@deprecated` on `IHostAdapter`
and **not part of that interface**. A repo grep found **zero external call sites** — the only references
were self-references inside the two `*HostAdapter.ts` files. `App.tsx` takes `IHostAdapter`, so it could
never have reached them.

---

## 5. Decisions

### 5.1 `createAndInitialize()` is called with NO host argument

The factory accepts an optional `hostType`. Passing `'word'`/`'outlook'` explicitly would be safer at
runtime — each entry point is host-specific by construction — but it would leave `detectHostType()` dead in
production and **silently dodge the exact risk the activation exists to surface** (R-4, escalation trigger
3). The no-argument call was chosen so detection is genuinely live. A detection failure raises a typed
`INVALID_HOST` that the existing `catch` renders visibly via `renderError(..., 'Host adapter creation')` —
a diagnosable failure, not a silent bad adapter. See §6 for the residual risk this leaves open.

### 5.2 The ported failure path is typed, not a bare `Error`

Divergence from a verbatim port, required by POML acceptance criterion 7. Failure path only; §2 proves the
success path is byte-identical.

### 5.3 `Office.MailboxEnums` added to `jest.setup.js` (scope judgment)

Not strictly this task's file, but without it the Outlook adapter suite could not execute at all, making
step 5's Outlook verification vacuous. Additive to a stub that previously had the key undefined, so it can
only unbreak. Recorded here because it changes the visible failure count (§3 note).

### 5.4 Doc pointers outside the POML's named file were corrected

POML step 7 names only `src/client/office-addins/CLAUDE.md`, but acceptance criterion 1 is repo-wide ("zero
references remain"). The architecture doc, the knowledge NOTES and two e2e `@see` comments pointed at a
now-deleted file. Corrections are one-line pointer swaps, not section rewrites.

---

## 6. The one verification this task could NOT close

**`Office.context.host` has never been exercised in production in this codebase.** Before task 010 the only
reader was `HostAdapterFactory.detectHostType()`, which was unreachable. Both panes' bootstrap now depends
on it.

- It is reliable in Word.
- In **Outlook** it is the documented risk: some older desktop builds are reported to leave
  `Office.context.host` unpopulated while `Office.context.diagnostics.host` and `Office.onReady`'s
  `info.host` are populated. This is precisely plan.md R-4 / escalation trigger 3.
- Unit tests **cannot** close this — the host value is mocked. A test was added pinning the *failure shape*
  (undefined host → typed `INVALID_HOST`, not a silently wrong adapter), which is the most a unit test can
  honestly do.

**This must be closed at deploy/UAT (task 042), on both hosts, on desktop and web.** If Outlook desktop
fails at Stage 4 with `INVALID_HOST`, the correct fix is **not** a host-sniffing fallback (explicitly
forbidden by escalation trigger 3). The factory already has `waitForOfficeReady()`, which resolves the host
from `Office.onReady`'s `info.host` — the signal both panes already await at Stage 1 and currently discard.
Routing detection through that, or passing the explicit host type at the entry point, are the two ADR-clean
options; either should be a deliberate decision, not an in-flight patch.

---

## 6a. The Word adapters were NOT equivalent outside `getCompressedFile()`

⚠️ Surfaced by this task’s adr-check (W-6). The byte path is proven identical (§2), but the deleted
`WordHostAdapter` and the surviving `WordAdapter` differ elsewhere, and **none of these deltas is
test-covered**. Recorded so a later reader does not have to rediscover them:

| Method | Deleted `WordHostAdapter` | Surviving `WordAdapter` | Assessment |
|---|---|---|---|
| `getItemId()` | `word-doc-{title}-{Date.now()}` — a NEW value on every call | cached hash of title+author+creationDate — stable across calls | Both are non-identifying placeholders; **tasks 012/013 own the real identity resolver**. But `SaveView` feeds this straight into `setDocumentUrl(id)`, so what lands in that field changes today. |
| `getCapabilities()` | `canGetDocumentContent` / `canInsertLink` hard-coded `true` | derived from `isSetSupported(‘WordApi’,’1.3’)` | `SaveView` treats a false `canGetDocumentContent` as “skip content capture, don’t fail” — a **silent-degradation** path. Low risk because `initialize()` now rejects first on the same probe (§6b). |
| `insertLink()` | `insertText(...)` + `range.hyperlink` — a native Word hyperlink | `insertHtml(‘<a href=…>’)` | **Dormant** — the Share tab that would call it is a documented placeholder (module CLAUDE.md fact 3). Escaping is correct and the test was strengthened to assert `&lt;script&gt;`, `&#39;`, `&amp;` and the absence of raw `<script>`. |
| `getCurrentContext()` / `getContentToSave()` / `supportsFeature()` | present (`@deprecated`) | absent | **Not on `IHostAdapter`, zero external call sites** — nothing orphaned (§4). |

The first three are real, if low-risk, behavior changes shipped by the consolidation. They were accepted
rather than reconciled because FR-04’s goal is ONE adapter and `WordAdapter` is the tested one; forcing it
back to `WordHostAdapter`’s semantics would re-import the code the task exists to delete. **`getItemId` is
the one to watch** — task 013 replaces it wholesale, so any interim consumer of `documentUrl` should not
depend on its shape.

---

## 6b. A second behavior delta on the Word path, recorded deliberately

Before this task the Word pane did `new WordHostAdapter()` and **never called `initialize()` at Stage 4**;
`App.tsx`’s `if (!hostAdapter.isInitialized()) await hostAdapter.initialize()` guard called it later, and
`WordHostAdapter.initialize()` only checked that the host was Word. It had **no requirement-set gate**.

`WordAdapter.initialize()` additionally asserts `Office.context.requirements.isSetSupported('WordApi', '1.3')`
and rejects with `API_NOT_AVAILABLE` if not. So on a Word client below WordApi 1.3 the pane now **fails fast
and visibly at Stage 4** where previously it would have started and failed later, unpredictably, at the first
API that needed 1.3.

This is judged **correct, not a regression**: the Word manifest declares WordApi 1.3 (commit `336ea3809`
fixed the 1.1-vs-1.3 mismatch), so Office should not surface the add-in on a client that lacks it — the gate
and the manifest now agree. It is recorded because it is a real behavior change on the Word bootstrap that a
reader comparing the two adapters would otherwise have to rediscover, and because it is a second thing for
task 042 to watch for at UAT (an `API_NOT_AVAILABLE` at Stage 4 means the requirement-set declaration and the
client disagree — a manifest/client problem, not an adapter problem).

---

## 6c. ESCALATION — trigger 3 fired. One decision is OPEN and belongs to the operator.

> 🔔 **Human Input Required — POML escalation trigger 3**
> *"If activating the factory surfaces a latent Office.js host-detection problem (for example
> `Office.context.host` undefined in a supported host), STOP and escalate per CLAUDE.md §6 — do not add
> a host-sniffing fallback that bypasses `detectHostType()`."*

**Both Step 9.5 gates independently flagged this, at Critical / Medium severity, without prompting:**
code-review **C-1** ("a total add-in outage on the product’s core save path, on a code path that was
working in Outlook production before this change") and adr-check **W-5** (same mechanism, same
recommended hardening).

### The situation

Both panes now bootstrap through `createAndInitialize()` with no host argument, so `detectHostType()`
reads **`Office.context.host`** — a property this codebase has **never exercised in production** (its only
reader was the dead factory). It is not in any requirement set and is reported as unpopulated in some
Outlook desktop builds. If it comes back empty on a real client, the pane throws `INVALID_HOST` at Stage 4
and **never renders** — in Outlook, that is a working surface going dark.

Meanwhile **both panes already receive the authoritative host at Stage 1** (`Office.onReady(info => ...)`)
and discard it, and the factory ships an unused `waitForOfficeReady()` that reads exactly that value.

### Why I did not just fix it

I chose the no-argument call deliberately (§5.1) so detection would be genuinely live rather than dodged.
Two reviews then argued the opposite — that deliberately exposing an untested API on a no-fallback
bootstrap path is backwards. **That is a legitimate disagreement about a product-risk tradeoff, not a bug
with a right answer**, and trigger 3 says the response to a surfaced host-detection problem is to STOP and
escalate, not to re-architect detection mid-task. So the code is left as the POML prescribed and the
decision is handed up.

### The three options

| Option | Change | Trade |
|---|---|---|
| **A — keep as-is** (current state) | none | Detection stays live and honest; the risk is real but **typed and visible** (`INVALID_HOST` rendered by `renderError`, which §W-1 just fixed to print `code: message` instead of `[object Object]`). Task 042 UAT closes it on both hosts. |
| **B — pass the Stage-1 host** (both reviewers’ recommendation) | ~3 lines: capture `info.host` at Stage 1, pass the mapped `HostType` into `createAndInitialize(hostType)` | Removes the outage risk entirely using the signal Office actually guarantees. Uses the factory’s own documented `hostType?` parameter, so it is **not** the forbidden "host-sniffing fallback". Cost: `detectHostType()` becomes dead in production again. |
| **C — re-source `detectHostType()`** | route it through `Office.onReady`’s `info.host` (the existing `waitForOfficeReady()`) | Keeps detection live AND reliable — arguably the best end state, but it changes shared factory behavior for every future caller, which is beyond what task 010 was scoped to decide. |

**My recommendation: B for this task, C as a follow-up.** B is reversible, minimal, and removes a
production-outage risk today; C is the better architecture but should be its own change with its own
review, not a late edit inside a prescriptive task.

**Whatever is chosen, task 042 must still verify host detection live on Word AND Outlook, desktop AND**
**web.** No unit test can close this — the host value is mocked in every one of them.

---

## 7. Recommended follow-on (not created as tasks)

1. **`OutlookAdapter.test.ts` — 18 failing tests, now visible.** Root cause is a mock-contract mismatch
   ("Unable to determine item mode"): the suite's mock item does not carry whatever `OutlookAdapter` reads
   to distinguish read vs compose mode. Pre-existing, unrelated to FR-04. Sizing: one focused task.
2. **`outlook/OutlookHostAdapter.ts` is now the last dead adapter** — zero references outside itself, the
   same duplication shape task 010 just removed for Word. Task 040 (Outlook parity/capability audit) is the
   natural owner. Leaving it invites the identical defect to recur.
3. The `Word.run` mock defect fixed below may exist in other suites that mock `Word.run`; worth a sweep.

**The two harness defects fixed in `WordAdapter.test.ts`** — recorded so nobody mistakes them for weakened
assertions:

- The suite-local `Word.run` mock was `async cb => { await cb(ctx); }`, discarding the callback's return
  value. Real `Word.run` resolves with whatever the callback returns, so every `Word.run`-based adapter
  method resolved `undefined` and **9 tests failed for a harness reason, not a product reason.** Fixed to
  `return await callback(ctx)`.
- The `insertLink` "should escape HTML" test asserted the output contained `&#39;` while the input string
  contained no apostrophe. The input now actually contains `<script>` and apostrophes, and the assertions
  were **strengthened** (added `&lt;script&gt;` and `not.toContain('<script>')`), not relaxed.

---

## 7b. Step 9.5 quality gates

### adr-check — **0 violations, 9 warnings**

Clean on every ADR the POML named, each verified empirically rather than read off the diff:

- **ADR-028** — no `new PublicClientApplication` / `createNestablePublicClientApplication` anywhere in the
  package (the only textual hits are prohibition comments). `@azure/msal-browser` appears solely as
  `import type { AccountInfo }`. Bootstrap ordering preserved in BOTH panes
  (`Office.onReady` → `authService.initialize` → `apiClient.configure` → adapter → render); only Stage 4’s
  construction mechanism changed. INV-1..INV-8 undisturbed.
- **ADR-049** — no Compose coupling, verified two ways; the throwaway parity harness is confirmed gone and
  a repo-wide `compose-corpus` grep returns zero hits under `src/client/office-addins/**`.
- **ADR-012** — no `@spaarke/ui-components` import added; the 2026-09-04 Path A exception holds.
- **ADR-021** — `useOfficeTheme.ts` untouched, `FluentProvider` intact at all three render branches.

**Warnings actioned in-task**: W-1 (ADR-038 §4 → **Path C**, two banned registration-shape tests deleted;
zero coverage loss — they were transitively proven by the `create()`/`createAndInitialize()` tests),
W-2 (ADR-038 §2 → **Path A**, recorded in the project CLAUDE.md Decisions table), W-6 (documented at §6a),
W-7 (test-isolation teardown added to `HostAdapterFactory.test.ts` + a hazard note on the shared mock
helper), W-8 (a duplicate stale bullet I had introduced in `knowledge/sharepoint-embedded/NOTES.md`,
removed). W-5 was already this task’s own §6; W-9 was already §2a–§2b.

### ⚠️ W-3 — nothing in CI runs these tests (risk disclosure for the PR)

`deploy-office-addins.yml` is the ONLY workflow touching `src/client/office-addins/**`, and its steps are
build `@spaarke/auth` → `npm install` → `npm run build` → SWA deploy. **There is no `npm test` step**, and
no other workflow references the directory. Combined with the project’s accepted-289-typecheck decision,
this means the `.docx` regression guard — the single artifact pinning the 2026-09-03 UAT defect — executes
**only when a human runs it**. ADR-038 §6 names three enforcement layers; none reach this suite. Adding a
`npm test` step (or a CI job for the package) is the obvious follow-on and is NOT in task 010’s scope.

---

## 8. Verification summary

| Check | Result |
|---|---|
| `npx tsc --noEmit --skipLibCheck` — production files | **0** (identical to the pre-task baseline) |
| — total (incl. the consciously-accepted test-file bucket) | **289** (identical; net zero added) |
| `npm run build` (production; env from `deploy-office-addins.yml`) | exit 0 |
| `WordAdapter.test.ts` | 36/36 pass (was 19 pass / 10 fail) |
| `HostAdapterFactory.test.ts` | 14/14 pass |
| Parity harness (run before the deletion) | 36/36 pass |
| Full jest suite | 11 failed / 11 passed / 22 suites; delta fully decomposed in §3 and `current-task.md` |
| `grep -rn WordHostAdapter src/` | zero code references (two provenance comments only) |
| `grep getOoxml shared/adapters/WordAdapter.ts` | zero |
| MSAL construction added | none (ADR-028) |
