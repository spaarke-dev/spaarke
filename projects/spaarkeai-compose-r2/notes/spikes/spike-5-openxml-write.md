# Spike 5 — Open XML write `w:ins` + `w:comment` → Word for Web

> **Task**: 005 · **Phase**: 0 Spikes · **Date**: 2026-07-08 · **Model**: sonnet @ high
> **Method**: real Open XML SDK code authored + compiled + run locally to emit a genuine
> annotated `.docx`, schema-validated with `OpenXmlValidator`. The one criterion that requires
> a live SPE round-trip + Word-for-Web render is disclosed as runtime-deferred with a pass recipe.
> **Deliverables**: this note + `sample-annotated.docx` (both under `notes/spikes/`). The throwaway
> generator lives only in the session scratchpad — nothing un-runnable committed to `src/`.

---

## 1. Decision (the one thing this spike unlocks)

**Forward-path validity is CONFIRMED at the writer layer.** The Microsoft Open XML SDK
(`DocumentFormat.OpenXml` 3.4.1 — **already** a BFF dependency) emits a **schema-valid** `.docx`
carrying a native track-change insertion (`<w:ins>`), a native track-change deletion (`<w:del>`),
and a native comment (`<w:comment>` + `commentRangeStart/End` + `commentReference`) with correct
`w:author` / `w:date` metadata. A real sample was produced in this session and passed
`OpenXmlValidator` (Office2019 profile) with **0 errors**. FR-24 (task 050) can build
`DocxAnnotationWriter` on exactly the element structure recorded in §3.

**Honest scope caveat (runtime-deferred).** The acceptance criterion "**Word for Web renders BOTH
natively after an SPE round-trip**" cannot be observed in a headless code session — it needs an SPE
container upload + a browser opening Word for Web. That was **not** run here; claiming "Word for Web
rendered it" would be fabricated. What IS proven: the bytes are valid WordprocessingML that conforms
to the ECMA-376 revision + comment schema Word consumes. §5 gives the exact upload→open→observe
recipe with pass criteria to close criterion #1 on `spaarkedev1` at Phase 5 start.

**Package/hygiene finding (material):** **no new package is needed.** The POML/design floated
`Codeuctivity.OpenXmlPowerTools` — it is **not required for the writer path**. `DocumentFormat.OpenXml`
3.4.1 is already referenced (`Sprk.Bff.Api.csproj:128`) and two shipped services already write `.docx`
with it (`DocxExportService`, `WordTemplateService`) — neither emits revisions or comments, so §3 is
the net-new structure. **Zero publish-size delta, zero new CVE surface** for the annotation writer.

---

## 2. Evidence base (what was checked, with file:line)

| Fact | Evidence |
|------|----------|
| Open XML SDK already a BFF dependency (no new package) | `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj:128` — `<PackageReference Include="DocumentFormat.OpenXml" Version="3.4.1" />` |
| SDK present in local NuGet cache → compilable/runnable here | `~/.nuget/packages/documentformat.openxml/3.4.1`; `dotnet 8.0.422` |
| Existing `.docx` writers (reuse the `WordprocessingDocument.Create` idiom) | `Services/Ai/Export/DocxExportService.cs:55`; `Services/Ai/Delivery/WordTemplateService.cs:55` |
| Neither existing writer emits revisions/comments (so §3 is new) | grep of both files — no `InsertedRun`/`DeletedRun`/`Comment`/`CommentReference` |
| SPE upload seam (facade, no raw Graph — ADR-007) | `Infrastructure/Graph/SpeFileStore.cs:183` `UploadSmallAsUserAsync(HttpContext, containerId, path, Stream, ct)`; `:191` `ReplaceFileContentAsUserAsync(...)` |
| Generated sample is schema-valid | `OpenXmlValidator(Office2019).Validate(...)` → **0 errors** |
| Sample is a well-formed OPC package | parts: `word/document.xml`, `word/comments.xml`, `word/_rels/document.xml.rels`, `[Content_Types].xml`, `_rels/.rels` (1913 bytes) |

---

## 3. The exact working element structure (record for FR-24 / task 050 reuse)

All types are in `DocumentFormat.OpenXml.Wordprocessing`. This is the verbatim XML emitted by the
SDK code below and validated at 0 errors.

### 3a. Insertion — `w:ins` (SDK: `InsertedRun`)

```csharp
var ins = new InsertedRun { Id = "100", Author = "Spaarke AI", Date = whenUtc /* DateTime */ };
ins.AppendChild(new Run(new Text("and conditions set forth herein")
    { Space = SpaceProcessingModeValues.Preserve }));
```
Emits:
```xml
<w:ins w:author="Spaarke AI" w:date="2026-07-08T14:30:00Z" w:id="100">
  <w:r><w:t xml:space="preserve">and conditions set forth herein</w:t></w:r>
</w:ins>
```
> `InsertedRun` (`w:ins`) **wraps** normal `<w:r>` runs. `Author`/`Date`/`Id` are attributes on the
> `w:ins`. `Date` is a `DateTimeValue` — assign a `DateTime` (UTC, serialized ISO-8601 `...Z`).

