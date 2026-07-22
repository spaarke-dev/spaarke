# Design Note — Server-Side DOCX→HTML Conversion (drop mammoth)

> **Status**: FINALIZED (Phase 1) — signed off 2026-07-21 for the structural correction. Phases 2/3 tracked as follow-ups (§11).
> **Author**: round-4 UAT follow-up (2026-07-21). Branch `work/spaarkeai-compose-r3`.
> **Reviews incorporated**: `CLAUDEREVIEW-docx-html-converter-design.md` (F-01…F-04) + `GPT-compose-docx-html-conversion-architecture-review.md` (§4 single-traversal, §11 fail-closed, §13 security). See §10 for the disposition of every finding.
> **Supersedes**: the OpenXmlPowerTools recommendation in `uat-round-4-handoff-CONTEXT.md` §ARCHITECTURE FINDING (corrected below).
> **Class of bug eliminated**: the recurring `"a tracked change could not be located" / "w14:paraId matches no paragraph in the retained original"` save failures.
>
> **Key correction from review** (both reviewers, independently): the original proposal joined a structural HTML walk to the pre-parse map by ordinal index (`paraIdMap[index]`) — that is the *same* two-engine drift class relocated server-side. Phase 1 replaces it with **one genuine traversal**: the id is read/minted from the *same `Paragraph` instance being projected*, and both the HTML and the id map are byproducts of that single pass. There is **no ordinal reconciliation** anywhere in the import path.

---

## 1. Problem — two engines walk the same document

On **Load**, two independent engines process the same `.docx`:

| Engine | Where | What it does |
|---|---|---|
| `ParaIdPreParser` | **Server** (`ParaIdPreParser.cs`) | Walks `body.Descendants<Paragraph>()` in document order; assigns each `<w:p>` a `w14:paraId` (existing verbatim, id-less **minted**); returns an ordered `ParaIdMap[]`. |
| **mammoth** | **Client** (`docxBridge.ts` `docxToTipTapHtml`) | Independently flattens the bytes → HTML. Discards `w14:paraId`. |

The client then calls `stampParaIds(editor, map)` — stamping `map[N].paraId` onto the **Nth** editor node **by position**.

**The defect**: mammoth's emitted-paragraph count is not guaranteed to equal the server's `<w:p>` count. Empty paragraphs (mammoth default `ignoreEmptyParagraphs:true` drops them), tab-laid-out content, and table reflow all shift the count. Once counts diverge, the position index `N` drifts, edited paragraphs receive `paraId`s the retained original lacks, and the delta save aborts.

`ignoreEmptyParagraphs:false` (committed `9cbea5d77`) patches **one** cause of the count mismatch. It does **not** fix the design: any future divergence between mammoth's flattening and the OOXML paragraph walk re-opens the same failure. **The bug class is unfixable while two engines walk the document.**

---

## 2. Decision

**Make the server the single engine.** Extend the existing server-side OOXML walk so it emits **paraId-tagged, TipTap-shaped HTML** in the *same pass* that assigns the ids. The client renders that HTML directly and never runs a second conversion.

Because one walk produces both the HTML **and** the ids, the paragraph set and the id set are **aligned by construction** — there is no second walk to drift against. Position-based stamping and the entire client↔server paraId reconciliation disappear.

### 2.1 Alternatives considered and rejected

| Option | Verdict | Reason |
|---|---|---|
| **Keep mammoth, patch alignment (`ignoreEmptyParagraphs:false`)** | Reject as the durable fix (keep as belt-and-suspenders) | Patches one symptom; two-engine drift class survives. |
| **OpenXmlPowerTools `HtmlConverter` (Ms-PL) server-side** | **Reject** | `ParaIdPreParser.cs` design notes (lines 26, 44-48) record a deliberate team decision to avoid this path **because it re-pulls SkiaSharp** — a heavy native dep that breaches the §10 NFR-01 publish-size ceiling. It also emits richly-styled HTML TipTap's schema won't round-trip, and still wouldn't tag paraIds. More work, more risk, against a standing decision. |
| **Client-side OOXML walker (read paraIds from bytes on the client)** | Reject | The client *does* hold the bytes, but this re-introduces a **second engine** (client XML walk that must exactly mirror the server walk) — the same drift class, relocated. Also duplicates in JS logic the server already owns in C#, and splits id-minting authority (client vs server mint id-less paragraphs differently). |
| **Server-authoritative single-walk hand-rolled converter** | **ACCEPT** | Zero new dependencies (`DocumentFormat.OpenXml` already referenced). One engine → alignment by construction. Reuses the pre-parser walk + mint. Full control of the exact HTML shape TipTap accepts. Honors the SkiaSharp-avoidance decision. |

