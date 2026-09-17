# Task 014 — FR-02 server-side custom XML stamp: decisions, evidence, and an ESCALATION

> Written BEFORE any production code, as the brief requires. Base: `9373c5abb` (project branch head, includes 039 at
> `bdb98c230`). Rigor FULL · opus @ high · directional. Symbols are named, not line numbers.
>
> **Status (original, 2026-09-13): STOPPED at the design crux.** The brief's hash/dedup rule fired (§6): stamping
> the stored bytes changes content-dedup behaviour in ways no owner decision covered. **No production code was
> written.** The byte-fidelity question (POML escalation trigger 1) was settled with a throwaway probe, which was
> not committed (§4). Everything needed to wire the stamp once a path was chosen is specified in §3, §5 and §10.
>
> ⚠️ **Status (2026-09-17): SHIPPED.** The owner resolved the escalation on 2026-09-17 (A + C with B supporting;
> §7 option A sequenced as C first) and the wiring is implemented. **§13 below is the implementation record** and
> supersedes this note's forward-looking language wherever they differ — in particular the error code (§5's
> `OFFICE_020` was taken by task 025 in the interim) and §6a's "idempotency is clean" verdict, which turned out to
> be true only of the *key* and NOT of the task-047 content check (§13.4).

---

## 0. Verdicts up front

| Question | Answer |
|---|---|
| **Ordering**: the id is unknown when a CREATE uploads | **Solvable without reordering or a second upload.** Pre-assign the `sprk_document` id in the BFF, stamp it, upload, then create the row WITH that id (§2). POML trigger 2 does **not** fire. |
| **Byte fidelity**: POML trigger 1 | **Does not fire.** A surgical stamp was tested on 28/28 real corpus documents. Every pre-existing part is byte-identical except the two registration parts, which change by **insertion only**. Validator error counts are unchanged (§4). |
| **039 idempotency keys** | **Unaffected.** Both keys hash the REQUEST `ContentBase64` before any stamping (§6a). |
| **028 link/graduate + 023 graduation** | 🔴 **Changed.** (1) The editable LINK half can no longer fire for a Word-pane save. (2) A pre-release hash-linked copy graduates on its first stamped save even if its content did not change. Neither is covered by an owner decision. **→ ESCALATION (§6, §7).** |
| **Compose coupling**: POML trigger 3 | Does not fire. The stamper is self-contained (`System.IO.Compression` + `System.Xml`) and needs nothing from `Services/Compose/**`. |
| **Forward-only**: POML trigger 4 | Does not fire. No scenario needs a retroactive rewrite. An unstamped document resolves through task 012. |

---

## 1. Save-path trace: where the bytes are, and where the id is

`POST /api/office/save`. Filters: rate-limit → `IdempotencyFilter` → `OfficeAuthFilter` → `EntityAccessFilter` →
`OfficeVersionSaveAuthorizationFilter`. Then `OfficeEndpoints.SaveAsync` → `OfficeService.SaveAsync`.

| Step | Symbol | Bytes in memory? | `sprk_document` id known? |
|---|---|---|---|
| Idempotency key | `ResolveIdempotencyKey` → `GenerateIdempotencyKey` (hashes `request.Document.ContentBase64`, the REQUEST) | base64 only | — |
| Job row | `_jobService.CreateProcessingJobAsync` (payload = the unstamped `request.Document`) | — | — |
| Decode | the `SaveContentType.Document` arm of the content switch in `SaveAsync` (`Convert.FromBase64String` → `MemoryStream`) | **yes** | create: **no** · version: **yes** (`versionTarget.DocumentId`) |
| **Version write** | `CompleteVersionSaveAsync` → `OfficeStorageUploader.WriteNewVersionAsync` → `SpeFileStore.ReplaceFileContentAsUserAsync` | yes | **yes** |
| **Create upload** | `OfficeStorageUploader.UploadToSpeAsync` → `SpeFileStore.UploadSmallAsync` (app-only, path-keyed, Replace) | yes | **no** |
| Create row | `OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync` → dedup → `IDocumentDataverseService.CreateDocumentAsync` → `UpdateDocumentAsync` | — | **minted here, by Dataverse** |

**The single hook point for both paths is right after the Document arm decodes, before the stream reaches either
writer.** The version path already knows the id. The create path does not, which is the ordering problem (§2).

---

## 2. Ordering (design question 1): pre-assign the id

**Decision (proposed):** on a Document CREATE, the BFF mints the id (`Guid.NewGuid()`), stamps it, and uploads. Then
`CreateDocumentAsync` creates the row **with that primary key**. Dataverse accepts a caller-supplied primary key on
Create (setting `Entity.Id` on the SDK `CreateAsync` call).

- **No reorder.** The upload still precedes the row, so dedup still reads the stored item before the create, exactly
  as today. POML trigger 2 ("making the id available would require create-record-before-upload") does not fire.
- **No second upload.** One SPE write per save, as today.
- **Code surface:**
  - an optional `Guid? Id` on `Spaarke.Dataverse.CreateDocumentRequest`;
  - one line in `DataverseServiceClientImpl.CreateDocumentAsync`: `if (request.Id is { } id) document.Id = id;`. It is the only
    `IDocumentDataverseService` implementation; the `_archive` copy is dead;
  - a new `documentId` parameter on `CreateDocumentWithSpePointersAsync`.

  This is an additive change to a **shared** contract, so it needs a §10 note in the PR. Callers that don't set
  `Id` are unchanged.