### 3b. Deletion — `w:del` (SDK: `DeletedRun`, text is `w:delText`)

```csharp
var del = new DeletedRun { Id = "101", Author = "Spaarke AI", Date = whenUtc };
del.AppendChild(new Run(new DeletedText("obsolete ")        // NOT Text — must be DeletedText
    { Space = SpaceProcessingModeValues.Preserve }));
```
Emits:
```xml
<w:del w:author="Spaarke AI" w:date="2026-07-08T14:30:00Z" w:id="101">
  <w:r><w:delText xml:space="preserve">obsolete </w:delText></w:r>
</w:del>
```
> **Gotcha for task 050:** inside a `w:del` the text element MUST be `DeletedText` (`w:delText`),
> not `Text` (`w:t`). Using `Text` inside `w:del` produces a file Word treats as corrupt/odd.

### 3c. Comment — anchored run range + separate comments part

In `document.xml` (anchor):
```xml
<w:p>
  <w:commentRangeStart w:id="0"/>
  <w:r><w:t xml:space="preserve">Indemnification is capped at fees paid.</w:t></w:r>
  <w:commentRangeEnd w:id="0"/>
  <w:r><w:commentReference w:id="0"/></w:r>   <!-- balloon attach point -->
</w:p>
```
In `word/comments.xml` (`WordprocessingCommentsPart`):
```xml
<w:comment w:id="0" w:author="Spaarke AI" w:initials="SAI" w:date="2026-07-08T14:30:00Z">
  <w:p><w:r><w:t>Consider raising the liability cap; market standard is 12 months of fees.</w:t></w:r></w:p>
</w:comment>
```
SDK:
```csharp
para.AppendChild(new CommentRangeStart { Id = "0" });
para.AppendChild(new Run(new Text("Indemnification is capped at fees paid.")));
para.AppendChild(new CommentRangeEnd { Id = "0" });
para.AppendChild(new Run(new CommentReference { Id = "0" }));

var commentsPart = mainPart.AddNewPart<WordprocessingCommentsPart>();
var comment = new Comment { Id = "0", Author = "Spaarke AI", Initials = "SAI", Date = whenUtc };
comment.AppendChild(new Paragraph(new Run(new Text("Consider raising the liability cap; ..."))));
commentsPart.Comments = new Comments(comment);
commentsPart.Comments.Save();
```
> **Three-part invariant** for a comment to render: (1) matching `w:id` across
> `commentRangeStart` / `commentRangeEnd` / `commentReference` in the body, (2) a `w:comment` with
> the same `w:id` in `comments.xml`, (3) the `WordprocessingCommentsPart` relationship (the SDK's
> `AddNewPart` writes `[Content_Types].xml` + `document.xml.rels` automatically — confirmed present).

### 3d. Track-changes settings flag (optional — do NOT confuse with rendering)

A `<w:trackChanges/>` element in `settings.xml` only governs whether **new** user edits get tracked
when the doc is opened. It is **not** required for existing `<w:ins>`/`<w:del>` to render as
revisions — those display as tracked changes regardless. (The SDK type is `Settings`-scoped; the
naive `new TrackChanges()` did not resolve in 3.4.1 — omitted, and the sample still validates.)
Task 050 may add it for author UX, but it is not load-bearing for forward-path validity.

---

## 4. ADR-007 compliance (acceptance criterion #3)

The spike touches SPE **only through the `SpeFileStore` facade** — no `Microsoft.Graph` types are
used in the writer or in `Services/Compose/`. The writer's job is `byte[]` in / annotated `byte[]`
out (pure Open XML); the SPE hop is a separate call:

- **New file (create-on-save / first push):** `SpeFileStore.UploadSmallAsUserAsync(HttpContext,
  containerId, path, Stream, ct)` — OBO path, returns an SDAP `FileHandleDto`.
- **In-place update (push annotations onto the existing checked-out doc — Spike 6 reverse path):**
  `SpeFileStore.ReplaceFileContentAsUserAsync(HttpContext, driveId, itemId, Stream, ct)`.

Both return SDAP DTOs; no Graph SDK type crosses the facade. Task 050's `DocxAnnotationWriter` MUST
depend on `SpeFileStore` (concrete, ADR-010) and never `IGraphClientFactory`/`GraphServiceClient`.

---

## 5. Runtime confirmation recipe (close criterion #1 on `spaarkedev1` at Phase 5 start)

