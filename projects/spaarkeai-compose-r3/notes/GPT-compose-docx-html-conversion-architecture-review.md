# Architecture Review — Server-Side DOCX → Compose Projection

> **Purpose:** Review and refine the proposed replacement of Mammoth-based DOCX → HTML conversion in Spaarke Compose.
>
> **Source design:** `design-server-side-docx-html-conversion.md`
>
> **Review posture:** Senior full-stack AI/application architecture review focused on DOCX fidelity, editor identity, save reliability, Open XML processing, TipTap integration, legal-document complexity, and implementation risk.
>
> **Recommendation:** **Approve the server-authoritative projection direction, subject to the required architectural corrections in this document.**

---

## 1. Executive Assessment

The proposal correctly identifies the root cause of the recurring save failures:

- The server walks the DOCX using `DocumentFormat.OpenXml` and assigns or reads `w14:paraId` values.
- Mammoth independently converts the same DOCX to HTML.
- The client then aligns the two outputs by paragraph position.
- Any difference in paragraph emission causes identity drift.

The conclusion is correct:

> Mammoth should not remain in the identity-critical import path.

Moving DOCX projection to the server and making the Open XML traversal authoritative is the correct architectural direction.

However, the proposed design is not yet a true single-pass architecture. It still performs two server-side traversals and joins them by ordinal position:

1. `ParaIdPreParser` walks `body.Descendants<Paragraph>()` and produces `ParaIdMap`.
2. `ComposeDocxHtmlConverter` walks the body again and assigns IDs from `ParaIdMap` by index.

This is materially safer than using Mammoth, but it does not fully eliminate the original defect class. The implementation must instead mint/read the paragraph ID and emit the editor representation from the **same source paragraph instance during the same traversal**.

The recommended long-term internal representation is also **TipTap JSON**, not HTML. HTML may be used as a short-lived migration step, but should not become the canonical Compose import contract.

---

## 2. Decision Summary

### Approve

- Remove Mammoth from imported DOCX processing.
- Make the server authoritative for DOCX parsing and paragraph identity.
- Retain the original DOCX bytes and retained baseline for delta-based save.
- Continue applying edits to the retained original rather than reconstructing the full DOCX from simplified editor content.
- Keep graceful-degradation protections until a representative legal-document corpus passes.

### Require before sign-off

1. Replace the indexed second walk with a genuine single traversal.
2. Prefer TipTap JSON over HTML for the server → editor contract.
3. Separate source OOXML paragraph identity from general editor-node identity.
4. Replace empty-output failure handling with explicit projection status.
5. Detect unsupported DOCX structures and fail closed where editing could destroy content.
6. Add source-version concurrency validation.
7. Add security and resource-consumption limits.
8. Expand tests from example-based checks to architectural invariants and a legal-document corpus.

---

## 3. Root Cause Validation

The design correctly explains the current failure mode.

### Current flow

```text
DOCX bytes
  ├─ Server ParaIdPreParser
  │    └─ ordered ParaIdMap
  │
  └─ Client Mammoth conversion
       └─ HTML paragraph structure

HTML paragraph[N] ← ParaIdMap[N]
```

The alignment assumes that both engines emit the same paragraph sequence.

That assumption is not reliable because conversion engines may differ in their treatment of:

- Empty paragraphs.
- Paragraphs inside tables.
- Paragraphs inside content controls.
- Tabs and layout-oriented content.
- Nested structures.
- Unsupported OOXML blocks.
- Revisions and deleted content.
- Text boxes and drawings.
- Alternate content.

`ignoreEmptyParagraphs:false` addresses one known divergence but does not solve the architectural problem.

### Correct architectural principle

Paragraph identity must not be inferred from positional correspondence between independent transformations.

Identity must be attached at the point where the source paragraph is read:

```text
source Paragraph object
  ├─ read or mint sourceParaId
  ├─ emit editor node
  ├─ emit revision/comment information
  └─ emit source binding
```

---

## 4. Critical Correction — Implement an Actual Single Traversal

### Problem in the proposed design

The proposal describes the solution as a single walk, but the proposed API takes both DOCX bytes and an already-generated `ParaIdMap`:

```csharp
ComposeDocxHtmlConverter(
    ReadOnlyMemory<byte> docx,
    IReadOnlyList<ParaIdMapEntry> paraIdMap)
```

The converter then walks the body and consumes `paraIdMap[index]`.