---

## 3. Architecture

### 3.1 New server component — `ComposeDocxProjectionBuilder` (single traversal)

- **Input**: `ReadOnlyMemory<byte>` docx + `CancellationToken`. **Does NOT take a pre-built `ParaIdMap`** — it produces the map itself, from the same walk that emits the HTML.
- **Output**: a `ComposeDocxProjection` record — `{ Status, Html, ParaIdMap, Warnings }` (see §3.3).
- **The single-walk invariant (the whole point).** The builder opens the package once and enumerates source paragraphs in the *identical* recursive document order `ParaIdPreParser` uses (`body.Descendants<Paragraph>()`, which reaches paragraphs inside `w:tbl`, nested tables, `w:sdt` content controls, and `w:txbxContent` text boxes). For **each `Paragraph` instance**, in one step, it:
  1. reads its `w14:paraId` (or mints an OOXML-valid one, collision-checked — same range/format as the existing pre-parser),
  2. emits that paragraph's editor block with `data-paraid="{that id}"`,
  3. appends the `(index, id, isMinted)` entry to the map.
  So the HTML block sequence and the `ParaIdMap` are the **same sequence by construction** — there is no second walk and no `paraIdMap[index]` join. **Forbidden**: any code of the form `id = paraIdMap[index]` in the projection path (enforced by a code-review checklist item + the identity tests in §8).