- **Honest costs:**
  - Microsoft's guidance prefers platform-generated sequential GUIDs for clustered-index locality. A random
    caller-supplied GUID is supported, but it gives up that locality for these rows. This is a performance note, not
    a correctness issue.
  - **If the create fails after the upload**, the stored bytes name a row that never came into existence. Today
    such an upload is already an orphan item. The reader must treat a stamp that resolves to nothing as **absent**
    (019 condition 3 already demands that).
  - A retry after a Failed job re-runs with a fresh id and re-uploads under the same path, so the item ends up
    carrying the id of the attempt that created its row. That holds except in the D1 case (§6e).

**Rejected alternatives:**
- **Upload unstamped, create the row, then write a second stamped version.** Two SPE versions per create, twice the
  latency, a window in which the stored bytes carry no stamp, and a second write that can fail on its own.
- **Create the row first.** Reorders the save (POML trigger 2) and changes when dedup runs relative to the row.
- **A deterministic id derived from the idempotency key.** A retry after a partial failure would collide with a row
  the first attempt created. That is too clever for a save spine.

---

## 3. Stamp representation (POML step 2)

| Element | Value | Why |
|---|---|---|
| Part | `/customXml/item{N}.xml`, with the lowest free `N` | Where Word keeps its data store. `N` skips existing parts, e.g. Appligent's bibliography `item1` → the stamp becomes `item2`. |
| Wiring | **Main document part** rels (`word/_rels/document.xml.rels`) → `…/relationships/customXml`, relative target (`../customXml/itemN.xml`); `customXml/_rels/itemN.xml.rels` → `…/relationships/customXmlProps` → `itemPropsN.xml` | The shape of the in-repo parts that survived four Word save cycles (019 §1.5). A part related only from the package root might not reach the Office.js data store. |
| Root | `<documentIdentity xmlns="urn:spaarke:office:document-identity:1">` | **019 condition 2**: an explicit default `xmlns` on the root, so `CustomXMLPart.namespaceUri` is populated and `getByNamespaceAsync` can find it. The namespace carries the schema version. |
| Content | `<documentId>{id:D}</documentId>`, the GUID **alone** | ADR-044: `Guid.ToString("D")` is bare-lowercase by definition. §11 and POML step 2: tenant or timestamp fields have no consumer, so they are not added. |
| Props | `itemPropsN.xml`: `ds:datastoreItem` with a fresh `ds:itemID` and `ds:schemaRef ds:uri` = the namespace | Matches Word's own data-store parts. |
| How a reader tells it apart | exact namespace-URI match on the root | Any other custom XML part (Google Docs, bibliography, SharePoint metadata) is ignored. |

**A stamp is a hint, never an authorization.** Anyone can author a custom XML part carrying any GUID. Every consumer
must confirm the id through an existing authorized read, and a version save already requires Dataverse `write` on
`existingDocumentId` through `OfficeVersionSaveAuthorizationFilter`. A forged stamp therefore gains nothing the user
could not already do by typing an id.

---

## 4. Byte fidelity: POML trigger 1 does NOT fire (probe evidence)

**Technique** (in the throwaway probe; NOT committed; sources at the session scratchpad `stampprobe/`):

1. Open the bytes with `ZipArchive` in **Update** mode. Entries that are not opened are never re-encoded.
2. Add three new parts: `itemN.xml`, `itemPropsN.xml`, and `customXml/_rels/itemN.xml.rels`.
3. Make two **surgical text insertions**, each placed just before the root end tag:
   - one `<Relationship>` into the main document rels (the part is created if it is absent), and
   - one `<Override>` for `itemPropsN.xml` into `[Content_Types].xml`, plus one for `itemN.xml` only when there is
     no `xml → application/xml` Default.

   Both parts are parsed read-only first (well-formedness check, root shape, existing ids). Every original byte of
   both parts survives.
4. Refuse the surgical path for shapes it doesn't handle: a prefixed root, a self-closing root, or a non-UTF-8 part.

**Why not the Open XML SDK for writing:** it re-serializes `[Content_Types].xml` and the rels parts it touches, which
normalizes namespaces, attribute order and whitespace. That is exactly the "library that normalizes XML on save" in
trigger 1. The SDK is used **only to validate** the result.

**Results.** Run over the whole compose corpus (26) plus the RealTemplates fixtures (2), 28 documents in all:

| Check | Result |
|---|---|
| Every pre-existing part (decompressed) other than the two registration parts is byte-identical | **28/28** |
| The two registration parts are the original plus one inserted element (common prefix + common suffix = the whole original) | **28/28** |
| Entry order preserved; the only new entries are the 3 stamp parts, plus `document.xml.rels` where the source had none (7 docs) | **28/28** |
| The Open XML SDK sees exactly one Spaarke part in `MainDocumentPart.CustomXmlParts`, with the stamped id | **28/28** |
| `OpenXmlValidator` (Microsoft365) error count before → after unchanged; e.g. 7→7 and 8→8 on docs with pre-existing errors, 0→0 elsewhere | **28/28** |
| Re-stamp with the **same** id returns the input `byte[]` unchanged (a true no-op) | **28/28** |
| Re-stamp with a **different** id touches only `itemN.xml`, and exactly one Spaarke part remains | **28/28** |
| The reader returns the id from stamped bytes, and `null` from every original, including Appligent (bibliography part) and commonpaper (Google Docs part) | **28/28** |
| A truncated `.docx`, and the **5-byte `PK\x03\x04…` fixtures the Office save tests use today** | classified **CORRUPT** (`InvalidDataException`) |
| A PDF | classified not-a-zip → pass through |