This remains a position-based reconciliation mechanism.

It can drift when the structural walk:

- Skips an unsupported paragraph container.
- Traverses nested tables differently.
- Handles `w:sdt` or `mc:AlternateContent` differently.
- Emits multiple editor blocks for one source paragraph.
- Emits no editor block for a source paragraph.
- Is later modified without a corresponding change to `ParaIdPreParser`.

### Required architecture

Refactor `ParaIdPreParser` and the proposed converter into a shared projection pipeline or replace both with one component:

```text
ComposeDocxProjectionBuilder
```

Recommended conceptual interface:

```csharp
public interface IComposeDocxProjectionBuilder
{
    ComposeDocumentProjection Build(
        ReadOnlyMemory<byte> docx,
        CancellationToken cancellationToken = default);
}
```

Recommended core behavior:

```csharp
private EditorNode ProjectParagraph(Paragraph paragraph, ProjectionContext context)
{
    var sourceParaId = context.ParagraphIdService.EnsureValidUniqueId(paragraph);

    var node = ProjectParagraphNode(paragraph, sourceParaId, context);

    context.Bindings.Add(new ParagraphBinding(
        SourceParaId: sourceParaId,
        NodeId: node.NodeId,
        NodePath: context.CurrentPath));

    return node;
}
```

### Non-negotiable invariant

There must be no code equivalent to:

```csharp
var paraId = paraIdMap[index];
```

in the import/projection path.

The paragraph ID must be derived directly from the source `Paragraph` currently being projected.

---

## 5. Recommended Component Model

Rename the component from:

```text
ComposeDocxHtmlConverter
```

to:

```text
ComposeDocxProjectionBuilder
```

The component is not merely converting DOCX to HTML. It is constructing the editable Compose projection and the source bindings required for safe save operations.

### Responsibilities

The projection builder should:

1. Open and validate the DOCX package.
2. Traverse supported document structures.
3. Read, validate, repair, or mint `w14:paraId` values.
4. Build the editor document.
5. Produce paragraph/source bindings.
6. Project revisions and comment anchors.
7. Detect unsupported or fidelity-sensitive constructs.
8. Produce warnings and edit-safety classifications.
9. Return source-version and projection-version metadata.

### Suggested output model

```csharp
public sealed record ComposeDocumentProjection
{
    public required ProjectionStatus Status { get; init; }

    public required bool CanEdit { get; init; }

    public JsonDocument? EditorDocument { get; init; }

    public required IReadOnlyList<ParagraphBinding> ParagraphBindings { get; init; }

    public required IReadOnlyList<ProjectionWarning> Warnings { get; init; }

    public required string ProjectionSchemaVersion { get; init; }

    public required string SourceVersion { get; init; }

    public required string BaselineId { get; init; }
}
```

Suggested statuses:

```csharp
public enum ProjectionStatus
{
    Success,
    Partial,
    Unsupported,
    Failed
}
```

---

## 6. Prefer TipTap JSON Over HTML

### Assessment

Server-rendered HTML is a reasonable tactical improvement over Mammoth, but it should not become the canonical internal representation.

TipTap internally operates on a ProseMirror-compatible JSON document. Sending JSON directly avoids an additional normalization stage:

```text
Open XML
  → server HTML
  → DOM parser
  → TipTap schema parsing
  → TipTap JSON
```

Recommended flow:

```text
Open XML
  → ComposeDocxProjectionBuilder
  → TipTap JSON
  → editor.commands.setContent(json)
```

### Benefits

Using TipTap JSON directly avoids:

- DOM whitespace normalization.
- HTML attribute-case concerns.
- Browser-dependent parsing behavior.
- Unsupported HTML structures being silently rewritten.
- Encoding editor concepts as arbitrary spans and data attributes.
- HTML sanitization being the primary editor-content boundary.
- A second conversion between the server projection and editor state.

### Example node

```json
{
  "type": "paragraph",
  "attrs": {
    "sourceParaId": "01AF4C2E",
    "nodeId": "6bb47cc7-89cb-48c1-a645-5e92f24a57de",
    "editSafety": "fullyEditable"
  },
  "content": [
    {
      "type": "text",
      "text": "Example paragraph"
    }
  ]
}
```

### Migration option

If TipTap JSON would materially increase the current implementation scope, HTML may be used for the first corrective release provided that:

- The contract explicitly labels HTML as transitional.
- The projection builder still performs a genuine single traversal.
- No ordinal paragraph-ID stamping remains.
- The response model includes status and warnings.
- A follow-up task is created to move to TipTap JSON.

---

## 7. Separate Source Paragraph Identity From Editor Node Identity

The current design treats `paraId` as both:

- The source Word paragraph identity.
- The editor node unique ID.

These are related but not equivalent.

### Recommended attributes

```text
sourceParaId
nodeId
```

#### `sourceParaId`

- Represents the source or server-minted OOXML `w14:paraId`.
- Must be valid for OOXML paragraph identity.
- Exists for paragraphs imported from the retained baseline.
- May be null for newly created editor paragraphs until save processing assigns an OOXML identity.

#### `nodeId`

- Represents the Compose/TipTap node identity.
- Should be unique for every editor node.
- May use a UUID or another editor-safe identifier.
- Must not be assumed to be a valid `w14:paraId`.

### Why this matters

Users can:

- Insert a paragraph.
- Split one paragraph into two.
- Merge two paragraphs.
- Paste multiple paragraphs.
- Move paragraphs.
- Duplicate content.

Those operations create editor nodes that do not necessarily correspond one-to-one with existing source paragraphs.

### Suggested save semantics

```text
Imported node with sourceParaId
  → update/delete/move against source paragraph

New node without sourceParaId
  → create new OOXML paragraph and mint valid w14:paraId

Split imported node
  → one node retains sourceParaId; additional node(s) become new paragraphs

Merged imported nodes
  → explicit merge operation or controlled delete/update semantics
```

Do not let a TipTap-generated UUID implicitly become the Word paragraph ID unless there is an explicit conversion and validation layer.

---

## 8. Treat Compose Content as an Editable Projection, Not a Lossless DOCX Representation

The proposal states that the converter is the inverse of `ComposeDocumentRenderer`.

That statement should be narrowed.

A renderer from the constrained Compose model to DOCX can be deterministic. The reverse conversion from arbitrary DOCX into that model is not generally invertible because WordprocessingML supports substantially more structure than the Compose editor schema.

The architecture should use this explicit principle:

> DOCX is the authoritative fidelity container. Compose operates on a controlled editable projection. Save applies identity-bound changes to the retained original.

This principle protects the project from gradually treating HTML or TipTap JSON as a complete replacement for the source DOCX.

---

## 9. Fidelity and Unsupported-Construct Handling

### 9.1 Tabs

A literal `\t` in HTML does not reliably preserve a Word tab because normal HTML whitespace processing can collapse it.

Recommended representation:

```json
{
  "type": "tab"
}
```

The editor extension should render the tab visually, and the save path should convert it back to:

```xml
<w:tab/>
```

Do not use repeated spaces as the canonical tab representation.

### 9.2 Numbering

Single-level list support is not sufficient for many legal documents.

Common legal numbering includes:

```text
1.
1.1
1.1(a)
1.1(a)(i)
```

The initial implementation does not necessarily need complete Word numbering support, but it must detect and classify unsupported numbering.

Allowed behaviors:

- Fully project supported numbering.
- Preserve unsupported numbering as a read-only block.
- Permit text-only editing while preserving numbering definitions in the baseline.
- Block editing and provide “Open in Word.”

Not allowed:

- Silently flatten complex numbering into a visually different list and then save destructive changes.

### 9.3 Tables

The converter must explicitly classify support for:

- `gridSpan` horizontal merges.
- `vMerge` vertical merges.
- Nested tables.
- Repeated header rows.
- Cell widths.
- Cell vertical alignment.
- Paragraphs within content controls inside cells.

At minimum, unsupported complex tables should be detected and either:

- Rendered read-only.
- Rendered with a visible fidelity warning.
- Excluded from editing.

### 9.4 Inline and embedded constructs

The projection builder should detect:

- Fields and field results.
- Cross-references.
- Bookmarks.
- Footnote and endnote references.
- Content controls.
- Drawings and inline images.
- Text boxes.
- Superscript and subscript.
- Nonbreaking spaces.
- Soft hyphens.
- Symbols and special-character runs.
- Alternate content.
- Embedded objects.

### 9.5 Edit-safety classification

Each source paragraph or block should be assigned an edit-safety level:

```csharp
public enum EditSafety
{
    FullyEditable,
    TextEditableWithPreservedArtifacts,
    ReadOnlyUnsupported
}
```

#### `FullyEditable`

The paragraph can be safely reconstructed from the Compose representation.

