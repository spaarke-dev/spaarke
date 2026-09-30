# Task 019 — FR-02 pre-flight: the custom-XML markup-vs-parts premise + the WordApi 1.4 manifest gap

> **Status**: COMPLETE. Both blockers cleared.
> **Task 014 (FR-02): GO** — see §6 for the conditions it inherits.
> **Date**: 2026-09-09 · Rigor FULL · model-tier opus @ high
> **Scope discipline**: no FR-02 code was written. Three files changed, all manifest/version.

---

## 0. Verdicts up front

| Item | Verdict |
|---|---|
| **Custom XML markup vs. parts premise** | **CONFIRMED.** Markup and parts are formally distinct artifacts. Only *markup* was removed (i4i, 2010). *Parts* are alive, documented, and empirically observed surviving modern-Word round trips — **including parts Word did not author, in namespaces Word does not know**. |
| **Manifest requirement gap** | **FIXED, without raising the WordApi floor.** Both manifests now declare the Common API set `CustomXmlParts` (1.1) *alongside* the existing `WordApi` 1.3. **`WordApi` deliberately NOT bumped to 1.4.** |
| **Live host required?** | **No.** The premise was settled from documentary evidence plus in-repo corpus forensics. **Spike-1 §8 step 6b can be DROPPED** from the operator probe (§5). |
| **Host-support scope decision needed?** | **No.** The chosen route excludes zero hosts. The rejected route (1.4 bump) would have excluded several — which is precisely why it was rejected (§3). |

---

## 1. The premise — CONFIRMED

### 1.1 What was in doubt

Microsoft's support page [Custom XML markup in Word](https://support.microsoft.com/en-us/office/custom-xml-markup-in-word-24bd455e-4b5d-402a-9265-8bb9af82a7d6) says:

> "Custom XML markup is no longer supported in Word. When you open a document containing custom XML markup, Word removes it from the document."

Spike-1 §6.5 caveat 2 flagged that FR-02 rests entirely on reading this as being about in-body `w:customXml` **markup elements**, *not* `/customXml/itemN.xml` package **parts** — and that nobody had verified it. **If the reading were wrong, FR-02 collapses entirely.**

Note the page displays **no publication or last-modified date** (checked 2026-09-09), and it uses the term "custom XML markup" exclusively — it never says "part", "custom XML data", "data store", or "content control". It is **silent on parts, not contradictory**.

### 1.2 Evidence line A — the two artifacts are formally distinct in Microsoft's own object model

The Open XML SDK models them as different classes in **different namespaces**:

| | In-body MARKUP (removed) | Package PART (alive) |
|---|---|---|
| Class | `DocumentFormat.OpenXml.`**`Wordprocessing`**`.CustomXmlBlock` / `CustomXmlRun` | `DocumentFormat.OpenXml.`**`Packaging`**`.CustomXmlPart` |
| Serializes as | `w:customXml` (inside `word/document.xml`) | `/customXml/itemN.xml` + `itemPropsN.xml` |
| Learn page | [documentformat.openxml.wordprocessing.customxmlblock](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.customxmlblock) (updated 2024-01-12) | [documentformat.openxml.packaging.customxmlpart](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.packaging.customxmlpart) (updated 2024-01-12) |

`CustomXmlBlock`, verbatim: *"Defines the CustomXmlBlock Class. … When the object is serialized out as xml, it's qualified name is `w:customXml`."* `CustomXmlPart` ships current in `DocumentFormat.OpenXml` v3.0.1.

### 1.3 Evidence line B — Microsoft's own i4i explainer says exactly this (the load-bearing citation)