"Part" here means the part's decompressed content. The zip container's own bytes necessarily change, since the
central directory is rewritten.

**Unverified:** opening a stamped file in real Word (desktop or web), and Word preserving the part across an
edit-save. The validator is a proxy only. 019 §1.5's forensics make preservation very likely.

---

## 5. Scope (design question 3)

| Input | Behaviour |
|---|---|
| `ContentType = Document`, **create** | Stamp with the pre-assigned id (§2) |
| `ContentType = Document`, **version** (`IsVersionSave`) | Stamp with `versionTarget.DocumentId` |
| `ContentType = Email` / `Attachment` | **Never stamped.** Immutable captures, not Word documents; their dedup is suppress (task 028). Not even inspected. |
| Document bytes that are not a zip (PDF, EML, arbitrary binary) | Pass through, **byte-untouched**, no error |
| A readable zip that is not WordprocessingML (e.g. `.xlsx` sent as Document, a plain `.zip`) | Pass through, byte-untouched. Detection is by **content** (`_rels/.rels` → main part → content type), not by file name. |
| Zip signature, or a `.docx/.docm/.dotx/.dotm` name, but **unreadable** (truncated/corrupt) | **Refuse** with a new `OFFICE_020` (400 ProblemDetails) **before any SPE write**, and mark the job Failed (POML AC "corrupt → handled ProblemDetails, no partial bytes") |
| A readable Word package in a shape the surgical writer won't take (prefixed root, UTF-16 rels, self-closing root) | Pass through **unstamped**, with a Warning. A missing stamp is a normal state (019 condition 3); the save does not fail. |
| Already stamped with the **same** id | Bytes untouched: no-op |
| Stamped with a **different** id (a copy of another Spaarke document, or the "save as new document" override of an identified document) | Rewrite that part's content to this save's id. **Exactly one** Spaarke part remains. Logged at Information with both ids. |
| More than one Spaarke part (only possible through foreign tampering) | All rewritten to the one id. The reader returns `null` when ids disagree. |
| Stamp removed by the Document Inspector | Re-stamped as a new part (019 condition 3) |
| Existing stored documents | **Untouched: forward-only** (owner decision 2026-09-04). No backfill, migration or stamp-on-read. |

`OFFICE_020` is a behaviour change: a corrupt `.docx` is stored as-is today. The POML acceptance set requires the
refusal, so it is in scope, but it is listed here so it isn't a surprise.

---

## 6. Hashes and dedup (design question 2): 🔴 the escalation

### (a) 039 idempotency: **unaffected**
Both the create key (`|create-content:`) and the version key (`|version-content:`) hash the REQUEST
`Document.ContentBase64` in `GenerateIdempotencyKey`, before decoding and before any stamp. The stamper works on the
decoded copy and never writes back to `request`.

- The ProcessingJob payload stays the unstamped request.
- A replay returns the earlier job, so no re-stamp happens.
- A retry after a Failed job is a new attempt with a new pre-assigned id.
- `IdempotencyFilter`'s Document binding reads the same authoritative key, so it is unchanged too.

### (b) 023 version path: **graduation semantics change**
`RecordNewVersionAsync` → `GraduateLinkedCopyIfDivergedAsync(itemId, liveHash)` compares the SPE `quickXorHash` of
the **stored** item with the row's `sprk_canonicalhash`. Once version saves are stamped, a hash-linked copy whose link
was recorded over **unstamped** bytes has a live hash that always differs, because the stamp was added. That covers
every link made before this release and every link made by a non-Word path. So the copy **graduates on its first
stamped save even when the user changed nothing.** The direction is conservative (a lost link, never lost data), but
it is a semantic change: "diverged" would now also mean "gained a stamp".

### (c) 028 link half on create: **inert for Word-pane saves**
`ContentDedupDetector.ResolveContentIdentityAsync` reads the SPE `quickXorHash` of the **stored** bytes. Stamped
stored bytes embed the record's **own** id, so two creates of byte-identical local content to different records never
hash-equal. Consequences:

- `LinkEditableCopyToCanonicalAsync` and `NotifyLinkedCopyAsync` never fire for a Word-pane create.
- **Spec Success Criterion 5 / NFR-08 in production.** The "Save as new document" override sends bytes that carry the
  original's stamp X. The server rewrites the stamp to Y, the stored bytes differ from X's, and **no
  `sprk_canonicaldocument` link is written.** SC-3 (stamp) and SC-5 (link on override) conflict in production.
- Cross-path duplicates never link either: Word-stamped content against Compose, Outlook or `DocumentsEndpoints`
  uploads of the same content.

What does NOT change:
- A create **still always creates** (no suppress, NFR-08's data-safety half intact).
- Email and Attachment suppress is untouched.
- `sprk_canonicalhash` is still stamped (over the stamped bytes).
- `ContentDedupDetector.cs` would not be modified.

### (d) In-repo evidence that this is real, not theoretical
- `OfficeVersionSaveWorld.LiveHash(itemId)` = SHA-256 of the item's current **stored** bytes (contract-tests
  file, `GetQuickXorHashAsync` setup). `OfficeSaveAsNewDocumentLinkGraduateTests` test 1 asserts
  `copy.CanonicalDocumentId == originalId` by hash equality. With stamping wired and a real `.docx` fixture, **that
  assertion fails**, which is the production change showing up in the suite.