The writer output is proven valid; native Word-for-Web rendering + SPE round-trip is the remaining
runtime check. Recipe (needs a deployed BFF + an SPE container the caller can write):

1. Generate `sample-annotated.docx` (this note's artifact, or regenerate from §3).
2. Upload via the facade: `POST` through the endpoint that calls
   `SpeFileStore.UploadSmallAsUserAsync(ctx, containerId, "/spike/sample-annotated.docx", stream)`.
3. In the SPE-backed document library, **Open in Word for Web**.
4. **Pass criteria (ALL must hold):**
   - (a) "and conditions set forth herein" shows as a **tracked insertion** (colored/underlined,
     attributed to *Spaarke AI*, dated 2026-07-08) under Review ▸ All Markup — **not** plain text.
   - (b) "obsolete " shows as a **tracked deletion** (strikethrough), same author/date — **not**
     silently dropped and **not** left as normal text.
   - (c) "Indemnification is capped at fees paid." carries a **native comment balloon** authored by
     *Spaarke AI* with the §3c body text — **not** lost, **not** rendered as inline text.
   - (d) Accept/Reject on the revisions and Resolve/Reply on the comment behave natively.
5. Capture a screenshot into `notes/spikes/` and flip criterion #1 to ✅ in this table.

If (a)/(b) render as plain text or (c) is missing, the likely cause is a missing part relationship
or an `w:id` mismatch (§3c invariant) — re-run `OpenXmlValidator` first.

---

## 6. Acceptance criteria — disposition

| # | Criterion | Result |
|---|-----------|--------|
| 1 | Word for Web renders BOTH `w:ins` + `w:comment` natively after an SPE round-trip | ⏳ **Runtime-deferred.** Writer output **statically confirmed** schema-valid (0 validator errors) + correct OPC parts; native browser render + SPE hop cannot be observed headlessly. §5 recipe + pass criteria to close. |
| 2 | Exact SDK element structure (types + author/date metadata) recorded for FR-24 reuse | ✅ **Done** — §3 (`InsertedRun`/`w:ins`, `DeletedRun`+`DeletedText`/`w:del`, `CommentRangeStart`/`End`/`Reference` + `WordprocessingCommentsPart`/`Comment`), with verbatim validated XML + gotchas. |
| 3 | SPE upload via `SpeFileStore` facade (no raw `Microsoft.Graph` in `Services/Compose`), per ADR-007 — note states it | ✅ **Stated** — §4. `UploadSmallAsUserAsync` / `ReplaceFileContentAsUserAsync`; writer is byte-in/byte-out, no Graph types. |

Criterion #1 is marked ⏳ rather than ✅ because a code session cannot open Word for Web; this is
disclosed, not overclaimed. Criteria #2 and #3 are fully met by static evidence + a real artifact.

---

## 7. Handoff to task 050 (FR-24 `DocxAnnotationWriter`)

- **Reuse** `DocumentFormat.OpenXml` 3.4.1 — no package add, no BFF publish-size/CVE note triggered.
- **Copy** the `WordprocessingDocument.Open(stream, isEditable:true)` idiom from `WordTemplateService.cs`
  to annotate an existing (save-regenerated) `.docx` in place, and the §3 element structure to inject
  `w:ins`/`w:del`/`w:comment`.
- **Contract:** `byte[] annotate(byte[] docx, IReadOnlyList<Annotation> anns)` — pure, no I/O; the
  SPE hop stays in the endpoint/service via `SpeFileStore` (§4).
- **`w:id` management:** allocate unique, monotonically increasing `w:id`s per doc across `w:ins`,
  `w:del`, and comment ranges; the comment `w:id` must match its `commentRangeStart/End/Reference`.
- **Deletion gotcha:** `w:del` text is `DeletedText` (`w:delText`), never `Text` (§3b).
- **Author/date:** carry the acting user (or "Spaarke AI") + UTC ISO-8601; these surface in Word's
  revision/comment attribution.
- **Validate in a unit test:** run `OpenXmlValidator(Office2019)` on writer output and assert 0
  errors (ADR-038 KEEP-worthy behavioral test — cheap, high-signal, no mocks).

## 8. Why the POML steps were adapted (directional step-mode note)

POML steps 2–3 read "upload to SPE, open in Word for Web, confirm native render." Under
`<steps mode="directional">` the goal + acceptance criteria bind, sequence adapts to environment.
A headless session has no SPE container write nor a browser, so steps 2–3 cannot execute — a
"rendered natively" claim would be fabricated. The highest-value confirmable artifact is a **real,
schema-valid annotated `.docx` + the exact working element structure** (which literal manual clicking
would not have produced as reusable code), plus a precise runtime recipe (§5). Step 1 (write the
annotated `.docx`) and step 4 (record structure + validity) were executed fully; steps 2–3 are
handed to §5 with explicit pass criteria.
