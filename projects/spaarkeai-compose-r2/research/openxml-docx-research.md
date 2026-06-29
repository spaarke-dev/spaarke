# OpenXML / DOCX comments + track-changes — research memo for Spaarke Compose R2

**Date**: 2026-06-29
**Author**: researcher subagent
**Scope**: Three questions for Compose R2 — (1) Open XML SDK status, (2) editor↔Word two-way patterns, (3) SharePoint Embedded implications.
**Calibration**: Primary sources cited. Speculation marked. Microsoft platform topics move fast — claims are pinned to docs current as of capture date.

---

## Q1: Open XML SDK Status

- **License**: **MIT, confirmed**. The repo at `github.com/dotnet/Open-XML-SDK` carries `The MIT License (MIT) Copyright (c) .NET Foundation and Contributors` (full text verified). The project is a .NET Foundation project.
- **Active maintenance**: **Yes**. Current line is 3.x. NuGet `DocumentFormat.OpenXml` shows 3.4.1 (2026-01-06) on the search side and the repo page reports 3.5.1 (2026-03-18); both within 6 months. 1,297 commits on main, 41 total releases. Active issue/PR throughput.
- **.NET 8 compatibility**: **Yes**. Package targets net8.0 plus netstandard2.0 and (still) net35; the 3.x line is the one we want for net8.
- **Capabilities for our use case**:
  - **Comments**: First-class. Strongly-typed classes `Comments`, `Comment`, `CommentRangeStart`, `CommentRangeEnd`, `CommentReference` in `DocumentFormat.OpenXml.Wordprocessing`. The Microsoft Learn how-to "Insert a comment into a word processing document" (still current) walks through `MainDocumentPart.WordprocessingCommentsPart`, attaching `w:author` and `w:date` attributes, and wiring `commentRangeStart`/`commentRangeEnd`/`commentReference` with a shared `w:id`.
  - **Track changes**: First-class. `InsertedRun` (`w:ins`) and `DeletedRun` (`w:del`) classes exist with `Author`, `Date`, and `Id` attributes settable via `OpenXmlAttribute` / strongly-typed properties. GitHub discussions #1187 and #1817 in the repo confirm the working pattern (set `Author`/`Date`/`Id` on `InsertedRun`/`DeletedRun`, wrap the `Run`). Microsoft's own "Accept all revisions" how-to demonstrates traversing `Descendants<InsertedRun>()` / `Descendants<DeletedRun>()` and is the canonical reference for the read side.
  - The SDK does **not** itself implement diff/accept/reject — it just gives you the typed XML tree. For diff-and-redline you typically pair it with `OpenXmlPowerTools` (see below).
- **Alternatives if SDK has issues**:
  - **OpenXmlPowerTools** — original Microsoft repo (`OfficeDev/Open-Xml-PowerTools`) is **archived**, MIT-licensed, but actively forked. Notable forks: **Clippit** (Sergey Tihon; modern .NET, performance work), **Codeuctivity/OpenXmlPowerTools** (cross-platform; replaced `System.Drawing.Common` with `SkiaSharp`), **EricWhiteDev/Open-Xml-PowerTools** (Eric White, the original author). All MIT. Notably ships a `WmlComparer` that diffs two DOCXs and emits a third with proper `w:ins`/`w:del` — this is the closest thing to a turnkey "given old + new, produce redlined DOCX" library in .NET.
  - **DocX** (community, `xceedsoftware/DocX` / Xceed) is **not MIT** — Xceed Community license. Avoid for proprietary BFF.
  - **drpedapati/docx-review** (MIT, .NET 8, Open XML SDK based) is a CLI shell over exactly this pattern (Open XML SDK + a JSON manifest of edits → tracked changes + comments). Useful as a working **reference implementation** but distributed as a CLI binary, not a library.
- **Recommendation**: `DocumentFormat.OpenXml` 3.x is unambiguously the right choice for Spaarke BFF. Pair with `Codeuctivity.OpenXmlPowerTools` or `Clippit` if we need diff→redline emission.

---

## Q2: Editor ↔ Word Two-Way Patterns

### Confirmed open-source implementations