- ⚠️ **False-green hazard.** Every Office save fixture today is a 5–6 byte array beginning `PK\x03\x04`:
  `OfficeSaveAsNewDocumentLinkGraduateTests`, `OfficeVersionSaveOneRowTests`, `OfficeSaveSpineIdempotencyTests`, and
  the contract file's `InitialBytes`/`RevisedBytes`/`Revision2`.
  - Under §5 they classify **CORRUPT**, so wiring the stamp turns them all into `OFFICE_020`, and a fixture migration
    to a real minimal `.docx` is part of the wiring.
  - If someone instead "fixes" the fixtures by dropping the `PK` prefix, the stamper passes them through, and **the
    link tests stay green while production has no link.** Any wiring task must migrate them to real `.docx` bytes
    and let the link test go red (then decide per §7), not route around it.

### (e) Interaction with D1 (task 025, ⛔ needs re-scope)
Take a same-name re-create into the same container. The path-keyed upload replaces the **first** row's item, and the
second row is created, then collides on `sprk_graphitemid_uk` (039 §5). With stamping, that upload carries the
**second** row's pre-assigned id, so the item the first row tracks now self-identifies as a row that has no file
pointers. A round trip would then resolve to the orphan, and a version save would be refused with `OFFICE_017`.
**Stamping makes D1's outcome worse.** Whatever option is chosen, create-path stamping should not reach production
before 025 lands. Version-path stamping has no D1 exposure: it writes by item id to the resolved row.

### Why this is an escalation
The brief: "If stamping would change content-dedup or idempotency behaviour in any way that isn't clearly intended …
STOP and report with options." (b) and (c) are dedup behaviour changes. No owner decision covers them, and the spec's
SC-3 and SC-5 conflict in production. Idempotency (a) is clean.

---

## 7. Options for the owner