#### `TextEditableWithPreservedArtifacts`

Text may be edited, but some source structures must be retained from the baseline rather than regenerated.

#### `ReadOnlyUnsupported`

Editing could delete or corrupt unsupported source structures, so the editor must block changes.

---

## 10. Revisions and Comments

The proposed design keeps imported revision and comment overlays as a separate client-side process after `setContent`.

This can remain for the MVP, but it leaves another reconciliation boundary:

```text
settled paragraph projection
  + separate revision/comment metadata
  + client-side offset application
```

Even with correct paragraph IDs, offsets may drift due to:

- Tabs.
- Fields.
- Deleted text.
- Whitespace normalization.
- Run merging.
- Special characters.

### Preferred architecture

Project revisions and comments during the same OOXML traversal and emit them directly as TipTap marks or nodes.

Example:

```json
{
  "type": "text",
  "marks": [
    {
      "type": "trackedInsertion",
      "attrs": {
        "revisionId": "12",
        "author": "Jane Smith",
        "date": "2026-07-20T14:30:00Z"
      }
    }
  ],
  "text": "inserted language"
}
```

### MVP requirement if overlays remain separate

Every imported revision/comment anchor must be validated against the final projection:

- `sourceParaId` exists.
- Start and end offsets are valid.
- Expected source text or token sequence matches.
- The anchor resolves to exactly one location.

Unresolved anchors must generate a projection warning and must not disappear silently.

---

## 11. Failure Semantics Must Fail Closed

The proposal states that malformed input should degrade to empty HTML and not fail load.

That behavior is unsafe.

An empty string does not distinguish:

- A valid empty document.
- Conversion failure.
- Unsupported document content.
- Partial projection.
- Server defect.

It also risks mounting a blank editable document over a non-empty retained baseline.

### Required result model

Use explicit status and capabilities:

```csharp
public sealed record ComposeDocumentProjection
{
    public ProjectionStatus Status { get; init; }

    public bool CanEdit { get; init; }

    public JsonDocument? EditorDocument { get; init; }

    public IReadOnlyList<ProjectionWarning> Warnings { get; init; }
}
```

### Required client behavior

| Status | Client behavior |
|---|---|
| `Success` | Mount editor normally. |
| `Partial` | Mount supported content; display warnings; enforce per-block safety. |
| `Unsupported` | Read-only preview or fallback; provide “Open in Word.” |
| `Failed` | Do not mount editable content; disable save. |

Load may still return a successful HTTP response if the file itself was retrieved successfully, but projection failure must not create an editable blank state.

---

## 12. Source Version and Concurrency Control

Paragraph identity does not protect against the underlying document changing after load.

The load response should include a source version identifier:

- SharePoint/Graph ETag.
- Document version ID.
- Cryptographic hash of the retained baseline.
- Server-generated `BaselineId` linked to retained bytes.

Suggested fields:

```csharp
public string SourceVersion { get; init; }

public string BaselineId { get; init; }

public string ProjectionSchemaVersion { get; init; }
```

The save request must return these values.

Before applying deltas, the server must verify that:

- The retained baseline still exists.
- The baseline matches the loaded source version.
- The current source document has not changed externally.
- The projection schema is supported by the save pipeline.

A mismatch should produce a conflict response requiring reload or controlled reconciliation.

Do not apply editor deltas to a stale or different baseline.

---

## 13. Security and Resource Limits

The custom converter must treat DOCX files as untrusted structured input.

Add explicit protections for:

- Maximum compressed package size.
- Maximum decompressed package size.
- Maximum paragraph count.
- Maximum run count.
- Maximum table count.
- Maximum rows and cells.
- Maximum nesting depth.
- Maximum hyperlink count.
- Maximum output document size.
- Cancellation token support.
- Execution timeout.
- Memory bounds.
- Malformed ZIP and malformed XML handling.

### External relationships

The converter must not resolve or fetch external relationships during projection.

### Hyperlinks

Use a protocol allowlist, for example:

```text
https
http
mailto
internal document anchors
```

Reject or neutralize:

```text
javascript:
data:
file:
custom unsafe schemes
```

### Privacy

- Never log document text.
- Never log generated TipTap JSON or HTML.
- Never log comments or tracked-change content.
- Diagnostic logs should contain identifiers, counts, status, warning codes, and timing only.

---

## 14. OpenXmlPowerTools Decision

