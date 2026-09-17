# Task Index — `spaarkeai-word-add-in-r1`

> **Generated**: 2026-09-04 by `/project-pipeline` (initialize-only)
> **Total**: 52 tasks across 5 phases (050 added 2026-09-17 — GitHub #975, owner-assigned here; 051 added 2026-09-17 — the client-side stamp reader, unowned and therefore ours, from the owner's document-matching decision; 028 added 2026-09-08 — finding F-h; 009 + 017 added 2026-09-09 — jest harness + RTL alignment; 038 added 2026-09-11 — required Matter Type on pane quick-create, from the owner's task-030 decision; 029 added 2026-09-12 — re-profile/re-index after a version save, from task 023's open item; 039 added 2026-09-12 — four save-spine server defects found by task 024; 045 + 046 added 2026-09-14 — two silent-data-loss defects found by the 025 analysis)
> **Status legend**: 🔲 not started · 🔄 in progress / needs retry · ✅ complete · ⛔ blocked · ⏭️ deferred

**Execute via `task-execute` only.** Never read a POML and implement manually (root CLAUDE.md §4).

---

## Phase 0 — De-risk and baseline

Gates most of Phases 1–3. Do not size Phase 1 until this closes.

| # | Task | Status | Rigor | Tier / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 001 | Worktree bootstrap and true typecheck baseline | ✅ | MINIMAL | sonnet / medium | — | none |
| 002 | **Spike-1**: `document.url` shape for SPE files in Word desktop | ✅ | STANDARD | opus / high | P0-spikes | none |
| 003 | **Spike-2**: Office Dialog API for opening a record | ✅ | STANDARD | sonnet / high | P0-spikes | none |
| 004 | **Spike-3**: can a task pane open the Copilot pane (timeboxed) | ✅ | MINIMAL | sonnet / medium | P0-spikes | none |
| 005 | **Spike-4**: does the add-in save path share the shipped collision semantics | ✅ | STANDARD | opus / high | P0-spikes | none |
| 006 | FR-18: clear typecheck debt in `shared/taskpane` — ⚠️ re-scope to production types (B1) | ✅ | FULL | sonnet / high | P0-typecheck | 001 |
| 007 | FR-18: clear typecheck debt in `shared/adapters` + `shared/services` — ⚠️ re-scope (B1) | ✅ | FULL | sonnet / high | P0-typecheck | 001 |
| 008 | FR-18: clear typecheck debt in `outlook/` (**`word/` has zero**) — re-scoped (B1) | ✅ | FULL | sonnet / high | P0-typecheck | 001 |
| 009 | Repair the jest harness (jest-dom) + re-measure test-file debt | ✅ | FULL | sonnet / high | P0-typecheck | 001 |
| 017 | Align `@testing-library/react` to v16 (React 19) + re-measure suite | ✅ | FULL | sonnet / high | — | 009 |
| 018 | 🔴 **NFR-11**: fix `useAnnounce` out-of-tree DOM lifecycle (React 19 unmount) | ✅ | FULL | sonnet / high | — | 017 |
| 019 | 🔴 **FR-02 pre-flight**: custom-XML premise + WordApi 1.4 manifest gap | ✅ | FULL | opus / high | — | 002 |

> ✅ **Operator decisions 2026-09-09 (post-wave).** **(1) FR-18 is MET** — production typecheck 88 → **0**
> (006 = 73, 007 = 11, 008 = 4). **(2) The 289 test-file errors are CONSCIOUSLY ACCEPTED**, trigger to
> revisit = *when a build surfaces them*. Verified inert: no CI job typechecks `office-addins`
> (`sdap-ci.yml` runs `tsc --noEmit` for `Spaarke.AI.Widgets` only), test files are not in webpack's
> graph (which is why the build is green today WITH all 289 present), and `ts-jest isolatedModules` is
> transpile-only so they cannot fail a test run. They surface only on a manual `npm run typecheck`.
> **(3) React 19 stays; `@testing-library/react` aligns to it** — PlaybookBuilder (^16.0.0) and
> SemanticSearch (^16.1.0) already run RTL 16 against React 19; `office-addins` on ^14.2.1 was the sole
> outlier. PCF (React 16.14 / RTL 12.1.5) is a separate correct lane. Task **017** executes the
> alignment; it resolves 009's escalation trigger 2.

> ✅ **UNBLOCKED 2026-09-09 by operator decision — A1 + B1.**
> **(A1)** The 11 foreign `Spaarke.Communication.Components/provenance.ts` errors are fixed by commit
> `6b987bb31`, already on this branch and already building. **FR-18's acceptance narrows to the
> `office-addins` package** — `npm run typecheck` need not be clean for foreign files pulled in by the
> `@spaarke/communication-components` path alias.
> **(B1)** **FR-18 means production types only (~99 errors), NOT all 395.** 296 of 395 (75%) sit in test
> files whose suite is already red (13/21 suites failing on a missing `jest-dom` registration) — a
> separate defect that must NOT be entangled with FR-18.
> **RE-SCOPED + re-measured 2026-09-09.** Post-`6b987bb31` the package measures **384** errors (not 395 —
> the 11 foreign ones are gone, confirming A1): **296 test-file · 88 production**. FR-18 now owns the 88.
> Directory boundaries were KEPT rather than merged, because each task carries a protection worth
> preserving (008 guards `WordHostAdapter.getCompressedFile()` for task 010; 007 guards the dead
> `HostAdapterFactory` for F-e). Production split: **006 = 73 · 007 = 11 · 008 = 4**.
> ⚠️ **`word/` now has ZERO production errors** — 008 is outlook-only and must not manufacture scope.
> All three are forbidden from touching test files or trying to make the jest suite pass.
> **New task 009** owns the jest-dom harness gap and re-measures the 296 test-file residual — leaving it
> unowned would have been exactly the burial the deferral policy forbids. 006/007/008/009 are mutually
> disjoint and dispatch in parallel.
>
> ✅ **009 CLOSED 2026-09-09 — hypothesis PARTIALLY confirmed, materially incomplete.** Task 001's
> "missing jest-dom registration" hypothesis was correct in kind but wrong in size: `@testing-library/jest-dom`
> was never installed at all (not just unregistered), and fixing it alone moved suites 13→12 and tests
> 57→41 failed (of 226) — 28%, not "a large share" — with **zero** effect on the typecheck count (jest
> runs tests transpile-only via `ts-jest isolatedModules`, decoupled from `tsc`). A **second, previously
> unknown harness gap of the identical class** (`@testing-library/user-event` imported by 4 suites, never
> installed) had to be found and fixed to unblock those suites structurally. Even with both installed,
> **12 of 21 suites remain red** — dominant cause is a **React 19 / `@testing-library/react@14`
> peer-declared-for-React-18 mismatch** (`useAnnounce.test.ts`, 16/16 tests, `NotFoundError: The node to
> be removed is not a child of this node.`) — **escalation trigger 2 fired**; a testing-library major
> bump was NOT performed (package-graph decision, outside this task's authority). Re-measured test-file
> typecheck: **289** (was 296) — the -7 is **-4 confirmed (user-event TS2307s)** + **-3 concurrent drift**
> from 006/007/008 editing `OutlookAdapter.ts`/`office-js.ts` live in the shared worktree during this
> task's window, not this task's effect. **Recommendation: the residual needs its own task(s)** — a
> React-19/testing-library major-version decision, a separate test-suite repair task for the ~12 failing
> suites' genuine mock/assertion defects, and an explicit operator decision on the still-unowned 289-error
> test-file typecheck debt (not folded into 006/007/008, which are forbidden from touching test files).
> Full record: [`notes/009-jest-harness-repair.md`](../notes/009-jest-harness-repair.md).
>
> ✅ **017 CLOSED 2026-09-09 — alignment done, suite outcome unchanged.** Bumped `@testing-library/react`
> `^14.2.1` → `^16.1.0` (matching `SemanticSearch`'s pin, the higher of the two sibling precedents) and
> discovered a second required change mid-task: RTL 16 demotes `@testing-library/dom` from a bundled
> dependency to a peer dependency, so it had to be added explicitly (`^10.4.1`, the exact pin both sibling
> packages already carry) — without it, 17 of 21 suites failed to *load* entirely (escalation trigger 3
> fired transiently, then resolved within the task). **Final state is suite-for-suite identical to 009's
> baseline**: 12 failed / 9 passed / 21 total suites, 92 failed / 237 passed / 329 total tests (within
> 009's 92-94 range). Critically, **`useAnnounce.test.ts` — the "signature failure" this task existed to
> fix — still fails 16/16 with the identical `NotFoundError`, unchanged by the RTL bump.** Root-cause
> investigation found the true cause is NOT an RTL-version peer mismatch (009's working hypothesis): the
> production hook (`useAnnounce.ts`) appends/removes DOM nodes directly on `document.body`, outside
> React's own tracked tree, which collides with React 19's unmount bookkeeping regardless of testing-
> library major version. A version bump cannot fix it; a production-code change (e.g. a React-owned
> portal target) would, and that is out of this task's scope. All 12 failing suites were individually
> categorized: 0 harness/config, 0 mechanical-RTL-16-migration-owed, all 12 (92 tests) genuine
> product/test defects, unchanged before/after. Production typecheck confirmed still **0**; test-file
> bucket re-measured at **289**, unchanged (not this task's job to clear). No test deleted/skipped
> (ADR-038). Quality gates (code-review + adr-check, mandatory per CLAUDE.md §8 TEST-MODIFYING override):
> 0 critical / 0 warnings / 0 violations both. Full record:
> [`notes/017-testing-library-alignment.md`](../notes/017-testing-library-alignment.md).
>
> <details><summary>Original block reason (resolved — retained for the record)</summary>
>
> ⛔ **006/007/008 were BLOCKED pending an operator decision.** Task 001's escalation trigger 3 fired: the UNASSIGNED bucket contains 11 errors in `../shared/Spaarke.Communication.Components/src/logic/connections/provenance.ts` — **outside the `office-addins` package**, pulled in by the `@spaarke/communication-components` path alias. FR-18's "typecheck clean" cannot be met by 006+007+008 alone. Two further findings change the decomposition: the split measures **309 / 45 / 4** (not balanced), and **75% of the debt (296/395) is in test files** whose suite is already red (13/21 suites failing on a missing `jest-dom` registration). See [`notes/typecheck-baseline.md`](../notes/typecheck-baseline.md) § Recommendations.
>
> </details>

> ✅ **005 is CLOSED** (core findings gathered as a review repair 2026-09-08; sections 8-18 — client
> side, full 9-hop server trace, small-vs-large payload, expanded comparison table, task 094 overlap
> boundary, task 025 implementation sketch, open questions — added and every citation independently
> re-verified under formal `task-execute` closure, 2026-09-08).
> [`notes/spikes/spike-4-collision-path.md`](../notes/spikes/spike-4-collision-path.md) confirms finding **F-a**:
> the add-in rides `POST /api/office/save` → the **no-policy** `UploadSmallAsync` overload, which hard-codes
> `ConflictBehavior.Replace` ([`UploadSessionManager.cs:103`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs#L103)).
> UAC-r2's shipped `Fail` default is on the client/OBO path the add-in does not use, so **FR-12's
> "consume, do not rebuild" premise is false** and its acceptance criterion cannot be met as written.
> A commit implementing a collision fix ahead of this spike (`45f45f626`) was **reverted** (`0b68943d1`); report §7.
>
> ✅ **Operator decisions, 2026-09-08** (report §5): **(1)** path **C → B** — build FR-11 (023 → 024) first,
> re-measure the residual collision surface, then amend FR-12 against what is left. No exception is granted for
> new Office-path collision logic ahead of FR-11; **025** is re-scoped and now depends on 023/024.
> **(2)** **F-h is owned by r1** as task **028**, and its fix is **host-neutral** — keyed on `SaveContentType`
> (`Email`/`Attachment` immutable, `Document` editable), never on Word-vs-Outlook. Host divergence belongs in the
> client adapters, never in save or dedup semantics.

> 🟠 **002 CLOSED 2026-09-09 — Spike-1 verdict AMBER. Link 4 GREEN; links 1–2 need an operator pass.**
> Full record: [`notes/spikes/spike-1-document-url.md`](../notes/spikes/spike-1-document-url.md).
>
> **The link the POML called most dangerous is fine.** `sprk_graphitemid` stores Graph's `driveItem.id`
> **verbatim** — traced with no transformation at any hop (`UploadSessionManager.cs:151` →
> `OfficeStorageUploader.cs:71` → `OfficeService.cs:455` → `OfficeDocumentPersistence.cs:190` →
> `DataverseServiceClientImpl.cs:787`), and the alt-key read uses the raw string
> (`ComposeRecordResolution.cs:181`). **Formats match; no key change needed or proposed — NFR-07 intact.**
> One finding: `sprk_graphitemid_uk` is keyed on the **item alone**, leaving `driveId` unvalidated
> (`DocumentVersionEndpoints.cs:59-61`) → **task 012 must add a caller-side `driveId` comparison** (one
> `string.Equals`; the column already rides along on the same retrieve).
>
> **Links 1–2 are AMBER, not RED — nothing was disproven.** No live Office host, and sideloading is gone
> (same wall task 011 hit), so the empirical half is an operator pass (report §8). Verdict deliberately
> **not** softened to GREEN, per the POML's own constraint.
>
> **Three things the POML did not anticipate:**
> 1. 🔴 **Spaarke's own open flow is a hazard to Link 1.** `DesktopUrlBuilder.cs:37-47` hands Word an
>    **abbreviated** `ms-word:{webDavUrl}` (the full `ofe|u|` verb is Restricted-Sites-blocked for SPE
>    `contentstorage` paths) and the file opens in **Protected View**. If Word treats it as a downloaded
>    copy, `document.url` may report a local path and Link 2 has nothing to encode. Microsoft's SPE docs
>    (2026-08-27) independently confirm the repo's `webUrl`-is-a-`doc2.aspx`-viewer-URL /
>    `webDavUrl`-is-canonical finding.
> 2. 🔴 **A live manifest defect, fixable today with no host.** `Word.Document.customXmlParts` **and**
>    `.settings` require **WordApi 1.4**; `word-manifest.xml:45` and `manifest.json:40` both declare
>    **1.3** — the same class of mismatch task 011 caught at 1.1-vs-1.3. **Recommended fix: use the
>    COMMON API** `Office.context.document.customXmlParts` + declare the `CustomXmlParts` requirement
>    set — no WordApi bump, a *lower* floor (perpetual Office 2016), and confirmed Word web/Mac/iPad
>    support. **Owned by task 014.**
> 3. 🟢 **Stamp-as-primary — an operator decision now on the table.** With the dev-only/legacy-irrelevant
>    decision removing its only stated disqualifier, FR-02's custom-XML stamp is the architecturally
>    better **primary** identity mechanism (identity-by-content; written server-side with no host API;
>    **one** hop to the **primary** key vs **three** unverified hops to an alternate key). Two honest
>    costs recorded: Document Inspector can strip a custom XML part, and the custom-XML
>    **markup-vs-parts** distinction is an unconfirmed premise FR-02 entirely rests on (a ten-minute
>    check, report §8.1 step 8). **If accepted, 014 moves ahead of 012/013 and both are re-scoped.**
>
> **Before task 012 builds**, run report §8: the four `document.url` cases · the Protected-View
> observation · the abbreviated-vs-`ofe|u|` A/B · the `/shares` SPE call (docs are **completely silent**
> on SPE, offer **no read-only scope**, don't document the no-access status code, and omit
> `parentReference` from the default projection — `$select` it) · the markup-vs-parts confirmation.
> A negative on Link 1 or 2 fires **escalation trigger 1 or 2**.
>
> Also recorded: `Word.Document.path`/`.fullName` (`WordApiDesktop 1.4`, desktop-only, Windows 2508+,
> **N/A on web**) as a *diagnostic* for the Protected-View question; and SPE container-type
> **`urlTemplate`** (`{drive-id}`/`{item-id}` tokens) as Microsoft's own documented alternative to the
> entire `/shares` route — **open question 10, owner decision** (container types are permanent, 25/tenant).

**Gate**: typecheck clean · four spike reports in `notes/spikes/` · FR-01, FR-12 and FR-20 scope decided.
⚠️ **FR-01's scope is decided only once the operator answers report §7 (stamp-as-primary) and runs §8.**

---

## Phase 1 — Foundation

| # | Task | Status | Rigor | Tier / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 010 | FR-04: consolidate onto one Word adapter via `HostAdapterFactory` — ⚠️ **implementation complete + verified; ONE operator decision open (host-detection, notes §6c)** | ✅ | FULL | opus / xhigh | P1-a | 006, 007, 008 |
| 011 | FR-05: migrate Word to the unified JSON manifest | 🔄 | STANDARD | sonnet / high | P1-a | none |
| 012 | FR-01 server: document-identity resolver extending `/api/documents` — `POST /api/documents/resolve-identity`, deployed to dev. **Live-verified on Word for the web AND Word desktop** (the desktop capture, 2026-09-11, is byte-identical and resolves). **Spike-1 is GREEN** (spike-1 §23). Graph 403 → `not_resolvable` (notes/012 §4). Full suite 12,139/0; ArchTests 191; no CVE; publish +0.016 MB. Contract test → 016. Optional: a live check of a file that exists but has no Spaarke record. | ✅ | FULL | opus / high | — | 002 ✅ |
| 013 | FR-01 client: `getDocumentUrl` capability and identity threading — **done 2026-09-11** (`74f255eb7`, `fc7b81b66`, `35c8195e9`, merged `8b5d58a2b`). `canGetDocumentUrl` + `getDocumentUrl()` (Word real / Outlook typed `CAPABILITY_NOT_SUPPORTED`); `documentIdentityService.ts` maps every 012 answer to a discriminated union (`resolved`/`new`/`conflict`/`indeterminate`/`denied`/`error` — 503 and the system_failure 403 are NEVER "new"); identity merged into `App.savedContext`. Prod typecheck 0; build green; jest 10→10 failing suites (pre-existing), +2 green suites (37 tests). Live Office ui-tests UNVERIFIED (no host). Notes: `notes/013-document-identity-client-wiring.md` | ✅ | FULL | sonnet / high | — | 010, 012 |
| 014 | FR-02: server-side custom XML part GUID stamp — **019 CLEARED BOTH PRECONDITIONS 2026-09-09: premise CONFIRMED; manifest now declares CustomXmlParts. GO** — inherits 4 conditions, see notes/019-customxml-premise-and-manifest.md §6 (use the COMMON API not Word.Document.customXmlParts; explicit xmlns on the stamp root; missing stamp = normal + re-stampable; runtime isSetSupported guard). MOVES AHEAD of 012/013 if Spike-1 §7 accepted. ⛔ **ESCALATED 2026-09-13 (no production code written; design note `e06d5ef03`, `notes/014-xml-part-stamp-decisions.md`)**: stamping the STORED bytes breaks 028's hash link — identical content saved to two records never hash-equal again, so the "Save as new document" override is no longer linked (spec SC-3 vs SC-5 conflict); create-stamping before 025 worsens D1. Design settled: pre-assigned `sprk_document` id (no second upload), insertion-only zip edit (byte fidelity proven on 28 real docs), `xmlns="urn:spaarke:office:document-identity:1"`, `OFFICE_020` for a corrupt `.docx`. **Owner decisions pending**: (1) options A–D; (2) URL (012) vs stamp precedence. Also: **no task owns the CLIENT stamp reader** (019 conditions 1 + 4) → **now task 051**. ✅ **UNBLOCKED 2026-09-17 (owner): BUILD IT.** Decision = A + C with B supporting. Ships as the note's own §7 **option A, sequenced as C first**: wire **version-path** stamping now, **create-path** only after 025 lands (create-path stamping makes D1 worse until then — §6e). ⚠️ **Precedence OVERRIDES POML step 5**: a resolved cloud URL wins, the stamp is the fallback, disagreement = `identity_conflict` (§9 documents the data-loss case for stamp-first). The §6b/§6c dedup consequences are ACCEPTED — spec SC-5 + NFR-08 amended and a path-A ADR Tensions row added 2026-09-17; do not re-escalate. **MUST migrate the `PK`-prefixed fixtures to a real `.docx`** and let the link test go red (§6d false-green trap) | 🔲 | FULL | opus / high | P1-b | 012 ✅ |
| 015 | FR-03: Save\|Find tab shell, enable navigation in Word — `aff1ca9e4`: Word gets Save + Find; Outlook gets Save + Create To Do + Find; Find is a static frame (the real view is 033-034) | ✅ | FULL | sonnet / high | P1-b | 010 |
| 016 | Un-skip `/api/office/save` contract tests + cover the identity route — **done 2026-09-11** (`aec79b316`, merged `c475ed764`). Both save tests execute + pass with assertions intact (5 missing fixture pieces + a 6th undocumented cause, `EntityAccessFilter`, found per §F.3; the 401 skip reason was simply stale). New `tests/integration/contract/Api/Documents/DocumentIdentityContractTests.cs` 6/6 (200, 200-not-404, 401, 403-no-leak, 400). **Premise corrected: the file had 11 skipped of 23, not 13 of 22 → 9 remain**, inventoried. Tests-only — no BFF publish change. Found + filed **ISS-003 / #975**: the global exception handler serves `SdapProblemException` as `application/json`, not `application/problem+json` (ADR-019, repo-wide). Full `Sprk.Bff.Api.Tests` 12,147 passed / 0 failed / 56 skipped. Notes: `notes/016-fixture-diagnosis.md` | ✅ | FULL | sonnet / high | — | 012 |
| 051 | **FR-02 client half: read the stamp in the pane + apply the identity precedence** — added 2026-09-17 with the owner's A+C decision. 014's note §9 found NO task owned the client reader (015 is complete and did not; 014 is server-side), and unowned work belongs here. Common API `Office.context.document.customXmlParts` only (019 condition 1 — `Word.Document.customXmlParts` is WordApi 1.4 and would drop Office 2019/2021 LTSC); `isSetSupported('CustomXmlParts')` guard by extending `WordAdapter.checkRequirementSet()`; gate on a capability, never `hostType === 'word'` (NFR-10). Precedence: resolved URL wins → stamp is fallback → disagreement is `identity_conflict`. A stamp is a hint, never an authorization | 🔲 | FULL | sonnet / high | — | 014 |

> 🔄 **011 is build-verified but NOT formally closed.** `word/manifest.json` is authored, `webpack.config.js`
> parameterizes it (id/resource/base-URL, mirroring Outlook's mechanism exactly), the WordApi 1.1→1.3
> mismatch is reconciled with cited evidence (`Word.DocumentProperties` is WordApi 1.3, forced by
> `WordAdapter.getItemId()`/`getSubject()`), both manifests are version-bumped, and `npm run build` /
> `npm run typecheck` are clean. **Acceptance criteria 6/7 (sideload verify on Word desktop + Word on the
> web) are UNVERIFIED** — this agent had no interactive Office host or browser tool available. `word-manifest.xml`
> is retained unchanged in behavior pending that verification. See
> [`notes/011-word-manifest-migration.md`](../notes/011-word-manifest-migration.md).

**Gate**: a Spaarke-sourced document is identified end-to-end · both hosts render both tabs · `/api/office/save` has executing tests.

---

## Phase 2 — Save flow

| # | Task | Status | Rigor | Tier / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 020 | FR-06: filename defaults to Document Name, editable in-pane — **done 2026-09-16** (`16caccc87` + `4abc9d1db`): the typed name now reaches `sprk_documentname` (it was going to `sprk_documentdescription` while the name column got the sanitized `.docx` file name); default is the open document's name minus its extension; pencil edit with Enter/blur commit and Escape revert; bounded to 850 at the write boundary; the dead `documentMetadata` wire field removed. Gated on a new `canProvideDocumentName` capability, not a host check (NFR-10, after main-session review). Hold lifted because 025 open question 8 (a *more specific* default) stays with the owner | ✅ | FULL | sonnet / high | P2-a | 015 ✅ |
| 021 | FR-07: Description becomes Profile, populated from the record — built 2026-09-11/12 (`45653864d`, merged `1fdf464cb`): read-only Profile section fed by the EXISTING `GET /api/v1/documents/{id}` (§11 — extended, no new route; `Spaarke.Dataverse` `GetDocumentAsync` now selects the 5 profile columns). **Main-session review caught a runtime defect**: `sprk_documenttype` is a Picklist but was read as a string → `InvalidCastException` for every `GetDocumentAsync` caller (incl. Find Similar) — fixed `7bf213cf0` with a real-`Entity` mapping test (fail 2/10 → pass 10/10). **Owner 2026-09-12: the kept "Notes" box was removed** (`ccd876a2e`; the pane no longer sends a description; server field still accepted for other clients). Merged `6d59d39cf`. **Done 2026-09-12** | ✅ | FULL | sonnet / high | P2-a | 013, 015 |
| 022 | FR-08: Generate Profile button and BFF trigger — **done 2026-09-12** (`a16de3bea` + review fix `9ccda2689`, merged `9f9fabb36`). `POST` trigger authorized by `DocumentAuthorizationFilter("write")`; dispatch reuses the `IDocumentProfileAi` facade (Path B, OBO, detached scope — mirrors Compose `refresh-profile`), deliberately NOT the Service-Bus job whose `analysis-{documentId}-documentprofile` key would silently skip an already-profiled document. **Main-session review caught a misleading 202 when nothing was dispatched** → now 202 only when dispatched, 503 `OFFICE_PROFILE_002` when the AI facade is unregistered, 401 `OFFICE_PROFILE_003` defensively (unreachable past the filter — proven). Contract 8/8; `Api.Office` 123/0/9; ArchTests 191; +0.04 MB; no CVE. ⚠️ Adds 6 test-file typecheck errors (jest-dom matcher types — same category as the accepted bucket) | ✅ | FULL | sonnet / high | P2-a | 013 |
| 023 | FR-11 server: make `ExistingDocumentId`/`IsNewVersion` real — **done 2026-09-12** (`d382bc7e5`, merged `32ae91373`): a Document save carrying `existingDocumentId` writes a new SPE version via `SpeFileStore.ReplaceFileContentAsUserAsync` (by item id, OBO) and updates the existing row — never a second row; write-level `OfficeVersionSaveAuthorizationFilter` → `DocumentAuthorizationFilter("write")`; version path skips suppress-dedup, runs 028's graduate-on-divergence; idempotency key gains a content hash for version saves only. Full suite 12,161/0; +0.03 MB. **Owner decisions:** AC4 — SPE has NO readable version comment (researcher, Graph `$metadata` checked) → `VersionComment` stays on the job record for r1; AC5 — unknown id → 403 (anti-enumeration) accepted; AI/index refresh gap → new task **029** | ✅ | FULL | opus / xhigh | — | 012 |
| 024 | FR-11 client: default to version; override routes link/graduate — **done 2026-09-12** (`ed544818b`, `c80759f96`; merged `7771aa71c`). A `resolved` identity defaults to a VERSION save (`existingDocumentId` canonical, `isNewVersion: true`); "A new document" is an explicit override sending no `existingDocumentId`, which the server routes link/graduate (028) — proven by data-mutation tests that fail 2/3 when Documents are forced onto the suppress path. `conflict` never offers save-as-new; `indeterminate`/`error`/`denied` never silently save as new; each `OFFICE_016`–`019` refusal has a way forward. No server production change. Jest 10→10 failing suites (pre-existing), +53 new tests green; 3 new suites gated. ⚠️ Found **4 save-spine server defects → new task 039** (incl. a silent lost-edits path); a 5th (same-name override collision) → task 025. Live ui-tests UNVERIFIED | ✅ | FULL | opus / high | — | 023, 013 |
| 025 | FR-12: surface collision handling per the Spike-4 outcome — ⚠️ **needs re-scope**, premise falsified. **Priority raised 2026-09-12:** with 024 live, the pane's "A new document" override saved under the SAME name into the original's container lands on the ORIGINAL's SPE file (upload path hard-codes `Replace`) — reachable from a normal pane action. Coordinate with 039 (same save spine; not together). ✅ **UNBLOCKED 2026-09-17 (owner): BUILD IT** — this is "C" in the A+C decision. Spike-4 is closed, and its finding that the add-in path has NO collision protection is already known, so **do not fire the re-scope escalation to re-ask it**: the owner has authorized building the refuse-before-upload check + the pane's two-option choice (a §6.5 path-A deviation from FR-12's "consume, do not rebuild"). Must remove D1 by refusing BEFORE bytes move, and must carry the create **B → A → B** test from 047. Landing this unblocks **045** and gates 014's create-path half | 🔲 | FULL | sonnet / high | — | **005** ✅, 023 ✅, 024 ✅ |
| 026 | FR-09: related-to record card honoring the two-slot model — **done 2026-09-15** (`5dd60c54d`, merged `a7bb22bea`): the card shows type + descriptive name + number from the EXTENDED `POST /api/documents/resolve-identity` (new optional `RelatedRecordIdentity.DisplayName` / `Number`; the extra lookup runs only after the document Read check — contract-proven); no new route; reads the 4 direct slots with 012's precedence; handles the primary-name trap (a Matter's/Project's lookup name IS its number). Merged-tree gates: BFF 299/0/9, ArchTests 191; add-in gated 23/23 (326 tests). Follow-up recorded in 035: the To Do "regarding" shows the number for Matter/Project | ✅ | FULL | sonnet / high | P2-b | 013 |
| 027 | FR-10: open the related record and the Document record — **done 2026-09-15** (`d660a5906`, merged `546a54e5b`): a browser tab via `Office.context.ui.openBrowserWindow` (Spike-2 Option 3; never the Dialog API or an iframe); new `canOpenBrowserWindow` capability; the pane re-reads when it regains focus. Main-session review (`6d950bdba`): the buttons now need `ORG_URL` too (unset, they did nothing), and the deploy workflow sets it | ✅ | FULL | sonnet / high | P2-b | 003, 026 |
| 028 | 🔴 **F-h/NFR-08**: editable Office saves must link/graduate, never immutable-suppress | ✅ | FULL | **opus / xhigh** | — | none |
| 029 | Re-profile + re-index a document after a version save — **done 2026-09-15** (`5ef7ce365`, merged `68b3af5b6`): keys carry the save's ProcessingJob id (version saves only), so every new save refreshes and a redelivery still skips; option B: upload, then trim the leftover chunks in the same index via the new `IRagService.DeleteChunksBeyondCountAsync`; +3,003 B | ✅ | FULL | opus / high | — | 023 ✅ |
| 039 | 🔴 **Save-spine server defects found by 024** — added 2026-09-12 (unowned → owned here). (1) `X-Idempotency-Key` header read but unused; (2) a FAILED job replays as a duplicate on retry; (3) jobs complete with no document id; (4) **the Word CREATE key ignores content → a second create save of an EDITED document to the same record within the window is answered from cache and never written (silent lost edits)**. Reproduce-first (§F.3). **Run before any production deploy of the save path.** parallel-safe=false (same region as 014/025). **Done 2026-09-13** (`3c2e6eef1` tests-first → `dec051738` fixes → `b3c740de6`; merged `bdb98c230`): all four reproduced (11 of 17 new tests failed before, 17/17 after) and fixed — one authoritative key (body key, else the server key; the header only names the response cache, now bound to that key for Documents); the Document CREATE key carries the content hash (same hashing as 023; version key unchanged); Failed/Cancelled jobs are retryable and the key lookup returns the NEWEST row; completed jobs carry the document id (also fixed for Email/Attachment — same defect). Full suite 12,233/0/56; +2,542 B. ⚠️ **An edited re-save of a new document under the same name now reaches task 025's D1 collision** (a visible error instead of silent loss) → **025 must land before production use of this path.** Follow-ups: remove 3 now-redundant pane workarounds once deployed; the idempotency filter's no-header path is a no-op on all 4 routes using it (repo-wide, needs an owner) | ✅ | FULL | opus / xhigh | — | 023 ✅, 024 ✅ |
| 045 | 🔴 **Pane uploads STALE bytes** — the Word document is read once when the Save tab mounts (`SaveView.tsx:202-219`, effect deps `[hostAdapter]`), never at Save press, retry or "Save Another" → edits made after the tab opened are silently never uploaded while the pane reports success. Found by the 025 analysis (R5/M12) — owned here. **Production-blocking.** Must land WITH or AFTER the 025 collision fix (fresh bytes make the same-name collision reachable). ✅ **Reachable 2026-09-17** — 025 is now GO, so this is no longer held on an owner decision; it is simply sequenced behind 025 | 🔲 | FULL | sonnet / high | — | 025 |
| 046 | 🔴 **Email/Attachment saves must never overwrite or delete another document's file** — **done 2026-09-15**: (a) `9b58c6d9b` (merged `dbcb4c18f`) — the dedup cleanup deletes an upload only when no document points at it (and nothing if that lookup fails); it also caught a hash-linked copy's file; (b) `5bbdbacca` (merged `eda1e2f75`) — owner decision B2: a unique suffix only on system-derived `.eml` names via a new optional `email.isNameSystemDerived` flag (older clients are treated as typed), on the stored file only; the inbound Exchange/Graph path was already unique. Full suite 12,249/0/56. Follow-ups owned here: a test pinning the inbound id prefix; the add-ins must be redeployed before the flag takes effect | ✅ | FULL | opus / high | — | 039 ✅ |
| 047 | 🔴 A version save that repeats an earlier version's content was answered "Duplicate" and never written — **done 2026-09-15** (`0d95c50e5`, merged `b07e4772e`): a Completed version job is a duplicate only while the document still holds exactly those bytes (app-only byte compare); version saves skip the 24 h response cache but keep the in-flight lock; server-side, so every client is fixed; +2,885 B. Finding for 025: create B → A → B hits the same loss through D1 | ✅ | FULL | opus / high | — | 039 ✅ |
| 048 | Replace a document's search chunks on every re-index, not only version saves — **done 2026-09-15**: 13 re-index paths audited; 6 fixed at the shared `PostUploadIndexingEnqueuer` seam (zero `Services/Compose/**` edits), 4 direct (manual Run Index, KB admin reindex, playbook Index node, check-in trigger), bulk tied to its own `ForceReindex`, enqueue-indexing now threads the caller's value. Keys and skip behaviour untouched. +9 B. **Residual**: a first index now makes a trim search call it never made before, so a trim failure is a new (retryable) way for indexing to fail there | ✅ | FULL | sonnet / high | — | 029 ✅ |
| 049 | Create To Do is a shared capability but its tab was still Outlook-only (found by 040) — **done 2026-09-15** (`4f3b7dbd3`): `availableFor` now both hosts; the test that pinned "only the Save tab for Word" was renamed and now asserts Save + Find + Create To Do; the guard and empty-state copy are host-neutral ("Save this to Spaarke first"), no host conditional added. Word path confirmed in code before flipping: identity resolution or any save sets the record regarding, and 035 sends the document carrier | ✅ | FULL | sonnet / high | — | 035 ✅, 040 ✅ |
| 050 | GitHub #975: every BFF error is served as `application/json`, never `application/problem+json` (ADR-019, repo-wide) — **owner 2026-09-17: assigned to THIS project**, not deferred. The global handler sets the header and `WriteAsJsonAsync` overwrites it one line later; zero tests asserted the media type anywhere in the repo | 🔲 | FULL | sonnet / high | — | none |

**Gate**: identified document saves as a version, not a duplicate row · override creates a linked copy · profile displays · record card opens the record.

---

## Phase 3 — Surfacing Spaarke

| # | Task | Status | Rigor | Tier / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 030 | FR-13: shared server-side creation service (**Matter**) — **done 2026-09-12, RE-SCOPED** (`0d53d3146` → `ecec444a2` → `4b05985b9`, merged `514ed0521`). Quick-create Matter now gets owner (403 if the caller has no Dataverse user), BU defaults, create-time field mapping, and a `QuickCreateSourceAccessFilter` (Read on the source record before an app-only read). **Numbering moved to a SEPARATE project per owner** (no plugin) — the server never writes `sprk_matternumber`; the probe-based generator survives in `0d53d3146` as a starting point. Matter Type optional, never rejected: unknown/unverifiable → created without it + warning. 19/19 contract tests; ArchTests 191; +0.03 MB. ⚠️ **`sprk_matternumber` is Matter's PRIMARY NAME (verified live) → pane-created Matters show a blank name until the numbering project ships.** Hand-off: `notes/030-numbering-handoff.md`; re-scoped ACs: `notes/030-creation-service-decisions.md` | ✅ | FULL | opus / xhigh | — | 012 |
| 031 | FR-13: Project creation completeness + QuickCreate routing — **UNBLOCKED 2026-09-17**: the owner ruled that Project numbering is a separate project and NOT a dependency (a Project may be created with no number), so this task MUST NOT write `sprk_projectnumber`; its numbering escalation trigger is answered. Remaining scope: owner + BU defaults + field mapping + routing Project through task 030's creation service, with a contract test incl. the unauthorized case | 🔲 | FULL | opus / high | — | 030 ✅ |
| 032 | **FR-16a: per-row authorization on the similarity surface** | ✅ | FULL | opus / xhigh | — | none |
| 033 | FR-16b: Find view three-state gating and Run Index — **done 2026-09-15** (`0a31802f8`, merged `4754a6af2`): every identity outcome maps to an honest Find state (only `new` shows the save prompt); Run Index calls the existing send-to-index with a lowercased `documentId`, and `ChunksIndexed = 0` is shown as a failure. Server: send-to-index now honours the per-record `sprk_searchindexname` (it was never passed at all), and D-032-2 — `GraphMetadata.Warnings` carries `PARTIAL_RESULTS` when the 100-row authorization budget is exhausted. Merged-tree gates: BFF 350/0/10, ArchTests 191; add-in gated 24/24 (352 tests); +0.05 MB. UI tests UNVERIFIED (no Office host). Found: after a create save the pane never records the new document's id — input to the document-matching discussion | ✅ | FULL | sonnet / high | — | 032, 015, 013 |
| 034 | FR-16c: Find results, lazy-scroll, records bridge decision — **done 2026-09-15** (`437c12ae0` + `2f35d97f0`, merged `c15bdfafb`): F-c = documents only; a ranked top-50 list revealed as the user scrolls, no second call, no pager (ADR-051 Path A, owner-approved, Find list only); the document's own records in a separate "not a similarity match" section. Review fix: rows are a real list | ✅ | FULL | sonnet / high | — | 033 |
| 035 | FR-14: Add To Do carrying document **and** related record — **done 2026-09-15** (`ae84e00b1`, merged `41b5223c6`): record lookup + document/communication lookup; resolver fields describe the record (ADR-024 Path A, owner-ratified); +0.05 MB | ✅ | FULL | sonnet / high | P3-c | 013, 026 |
| 036 | FR-15: Send Email via Outlook with document and record links — **done 2026-09-15** (`74a3d05fb`, merged `e6e38422e`): `displayNewMessageForm` (Mailbox 1.6, read mode); document link through the existing share-link route; record link through 027's URL builder; a failed mint opens nothing. **Word: hidden** (no mailbox in Word) — owner confirmed 2026-09-15 for r1 | ✅ | FULL | sonnet / high | P3-c | 026 |
| 037 | FR-17: wire `quickSave` and `shareDocument` ribbon commands | 🔲 | FULL | sonnet / high | P3-c | 011, 010 |
| 038 | Pane quick-create: **required Matter Type** field, sent as `matterTypeId` (owner decision 2026-09-11; the pane never shows or sends a number) — added 2026-09-11. **Done 2026-09-13** (`5350e129f` + `92fe94ddf`, merged `49147eee9`): new `GET /api/office/search/matter-types` (added past its escalation trigger; **owner-accepted**), list cached ~24h in the pane, a failed load offers a user-initiated Retry, a "type not found" warning clears the cache. Merged-tree gates: BFF 268/0/9 + ArchTests 191; add-in gated 21/21 suites (300 tests), full jest 10 failing suites unchanged. Also found: `EntityPicker`'s quick-create path is dead code since 021/024 | ✅ | FULL | sonnet / high | — | 030, 021 (same `SaveFlow.tsx`) |

**Gate**: a pane-created Matter is complete · Find returns permission-trimmed results with a **passing negative test**.

---

## Phase 4 — Parity, deploy, close

| # | Task | Status | Rigor | Tier / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 040 | FR-19: Outlook parity pass and capability-gating audit — **done 2026-09-15** (`a20daea69`): two real gates became capabilities (`canShowLinkedTodos`, `canSuggestRelatedRecords`), a no-op ternary removed, nine remaining `hostType` reads classified as host-shaped data with reasons, `notes/parity-checklist.md` lists every capability per host with evidence and a "live check: pending — 042 UAT" column. Found the stale Create To Do tab gate → task 049 | ✅ | FULL | sonnet / high | — | 033 ✅, 035 ✅, 036 ✅ |
| 041 | NFR-09: per-env Entra SPA redirects + deploy-workflow trigger — **done 2026-09-17; NO Entra write was needed** (`notes/041-entra-redirects.md`). Both required SPA URIs (`brk-multihub://icy-desert-0bfdbb61e.6.azurestaticapps.net` and `https://icy-desert-…/auth-callback.html`) were ALREADY registered on **`Spaarke Office Add-in` `c1258e2d-1688-49d2-ac99-a7485ebd9995`** — the app the workflow actually injects as `ADDIN_CLIENT_ID` — and `spaarke-office-addins` is the ONLY add-in SWA in the subscription, so dev is the only environment to register. Host taken from the **deployed** manifest (1.0.8.0; that origin only), not a repo file, so 011's 🔄 does not block it. **Trigger decision: `workflow_dispatch`, and the workflow file is UNCHANGED** — three successful dispatch runs on this branch already prove the path, and a push trigger would auto-deploy a feature branch onto the shared dev SWA. 042 launches with `gh workflow run deploy-office-addins.yml --ref work/spaarkeai-word-add-in-r1`. Recorded but deliberately NOT acted on: `/auth-dialog.html` + `/auth-end.html` are registered redirects whose targets 404 (inert; additive-only forbids deleting), and `Spaarke Word` / `Spaarke Outlook` are unused registrations with zero SPA URIs — not the add-in's app | ✅ | STANDARD | opus / high | — | 011 |
| 042 | Deploy the add-in and BFF; run UAT against the acceptance set | 🔲 | STANDARD | sonnet / high | — | 040, 041 |
| 043 | CI gate for the office-addins jest suite (runs nightly, **cannot fail a PR**) | ✅ | FULL | opus / high | `notes/043-office-addins-ci-gate.md` | 010, 018 |
| 044 | 🔴 **ISS-002**: diagnose the shadow-window FALSE GREEN (PR #934) | ✅ | FULL | **opus / xhigh** | `notes/044-false-green-diagnosis.md` | none |
| 090 | Project wrap-up, lessons learned, `/test-diet` gate | 🔲 | MINIMAL | sonnet / medium | — | 042 |

---

## Parallel execution groups

Dispatch each subagent at its POML's `<model-tier>` and `<effort>`. **Max 6 concurrent.** Verify the build between waves (root CLAUDE.md / project-pipeline Step 5): `dotnet build src/server/api/Sprk.Bff.Api/` if any `.cs` changed; `npm run build` in `src/client/office-addins` if any `.ts`/`.tsx` changed (**there is no `build:prod` here** — `build` is the production build).

| Wave | Tasks | Prerequisite | Notes |
|---|---|---|---|
| W0 | 001 | — | Serial. Sizes W2. |
| W1 | 002, 003, 004, 005 | — | 4 independent spikes. Can run alongside W0. |
| W2 | 006, 007, 008 | 001 | Disjoint directories. |
| W3 | 010, 011 | W2 · (011 needs nothing) | Disjoint: adapters vs manifest+webpack. |
| W4 | 012 | 002 | Serial — gated on Spike-1. ⚠️ **Spike-1 closed AMBER**: run its §8 operator pass and answer its §7 before starting 012. |
| W5 | 013, 016 | 010, 012 | Disjoint: client wiring vs tests. |
| W6 | 014, 015 | 012 / 010 | Disjoint: server stamp vs tab shell. ⚠️ **014 appends to the same contract-test file as 016** — the W5→W6 ordering keeps them apart; do not co-schedule 014 and 016. |
| W7 | 020, 021, 022 | 013, 015 | ⚠️ **NOT disjoint (corrected 2026-09-11 from the POMLs' own file lists).** 020/021/022 (and 026) all modify `SaveFlow.tsx`, `SaveView.tsx` and `OfficeEndpointsContractTests.cs`; **022 modifies the two files 021 creates** (`DocumentProfileSection.tsx`, `useDocumentProfile.ts`) → 021 before 022; 020 shares `OfficeDocumentPersistence.cs` with 023. Run 021 alongside 023 (021 puts its contract tests in a NEW file); then 020, 022, 026 one at a time. |
| W8 | 023 → 024 | 012 | **Strictly serial** — data-integrity change. |
| W9 | 026 → 027 | 013 / 003 | Serial. |
| W10 | 025 | **005** | ⛔ Blocked until Spike-4 closes. `parallel-safe=false`. |
| W11 | 030 → 031 | 012 | Serial. |
| W12 | 032 → 033 → 034 | — | **Strictly serial.** 032 is `parallel-safe=false`. |
| W13 | 035, 036, 037 | 013, 026, 011, 010 | Disjoint. |
| W14 | 040 | 033, 035, 036 | Serial. |
| W15 | 041 → 042 → 090 | 040 | Serial. Deploy is CI-only. |

### Goal-eligibility (`/goal` wave loop)

| Wave | Eligible | Reason |
|---|---|---|
| W2 (006–008) | ✅ | Machine-verifiable end-state (`npm run typecheck` clean), 3 well-specified low-ambiguity tasks. Condition: *"`npm run typecheck` in `src/client/office-addins` exits 0 and `npm run build` is green."* |
| W7 (020–022) | ✅ | 3 well-specified view tasks with closed acceptance sets. |
| W13 (035–037) | ✅ | 3 disjoint adaptation tasks. |
| W1 (spikes) | ❌ | Investigation — no machine-verifiable end-state; outcomes need operator judgment. |
| W8, W10, W12 | ❌ | Data-integrity / security / blocked. Never auto-loop. |
| W15 | ❌ | Deploy and irreversible. |

> The `/goal` evaluator is a **stopping-condition check, not a quality gate.** Step 9.5 (`code-review` + `adr-check`) authority is unchanged, and tasks are never auto-completed on goal achievement.

---

## Critical path

`001 → 002 → 012 → 013 → 023 → 024 → 026 → 040 → 042`

**Spike-1 (002) is the keystone.** If `document.url` is unusable on Word desktop for SPE files, FR-01's primary path fails and the FR-02 stamp becomes the only identity mechanism — which does not work for documents saved before this release. That outcome invalidates the sizing of 012, 013, 021, 023, 024, 026 and 027.

---

## High-risk items

| Task | Risk |
|---|---|
| **002** | Negative result cascades through most of Phases 1–2. Run first. |
| **005** | May reveal the add-in save path has no collision protection at all — a live data-loss finding, not a UX gap. |
| **010** | `HostAdapterFactory` has zero call sites and the "tested" `WordAdapter` uses the broken `body.getOoxml()` path. **Prescriptive order is load-bearing**: port `getCompressedFile()` first, verify Outlook, delete last. |
| **023 / 024** | Data integrity. NFR-08: editable documents MUST use link/graduate, never the immutable suppress path — suppress-forever collapses two distinct drafts into one record. |
| **032** | ✅ **RESOLVED 2026-09-08 — hardened, NOT descoped.** The descope valve ("if hardening proves large, cut Find from r1") was evaluated and not needed: every primitive already existed, including `IAiAuthorizationService`, which was already a dependency of the under-authorizing filter. Rows are now authorized per document, both routes refuse without the published obligation, and `VisualizationEndpoints.cs` is in the `RouteAuthorizationGuardTests` census. The NFR-02 negative test passes and was verified to FAIL (7 of 11) with the row check disabled. Gate for 033 is met. Latency to size 033 against, plus 3 deferred findings, in [`notes/032-authorization-hardening.md`](../notes/032-authorization-hardening.md). |
| **030** | New server-side creation service; blast radius beyond the add-in. Existing `Create*Wizard` components MUST NOT be modified. |

---

## Cross-project coordination

Run `/conflict-check` before **every** BFF PR.

| Project | Overlap |
|---|---|
| `spaarkeai-compose-r8` | The other `.docx` write path; `parallel-safe:false` across the Compose spine. ADR-049 governs. Never delete `docxBridge.ts`. |
| `unified-access-control-r2` | Task 094 (collision pre-flight — task 005/025) · task 095 (two-slot association — task 026). **Do not duplicate.** |
| `email-communication-intelligence-r2` | Authored the current add-in surface + both handoff docs. |

⚠️ **`ci-router.yml`, `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml` are FROZEN** under the shadow-comparison window (open 2026-08-27). `deploy-office-addins.yml` is editable — and **does not currently trigger on this branch** (only `master` and `work/SDAP-outlook-office-add-in`).

---

## Findings bound to tasks

Six discovery findings modify spec assumptions. Full detail in [`../plan.md`](../plan.md) §3.

| ID | One-line | Owning task |
|---|---|---|
| **F-a** | The shipped collision handling is on an upload path the add-in does not use — ✅ **CONFIRMED** 2026-09-08, [spike-4 report](../notes/spikes/spike-4-collision-path.md) | 005 → 025 |
| **F-h** | 🔴 Editable Office saves (**both hosts**) run the *immutable suppress* dedup path, which NFR-08 / `DEDUP-AND-SAVE-BACK-IDENTITY.md` §3 forbid; two distinct drafts that are momentarily byte-identical collapse into one record | **028** — owned by r1 per operator decision 2026-09-08; host-neutral, keyed on `SaveContentType` ([spike-4 §3 D2](../notes/spikes/spike-4-collision-path.md)) |
| **F-b** | FR-16's similarity engine has **no per-row authorization** | 032 (gates 033) |
| **F-c** | No single endpoint returns similar documents *and* records | 034 |
| **F-d** | FR-11's `ExistingDocumentId` hook is inert on both sides | 023, 024 |
| **F-e** | FR-04 as written would regress the `.docx` save | 010 |
| **F-f** | `POST /api/office/save` has zero executing contract coverage | 016 |
| **F-g** | `sprk_event` does not exist on `sprk_document` (only `sprk_relatedevent`), yet shipped code authorizes and writes it — the coordination doc's "mappable set" is wrong on `event`, `todo` and `contact` | 026, 035 (premise corrected) · **fix owned by UAC-r2** |