| # | What ships | Dedup effect | FR-02 coverage | Size |
|---|---|---|---|---|
| **A** | Stamp **create + version** | Accept (c): the link half is inert for Word-pane saves, and an override of an identified document produces no link. Accept (b): pre-release linked copies graduate on their first stamped save. Amend SC-5/NFR-08 wording to "the override always creates its own record (never suppressed); the hash link is not produced for Word-stamped saves". | Full | Small (≈ §10) |
| **B** | Stamp create + version, **and preserve dedup** by computing content identity over the package **with the Spaarke stamp removed** | None (links behave as today) | Full | **Large, and cross-project.** It replaces the dedup hash source (SPE `quickXorHash` of the stored item → a server-computed normalized hash) on the create **and** graduate paths. That puts a second hash domain in `sprk_canonicalhash`, a column shared with email-r2 and Compose. Needs `ContentDedupDetector` changes (028's non-modification line) and owner sign-off from those projects. Not recommended in r1. |
| **C** | Stamp **version saves only** | (c) does not happen (creates are unstamped); (b) still does | Partial: a new document self-identifies only after its first version save | Smallest; no `CreateDocumentRequest` change, no D1 exposure |
| **D** | Nothing now; wire after 025 | None | None yet | 0 |

**Recommendation: A, sequenced as C first.** Wire version-path stamping now: no D1 exposure, no shared-contract
change, and (b) is a conservative metadata-only effect. Wire create-path stamping when 025 lands, with the SC-5/NFR-08
wording amended per A. The link half being inert for Word saves costs a notification, never a record. B is the only
option that keeps links, and its price is out of proportion to what a link is worth.

Secondary decision in the same escalation (§9): **precedence** when a document carries both a stamp and a resolvable
cloud URL.

---

## 8. The four 019 conditions: where each binds

| # | Condition | Binds | How it carries over |
|---|---|---|---|
| 1 | Common API `Office.context.document.customXmlParts`, not `Word.Document.customXmlParts` | **Client** (read) | The server writes a part **related from the main document part, with `itemProps`**, which is the data-store shape the Common API enumerates. Verified via the SDK's `MainDocumentPart.CustomXmlParts` only; a live Office.js `getByNamespaceAsync` is **unverified**. |
| 2 | Explicit `xmlns` on the stamp root | **Server**: binding here | Default `xmlns="urn:spaarke:office:document-identity:1"` on `<documentIdentity>`; the probe reads back the root `NamespaceURI`. The client queries `getByNamespaceAsync("urn:spaarke:office:document-identity:1")`. |
| 3 | A missing stamp is normal and re-stampable | **Both** | Server: absence → add; an unhandleable shape → pass through unstamped + Warning; never an error. Reader: absent, unparseable, or ids-disagree → `null` → fall back to 012. |
| 4 | Runtime `isSetSupported('CustomXmlParts')` guard | **Client** | Extend `WordAdapter.checkRequirementSet()` by making `version` optional (019 §6). Not this task's code. |

---

## 9. Read side and precedence

- **Server read**: a `TryReadStamp(bytes) → Guid?` (in the probe) that follows the main part's `customXml`
  relationships and returns an id only when exactly one distinct Spaarke id is present.
- **Client read**: 019 conditions 1 and 4. There is **no client task in TASK-INDEX that owns it.** 015 (tab shell) is
  complete and did not. The wiring task must add a client step, or 013's resolver hook must be extended.
- **Precedence: recommend 012 first when the URL is a cloud URL, the stamp otherwise.** POML step 5 says "prefer the
  stamp", but consider a Spaarke document copied into SPE through a non-stamping upload path (e.g.
  `PUT /api/drives/{driveId}/upload`, email capture). It **carries its source's stamp X** while its URL resolves to
  row W. Stamp-first would name X, and the default version save would then write W's content **into X's file**.
  Recommended order:
  1. A cloud URL that resolves (012 `resolved`) wins.
  2. The stamp is used when 012 answers `not_cloud_document`, `not_resolvable` or `not_spaarke_document`.
  3. A resolved URL whose id disagrees with the stamp is treated as `identity_conflict` (no default save target).

  For a local file there is no Graph call at all (`not_cloud_document`), so AC2 ("self-identifies without a Graph
  round-trip") still holds.

---

## 10. Wiring spec, for whichever path is chosen

- **New file** `Services/Office/OfficeDocumentStamp.cs`: a static, stateless helper, ported from the probe. It has
  `Classify`, `Stamp(bytes, id)` and `TryReadStamp(bytes)`, and no `DocumentFormat.OpenXml` in the production path.
  - ADR-010: no DI registration.
  - ADR-049: no reference to `Services/Compose/**`.
  - ADR-007: bytes-in/bytes-out, with no Graph type.
- **`OfficeService.SaveAsync`**, the Document arm: classify → refuse `OFFICE_020` when corrupt → stamp → stream.
  - Version: pass `versionTarget.DocumentId`.
  - Create: `Guid.NewGuid()`, passed through to `CreateDocumentWithSpePointersAsync(..., documentId)`.
  - `fileSize` must be the **stamped** length.
- **Other code changes**: `OfficeErrorCodes.OFFICE_020` → 400; `CreateDocumentRequest.Id`;
  `DataverseServiceClientImpl.CreateDocumentAsync` sets `document.Id`; and the world's `CreateDocumentAsync` honours
  a supplied id.
- **Tests**, appended to `OfficeEndpointsContractTests`:
  - a create is stamped (download the world's item bytes and read the stamp = the created row's id);
  - a version is stamped with the target id;
  - fixture parts are byte-identical except the two registration parts, which change by insertion only;
  - PDF and EML pass through byte-identical;
  - a corrupt `.docx` → 400 with no SPE write;
  - a re-save leaves one part;
  - a pre-release unstamped document's version save still works.

  Also migrate the `PK`-prefixed fixtures to one real minimal `.docx` (§6d).
- **Placement Justification** (§10 / `bff-extensions.md`): inside the existing `POST /api/office/save`, the same
  answers as 023 §8. No route, no DI registration, no package. `System.IO.Compression` and `System.Xml` are
  in-box, so the publish delta is expected to be ≈ 0.
- **Component justification** (§11):
  1. Existing: none. There is no stamp mechanism, and the Compose OOXML path is out of bounds.
  2. Extension: the save spine is extended; the helper is new because nothing expresses a package-part insertion.
  3. Cost of doing nothing: a round-tripped local copy creates a new row on every re-upload (POML justification).

## 11. What was run

- `dotnet build` + run of the throwaway probe (scratchpad `stampprobe/`, `net10.0`, `DocumentFormat.OpenXml`
  3.5.1 used only to validate the output): **compose-corpus 26/26 PASS, RealTemplates 2/2 PASS.**
- **Not run, because no production code changed:** the BFF build, the test suites, publish size, the CVE check and
  `/conflict-check`. They belong to the wiring task.

## 12. Unverified

- A stamped file opened and re-saved in real Word, desktop and web. The part surviving an edit-save is inferred from
  019 §1.5, not observed.
- Office.js `getByNamespaceAsync` finding the part (no host run).
- Whether Word's `getFileAsync` returns byte-identical bytes for an unchanged document. If it doesn't, the
  production reach of the 028 link half is already smaller than the tests suggest, which would shrink the cost of
  option A. It is worth one operator probe before the owner decides.

---

# 13. Implementation record (2026-09-17)

Shipped per the owner's 2026-09-17 decisions: **§7 option A, sequenced as C first** — version-path stamping
proven first, then create-path (unblocked because task **025 landed** and is in this branch's base `d21e54e98`).

## 13.1 What shipped

| File | Change |
|---|---|
| `src/.../Services/Office/OfficeDocumentStamp.cs` | **NEW.** Static, stateless; `Classify` / `Stamp` / `TryReadStamp`. No DI (ADR-010), no Graph type (ADR-007), no reference to `Services/Compose/**` or `Spaarke.Compose.Components` (ADR-049 independence). |
| `src/.../Api/Office/Errors/OfficeErrorCodes.cs` | `CorruptDocumentPackage = "OFFICE_021"` → 400 / `validation-error` / "Document File Unreadable". |
| `src/.../Api/Office/OfficeEndpoints.cs` | One new `MapSaveErrorToProblem` arm for `OFFICE_021`. The three `problem+json` content-type sites belong to **task 052** and were left alone. |
| `src/.../Services/Office/OfficeService.cs` | Document arm: classify → refuse `OFFICE_021` → stamp → stream, with `fileSize` = the **stamped** length. Version stamps `versionTarget.DocumentId`; create mints and stamps a pre-assigned id. Plus the §13.4 fix. |
| `src/.../Services/Office/OfficeDocumentPersistence.cs` | Optional trailing `Guid? preAssignedDocumentId = null`, passed to `CreateDocumentRequest.Id`. Trailing + optional **on purpose**: ~14 existing test call sites compile unchanged. |
| `src/server/shared/Spaarke.Dataverse/Models.cs` | `CreateDocumentRequest.Id` (`Guid?`) — the **shared-contract** addition §2 called for; additive, opt-in. |
| `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` | Honours `request.Id` by setting `document.Id`. The only live implementation (`_archive` copy is dead). |

## 13.2 The error code: `OFFICE_021`, not `OFFICE_020`

§5 assigned `OFFICE_020` to the corrupt-`.docx` refusal. **That code was taken in the interim** by task 025's
name-collision refusal (`OfficeErrorCodes.NameCollision`). Verified by grep before choosing: `OFFICE_001`
through `OFFICE_020` are all in use across `src/` **and** `tests/`, and `OFFICE_021` / `OFFICE_022` return zero
hits repo-wide. `OFFICE_021` is therefore the next free code.

Keeping them distinct is load-bearing, not cosmetic: both refusals are reachable from the same create request,
and the pane offers a two-option retry for a collision (`OFFICE_020`) but must NOT offer one for unreadable
bytes (`OFFICE_021`) — there is nothing to keep-both or version.

## 13.3 The contract task 051 must read (client-side stamp reader)

```
part        /customXml/item{N}.xml      (N = lowest free; skips pre-existing custom XML such as a bibliography)
props       /customXml/itemProps{N}.xml (ds:datastoreItem, ds:schemaRef ds:uri = the namespace below)
wired from  word/_rels/document.xml.rels  ->  .../relationships/customXml  ->  target "../customXml/item{N}.xml"
            customXml/_rels/item{N}.xml.rels -> .../relationships/customXmlProps -> "itemProps{N}.xml"
namespace   urn:spaarke:office:document-identity:1     (explicit DEFAULT xmlns on the root — 019 condition 2)
root        <documentIdentity xmlns="urn:spaarke:office:document-identity:1">
payload     <documentId>{guid:D}</documentId>          (bare lowercase, ADR-044 — no normalisation needed)
```

Read it with the **Common API** (019 condition 1), guarded by `isSetSupported('CustomXmlParts')` (condition 4):
`Office.context.document.customXmlParts.getByNamespaceAsync("urn:spaarke:office:document-identity:1")` →
`getXmlAsync` → text of the single `documentId` child. The constants are exported as
`OfficeDocumentStamp.StampNamespace` / `.StampRootElement` / `.StampIdElement` so the two sides cannot drift.

**Absence is normal, never corruption** (condition 3). The server likewise returns `null` when two stamps
disagree: a wrong identity on a save path is worse than no identity, and the caller then falls back to task 012.

## 13.4 🔴 NEW FINDING — the note's §6a verdict was incomplete, and stamping regressed task 047

§6a concluded "039 idempotency: **unaffected**", because both keys hash the REQUEST `ContentBase64` before any
stamping. That is true **of the keys** and remains true. But task **047** added a second, non-key check —
`OfficeService.IsStillTheSameOperationAsync` → `OfficeStorageUploader.ItemHoldsContentAsync` — which compares the
request's bytes against the document's **stored** bytes to decide whether a Completed job may still answer a
version save as a duplicate. §6a did not consider it.

Once version saves are stamped, the stored bytes are stamped and the request's are not, so that comparison can
**never** match. Every identical version-save retry would then be treated as a new operation and would write a
redundant SPE version — silently undoing the guarantee task 047 exists to provide. Not hypothetical: it is
exactly what `OfficeVersionSaveRevertTests` pins.

**Fix**: compare like with like — stamp the request with the target document id before comparing. This is sound
only because two properties hold, and both are now pinned by tests:

1. **`Stamp` is byte-deterministic** — `Stamp_CalledTwiceOnTheSameInput_ProducesByteIdenticalOutput`. Achieved by
   *deriving* the `ds:itemID` from the document id instead of generating a fresh GUID, and by giving every new
   zip entry a fixed timestamp. This is why those two choices are not incidental.
2. **A re-stamp with the same id is a true no-op** (returns the input array) — so a round-tripped document that
   already carries the id compares correctly too.

## 13.5 Fixture migration — wider than the brief stated

The brief named three suites plus the contract file. The actual blast radius, found by grepping for Office
Document-content-type saves, was **seven files and three separate classes inside the contract file**:

`OfficeVersionSaveOneRowTests` · `OfficeSaveSpineIdempotencyTests` · `OfficeSaveAsNewDocumentLinkGraduateTests` ·
`OfficeVersionSaveRevertTests` · `OfficeVersionSaveAiRefreshPayloadTests` · **`OfficeCreateCollisionTests`** ·
and `OfficeEndpointsContractTests`'s `OfficeVersionSaveContractTests`, `OfficeSaveAddInWireContractTests` and
`OfficeSaveSpineIdempotencyContractTests`.

All now build real packages via the new **`tests/integration/Shared/Office/MinimalDocx.cs`** helper (deterministic
bytes; also reads body text back). The §6d false-green trap was **not** taken — the `PK` prefix was kept and the
fixtures made real, never "fixed" by dropping the prefix.

Two suites NOT migrated, deliberately: `VersionSaveAiRefreshSeamTests` sends plain UTF-8 text (classifies
not-a-zip → passes through untouched, which is correct), and `DuplicateDetectionTests` only computes keys with a
test-local helper and never enters the save path.

Assertions over stored bytes moved from raw `Should().Equal(fixture)` to **body text + stamp id**, because the
stored bytes now legitimately differ from the posted bytes. That is a stronger claim than byte equality, not a
weaker one: it says "this is the draft the user saved, and it names this record".

## 13.6 The §6b / §6c consequences, as they now appear in the suite

- **§6b (graduation)** — `OfficeVersionSaveOneRowTests`'s fourth test formerly asserted "byte-identical re-save
  → the link stands". That is no longer true and cannot be: a pre-release linked copy's stored bytes gain a
  stamp, so its live hash always diverges. Rewritten as
  `VersionSave_OfAPreReleaseLinkedCopy_WithUnchangedContent_GraduatesIt_BecauseTheStoredBytesGainTheStamp`,
  which pins the accepted behaviour *and* everything that must not change with it (the version is still written,
  nothing deleted, no row created, no redirect to a canonical).
- **§6c (link half inert)** — `OfficeSaveAsNewDocumentLinkGraduateTests` rewritten against the **amended SC-5**.
  The binding half of NFR-08 is what it still defends: a create always creates and is never suppressed. The test
  now also asserts *why* no link exists — the copy's stored bytes carry the copy's **own** id — so the reason is
  visible at the failure site rather than inferred from a spec row.
  Its second test's premise (graduating a link *made by the override*) became unreachable, so it was re-scoped to
  the part that survives: the override's output is an independently versionable document. No scenario was lost —
  graduation is covered twice in the same data-mutation KEEP path by the two `OneRowTests` cases above.

## 13.7 Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | **0 warnings / 0 errors** |
| Test project build | **0 warnings / 0 errors** |
| `OfficeDocumentStampTests` (new, `tests/unit/domain/Office/`) | **23 / 23** |
| `OfficeSaveDocumentStampContractTests` (new, contract KEEP path) | **9 / 9** |
| ArchTests | **191 / 191** (baseline held) |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages |
| Publish size | **+0.0111 MB** (45.41 → 45.43 MB). See `014-publish-size.md` |

**Byte fidelity (POML escalation trigger 1 — does not fire).** Asserted structurally, not by "it still opens":
every pre-existing part's decompressed content is byte-identical except `[Content_Types].xml` and
`word/_rels/document.xml.rels`, and each of those is proven to be *the original plus one inserted element* by
reconstructing the whole original from the common prefix + common suffix of before/after. Entry order is
preserved and exactly three entries are added. A truncated package and a bare `PK` stub classify Corrupt; a PDF,
an EML, an arbitrary binary and a readable non-Word zip are returned **reference-identical**.

## 13.8 Scope boundaries and deviations

- **POML step 5 is overridden by the owner's precedence decision.** The server exposes `TryReadStamp` as the
  primitive, and forward-only holds. Wiring the precedence ladder (resolved cloud URL wins → stamp → otherwise
  `identity_conflict`) is **client-side and belongs to task 051** (plus 013's resolver hook). AC2
  ("a re-uploaded stamped document self-identifies without a Graph round-trip") is therefore **only fully
  realised once 051 lands** — the server half is in place and tested; nothing consumes it yet. Recorded as a
  limitation rather than claimed as done.
- **POML step 10 not performed**: `tasks/TASK-INDEX.md`, `current-task.md`, the project `CLAUDE.md` and
  `src/client/office-addins/ci-gated-suites.txt` are main-session-owned for this dispatch.
- **`/conflict-check` could not be executed**: the worktree-isolation guard refuses git commands aimed at other
  worktrees, so `unified-access-control-r2`'s unpushed edits to `OfficeService.cs` / `OfficeEndpoints.cs` could
  not be inspected from here. The overlap surface is handed to the main session.
- **Forward-only verified by construction**: the only write is to bytes the client just sent. No backfill, no
  migration, no stamp-on-read exists — and `Post_OfficeSave_VersionSaveOfAnUnstampedPreReleaseDocument_…` pins
  that a pre-release stored version stays byte-for-byte untouched.
- **Still unverified** (unchanged from §12, needs a live host): a stamped file opened and re-saved in real Word,
  and Office.js `getByNamespaceAsync` actually finding the part. Both are task 051 / 042 territory.

## 13.9 Step 9.5 review — findings and dispositions

`code-review` + `adr-check` were run in-session (not as background agents). **`adr-check`: clean**, and verified
by grep rather than asserted — zero `Services/Compose` references in the new file or in `OfficeService`'s added
lines (ADR-049 independence), zero `Microsoft.Graph`/`DriveItem` types (ADR-007), zero new DI registrations and
zero new interfaces (ADR-010), zero new endpoints (ADR-001/008), `Guid.ToString("D")` for the stamped value
(ADR-044). Publish +0.0111 MB (ADR-029 / §10).

`code-review` found **two real defects in this task's own new code**. Both were fixed before commit, and both
exist because FR-02 introduces a decompression surface that did not previously exist: before this task the
uploaded bytes were streamed to storage **without ever being expanded**.

| # | Finding | Severity | Disposition |
|---|---|---|---|
| 1 | **Zip-bomb exposure.** Package parts were read with an unbounded `CopyTo`. A zip expands at roughly 1000:1, so an upload well inside the request limit can declare a part of many gigabytes, on a save path, from an untrusted file. | **Critical (security)** | **Fixed.** `MaxPartBytes` (8 MB) enforced in a single `TryReadPart` used by all three read sites, checking the central-directory length first *and* the running total while reading — a zip header can lie about the uncompressed size, so the declared value alone is not a guard. Over-size ⇒ pass through **unstamped**, never a failed save. Pinned by `Stamp_WhenAPackagePartExceedsTheSizeBound_…`. |
| 2 | **O(n²) item-number probe.** `LowestFreeItemNumber` called `FindEntry` per candidate, and a miss falls through to a linear scan — quadratic in entry count, and unbounded, on a hostile package with a long run of `customXml/item*` parts. | **Warning (DoS)** | **Fixed.** Entry names hashed once; probe bounded by `MaxCustomXmlProbe` (5000); exhaustion ⇒ `Unsupported` (pass through unstamped). |

Reported and **consciously accepted** (no change made):

- **`ArgumentNullException.ThrowIfNull(bytes)` on a non-nullable `byte[]`** — flagged by the AI-smell checklist
  (smell 3) because NRT already forbids null. **Kept**: NRT is compile-time only and unenforced across assembly
  boundaries; this is a public static entry point on a save path, where the alternative is a
  `NullReferenceException` surfacing deep inside zip handling rather than at the boundary.
- **`OfficeService.SaveAsync` grew ~60 lines** (file 3207 → 3292, +2.6%) and was already a very large method.
  Per `COMPONENT-COMPLEXITY.md` the judgement is cohesion, not line count, and the added block is one linear
  concern (decode → classify → refuse → stamp) in the branch that already owns decoding. **Not decomposed
  here** because `unified-access-control-r2` has unpushed edits to this same file and a structural extraction
  would raise merge risk for no behavioural gain. Flagged for the main session as a deliberate deferral, not an
  oversight. Verified there is no latent bug in the early `return` from inside the `switch`: `contentStream` is
  still null at that point (so the `finally`'s `?.Dispose()` is a no-op), the job is marked Failed first, and no
  SPE write has occurred.
- **`OfficeDocumentStamp.cs` is 811 lines** — roughly 45% XML doc comment. Single responsibility (produce
  stamped bytes); every private member serves the one public operation. Legitimate under the standard's
  "a large *cohesive* file is not a defect" rule.

**Security check that passed and is worth recording**: the obvious attack on a caller-supplied primary key is a
client choosing the id. It cannot: `preAssignedDocumentId` is minted server-side with `Guid.NewGuid()` inside
`OfficeService` and is never read from the request body; `DataverseServiceClientImpl` additionally ignores
`Guid.Empty`. `CreateDocumentRequest.Id` is not bound from any HTTP surface.

## 13.10 How the full suite was run — and a trap worth naming

Recorded because it will bite the next task that has to produce full-suite numbers for this assembly.

**The suite cannot be run in one command under the agent harness.** A single tool call is capped at 600 s, and
`Sprk.Bff.Api.Tests` takes far longer — the cost is not test COUNT but `WebApplicationFactory` construction:
`.Services.Ai.` runs 4,677 tests in ~35 s, while `.Api.` (≈3,500) exceeds 13 minutes because its contract
suites build a fresh host per test. Sizing chunks by class count is therefore misleading; size them by whether
the classes are factory-based.

**🔴 Second trap — `FullyQualifiedName~.Api.` matches the ENTIRE assembly.** Every test's fully-qualified name
begins `Sprk.Bff.Api.Tests.`, which itself contains the substring `.Api.`. So a `~.Api.` chunk is not a chunk at
all — it is the whole suite — and its complement `!~.Api.` selects **nothing** (VSTest reports "No test matches
the given testcase filter", which is easy to misread as "this area is empty" rather than "your filter is
wrong"). The symptom that exposed it: a supposed "rest of `.Api.`" chunk ran 11,627 tests and its skip list
contained `…Tests.Services.Office.…`, a namespace that filter should never have reached.

**Use the fully-qualified prefix `Sprk.Bff.Api.Tests.Api.`** when selecting the `Api` area. `.Api.Ai.` and
`.Api.Office.` are safe as-is because those substrings are unambiguous.

The corrected partition is complete and disjoint (`~` / `!~` / `&`; no parentheses — VSTest does not handle them
reliably). Measured on the final binaries for this task:

| Chunk (filter) | Passed | Failed | Skipped |
|---|---|---|---|
| `~.Services.Ai.` | 4660 | 0 | 17 |
| `~.Services.` & `!~.Services.Ai.` | 2239 | 0 | 7 |
| `~.Seam.` | 1749 | 0 | 0 |
| `~.Api.Ai.` | 601 | 0 | 14 |
| `~.Api.Office.` | 171 | 0 | 9 |
| `~Sprk.Bff.Api.Tests.Api.` & `!~.Api.Ai.` & `!~.Api.Office.` | 721 | 0 | 0 |
| `!~.Services.` & `!~.Seam.` & `!~Sprk.Bff.Api.Tests.Api.` | 2225 | 0 | 9 |
| **Total** | **12,366** | **0** | **56** |

Reconciliation: 12,422 tests total; skipped **56** matches the pre-task baseline exactly, and passed
**12,366 = 12,333 (baseline) + 33 new tests** (24 `OfficeDocumentStampTests` + 9
`OfficeSaveDocumentStampContractTests`). Every test in the assembly is in exactly one row.

**🔴 The trap: a failed build + `--no-build` silently yields numbers for code that was never tested.**
A chunk that overruns the cap is moved to the BACKGROUND, and its `testhost` keeps a **write lock** on
`Sprk.Bff.Api.dll`. The next `dotnet build` of the test project then fails with `MSB3027 / MSB3021` ("the file
is locked by: testhost") — but if the following `dotnet test --no-build` is chained with `;` or run separately,
it happily executes the **stale** assembly and reports a confident green.

That happened here: three chunk results came back green against pre-fix binaries. The tell was a COUNT, not an
error — the stamper suite reported 32 where 33 was expected, because the test added minutes earlier did not
exist in the assembly under test. All three results were discarded and re-run.

Defences, in order of reliability:
1. **Gate the tests on the build's exit code** — `dotnet build … && dotnet test …`, with no pipe on the build
   (a pipe replaces its exit status with the pipe's).
2. **Assert an expected count** for a suite you just changed. A number that did not move is the cheapest
   possible detector of "my change is not in the binary".
3. **Stop the background task first** (`TaskStop`) to release the lock; `tasklist | grep testhost` confirms.
4. Note that concurrent `dotnet test` runs on the same assembly are safe for the LOCK (shared read) but split
   the CPU, which can push a borderline chunk over the cap — so run chunks serially.