Rejecting OpenXmlPowerTools for this implementation is reasonable given the project constraints described in the source design:

- Native dependency and publish-size concerns.
- SkiaSharp reintroduction.
- Rich HTML output that still requires schema reduction.
- Lack of native Spaarke paragraph-identity integration.
- Additional dependency and CVE surface.

The custom converter is appropriate provided that:

1. Its supported projection scope is explicit.
2. Unsupported structures are detected.
3. The project maintains a representative test corpus.
4. The component is not treated as a general-purpose, lossless DOCX renderer.

---

## 15. Revised Load Contract

Recommended contract shape:

```csharp
public sealed record LoadComposeDocumentResult
{
    /// <summary>
    /// Original DOCX bytes retained for the current save/baseline workflow.
    /// Tier-3 content; never log.
    /// </summary>
    public required byte[] Content { get; init; }

    /// <summary>
    /// Controlled editor projection produced from the authoritative OOXML traversal.
    /// </summary>
    public required ComposeDocumentProjection Projection { get; init; }

    /// <summary>
    /// Existing paragraph map retained only where required by baseline stamping,
    /// diagnostics, or backwards-compatible save behavior.
    /// It must not be used for ordinal client stamping.
    /// </summary>
    public IReadOnlyList<ParaIdMapEntry> ParaIdMap { get; init; } = [];

    public required string SourceVersion { get; init; }

    public required string BaselineId { get; init; }
}
```

### Transitional HTML contract

If HTML is retained temporarily:

```csharp
public sealed record ComposeDocumentProjection
{
    public ProjectionStatus Status { get; init; }

    public bool CanEdit { get; init; }

    public string Html { get; init; } = string.Empty;

    public IReadOnlyList<ProjectionWarning> Warnings { get; init; } = [];

    public string ProjectionSchemaVersion { get; init; } = "compose-html-v1";
}
```

The client must not infer success from `Html.Length > 0`; it must use `Status` and `CanEdit`.

---

## 16. Revised Client Flow

### Imported DOCX

```text
LoadComposeDocumentResult
  → inspect Projection.Status
  → setContent(Projection.EditorDocument)
  → apply any transitional overlays
  → capture baseline node/source bindings
  → enable editing according to EditSafety
```

### Remove

- Client-side Mammoth conversion.
- Mammoth dependency.
- `docxToTipTapHtml`.
- `stampParaIds`.
- Any `N`th editor node ↔ `N`th source paragraph logic.

### Preserve

- Retained DOCX bytes.
- Save against retained original.
- Imported revision/comment behavior, subject to anchor validation.
- Born-in-editor document path.
- Existing graceful-degradation safety net during rollout.

---

## 17. Save-Side Semantics to Confirm

The design states that save remains unchanged. That is acceptable for the immediate correction, but the following operations must be explicitly supported or rejected:

- Edit paragraph text.
- Delete paragraph.
- Insert paragraph.
- Split paragraph.
- Merge paragraphs.
- Move paragraph.
- Edit list item.
- Insert list item.
- Edit table-cell paragraph.
- Paste multiple paragraphs.
- Undo and redo.

For each operation, define:

```text
editor operation
  → source binding behavior
  → delta model
  → retained-original mutation
  → resulting tracked change
```

Do not assume that paragraph text replacement alone covers structural editor operations.

---

## 18. Required Architectural Invariants

### Identity invariants

1. Every editable imported paragraph has exactly one valid `sourceParaId`.
2. Every `sourceParaId` is unique within the document.
3. IDs are read or minted while projecting the same source paragraph.
4. No imported paragraph identity is assigned by position.
5. No new editor paragraph may impersonate an imported source paragraph.
6. Duplicate or invalid source IDs are repaired deterministically.
7. The retained baseline contains the same source IDs used by the projection.

### Projection invariants

1. Every projected node has a stable `nodeId`.
2. Unsupported constructs are not silently discarded from editable content.
3. Every editable block has an explicit `EditSafety` classification.
4. Projection failure cannot produce an editable blank document.
5. Projection warnings are machine-readable and user-presentable.

### Save invariants

1. Save applies only to the baseline used to create the projection.
2. A stale `SourceVersion` or `BaselineId` is rejected.
3. Unedited source structures remain unchanged.
4. Editing one paragraph does not mutate unrelated paragraphs.
5. Unsupported structures are preserved or editing is blocked.

---

## 19. Test Plan

### 19.1 Unit tests — projection builder