- **`ParaIdPreParser` disposition**: refactored to delegate to the shared enumeration (or retired in favour of the builder's map output) so there is exactly **one** paragraph-enumeration authority. Its public `ParaIdMapEntry`/result shape is preserved for the save-side consumers (`ComposeBaselineParaIdStamper`, revision/comment paraId resolution) — only its *internal* walk is unified.
- **Dependencies**: `DocumentFormat.OpenXml` only (already a BFF ref — **zero package/publish-size delta**, NFR-01 clean). Operates on the opened `WordprocessingDocument` / `MainDocumentPart` (hyperlink `r:id`s resolve against `HyperlinkRelationships`; "pure `byte[]`-in" is not "body-XML-only").
- **Purity**: `byte[]`-in / record-out. No network I/O, no Graph, no AI types (mirrors `ParaIdPreParser`'s Tier-1 NetArchTest posture). Safe singleton.
- **Privacy**: produces Tier-3 content (document text) — never logged; diagnostics carry counts/status/warning-codes only.
- **Security (GPT §13, scoped to our threat model — a tenant document fetched via OBO, not an anonymous upload):** hyperlink `href` restricted to a protocol allowlist (`http`, `https`, `mailto`, internal anchors); `javascript:`/`data:`/`file:` neutralized. Never resolve/fetch external relationships during projection. Sane resource caps (max paragraph/run/table counts, output size) + `CancellationToken` honored; malformed ZIP/XML degrades to `Status = Failed` (never a throw that fails Load).

### 3.2 HTML subset (the LOCKED OOB set → HTML)

Mirrors the inverse of `ComposeDocumentRenderer` (HTML/model → docx) and the client `buildContentModel`, so this is the inverse of code that already exists.

| OOXML | HTML emitted |
|---|---|
| `<w:p>` (body/normal) | `<p data-paraid="ID">…runs…</p>` |
| `<w:p>` with heading `pStyle` (1-6) | `<h1..h6 data-paraid="ID">…</h1..>` |
| `<w:p>` in a numbered/bulleted list | `<ul>/<ol><li><p data-paraid="ID">…</p></li>` (single-level OOB; multi-level degrades per spike §3.2) |
| `<w:tbl>` | `<table><tbody><tr><td><p data-paraid="ID">…</p></td>…` (cell paragraphs keep their ids) |
| Run `<w:b>/<w:i>/<w:u>/<w:strike>` | `<strong>/<em>/<u>/<s>` |
| `<w:tab/>` / `<w:br/>` | `<br>` for breaks; tab preserved as a **non-collapsing** representation (styled span / entity — never a bare `\t`, which HTML whitespace-collapses, GPT §9.1). Full tab *node* round-trip is a Phase 3 fidelity item; Phase 1 only needs the tab not to vanish, and save no longer text-searches so a tab can't 422. |
| `<w:hyperlink>` | `<a href="…">` |
| Empty `<w:p>` | `<p data-paraid="ID"></p>` — **preserved** (id alignment) |

**Revision-flattening rule (F-02 — normative, stated independent of mammoth).** The builder emits *settled prose with all text present and revision wrappers stripped to plain runs* — it does **not** "accept" or "reject" revisions:
- `w:ins` runs → emit the inserted text as a normal run (wrapper stripped).
- `w:del` runs (`w:delText`) → emit the deleted text as a **normal run** (text present). The deleted text MUST be present in the base HTML so the client `applyImportedRevisions` overlay has something to anchor its deletion mark to.
- A paragraph-mark deletion (`w:del` on the paragraph mark — a fully "deleted" paragraph) is a `<w:p>` that `Descendants<Paragraph>()` still enumerates, so it is **emitted with its `data-paraid`** (content possibly empty). Omitting it would break the count/id sequence — the exact drift this design eliminates.

The `ImportedRevisions`/comment overlays then re-apply the marks client-side, keyed by the now-exact `paraId` (see §5). This keeps the builder's scope to structure + text, not revision rendering. (Projecting revisions directly as marks in one pass — GPT §10 — is a Phase 2/3 item; §11.)

### 3.3 Contract change — projection status + HTML (fail-closed)

`LoadComposeDocumentResult` (`IComposeService.cs:336`) gains an explicit projection block. Fail-closed (F-04 + GPT §11): the client must **never** infer success from `Html.Length > 0` — a conversion *failure* must not mount a blank editable document over a non-empty retained baseline.

```csharp
/// <summary>Server-side DOCX→editor projection (Phase 1: HTML, transitional — JSON is a Phase 2 option).</summary>
public sealed record ComposeDocxProjection
{
    /// <summary>Success = fully projected; Partial = projected with fidelity warnings; Failed = could not project.</summary>
    public required ComposeProjectionStatus Status { get; init; }
    /// <summary>False ⇒ client mounts read-only / offers "Open in Word", never a blank editable doc over a non-empty source.</summary>
    public required bool CanEdit { get; init; }
    /// <summary>paraId-tagged TipTap HTML (data-paraid per block). Tier-3 — never logged. Empty when Status = Failed.</summary>
    public string Html { get; init; } = string.Empty;
    /// <summary>Machine-readable, user-presentable fidelity warnings (codes + counts only; no document content).</summary>
    public IReadOnlyList<ComposeProjectionWarning> Warnings { get; init; } = Array.Empty<ComposeProjectionWarning>();
    /// <summary>Contract version so a future JSON/marks projection is a versioned change, not a silent one.</summary>
    public string SchemaVersion { get; init; } = "compose-html-v1";
}

public enum ComposeProjectionStatus { Success, Partial, Failed }
```

`LoadComposeDocumentResult` carries `Projection` alongside the existing fields. **Nothing is removed:**
- `Content` (docx bytes) — retained (client save fast-path `state.docxBytes`; server delta baseline).
- `ParaIdMap` — retained (save-side `ComposeBaselineParaIdStamper` + `ImportedRevisions`/comment paraId resolution) — now produced by the builder's single walk, **not** consumed for ordinal client stamping.
- `VersionId` / `ETag` — retained (load-time SPE version already captured + carried to save as `BaselineVersionId`; the source-version plumbing GPT §12 asks for largely **exists already** — hardening stale-reject is a Phase 2 item, §11).

`ComposeService.LoadAsync` calls the builder in the same best-effort block that runs the pre-parse: a source that cannot be projected yields `Status = Failed, CanEdit = false` (Load still returns HTTP 200 + `Content` bytes; the **client** fails closed on status).

### 3.4 Runtime alignment guard (F-03)

Even though single-walk makes count equality tautological, `LoadAsync` (or the builder) asserts the invariant on every load — emitted `data-paraid` count == `ParaIdMap.Count` — and on mismatch emits a **counts-only** telemetry metric (no document content) and degrades to `Status = Partial`/`Failed`. This converts any residual unknown-construct risk from "silent drift found at save time by a user" into "observed at load time by engineering." Zero new dependencies — a counter and a comparison.

---

## 4. Client changes (`docxBridge.ts` + `ComposeEditor.tsx`)

> **As-built note (2026-07-21).** Mammoth is dropped from the **identity-critical stored-document Load
> path** (the one that was failing). It is **retained as a fallback** for *projection-less* mounts —
> Browse-local / assistant-upload / AI-draft transient docs that have no server round-trip. Those save via
> create-on-save (content passthrough / born-in-editor render), **not** the paraId-delta path, so mammoth's
> drift cannot cause a save-abort there. Full mammoth removal (routing browse-local through a server
> projection too) is a **Phase 2** item. The mount branch: `projection` present → server HTML; `projection`
> null → mammoth fallback (§11).

1. `stampParaIds` is **not called** on the projection path — ids arrive in the HTML (`data-paraid`).
3. Mount reads `result.Projection`:
   - `Status = Failed` **or** `CanEdit = false` → do **not** mount an editable blank; render a read-only / reference state with the FR-12 *Open in Word* affordance (fail-closed, §3.3). This reuses the existing `referenceOnly` mount branch in `ComposeEditor.tsx`.
   - otherwise → `editor.commands.setContent(result.Projection.Html)` → the paraId extension's `parseHTML` (paraIdExtension.ts:80, `element.getAttribute('data-paraid')`) lands each id as the node's `paraId` attribute automatically. If `Status = Partial`, surface the `Warnings` as a dismissible banner (reuses the round-4 banner stack).
4. **Unchanged**: `applyImportedRevisions`, `applyImportedCommentAnchors`, `captureParaIdSnapshot`, `collectEditedParagraphs`, the save path, born-in-editor drafting. These already key off `paraId` — which is now **exact**.

### 4.1 Confirmed load-bearing fact

`@tiptap/extension-unique-id` is configured with `attributeName: 'paraId'` and a `parseHTML` that reads `data-paraId` from source HTML (paraIdExtension.ts:77-80). It parses the attribute but never renders it back to the DOM (`renderHTML: () => ({})`) — so server HTML `<p data-paraid="X">` round-trips into the hidden node attribute with no visible output and no stamping. **This is why the design needs no client id-carry step.**

---

## 5. Scope guardrails — what stays the same

- **Save / synthesizer** (`ComposeParagraphRedlineSynthesizer`, delta-onto-retained-original) — untouched. It becomes *reliable* because the editor's paraIds are now the server's paraIds by construction.
- **Imported revision + comment overlay** — untouched; still applied client-side after `setContent`, keyed by paraId (now exact).
- **Born-in-editor drafting** (`initialHtml`, `buildContentModel`, full-render save) — untouched.
- **BFF graceful degradation** (`3fd00afad`) — kept as a safety net for any residual mismatch; expected to be a no-op once conversion is single-engine.

---

## 6. Governance

### 6.1 §10 BFF Placement Justification
- **New surface**: one pure converter class in `Services/Compose/`, called only from `ComposeService.LoadAsync`. No new endpoint, no new DI graph reach, no new package.
- **Publish-size (NFR-01)**: **zero delta** — `DocumentFormat.OpenXml` already referenced; no SkiaSharp, no OpenXmlPowerTools. Will still measure `dotnet publish` before/after per the per-task rule.
- **CVE**: no new package → no new advisory surface.
- **Facade/Tier rules**: converter is Tier-1 pure (no AI/Graph types) — same posture as `ParaIdPreParser`.

### 6.2 §11 Component Justification
- **Existing**: overlaps with `ParaIdPreParser` (walk) and `ComposeDocumentRenderer` (the docx-authoring inverse).
- **Extension**: extends the compose OOXML surface rather than adding a parallel one; shares the pre-parser's walk order and mint. Could co-locate with `ParaIdPreParser` if preferred (both operate on the same walk) — proposed as a sibling class for single-responsibility.
- **Cost-of-doing-nothing**: without it, docx→editor conversion stays client-side on a second engine and the save-abort bug class remains structurally present — concretely, every complex `.docx` (empties/tabs/tables) risks an unsaveable session.

### 6.3 §6.5 ADR Conflict Resolution — **Path A (project-scoped exception)**
Client-side conversion was a **spike decision** (spike-1 §4.5), not an ADR MUST — so this is not an ADR violation. It is a documented reversal of a spike-era choice. Recording it here as a Path A note; no ADR amendment required. NFR-03 (no TipTap Pro / no AGPL) is **honored** — the fix removes mammoth and adds no licensed conversion library.

---

## 7. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Converter fidelity < mammoth on some construct | OOB subset is LOCKED + small; converter is the inverse of `ComposeDocumentRenderer`. Fidelity gaps degrade to plainer HTML, never to a save failure. `Open in Word` (FR-12) remains the escape hatch. |
| `data-paraid` case-sensitivity through DOMParser | HTML attributes are case-insensitive; emit lowercase `data-paraid`; a unit test asserts the extension picks it up on `setContent`. |
| Larger Load response (HTML + bytes) | HTML is text (gzip-friendly); bytes were already sent. Acceptable; measured in test. |
| Hidden coupling in tests that mock `docxToTipTapHtml` | Update the affected client tests (mount now consumes `result.Html`); mammoth mocks removed. |

---

## 8. Test plan

**Server — `ComposeDocxProjectionBuilderTests`**
- Structure/runs: paragraphs, headings 1-6, single-level lists, tables, bold/italic/underline/strike, hyperlinks (with protocol allowlist — `javascript:` neutralized), tabs, breaks, empty paragraphs — each carries the right tag + `data-paraid`.
- Revision rule (F-02): a doc with an inserted paragraph, a fully paragraph-mark-deleted paragraph, and a paragraph with inline `w:ins`+`w:del` → all text present, wrappers stripped, deleted paragraph still emitted with its id.
- **Identity tests (the single-walk proof)**: the emitted `data-paraid` sequence == the `ParaIdMap` sequence, one-to-one, order-identical; **no `paraIdMap[index]` lookup exists in the projection path** (asserted structurally / by review checklist).
- **Adversarial fixtures (F-01)** — each asserts `data-paraid` count == `ParaIdMap.Count` and id-order identity:
  - `fixture-sdt-wrapped-paragraphs.docx` (block-level + inline content controls)
  - `fixture-nested-table.docx`
  - `fixture-textbox-content.docx` (`w:txbxContent`)
  - `fixture-tracked-paragraph-mark-deletion.docx`
- **Golden**: the real **CIPO letter** — count + id-uniqueness alignment.
- **Fail-closed**: malformed ZIP/XML → `Status = Failed, CanEdit = false`, empty `Html`, **no throw** (Load still returns bytes).
- **Runtime guard (F-03)**: forced count-mismatch fixture → telemetry metric emitted (counts only), `Status` degraded.

**Client**
- Mount test: `setContent(Projection.Html)` yields nodes whose `paraId` attrs equal the `data-paraid`s (no stamping; `stampParaIds` not invoked).
- Fail-closed test: `Status = Failed` / `CanEdit = false` → reference-only mount, save disabled, no blank editable doc.
- `Partial` → warnings banner renders.
- `collectEditedParagraphs` after an edit still emits the correct paraId delta.
- Remove/replace mammoth-mock tests.

**End-to-end (seam + UAT)**
- Through-the-wire `WebApplicationFactory` seam slice (NFR-06 DoD): Load returns a projection; the projected ids match what the save-side stamper expects.
- CIPO doc UAT: AI edit + manual edit + paste edit → **Save succeeds** with correct tracked changes.

---

## 9. Rollout (Phase 1)
1. Implement + unit-test `ComposeDocxProjectionBuilder` (single walk) with the adversarial + CIPO fixtures.
2. Refactor `ParaIdPreParser` to the shared enumeration (one paragraph-enumeration authority); keep its public result shape.
3. Wire `LoadAsync` → `Projection`; contract + seam test.
4. Rewire client mount (projection status handling, drop mammoth + `stampParaIds`); update client tests.
5. Full both-suite green + `dotnet publish` size check (vs ~49.63 MB baseline; expect ≈0 delta).
6. Coordinated deploy (push → worktree-sync → BFF + SpaarkeAi) → re-UAT CIPO doc.
7. Keep the graceful-degradation net (`3fd00afad`) until the Phase 3 corpus passes + telemetry is clean (both reviews). Remove the now-redundant `ignoreEmptyParagraphs` note only after Phase 1 is confirmed live.

---

## 10. Review-finding disposition

| Finding | Source | Disposition |
|---|---|---|
| Single genuine traversal — no `paraIdMap[index]` ordinal join | CLAUDEREVIEW F-01 · GPT §4 | **Adopted (core).** §3.1 single-walk builder; identity tests §8. |
| Walk-mirroring proof for `w:sdt` / `w:txbxContent` / nested tables | CLAUDEREVIEW F-01 | **Adopted.** Same `Descendants<Paragraph>()` authority + 4 adversarial fixtures §8. |
| Normative revision-flattening rule (text present, wrappers stripped, deleted para still emitted) | CLAUDEREVIEW F-02 | **Adopted.** §3.2. |
| Runtime alignment invariant + counts-only telemetry | CLAUDEREVIEW F-03 | **Adopted.** §3.4. |
| Fail-closed status; never blank-editable over non-empty baseline | CLAUDEREVIEW F-04 · GPT §11 | **Adopted.** `ComposeDocxProjection.Status`/`CanEdit` §3.3; client §4. |
| Hyperlink relationship resolution / protocol allowlist; no external fetch; resource caps | GPT §3, §13 | **Adopted (scoped).** §3.1 security bullet — sized to the OBO-tenant-document threat model, not anonymous upload. |
| Unsupported-construct **detection** + fidelity banner (surfaced) | GPT §9; CLAUDEREVIEW F-04 | **Adopted lightly** — detection + `Warnings` + document-level banner. Full per-block read-only *enforcement* → Phase 2. |
| TipTap JSON as canonical contract | GPT §6 | **Deferred (Phase 2).** HTML now, `SchemaVersion`-gated + labeled transitional; JSON is a versioned change, not silent. Rationale: existing client mount + overlays are HTML/editor-state based; JSON couples BFF to the ProseMirror schema. |
| `sourceParaId` vs `nodeId` split | GPT §7 | **Deferred (Phase 2).** The specific danger (new node impersonating a source paragraph) is **already bounded**: `collectEditedParagraphs` only deltas load-time paraIds and drops brand-new ones; the extension re-mints on split (FR-10). Clean model is a refactor, not a Phase-1 correctness gap. |
| Per-block `EditSafety` enforcement | GPT §9.5 | **Deferred (Phase 2/3).** |
| Source-version / stale-baseline **hard reject** | GPT §12 | **Mostly already present** (`VersionId`/`BaselineVersionId`/`ETag`); hard-reject-on-external-change hardening → Phase 2. |
| Structural save ops (split/merge/move/paste) semantics | GPT §17 | **Pre-existing E1 scope boundary** (documented `docxBridge.ts:336`), not introduced or regressed by dropping mammoth. Unchanged. |
| Legal-document regression corpus as CI gate | GPT §19.6 | **Deferred (Phase 3).** Phase 1 ships the CIPO golden + 4 adversarial fixtures. |

## 11. Phasing (agreed 2026-07-21 — Phase 1 now)

- **Phase 1 (this change)** — structural correction: single-walk `ComposeDocxProjectionBuilder`, normative revision rule, fail-closed projection status, runtime guard, scoped security, unsupported-construct detection + banner, drop mammoth + `stampParaIds`, CIPO + adversarial fixtures. **Eliminates the drift class and unblocks UAT saves.**
- **Phase 2 (tracked follow-up)** — contract hardening: TipTap-JSON projection option, `sourceParaId`/`nodeId` split, per-block edit-safety enforcement, stale-baseline hard-reject.
- **Phase 3 (tracked follow-up)** — fidelity expansion: multi-level numbering, complex tables (`gridSpan`/`vMerge`), revisions/comments projected in-pass as marks, legal-document regression corpus promoted to a CI gate.

Phases 2/3 will be filed as GitHub issues at push time (per `push-to-github` defer/issue audit).