| Project | License | Tech | Coverage | Maturity |
|---|---|---|---|---|
| **SuperDoc** (`superdoc-dev/superdoc`) | **AGPL-3.0** + commercial option | TypeScript, ProseMirror schema, Yjs CRDT, framework-agnostic with React/Vue/Angular/Svelte/vanilla wrappers; **parses OOXML directly with own paged layout engine — not HTML round-trip** | Comments + tracked changes + multiplayer + headless Node.js mode for agentic redlining | Production-ready; HN launch May 2026; explicit "real Word file as source of truth, not HTML" stance |
| **DOCX Editor / `@eigenpal/docx-editor-core`** (`mhurhangee/docx-editor`) | **Apache-2.0** | ProseMirror-based, React + Vue 3, "canonical OOXML" parser/serializer core | Comments + tracked changes + real-time collab | **Archived fork**; original maintainer Eigenpal went dark June 2026; v1.9.0 last npm release |
| **adeu** (`dealfluence/adeu`) | **MIT** | Python (62%) + TypeScript (32%); DOCX→Markdown→DOCX with tracked-change emission | Tracked changes + preserves existing comments + adds new comments | Production-ready, 34 releases, Claude Code plugin + MCP server + n8n node |
| **drpedapati/docx-review** | **MIT** | C# / .NET 8 / Open XML SDK / Docker | Comments + tracked changes via JSON manifest; multi-run text matching; semantic diff | Active (14 releases, latest May 2026); CLI binary not library |
| **pablospe/docx-editor** | Python | Track changes + comments without Word | Less prominent, Python-only |
| **balalofernandez/docx-revisions** | Python | `<w:ins>`/`<w:del>` extension to `python-docx` | Library only, Python |

### Architectural patterns observed

1. **OOXML-native vs HTML round-trip** — emerging consensus in 2026 is **OOXML-native**. SuperDoc and DOCX Editor both reject the HTML round-trip path explicitly. The reason: HTML lossily strips `w:rsid`, comment ranges, revision metadata, sectPr details, list numbering. OOXML-native parsers map directly to a ProseMirror/Lexical-compatible schema and serialize back to canonical OOXML.
2. **Where the parse runs**:
   - SuperDoc, DOCX Editor: **browser** (also headless Node.js for agent mode).
   - adeu: **server**.
   - drpedapati/docx-review: **server (CLI)**.
   - **For Spaarke BFF the natural placement is server-side via Open XML SDK**, parallel to drpedapati.
3. **Anchoring across edits** — the hard problem. Approaches:
   - **CRDT (Yjs)** — SuperDoc / DOCX Editor track positions through CRDT identifiers; comment anchors are CRDT-anchored, then projected back to `commentRangeStart`/`End` on serialize. Survives concurrent edits.
   - **Atomic edit transactions** — adeu's approach: edits are emitted as a sequence of atomic operations (replace/insert/delete) against the *original* text, applied in order. No anchoring across drift because the diff is committed in one shot.
   - **Multi-run text matching** — drpedapati matches the target text within paragraph runs even when split across `<w:r>` boundaries. This is the *minimum* correctness bar for the SDK approach.

### Known gotchas (cited from the open-source projects and Microsoft docs)

1. **Word's "undocumented quirks"** — explicitly called out by SuperDoc founders on HN: OOXML spec is ~6,000 pages, but Word silently rejects/repairs patterns the spec allows. Empirical iteration with real Word is unavoidable.
2. **Comment-on-tracked-change interaction** — pandoc issue #9833 documents that comments anchored *inside* a tracked-change insertion are mishandled by some tools. Must apply comments **before** track changes (drpedapati explicitly does this in their pipeline).
3. **Paragraph-boundary track changes** — `<w:del>` of an entire paragraph requires deleting the paragraph mark `<w:pPr>` (`w:del` inside `w:rPr` inside `pPr`). Inline `w:del` is straightforward; paragraph delete is the edge case that breaks.
4. **Revision metadata fidelity** — `w:author`, `w:date`, `w:id` must be unique and monotonic. SDK does not auto-assign; you must track max-id across the document.
5. **Round-trip after Word accept/reject** — when Word accepts/rejects, the resulting DOCX has the wrapper elements stripped. The web editor's representation of "outstanding suggestions" must reconcile against the new DOCX state (i.e., suggestions persisted in the editor must be revalidated as still present in OOXML after a Word round-trip).

### Companies confirmed doing this (with sources)