Cover:

- Normal paragraph.
- Empty paragraph.
- Heading levels 1–6.
- Bold, italic, underline, strike.
- Tabs.
- Line breaks.
- Hyperlinks.
- Nonbreaking spaces.
- Lists.
- Multi-level numbering detection.
- Tables.
- Merged-cell detection.
- Nested-table detection.
- Content controls.
- Fields.
- Bookmarks.
- Images/drawings.
- Existing tracked changes.
- Comments.
- Duplicate paragraph IDs.
- Invalid paragraph IDs.
- Paragraphs without IDs.
- Malformed DOCX.
- Resource-limit violations.

### 19.2 Identity tests

Assert:

- Every projected imported paragraph has the correct `sourceParaId`.
- No ordinal map lookup is used.
- IDs are unique.
- IDs in the projection exist in the stamped retained baseline.
- Every paragraph binding resolves to exactly one editor node.
- Every editor node binding resolves to exactly one source paragraph where applicable.

### 19.3 Client tests

Test:

- `setContent(serverProjection)` loads source IDs correctly.
- No stamping function is invoked.
- Editing a paragraph preserves its `sourceParaId`.
- Inserting a paragraph creates a `nodeId` without impersonating a source paragraph.
- Split and merge behavior follows the documented model.
- Read-only unsupported blocks cannot be edited.
- `Partial`, `Unsupported`, and `Failed` statuses render correctly.
- Save is disabled when `CanEdit == false`.

### 19.4 Save tests

Test:

- No-op save.
- Manual text edit.
- AI text edit.
- Paste edit.
- Insert paragraph.
- Delete paragraph.
- Split paragraph.
- Merge paragraph.
- Move paragraph.
- Edit list item.
- Edit table cell.
- Undo and redo before save.
- Existing tracked changes remain intact.
- Existing comments remain anchored.
- Stale baseline is rejected.

### 19.5 Round-trip tests

Required assertions:

- Load → no edits → save produces a semantically equivalent document.
- A single-paragraph edit changes only the intended OOXML structures.
- Unsupported constructs outside an edited paragraph remain byte- or structure-equivalent where practical.
- Unsupported constructs inside an unsafe paragraph cause editing to be blocked.

### 19.6 Legal-document regression corpus

Build a corpus containing:

- CIPO letter fixture.
- Agreement with multi-level numbering.
- Definitions and cross-references.
- Headers and footers.
- Footnotes and endnotes.
- Nested tables.
- Merged table cells.
- Content controls.
- Fields and bookmarks.
- Existing tracked changes.
- Overlapping comments.
- Images and text boxes.
- Word Desktop-generated DOCX.
- Word Web-generated DOCX.
- Google Docs-exported DOCX.
- DMS-exported DOCX.

---

## 20. Rollout Recommendation

### Phase 1 — Structural correction

1. Implement `ComposeDocxProjectionBuilder`.
2. Perform ID minting and node emission in one traversal.
3. Add explicit projection status and warnings.
4. Wire the load contract.
5. Remove Mammoth and positional stamping.
6. Retain current save behavior and graceful-degradation handling.
7. Pass the CIPO document UAT.

### Phase 2 — Contract hardening

1. Move server output from HTML to TipTap JSON.
2. Separate `sourceParaId` and `nodeId`.
3. Add per-block edit-safety classification.
4. Add source-version and baseline conflict checking.
5. Add resource and security limits.

### Phase 3 — Fidelity expansion

1. Improve multi-level numbering projection.
2. Improve table support.
3. Project revisions and comments directly into TipTap JSON.
4. Expand supported legal-document constructs.
5. Promote the regression corpus into a permanent CI gate.

### Cleanup timing

Do not remove the graceful-degradation safety net immediately after the first successful UAT.

Keep it until:

- The legal-document corpus passes.
- Production telemetry shows no unresolved identity mismatches.
- Save conflict and projection-warning telemetry is stable.
- At least one release cycle has completed without recurrence.

---

## 21. Acceptance Criteria

The implementation is ready for sign-off only when all of the following are true:

- [ ] Mammoth is removed from imported DOCX conversion.
- [ ] No client-side positional paragraph stamping remains.
- [ ] No server-side `ParaIdMap[index]` reconciliation remains in projection.
- [ ] Paragraph IDs are minted/read and emitted from the same source paragraph traversal.
- [ ] The retained baseline contains the IDs used by the editor projection.
- [ ] Projection status distinguishes success, partial support, unsupported content, and failure.
- [ ] Failed conversion cannot mount a blank editable document.
- [ ] Unsupported fidelity-sensitive content is detected.
- [ ] Unsafe blocks are read-only or editing is blocked.
- [ ] Source version and baseline identity are validated on save.
- [ ] Security and resource limits are implemented.
- [ ] CIPO UAT succeeds for AI edit, manual edit, and paste edit.
- [ ] Split, merge, insert, delete, list, and table-cell behaviors are tested.
- [ ] No-op round trip is semantically equivalent.
- [ ] Legal-document regression corpus passes.
- [ ] `dotnet publish` size and dependency checks pass.

---

## 22. Final Architecture Position

The correct architectural boundary is:

```text
DOCX = authoritative fidelity and persistence container

Compose projection = controlled editable representation

sourceParaId = stable binding to retained OOXML paragraph

nodeId = stable editor identity

Save = apply validated identity-bound deltas to the retained original
```

The key objective is not to make DOCX and HTML losslessly interchangeable. They are not.

The objective is to:

1. Produce an editor projection from one authoritative OOXML traversal.
2. Preserve stable source identity.
3. Make unsupported fidelity explicit.
4. Apply edits back to the retained source safely.

With the required corrections above, the proposal becomes a strong and defensible foundation for Spaarke Compose.

---

# Claude Code Implementation Directive

Use the following as the implementation orientation for this design:

```text
Implement the server-authoritative DOCX import correction for Spaarke Compose, but do not reproduce the current two-pass ordinal alignment under a different name.

Required architectural outcome:

1. Replace Mammoth-based client conversion and client positional paraId stamping.
2. Create a ComposeDocxProjectionBuilder that opens the DOCX once and traverses each source Paragraph once.
3. During that same traversal:
   - read, validate, repair, or mint the source w14:paraId;
   - emit the corresponding editor node;
   - emit its source binding;
   - collect revision/comment metadata;
   - classify unsupported constructs and edit safety.
4. Do not assign paragraph IDs from ParaIdMap[index] or any ordinal reconciliation.
5. Prefer TipTap JSON as the projection contract. If HTML is required for the first patch, isolate it behind the projection contract and mark it transitional.
6. Separate sourceParaId from editor nodeId.
7. Return explicit ProjectionStatus, CanEdit, Warnings, ProjectionSchemaVersion, SourceVersion, and BaselineId.
8. Conversion failure must fail closed: never mount a blank editable document over a non-empty source.
9. Preserve the original DOCX and retained baseline; continue applying validated deltas to the retained original.
10. Add source-version validation before save.
11. Detect unsupported numbering, complex tables, fields, content controls, drawings, bookmarks, footnotes, and other fidelity-sensitive structures. Preserve them or block unsafe editing; never silently discard them from an editable paragraph.
12. Add resource limits, cancellation, external-relationship blocking, and hyperlink protocol validation.
13. Keep the existing graceful-degradation save protection until the legal-document regression corpus passes and production telemetry confirms stability.

Implementation constraints:

- Use DocumentFormat.OpenXml only; add no new conversion package.
- Preserve BFF dependency and publish-size constraints.
- Never log document text, generated HTML/JSON, revision content, or comment content.
- Keep born-in-editor behavior unchanged unless a shared abstraction is required.
- Avoid broad refactoring outside the Compose load/projection boundary.

Required tests:

- identity invariants;
- empty paragraphs;
- headings, runs, tabs, hyperlinks, lists, tables;
- duplicate/missing/invalid paraIds;
- revisions and comments;
- insert, delete, split, merge, move, paste, list edit, table-cell edit;
- no-op round trip;
- stale baseline rejection;
- unsupported-content fail-closed behavior;
- CIPO fixture UAT;
- representative legal-document regression corpus.

Before implementation, inspect the existing ParaIdPreParser, ComposeDocumentRenderer, ComposeBaselineParaIdStamper, ComposeParagraphRedlineSynthesizer, paraId TipTap extension, imported revision/comment application, and load/save contracts. Reuse existing behavior where correct, but eliminate every ordinal identity bridge.

Deliver:

- implementation;
- updated contracts;
- unit and integration tests;
- removed Mammoth dependency and mocks;
- architecture note describing the sourceParaId/nodeId model;
- publish-size comparison;
- concise implementation summary and any remaining unsupported DOCX constructs.
```
