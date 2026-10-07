# Task 089 — the open document carries its Spaarke identity; Quick Save versions or keeps both and says what it did; "Open Spaarke" replaces Share

> UAT round 3 (2026-10-03): **UAT-9** ("Quick Save gives 'Failed to save'") and **UAT-10** ("Share — don't know what
> this does"). Diagnosis and the owner's decisions: `notes/042-uat-round3-2026-10-03.md` §2, §3, §3a.
> Executed in wave UAT3-W2 beside task 093 (server-only, same worktree). Client-only: no `.cs` file edited, no
> `dotnet` and no `npm run build` run (the main session builds after the wave). Nothing committed by this task.

## 1. What shipped

| Area | Change |
|---|---|
| **Stamp contract, one client copy** | NEW `shared/adapters/documentStampContract.ts`: `STAMP_NAMESPACE` / `STAMP_ROOT_ELEMENT` / `STAMP_ID_ELEMENT` + `buildStampXml(id)`. `WordAdapter` imports it (its private copies are gone). NEW `documentStampContract.test.ts` **reads `OfficeDocumentStamp.cs`** and fails if any of the three values differ from the server's. |
| **Adapter write** | `IHostAdapter.writeDocumentStamp(id)` → `'written' \| 'unchanged'`; `HostCapabilities.canWriteDocumentStamp`. `WordAdapter`: Common API only (`customXmlParts.getByNamespaceAsync` / `addAsync`, `CustomXmlPart.deleteAsync` — 019 condition 1), guarded by `isSetSupported('CustomXmlParts')` (condition 4). A part already carrying the id → **nothing written** (no add, no delete); every other part in the namespace (other id, duplicate, unreadable) → deleted; none carried the id → one added. Refuses a non-canonical id before any call. `OutlookAdapter`: capability `false`, method rejects `CAPABILITY_NOT_SUPPORTED`. |
| **Shared post-save call** | `documentIdentityService.writeIdentityStampAfterSave(adapter, id)`: capability-gated (NFR-10), `cleanGuid` first (ADR-044), **non-fatal** (logs, returns `'failed'`, never throws). The pane and the ribbon both call it, so they cannot drift. |
| **Pane** | `SaveView` wraps the `onComplete` it hands `SaveFlow`: every successful completion (create, version, Keep both, Save as new version, "Save version" — `useSaveFlow` calls it from both its poll and SSE branches) writes the stamp, then calls the caller's `onComplete` unchanged. |
| **Quick Save** (`word/commands/index.ts`) | (1) identity first — URL, then the stamp (`applyStampPrecedence`, the pane's rule; formerly Share's code). (2) `resolved` → **version** (`existingDocumentId` + `isNewVersion`; the version id also enters the idempotency key). (3) `new` → **create** with the pane's file-name rule and `allowRename: true` (keep-both). (4) `conflict` / `indeterminate` / `denied` / `error` → **nothing saved**, the reason shown (the pane never saves silently there either, task 024). (5) after success: `GET` the job → `result.artifact` (id + stored webUrl) → stamp the open document. (6) the notification says what happened (below). |
| **Notifications** | Success: "Saved to Spaarke as '{stored file}'." / "…, A different document is already named '{requested}', so both were kept." / "Saved a new version of '{name}'." / duplicate → "…Nothing new was saved." Refusal: "Spaarke did not save this document: {server detail \| catalog message for its errorCode \| title} ({status})". No server reason: "Couldn't read this document from Word, so nothing was saved (…)" / "Couldn't connect to Spaarke…" / "Couldn't reach Spaarke…". The fixed "Failed to save. Open Spaarke to try manually." is gone. `notify.html`: success closes after 2.2 s; **error stays 8 s, has a Close button, `role="alert"`**, larger window (30×35 vs 20×25). Text is rendered with `textContent` only. |
| **File-name rule** | NEW `shared/taskpane/utils/documentFileName.ts` (`stripDocumentExtension`, `toDocxFileName`) — moved **unchanged** from `SaveFlow.tsx` (Document Name default) and `useSaveFlow.ts` (upload name). Quick Save's own divergent copy (`'Document'` fallback, no `.doc` strip) is gone; `quickSaveDocumentNames(title)` applies the pane's two steps. |
| **Open Spaarke** | `openRecordLauncher.buildOpenSpaarkeUrl(ORG_URL, SPAARKE_APP_NAME)` → `{ORG_URL}/main.aspx?appname=sprk_MatterManagement&pagetype=webresource&webresourceName=sprk_spaarkeai` (every value encoded; `null` without either setting or for a non-https org). Reuses 088's `configuredSpaarkeAppName()` (no second reader) and 088's capability-chosen opener `openFileUrl` (`openBrowserWindow` where `OpenBrowserWindowApi` 1.1 is supported, else `window.open` with `opener` cut; a blocked window is reported). No auth bootstrap, token or BFF call. Not configured → opens nothing, says "isn't set up". |
| **Share removed** | Word XML (`OpenSpaarkeButton`, `<FunctionName>openSpaarke</FunctionName>`, label + supertip strings), Word JSON (action `openSpaarke`, control `OpenSpaarkeButton`), unified package (`WordOpenSpaarkeButton` → `openSpaarke`, by the existing merge — `mergeUnifiedManifest.js` needed no change), the `shareDocument` registration/global/export and its tests. `shareLinkService` **kept** (Send Email, task 075); its header updated. Outlook's compose "Share from Spaarke" untouched. |
| **Versions** | `UNIFIED_PACKAGE.VERSION` 1.1.0 → **1.1.1**; Word JSON 1.0.9 → **1.0.10**; Word XML 1.0.9.0 → **1.0.10.0**. Footer: `APP_VERSION = process.env.ADDIN_PACKAGE_VERSION` (DefinePlugin from `UNIFIED_PACKAGE.VERSION`), no literal. A merge test pins XML = JSON + ".0". |

## 2. Server compatibility of a Word-authored stamp — verdict: COMPATIBLE, no server change (trigger 1 did NOT fire)

Read: `OfficeDocumentStamp.cs` (`Stamp`, `Survey`, `TryReadStamp`, `CustomXmlPartPaths`, `TryReadStampId`, `ParseXml`)
and `tests/unit/domain/Office/OfficeDocumentStampTests.cs`.

- **Where Word puts a data-store part.** The server finds stamps only through the **main document part's**
  relationships (`word/_rels/document.xml.rels`, type `…/relationships/customXml`). Word's own data-store parts are
  wired exactly there: unzipping every Word-written `.docx` in the repo that carries a custom XML part (Word desktop
  "Microsoft Office Word": `AppligentNDA_Signed.docx`, both `commonpaper-*` templates; Mac Word: five
  `e2e-061/test-docs`) shows `document.xml.rels` → `../customXml/item1.xml` (`item2` where 1 is taken) + a
  `customXml/_rels/itemN.xml.rels` → `itemPropsN.xml`. Word chooses its own `N`, `rId` and `ds:itemID` — the server
  keys on none of them: the path comes from the relationship target (relative or absolute both resolved by
  `ResolvePartPath`).
- **What Word writes inside the part.** Word's own serialization is `<?xml version="1.0" standalone="no"?>` +
  the element as given (no `encoding` → UTF-8). `ParseXml` reads it with `XmlReader` (encoding auto-detected, DTD
  prohibited); `TryReadStampId` checks only root namespace + local name + the namespaced `documentId` child + a
  non-empty GUID. `buildStampXml` produces exactly that root (default xmlns) and child.
- **Same id → no-op.** `Survey` collects the Word part as the single stamp part with the same id →
  `AlreadyCarriesThisId` → `StampOutcome.AlreadyStamped`, input bytes returned **reference-identical** (no second
  part). The task-047 duplicate check (stamp the request, compare to stored bytes) is therefore unaffected for a
  version save of a Word-stamped document.
- **Reader.** `TryReadStamp` returns the id (one distinct id). A different id on the server's side (the "save as new
  document" override) → the existing Word part is **rewritten in place**, exactly one part remains.
- **Client never writes two identities**: `writeDocumentStamp` deletes every other part in the namespace, so the
  server's "two disagreeing stamps → null" case cannot be created by the client.

**What is inferred, not observed:** that Word persists an `addAsync` part in the same shape as its own data-store
parts (the bibliography part above is Word's own data-store part, written by the same data store — strong
evidence, not a host observation). The live check is AC 11.

**What the main session MAY run (optional — no server test is required for this verdict, and none fails today):**
the existing stamp suites still pass by construction (no `.cs` changed) —
`dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~OfficeDocumentStampTests|FullyQualifiedName~OfficeSaveDocumentStampContractTests"`.
They do **not** contain a Word-shaped part (their packages are server-stamped; the "foreign part" test adds a part
without a relationship). If the main session wants the verdict pinned server-side, the test to add to
`OfficeDocumentStampTests` (a `.cs` change — not this task's to make in W2) is:
build `MinimalDocx.Create("Brief")`, add `customXml/item3.xml` =
`<?xml version="1.0" standalone="no"?><documentIdentity xmlns="urn:spaarke:office:document-identity:1"><documentId>{id}</documentId></documentIdentity>`,
`customXml/itemProps3.xml` (`ds:datastoreItem ds:itemID="{RANDOM-UPPER-GUID}"`, schemaRef = the namespace),
`customXml/_rels/item3.xml.rels`, a `<Relationship Id="rId9" Type="…/customXml" Target="../customXml/item3.xml"/>`
in `word/_rels/document.xml.rels` and the itemProps `Override`; assert `Stamp(bytes, id)` →
`AlreadyStamped` + `BeSameAs(bytes)`, `TryReadStamp(bytes) == id`, and `Stamp(bytes, otherId)` → one stamp part
reading `otherId`.

## 3. Escalation triggers

| # | Trigger | Outcome |
|---|---|---|
| 1 | Server stamper/reader does not treat a Word-authored same-id part as stamped | **Did not fire** — §2. |
| 2 | `addAsync` unavailable or throws on Word web/desktop for a reason other than an unsupported set | **Cannot be evaluated here** (no Office host). The write is documented in the `CustomXmlParts` set (019 §3.2: `addAsync`, `deleteAsync` are members; Word web / Windows / Mac / iPad). It is not silent on failure: a refusal rejects with the host's reason, `writeIdentityStampAfterSave` logs it (`console.warn`) and the save still reports success (owner: non-fatal). Live verification is AC 11; if it throws on a host, that is this trigger and the main session should stop the rollout. |
| 3 | The pane has no single, callable file-name rule (assembled inline in more than one place) | **Judged not fired.** The pane assembled the FILE name in exactly ONE place (`useSaveFlow.startSave`, inline) and the Document Name default in one function (`SaveFlow.stripDocumentExtension`). The only second copy was the ribbon's own (`quickSaveHelpers.buildDocumentSaveRequest`), which is the copy the POML says to retire. Both pane pieces were moved **unchanged** into `utils/documentFileName.ts`; the pane's existing gated suites (SaveFlow.documentName, useSaveFlow.*) stay green. Places, for the record: `useSaveFlow.ts` (was :1076-1077), `SaveFlow.tsx` (was :72-74), `quickSaveHelpers.ts` (was :187-188, removed). |

## 4. Deviations from the POML (directional mode)

- **Pane write lives in `SaveView`, not `useSaveFlow`.** The hook has no host adapter; threading a callback through
  `SaveFlow.buildSaveContext` would touch three files for the same effect. `SaveView` already owns the adapter and
  wraps the single `onComplete` every completion path reaches. `useSaveFlow.ts` changed only to call the extracted
  name rule.
- **The pane does not re-resolve identity after a save.** Doing so would call `applyDocumentIdentityOutcome`, which
  overwrites `App.savedContext.relatedRecord` with `null` for a stamp-only resolution — losing the record
  `handleSaved` just set (Send Email reads it). So a **tab switch** that remounts the Save tab still starts from the
  identity resolved when the pane opened (088's open item 2) until the pane is reopened; then the stamp resolves it.
  Same-session re-saves are already versions (088's saved state); reopening resolves by stamp; Quick Save resolves by
  stamp every time. Fixing the remount case belongs in `App.tsx` (merge identity without clobbering the filing).
- **Open Spaarke "not shown" is a runtime refusal, not a hidden button.** The ribbon comes from a static manifest; a
  build-time strip of the control was judged not worth the build complexity because every deployed build sets both
  settings (`deploy-office-addins.yml`). Without them the button opens nothing and says why — never a broken link.
- **Quick Save does not save on `conflict` / `indeterminate` / `denied` / `error`.** Not named in the POML, which
  covers known (`resolved`) and unknown (`new`) only; refusing matches the pane's binding rule (task 024) and the
  #1005 lesson.
- **The stored file name comes from the job's `result.artifact.webUrl`** (`file=` of SPE's documented
  `doc2.aspx` URL, or the last segment of a direct URL). `SaveResponse` carries no file name and no BFF change was
  allowed. If the name cannot be read, the notification names no file ("Saved to Spaarke.") rather than a guess.
- `mergeUnifiedManifest.js` needed **no change** — the existing merge maps the renamed control and action correctly
  (`openSpaarke` collides with no Outlook action). Its tests gained the Open Spaarke and version-parity checks.

## 5. Verification

| Check | Result |
|---|---|
| Changed + new suites (12) | **12 / 12, 156 tests** (first run, before the review fix) |
| Gated set (`ci-gated-suites.txt`, 69 suites incl. 6 new; every path exists) | **69 / 69 suites, 962 / 962 tests** |
| `npm run lint` | **0** problems |
| `npm run typecheck` | **68 total / 0 production** (baseline measured at task start: 68 / 0) |
| Unified package v1.1.1 (production + TEST) vs Microsoft's v1.30 schema (ajv, draft-04) | **VALID**; controls (missing `id`; bad control `type`) **REJECTED** |
| Word XML 1.0.10.0 (`office-addin-manifest validate`, production base URL substituted) | **"The manifest is valid."** |
| Seeded checks (each reverted byte-identical, `cmp`) | (1) write always adds (no "unchanged") → writeDocumentStamp suite **1 red**; (2) create without `allowRename` → **3 red**; (3) Quick Save ignores identity (always create) → **3 red**; (4) error close 8 s → 2.2 s → notify suite **1 red**; (5) client namespace drifts from the server's → contract suite **1 red** |

**Found by the gates and fixed:** the first gated run had **1 red** — task 051's
`checks the requirement set with the BARE one-argument call` pins ONE `isSetSupported('CustomXmlParts')` call per
`getCapabilities()`; the new write flag had added a second. Fixed in production (checked once, shared by both flags),
not by editing the test. **Code review** found an unbounded server reason travelling in the dialog URL; `notify()` now
caps the text at 400 characters (test added).

## 5a. Step 9.5 — code-review and adr-check

| # | Finding | Severity | Disposition |
|---|---|---|---|
| 1 | Server `detail` is unbounded and goes into the notification dialog's URL | Warning | **Fixed** — capped at 400 chars, pinned by a test |
| 2 | Pane and ribbon writing the stamp at the same moment could both `addAsync` → two parts with the SAME id | Suggestion (low likelihood) | Accepted: both readers return the id when parts agree; the server rewrites both in place; the next client write keeps one and deletes the other |
| 3 | Open Spaarke on **Word on the web**: `OpenBrowserWindowApi` is not supported there, so it falls back to `window.open` from the function-command runtime, which a popup blocker may stop | Warning (live) | Not silent: a blocked window is reported in the notification. Verify at AC 11; if blocked on the web, the follow-up is a Dialog-API hop or a pane button |
| 4 | Pane fires the stamp write without awaiting it (`void`), so an immediate "Save version" may capture bytes before or after the mark | Suggestion | Accepted: either outcome is correct — the server stamps the stored copy itself, and a same-id Word part is a no-op for it (§2) |
| 5 | `quickSaveHelpers.ts` 219 → 396 lines (Word Quick Save request + messages beside Outlook's) | Suggestion | Cohesive (one reason to change: the ribbon quick-save contract); not decomposed |
| 6 | No new interfaces, no try/catch-rethrow, no `any`; `hostType` never used as a gate | — | Clean |
| ADR | ADR-028 (no raw fetch/Bearer — every call through `apiClient`; no token in the Open Spaarke URL), ADR-044 (`cleanGuid` before the id reaches the document or the request; adapter refuses non-canonical ids), ADR-021 (no Fluent change; `notify.html` adds no colour), ADR-038 (tests in KEEP paths, listed in `ci-gated-suites.txt`), ADR-002 (no Dataverse write path touched — the stamp lives in the user's file), NFR-10 (capability gates only) | — | **Compliant; no violations, no exceptions needed** |

**Owed to the main session after the wave:** `npm run build` (then `grep 1.1.1 dist/word/taskpane.bundle.js` for the
footer and check `dist/spaarke/manifest.json` shows `WordOpenSpaarkeButton`); deploy; admin-center re-upload of
**`spaarke-addin-1.1.1.zip`** (the ribbon changed).

## 6. Live acceptance (AC 11) — OPEN

Needs deploy + the owner's re-upload of 1.1.1, then in Word: save from the pane, Quick Save the same document →
"Saved a new version of …" (record shows version 2); save the local file, close Word, reopen → the pane says the
document is in Spaarke; Open Spaarke → Matter Management on the Workspace. Also watch for trigger 2 (a host that
refuses `addAsync`): the browser console shows "[Spaarke] The document was saved, but its Spaarke identity mark could
not be written".