[What is custom XML and the impact of the i4i judgment on Word](https://learn.microsoft.com/en-us/archive/blogs/gray_knowlton/what-is-custom-xml-and-the-impact-of-the-i4i-judgment-on-word) — Gray Knowlton, then GM of Office product management. `ms.date` **2009-12-23**, `updated_at` **2024-11-06**, archived on Learn.

**"Affected"**, verbatim:
> "The Word 2007 product distributed by Microsoft after 1/10/2010 will no longer read the Custom XML markup contained within .DOCX, .DOCM, or .XML files. These files will continue to open, but the **Custom XML markup tags** will be removed."

**"Not Affected"**, verbatim:
> "Word 2007 also added features allowing Content Controls to map to XML data stored in a DOCX or DOCM file… **Content Controls and XML data stored within DOCX or DOCM files will not be affected by this change.**"

And identifying what was removed: *"Custom XML Tags in Word documents are visible in the Word user interface as **Pink Tags surrounding tagged content**."* An author comment adds: *"Yes, your customers can process the XML parts inside the zip package which is a .docx file."*

"XML data stored within DOCX/DOCM" bound to content controls **is** the custom XML data store — the `/customXml/itemN.xml` parts. This is the most direct Microsoft statement that the parts survived.

### 1.4 Evidence line C — Microsoft kept shipping and documenting the parts APIs, for 12+ years after

- **Word VBA `Document.CustomXMLParts`** — [learn.microsoft.com/office/vba/api/word.document.customxmlparts](https://learn.microsoft.com/en-us/office/vba/api/word.document.customxmlparts) (`ms.date` 2017-06-08, updated 2024-01-23): *"Returns a CustomXMLParts collection that represents the custom XML in the XML data store."* **No deprecation notice.**
- **`Word.Document.customXmlParts` was *introduced* in WordApi 1.4 (Word 2208, Aug 2022)** — Microsoft added a brand-new first-class typed API for these parts **more than a decade after** the markup removal. Nobody ships a new API for a thing the product deletes on open.
- **VSTO "Custom XML parts overview"** — [learn.microsoft.com/visualstudio/vsto/custom-xml-parts-overview](https://learn.microsoft.com/en-us/visualstudio/vsto/custom-xml-parts-overview) (`updated_at` **2026-04-24**, still serviced).
- **[Persisting add-in state and settings](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/persisting-add-in-state-and-settings)** (`ms.date` 2026-03-24, `updated_at` 2026-04-17), verbatim: *"The Open XML **.xlsx** and **.docx** file formats let your add-in embed custom XML data in the Excel workbook or Word document. **This data persists with the file, independent of the add-in.**"*

### 1.5 Evidence line D — EMPIRICAL, from this repository, no live host needed

This is the part Spike-1 assumed needed a Word host. It did not. All 48 `.docx`/`.dotx`/`.docm` files in the worktree (`node_modules` excluded) were unzipped and inspected:

| Finding | Result |
|---|---|
| `w:customXml` **MARKUP** elements in `word/document.xml` | **0 occurrences, in 0 of 48 files** — markup is extinct, exactly as the support page says |
| `/customXml/itemN.xml` **PARTS** | present in **6 distinct real documents** |

**The decisive case** — `tests/unit/Sprk.Bff.Api.Tests/Fixtures/Compose/RealTemplates/commonpaper-cloud-service-agreement.docx`:

- Carries `customXml/item2.xml` in the **third-party, non-Microsoft namespace `http://customooxmlschemas.google.com/`** (`go:gDocsCustomXmlDataStorage` — Google Docs' round-trip data blob, ~24 KB).
- Correctly wired: `word/_rels/document.xml.rels` relationship type `…/officeDocument/2006/relationships/customXml` → `../customXml/item2.xml`; `[Content_Types].xml` override for `/customXml/itemProps2.xml`.
- And **Word is provably the last writer**, on signals no foreign exporter emits:
  - `<w:rsids>` block in `word/settings.xml` plus **1,820 `w:rsid*` attributes** in `document.xml` (revision-save IDs — a Word-only mechanism, appended per editing session)
  - `w15:docId`, `<w:proofState>`, `themeFontLang`, latent styles
  - `mc:Ignorable="w14 w15 w16se w16cid w16 w16cex w16sdtdh wp14"` — a Word 2021/M365-era namespace list
  - `<Template>Normal.dotm</Template>`, `<Application>Microsoft Office Word</Application>`, `<AppVersion>16.0000</AppVersion>`, `<TotalTime>2</TotalTime>`, `<lastPrinted>`
  - **`<cp:revision>4</cp:revision>` — four Word save cycles.**

→ **A custom XML part written by a completely foreign producer, in a namespace Word has never heard of, survived four modern-Word open/save cycles intact.** That is precisely FR-02's scenario: the BFF writes a part server-side; Word opens the file; the part is still there.

Independently reproduced by `commonpaper-mutual-nda.docx` (same Google namespace, `cp:revision 2`, 128 rsids), by four Mac-Word files under `projects/ai-advanced-capabilities-agreements-r1/assets/e2e-061/test-docs/`, and by `tests/fixtures/compose-corpus/AppligentNDA_Signed.docx` (Word's own `b:Sources` bibliography part, `cp:revision 3`).

### 1.6 Honest limits of the confirmation

- **There is no single Microsoft sentence reading "custom XML parts are not removed on open."** The documentary confirmation is a convergence of four independent sources (§1.2–1.4). Stated plainly rather than overstated — but there is **zero counter-evidence**, and §1.5 is direct observation.
- A dedicated search for any Microsoft doc/KB/update note claiming parts *are* removed on open returned **nothing**.
- Not verified: ECMA-376 / [MS-DOCX] clause numbers for the two artifacts. The SDK namespace split (§1.2) stands in as the structural evidence.

### 1.7 The real residual risk is the Document Inspector — a design obligation, not a blocker

The Document Inspector module is literally named **"Custom XML Data"**, described as *"Custom XML data that might be stored within a document"*, with a **Remove All** button; the page warns *"it is not always possible to restore the data that the Document Inspector removes."* ([Remove hidden data and personal information by inspecting documents](https://support.microsoft.com/en-us/office/remove-hidden-data-and-personal-information-by-inspecting-documents-presentations-or-workbooks-356b7b5d-77af-44fe-a07f-9aa4d085966f) — **no date shown**.)

This module targets the **parts** (the markup no longer exists to inspect). File → Info → Inspect Document is common practice in legal work.

→ **Binding design obligation for task 014**: a missing stamp is a **normal, recoverable state** — re-stamp. It must never be treated as corruption or as an error condition. This does not threaten the premise; it shapes the implementation.

---

## 2. The manifest gap — verified at two independent sources

Per the POML constraint, the requirement level is cited to **both** the type definitions **and** dated Microsoft Learn — no inferred versions.

| API | `@types/office-js@1.0.568` (installed, verified this session) | Microsoft Learn |
|---|---|---|
| `Word.Document.customXmlParts` | `[Api set: WordApi 1.4]` — `index.d.ts:100768-100773` | [word.document](https://learn.microsoft.com/en-us/javascript/api/word/word.document) (`updated_at` **2026-09-02**): **`[ API set: WordApi 1.4 ]`** |
| `Word.Document.settings` | `[Api set: WordApi 1.4]` — `index.d.ts:100895-100899` | same page: **`[ API set: WordApi 1.4 ]`** |
| `Office.context.document.customXmlParts` | members annotated `**Requirement set**: CustomXmlParts` — **unversioned** — `index.d.ts:5275-5300`; property at `:5743-5745`; interface annotated `**Applications**: Word` | [Common API requirement sets](https://learn.microsoft.com/en-us/javascript/api/requirement-sets/common/office-add-in-requirement-sets) (`ms.date` **2025-10-10**): own `### CustomXmlParts` section |

Both manifests declared `WordApi` minVersion **1.3**. The gap was real: a host at exactly 1.3 installs cleanly, then fails at runtime. **Spike-1 was right on both members** — the 1.3 declaration under-declared for `customXmlParts` *and* `settings`.

> **Common sets are all version 1.1.** Verbatim from the Common requirement-sets page: *"All of these API requirement sets are version 1.1, unless otherwise specified."* `CustomXmlParts` carries no version qualifier and has no versioned sub-page, so 1.1 is the only version.

---

## 3. The decision: Common API + `CustomXmlParts`. **WordApi NOT bumped.**

Per the POML constraint — *"Do NOT raise the WordApi floor unless you show that the common-API alternative genuinely cannot work"* — here is the comparison, with the floors verified at source ([Word API requirement sets](https://learn.microsoft.com/en-us/javascript/api/requirement-sets/word/word-api-requirement-sets), `ms.date` **2026-04-21**).

### 3.1 What each route costs, in hosts

| Requirement | Word web | Win — M365 sub / retail perpetual | Win — volume-licensed perpetual / **LTSC** | Mac | iPad |
|---|---|---|---|---|---|
| **`WordApi 1.3`** (already declared, unchanged) | Supported | Version **1612** (Build 7668.1000) | **Office 2019**: Version 1612 | 15.32 | 2.22 |
| **`CustomXmlParts` 1.1** — **CHOSEN** | Supported | M365 sub + **perpetual Office 2016** | (covered by the above) | Supported | Supported |
| `WordApi 1.4` — **REJECTED** | Supported | Version **2208** (Build 15601.20148) | **Office 2024**: Version 2208 | 16.64 | 16.64 |

**The `CustomXmlParts` floor (perpetual Office 2016) sits at or BELOW the WordApi 1.3 floor the add-in already declares. Declaring it excludes literally zero hosts.**

**A 1.4 bump, by contrast, would drop:**
- every Windows build older than **Version 2208 (August 2022)** — a ~5.7-year jump from 1612 (Dec 2016);
- **Office 2019 LTSC *and* Office 2021 LTSC entirely** — the LTSC column jumps straight from "Office 2019" at WordApi 1.3 to "Office 2024" at 1.4. Volume-licensed LTSC is exactly what enterprise legal departments run, which is Spaarke's market;
- Mac before 16.64 and iPad before 16.64.

### 3.2 Can the common API genuinely do the job? Yes.

FR-02's client-side need is: *find the stamp part by namespace, read its XML.* The `CustomXmlParts` set contains **17 methods**, including exactly the ones required — `CustomXmlParts.getByNamespaceAsync`, `.getByIdAsync`, `.addAsync`, and `CustomXmlPart.getXmlAsync`, `.getNodesAsync`, `.deleteAsync`. Nothing FR-02 needs is missing.

The only real difference is ergonomics: the Common API is callback-style, the Word-typed API promise-style. **That is a developer-convenience argument, and it does not justify excluding two LTSC releases.** The codebase already promisifies callback-style Office APIs — `WordAdapter.getCompressedFile()` wraps `getFileAsync` exactly this way (task 010) — so the pattern to copy already exists in-repo.

> **Note on the research recommendation.** The desk-research pass recommended bumping to 1.4 *and* declaring `CustomXmlParts`, on the ground that the codebase is "already Word-typed and promise-based," and that adding the Common set on top of 1.4 is free. The second half is true but answers the wrong question: the cost is in the **bump**, not in the addition. Recommendation **not taken**, for the host-exclusion reason in §3.1.

### 3.3 Why declare it at all, rather than only feature-detect?

A requirement set in `extensions.requirements` (JSON) / `<Requirements><Sets>` (XML) is a **hard install gate** — a host lacking it refuses to install. Spike-1 called that the honest failure, and it is: install-time refusal beats a runtime `undefined`. Here the gate is free (§3.1), and with the operator's stamp-as-primary direction the stamp is near-core rather than peripheral.

**Belt and braces: declare *and* feature-detect.** §6 condition 4 makes the runtime guard a binding requirement on task 014 — see §7 for why that matters more than this one fix.

---

## 4. What changed

Three files. **No FR-02 code. No stamping code. `shared/taskpane/hooks/**` untouched** (task 018 owns it concurrently).

| File | Change |
|---|---|
| `src/client/office-addins/word/manifest.json` | Added `{ "name": "CustomXmlParts", "minVersion": "1.1" }` to `extensions[0].requirements.capabilities`, alongside the unchanged `WordApi` 1.3. Version `1.0.7` → **`1.0.8`** (3-part SemVer). |
| `src/client/office-addins/word/word-manifest.xml` | Added `<Set Name="CustomXmlParts" MinVersion="1.1"/>` alongside the unchanged `<Set Name="WordApi" MinVersion="1.3"/>`, plus a comment block recording the reasoning and the stay-in-step obligation. `<Version>` `1.0.7.0` → **`1.0.8.0`** (4-part). |
| `src/client/office-addins/word/taskpane/index.tsx` | `APP_VERSION` `'1.0.7'` → **`'1.0.8'`** (task 011's linkage kept). |

**`word-manifest.xml` is RETAINED** per the task-011 constraint — with sideload gone, it is the rollback path and a bad manifest cannot be swapped locally. The two manifests are **in step**: identical requirement content, each in its own schema's version format (the format divergence task 011 already recorded and justified).

Schema legality of the JSON declaration is established, not assumed — [requirements capabilities schema](https://learn.microsoft.com/en-us/microsoft-365/extensibility/schema/requirements-extension-element-capabilities) (`ms.date` **2026-08-11**): `name` is `{"type":"string","maxLength":128}` with **no enumeration**; `minVersion` optional. Microsoft's own examples name Common sets in `capabilities` — verbatim from [specify-office-hosts-and-api-requirements-unified](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/specify-office-hosts-and-api-requirements-unified) (`ms.date` 2026-02-26): `{ "name": "TableBindings", "minVersion": "1.1" }`, `{ "name": "OoxmlCoercion", "minVersion": "1.1" }` — both Common sets, siblings of `CustomXmlParts` on the same page. Same page, verbatim: *"The `"minVersion"` property is optional. If it isn't present, Office assumes version `"1.1"`."* Written explicitly anyway, for self-documentation. `devPreview` (`m365-app-prev`) shares one moniker range with `m365-app-1.19 … 1.30` for this property — **no devPreview difference**.

> **Casing is load-bearing and Microsoft's docs are internally inconsistent** (Learn writes `DialogAPI` where the set is `DialogApi`). The string used here, `CustomXmlParts`, is copied verbatim from the Common requirement-sets page. Do not "correct" it.

### Verification actually run

```
cd src/client/office-addins
npm run build      # env vars from .github/workflows/deploy-office-addins.yml (non-secret)
npx tsc --noEmit --skipLibCheck
npx eslint word/taskpane/index.tsx
```

- **`npm run build` → exit 0**, zero errors (`--stats errors-only`).
- `dist/word/manifest.json`: parses as JSON; `version` `1.0.8`; capabilities `[{"name":"WordApi","minVersion":"1.3"},{"name":"CustomXmlParts","minVersion":"1.1"}]`; `id` and `webApplicationInfo.id` substituted to the real `ADDIN_CLIENT_ID`; `resource` → `api://{BFF_API_CLIENT_ID}`. **Placeholder scan clean** — no residual `b3965ea0-…` GUID, no `{{…}}` / `${…}` / `__TOKEN__`.
- `dist/word/manifest.xml`: `<Version>1.0.8.0</Version>`, both `<Set>` lines present, placeholder scan clean.
- XML **well-formedness validated** via `System.Xml.XmlDocument.Load` (not eyeballed); CRLF endings preserved exactly (136 → 136, no mixed endings).
- **Typecheck: production errors 0 → 0.** Total moved 289 → 284, and the entire diff is inside `shared/taskpane/hooks/__tests__/useAnnounce.test.ts` — **task 018's concurrent work, not this task's**. None of this task's three files appears in the error list. ESLint on the changed `.tsx`: exit 0.

---

## 5. Spike-1 §8 operator probe — step 6b can be DROPPED

Spike-1 §8 step 6b reserved 10 minutes of operator time to *"write a custom XML part…, close Word completely, reopen…, re-run the read"* and gated §7 on it.

**That observation has been made, more strongly, without a host.** §1.5's evidence is a *foreign-namespace* part surviving **four** Word save cycles in a package Word demonstrably wrote last — strictly stronger than a single manual open/close of a part Word itself just created (which cannot distinguish "Word preserves parts" from "Word preserves its own session state"). Combined with §1.3's Microsoft statement of what i4i actually removed, the premise is settled.

**Action: remove step 6b from the §8 operator pass.** The remaining §8 steps (the `document.url` shape questions, steps 1–7) are untouched and still needed — this task settles §6.5 caveat 2 only, and takes nothing else off the operator's list.

One cheap check *is* worth adding in its place, and it is not about the premise:

> **New §8 item (low cost, pre-merge)** — run the unified manifest through the Teams manifest validator or an M365 admin-center upload and confirm the `CustomXmlParts` capability name is accepted. Rationale: the schema permits any ≤128-char string for `capabilities[].name` and **Microsoft publishes no allow-list of accepted requirement-set names**, so legality is established by construction plus Microsoft's own Common-set examples rather than by an enumeration. The XML manifest side carries no such doubt.

---

## 6. Task 014 — GO, with four inherited conditions

Task 014 (FR-02) is unblocked. Both preconditions are cleared: the premise holds (§1), and the manifest now declares what the mechanism needs (§4). Conditions it inherits:

1. **Use the Common API** — `Office.context.document.customXmlParts` (`getByNamespaceAsync` → `getXmlAsync`). **Do NOT use `Word.Document.customXmlParts`**; it is WordApi 1.4 and the manifest deliberately stays at 1.3 (§3.1). The same prohibition applies to `Word.Document.settings`.
2. **The server-side writer MUST emit an explicit `xmlns` on the stamp's root element.** Verbatim from [Persisting add-in state and settings](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/persisting-add-in-state-and-settings): *"`CustomXMLPart.namespaceUri` is only populated if the top-level custom XML element contains the `xmlns` attribute"* and *"The XML string must include an `xmlns` attribute."* Since the read is by namespace, omitting it produces a **silent, total failure with no error** — the client never finds the part it just wrote. Carried forward from Spike-1 §6.5.
3. **Treat a missing stamp as normal and re-stampable, never as corruption** (§1.7 — the Document Inspector's "Custom XML Data → Remove All" is a real, user-reachable un-stamping path).
4. **Guard the read at runtime** — see §7. `Office.context.requirements.isSetSupported('CustomXmlParts')`; `minVersion` is optional in the signature (`index.d.ts:7714`), so the bare one-argument call is correct for an unversioned set. **Extend the existing helper** `WordAdapter.checkRequirementSet()` (`shared/adapters/WordAdapter.ts:490`) — currently `(set: string, version: string)`, make `version` optional — rather than adding a new one (root CLAUDE.md §11: extend, don't duplicate).

Also for whoever schedules it: **task 014's write path is server-side and needs no Office host** — the round-trip risk it was carrying is now retired.

---

## 7. The pattern is the finding — three strikes, same failure class

This is the **third** instance in this project of *"the manifest declares one capability level while the code requires another; the host installs cleanly and fails at runtime."*

| # | Task | Instance |
|---|---|---|
| 1 | **011** | Manifest advertised `WordApi` **1.1**; `WordAdapter.getItemId()` required **1.3**. |
| 2 | **010** | Same shape on the bootstrap path — `Office.context.host` assumed populated. |
| 3 | **019** (this) | Manifest declares `WordApi` **1.3**; `customXmlParts` *and* `settings` require **1.4**. |

All three share one root cause: **a capability level inferred from a sibling API, a plausible-looking version, or an assumption — instead of read at its source.** Fixing the number a fourth time will not stop a fifth.

**Two structural mitigations, offered as recommendations — neither implemented here (out of scope for a pre-flight task):**

- **(a) Runtime capability guard at every Office.js entry point.** Manifest declarations are install-time; a guard makes the mismatch *observable and diagnosable* instead of a silent `undefined`. Condition 4 in §6 applies this to FR-02 specifically. The helper already exists and is already used for `WordApi`/`Mailbox` — the gap is that it is not applied uniformly.
- **(b) A build-time reconciliation check.** Every `[Api set: X]` / `**Requirement set**: Y` annotation reachable from the add-in's Office.js call sites is machine-readable in `@types/office-js`. A script that walks the call sites, extracts the required sets, and diffs them against both manifests would convert this entire bug class into a build failure. **This is the durable fix**; the three manual reconciliations to date are evidence it is worth building. Suggested as a follow-up task, not filed by this one.

---

## 8. Files touched

- `src/client/office-addins/word/manifest.json`
- `src/client/office-addins/word/word-manifest.xml`
- `src/client/office-addins/word/taskpane/index.tsx`
- `projects/spaarkeai-word-add-in-r1/notes/019-customxml-premise-and-manifest.md` (this file)
- `projects/spaarkeai-word-add-in-r1/notes/spikes/spike-1-document-url.md` (§8 step 6b marked resolved)
- `projects/spaarkeai-word-add-in-r1/tasks/TASK-INDEX.md`, `tasks/019-*.poml`, `tasks/014-*.poml` (status)

**Not touched**: any BFF/server file, `shared/taskpane/hooks/**` (task 018), CI workflows, `.env` (none exists in this worktree; build env supplied inline from the deploy workflow's non-secret values).

---

## 9. M365 Admin Center re-registration obligation

Both manifests changed version (`1.0.7`/`1.0.7.0` → `1.0.8`/`1.0.8.0`) **and changed their requirement declarations** — the latter matters more: a requirement change alters the install gate, so a stale registration would install the old gate. Per `src/client/office-addins/CLAUDE.md`, **task 042 (deploy + UAT) MUST re-register the Word add-in at 1.0.8** before UAT. This supersedes task 011's 1.0.7 re-registration note.