- **SuperDoc / superdoc-dev** — confirmed in their own GitHub + HN launch (item 48228411). Public engineering content. Target: any product that needs to embed DOCX editing in-browser.
- **Eigenpal** (the original of `mhurhangee/docx-editor`) — went dark June 2026; archived fork preserves the work. **Caveat**: company may be defunct.
- **dealfluence** (adeu) — DOCX↔LLM translator pitched at AI-agent document editing.
- **Litera, Spellbook, Kira, ContractPodAi, Ironclad, LinkSquares**: I found **no public engineering writeups** describing their internal DOCX comment/track-changes plumbing. Marketing pages describe outcomes (redlines, AI suggestions in Word) but not implementation. Treat these as "we know they ship the outcome, we do not know how."
- **TipTap (ueberdosis)** — has DOCX import/export but **not full tracked-changes round-trip**. Their own docs note "content added via suggestion mode is not included in the imported ProseMirror document." Their commercial Conversion product handles DOCX but the suggestion/comment round-trip side is partial.

### Architectural lesson for Compose R2

The most defensible pattern in the open-source landscape (2026) is:
1. **Server-side OOXML parse/serialize** (Open XML SDK + Codeuctivity.OpenXmlPowerTools).
2. **Atomic edit-transaction model** for agent/AI edits → tracked changes (adeu pattern; drpedapati's JSON manifest).
3. **CRDT-anchored comments** if and only if the web editor surface is collaborative; otherwise stable run-relative anchors are enough.
4. **Pre-stage comments before track changes** (drpedapati ordering rule).

---

## Q3: SharePoint Embedded Implications

### Comment / track-changes API surface

- **No SPE-native comment API for DOCX semantic comments.** SPE / Graph driveItem exposes no `/comments` collection for the in-document `w:comment` elements. Comments authored in Word for Web on a SPE-hosted DOCX are persisted **inside the DOCX bytes** as `w:comment` (single source of truth, no parallel SPE metadata store). *Confidence: high* — derives from Word for Web saving back through WOPI to the same storage the file lives in, and the DOCX format spec.
- **Track changes**: same story — stored as `w:ins`/`w:del` inside the DOCX bytes. SPE has no separate revision model.
- **SPE preview API does surface "comments and sticky notes" but only for PDFs** ("native PDF viewing experience now supports viewing comments and sticky notes embedded on the file" — Microsoft Learn driveItem: preview). Not relevant to DOCX.
- **Practical consequence for Compose R2**: there is exactly **one** integration surface for DOCX comments + track changes — the DOCX byte stream. We do not have to mirror state into SPE-side metadata.

### Version / change detection

- **Webhooks on driveItem updates work for SPE.** Microsoft Learn "Using Webhooks" tutorial (updated 2026-02-04) confirms the pattern:
  - `POST /v1.0/subscriptions` with `changeType: "updated"` and `resource: "drives/{ContainerId}/root"`.
  - Max subscription lifespan: **4230 minutes** (~70 hours) — must renew.
  - Notification payload contains `driveId` (= containerId here) but does **not** name the specific item changed.
  - The standard pattern is **webhook → delta query** (`/drives/{id}/root/delta`) to get the list of items that changed since the last cursor.
- **Permissions**: webhook subscription needs the standard Graph permissions plus, for SPE, `FileStorageContainer.Selected` and container-type permissions.
- **Verdict**: Webhook + delta is the right "user came back from Word with edits" detection path.

### Concurrency with Word for Web

- **`POST /drives/{id}/items/{itemId}/checkout`** is supported on SPE driveItems with `FileStorageContainer.Selected` plus container-type permissions (Microsoft Learn "driveItem: checkout"). 204 No Content on success. Companion `:/checkin`.
- **The known sharp edge** (not SPE-specific, applies to all driveItem-on-Graph use): when a user has the file open in Word Desktop / Word for Web with an active editing session, programmatic `PUT /content` returns **HTTP 423 Locked**. Graph exposes **no API to check lock state in advance** — you discover it on attempt. (Microsoft Q&A confirmed.)
- **Coauthoring case**: if Compose checks out a DOCX (programmatic check-out) and a user tries to open it in Word for Web, the user gets read-only / can't save. If we don't check out and a user opens it in Word for Web while we try to PUT bytes, we hit 423.
- **For Compose R2 this matters**: any "the user is editing in Word" path conflicts with any "Compose is writing tracked changes to the bytes" path. We need a state machine. Likely:
  - Compose locks (checkout), writes redlines, checks in.
  - Word for Web open from that point sees the redlines and the user accepts/rejects.
  - On save (Word for Web autosaves continuously), our webhook fires → delta → we re-read the DOCX and reconcile.

### Comment storage location

- **In-DOCX**, as `w:comment` inside `word/comments.xml` (part `WordprocessingCommentsPart`). Confirmed by ECMA-376 + Microsoft Learn how-to "Insert a comment into a word processing document".
- **Word for Web @mentions in comments** (Office 365 IT Pros): these are still stored in `w:comment` but with `w15:personId` references. Spaarke should not need to consume the mention payload for v1, but be aware the comment XML may carry these extensions.

### SPE-specific constraints on programmatic DOCX modification

- **Container-type permissions are mandatory.** Beyond Graph delegated/app permissions, the app's container-type permission set governs what operations are allowed (read-only vs read-write). The boilerplate-csharp `MSGraphService.excerpt.cs` (curated in `knowledge/sharepoint-embedded/`) shows the exact wire shape.
- **No SPE-specific document-rewrite block** beyond standard driveItem lock/coauthor semantics.
- **File-locking semantics**: the standard `etag` / `If-Match` pattern works. PUTs without `If-Match` may race with Word for Web autosave. **Recommendation**: always pass `If-Match` from the most recent GET when writing programmatic redlines, and handle 412 Precondition Failed → re-read → re-apply.

### Webhooks summary

| Aspect | Value |
|---|---|
| Endpoint | `POST /v1.0/subscriptions` |
| Resource | `drives/{containerId}/root` |
| changeType | `updated`, `created`, `deleted` (separate subscriptions if multiple needed) |
| Notification payload | Contains driveId; does NOT name changed items |
| Discovery pattern | Webhook → `/drives/{id}/root/delta?token=…` to enumerate changed items |
| Max lifespan | 4230 minutes (~70 hours) |
| Renewal | App responsibility |

---

## Calibration

### High confidence
- Open XML SDK is MIT, .NET 8, actively maintained, supports comments + track changes — directly from `github.com/dotnet/Open-XML-SDK` LICENSE + NuGet + Microsoft Learn how-tos.
- OpenXmlPowerTools original archived but MIT-licensed forks live and active.
- SuperDoc, DOCX Editor, adeu, drpedapati exist as cited with the licenses listed (verified from their GitHub READMEs / WebFetch).
- SPE webhook flow and 4230-minute lifespan from Microsoft Learn (updated 2026-02-04).
- `driveItem: checkout` supported for SPE with `FileStorageContainer.Selected` + container-type permissions (Microsoft Learn).
- HTTP 423 Locked on programmatic PUT when Word desktop/web has active session (Microsoft Q&A, multiple corroborating posts).

### Medium confidence
- Word for Web persists `w:comment` directly into DOCX bytes with no parallel SPE metadata mirror. **Confidence: medium-high** — derived from WOPI architecture + DOCX spec + the lack of any `/comments` driveItem endpoint, not from an explicit Microsoft architecture statement. Worth verifying empirically.
- Word's silent rejection of certain OOXML patterns (the "undocumented quirks" claim) — cited from SuperDoc founders on HN, broadly believed in the OOXML community but not pinned to a Microsoft doc.
- Companies like Litera, Spellbook, Kira's exact implementation — **inferred** that they use some variant of Open XML SDK / OpenXmlPowerTools server-side, but no public engineering writeup found.

### Open questions remaining
1. **Does SPE expose item-level webhooks?** (Current tutorial subscribes at the container/root level.) If we want per-item notifications, is `drives/{id}/items/{itemId}` supported? Not confirmed.
2. **Behavior when Compose check-out collides with Word for Web active session**: does SPE serialize and queue, or does the user's Word for Web get an error? Worth a spike.
3. **Delta query cost/limits at high write volume**: undocumented in the SPE tutorial.
4. **Long-form `w15:` extension elements** (modern comments, threaded replies) — does the Open XML SDK 3.x have strongly-typed coverage for `w15:commentEx`? Not verified; may require raw XML.
5. **TipTap commercial conversion + SPE**: TipTap's DOCX conversion is partial on track changes. If we ever picked TipTap for the editor surface, we'd be doing our own OOXML emit anyway.

---

## Recommendation

**For Compose R2 design lock-in:**

1. **Adopt `DocumentFormat.OpenXml` 3.x in the BFF for all DOCX read/write** — confirmed MIT, .NET 8, first-class comments + track changes. Pair with `Codeuctivity.OpenXmlPowerTools` (active fork) or `Clippit` for diff/redline if we need "given old + new produce redlined DOCX."
2. **Server-side parse/serialize, not browser**. Spaarke already runs the BFF; placing OOXML logic in browser duplicates the Open XML SDK in TypeScript, which is what SuperDoc/Eigenpal had to build at significant cost. We do not need to ship a browser-side OOXML engine for v1.
3. **Comment + track-change emission pipeline** (drpedapati pattern):
   - Receive JSON manifest of edits from the web editor or AI agent.
   - Apply comments first (before structural XML changes).
   - Apply tracked changes second (`InsertedRun`/`DeletedRun` with author/date/id).
   - Write back via Graph driveItem `PUT /content` with `If-Match` etag.
4. **Word-for-Web round trip detection** via SPE webhook on `drives/{containerId}/root` + delta query, with subscription renewal every <4230 min.
5. **Concurrency**: design for checkout/checkin around AI-driven redline operations; do not race with Word for Web autosave. Plan a state machine.

### Critical spike items before R2 design locks

- **SPIKE-1**: Smallest-possible end-to-end: SDK creates DOCX with `w:ins`+`w:comment` → upload to SPE container → open in Word for Web → verify both render natively. (Half-day.)
- **SPIKE-2**: Reverse direction — Word for Web user adds comment + track-change → webhook fires → BFF downloads → Open XML SDK reads them out with correct author/date. (Half-day.)
- **SPIKE-3**: Concurrency probe — Compose checks out while a user has Word for Web open. What does the user see? Document it. (Half-day.)
- **SPIKE-4**: Etag race — Compose writes redlines without checkout while Word for Web is autosaving. What's the 423/412 pattern? (Half-day.)
- **SPIKE-5**: `w15:commentEx` (modern threaded comments) — does Open XML SDK 3.x have strongly-typed coverage, or do we need raw XML? (Half-day.)

If any of SPIKE-1/2/3 surface blockers, R2 architecture should be revisited before committing.

---

## Sources

### Primary (Microsoft)
- [github.com/dotnet/Open-XML-SDK](https://github.com/dotnet/Open-XML-SDK) — LICENSE (MIT) + release history
- [NuGet DocumentFormat.OpenXml 3.x](https://www.nuget.org/packages/documentformat.openxml) — .NET 8 target confirmation
- [Microsoft Learn: Insert a comment into a word processing document](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-insert-a-comment-into-a-word-processing-document)
- [Microsoft Learn: Accept all revisions in a word processing document](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-accept-all-revisions-in-a-word-processing-document)
- [Microsoft Learn: CommentRangeStart / CommentRangeEnd / CommentReference / DeletedRun / InsertedRun classes](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.commentrangestart)
- [Microsoft Learn: SharePoint Embedded — Using Webhooks](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/development/tutorials/using-webhooks) (updated 2026-02-04)
- [Microsoft Learn: driveItem: checkout](https://learn.microsoft.com/en-us/graph/api/driveitem-checkout?view=graph-rest-1.0)
- [Microsoft Learn: Create subscription (change notifications)](https://learn.microsoft.com/en-us/graph/api/subscription-post-subscriptions)

### Secondary (open-source landscape)
- [github.com/superdoc-dev/superdoc](https://github.com/superdoc-dev/superdoc) — AGPL-3.0; OOXML-native browser+server editor
- [HN: SuperDoc launch discussion](https://news.ycombinator.com/item?id=48228411) — founder commentary on quirks
- [github.com/mhurhangee/docx-editor](https://github.com/mhurhangee/docx-editor) — Apache-2.0; archived Eigenpal fork
- [github.com/dealfluence/adeu](https://github.com/dealfluence/adeu) — MIT; atomic-edit-transaction pattern
- [github.com/drpedapati/docx-review](https://github.com/drpedapati/docx-review) — MIT; .NET 8 + Open XML SDK reference implementation
- [github.com/Codeuctivity/OpenXmlPowerTools](https://github.com/Codeuctivity/OpenXmlPowerTools) — MIT; active cross-platform fork
- [Clippit](https://sergey-tihon.github.io/Clippit/) — modern .NET fork
- [pandoc issue #9833 — comments on tracked-changes insertions](https://github.com/jgm/pandoc/issues/9833) — concrete gotcha
- [Open-XML-SDK issue #1817 — track changes pattern](https://github.com/dotnet/Open-XML-SDK/issues/1817)
- [Open-XML-SDK discussion #1187 — delete paragraph with tracking](https://github.com/dotnet/Open-XML-SDK/discussions/1187)
- [TipTap Legacy DOCX import/export](https://tiptap.dev/docs/conversion/legacy/overview) — partial coverage caveats

### Local knowledge base
- `c:/code_files/spaarke/knowledge/sharepoint-embedded/SOURCE.md` — SPE samples + curated Microsoft Learn snapshots (2026-05-14)
- `c:/code_files/spaarke/knowledge/sharepoint-embedded/docs/learn-overview.md`, `learn-containertypes.md`
