# Adeu Architecture Study — Patterns for Spaarke Compose

**Date**: 2026-06-29
**Repo studied**: `github.com/dealfluence/adeu` @ main (shallow clone, ~13k LOC TypeScript core + parallel Python port)
**License**: MIT — patterns extractable, code is reference-only
**Reading basis**: Read top-level docs in full (README, AI_CONTEXT.md ~700 lines, ARCHITECTURE.md, spec.md, FREE_AGENT_PROBLEM.md, search_and_targeted_write.md, the entire `skills/adeu-redlining/` directory). Read in full: `node/packages/core/src/models.ts` (52 lines), `markup.ts` (441 lines), `comments.ts` (470 lines), `engine.ts` lines 1-2300 of 3021 (~75%), `mapper.ts` lines 1-1020 of 1092 (~95%), `docx/bridge.ts` (245 lines), `ingest.ts` (~120 lines of 597), `domain.ts` (~170 of 365), `mcp-server/src/index.ts` tool registrations, `mcp-server/src/tools/email.ts` (~750 lines). Skimmed remaining engine.ts (auxiliary helpers + table edits) and the Python port (parity confirmed via AI_CONTEXT.md "Make Both Perfect" principle).

## Executive Summary

Adeu is a production "Virtual DOM for DOCX" — an LLM ↔ Word translation layer that ingests `.docx` into Markdown with [CriticMarkup](https://fletcher.github.io/MultiMarkdown-6/syntax/critic.html) for the LLM, accepts the LLM's plain-text edits, and writes back native Word `<w:ins>`/`<w:del>` track changes plus `<w:comment>` margin comments. The team has parallel TypeScript (`@adeu/core`) and Python (`adeu`) implementations they keep in lockstep via a "Make Both Perfect" principle, ship as MCP servers (Claude Desktop, Cursor, Windsurf, Gemini CLI, Smithery), and also expose as a Claude Code plugin/Agent Skill, an n8n community node, a LangChain toolkit (WIP), and a CLI. The README claims production use and the AI_CONTEXT.md reads like a battle-scarred operations log.

The single highest-leverage idea: **strict input/output asymmetry around CriticMarkup**. The LLM **reads** CriticMarkup (so it can see existing redlines/comments inline), but it MUST NOT write CriticMarkup — instead it submits plain `target_text`/`new_text` pairs and the engine generates the OOXML revisions. This collapses the LLM's job from "produce valid OOXML edit deltas" to "find-and-replace text," which is the prompt-task LLMs are best at. A close second idea: the **batch-evaluates-against-original-state** rule with **ambiguity-as-error-with-resolution-hints** — the engine refuses ambiguous matches and tells the LLM exactly how to disambiguate (`match_mode: "strict"|"first"|"all"`), turning what would be a 5-turn correction loop into a 1-turn fix.

The architecture worth adopting wholesale for Compose: the wire format (CriticMarkup for read, structured DocumentChange union for write), the validation gate's specific refusal categories, the reverse-order batched-apply for index stability, and the dual-mapper "clean view vs raw view" extraction model. The architecture **not** worth copying directly: the Markdown-as-intermediate is appropriate for chat-only agents, but Compose is a rich-text editor — we already have ProseMirror/TipTap nodes that ARE the LLM-friendly representation. The interesting question is whether to project our editor's JSON to CriticMarkup or directly to OOXML annotations.

The Outlook/email finding is significant: **adeu has Outlook integration, but only via a paid cloud backend (Adeu Cloud) that returns sanitized HTML→Markdown bodies + attached DOCX files**. The OSS code never parses `.msg`/`.eml`/MIME natively. Email is a *retrieval/dispatch wrapper around DOCX manipulation*, not a separate edit domain. For Spaarke this is a useful negative result.

## Repository Map

```
adeu/
├── README.md                 — User-facing tool description and install paths
├── AI_CONTEXT.md             — ~700-line engineering operations log; the most valuable single file in the repo
├── docs/
│   ├── ARCHITECTURE.md       — One-page "Virtual DOM / Reconciler Pattern" justification
│   ├── spec.md               — Technical spec; identifies engine.py, mapper.py, ingest.py as the load-bearing 3
│   ├── spec-table-edits.md   — Table row insert/delete OOXML refresher
│   ├── spec-undo-record.md   — Undo log spec (not deeply read)
│   ├── FREE_AGENT_PROBLEM.md — Position essay about LLM agent containment (general AI safety, not adeu-specific)
│   ├── MCP_APPS_PROTOCOL.md  — UI rendering via MCP iframe + postMessage
│   └── QA_ISSUES_DISCOVERED.md — Bug list discovered during testing (not read in this pass)
├── python/src/adeu/          — Python implementation: server.py (FastMCP), cli.py, ingest.py, markup.py, diff.py
│   ├── redline/              — engine.py, mapper.py — the core
│   ├── sanitize/             — Author-metadata scrubbing for finalization
│   ├── mcp_components/tools/ — MCP tool implementations: document, email, live_word (Windows COM), sanitize, validation
│   └── templates/            — HTML for MCP Apps UI
├── node/
│   ├── packages/core/        — TypeScript port; identical surface to Python
│   │   └── src/              — models.ts (DocumentChange union), markup.ts (CriticMarkup builder),
│   │                           mapper.ts (text↔run index), engine.ts (apply pipeline, 3021 lines),
│   │                           comments.ts (Modern Comments 4-part XML), ingest.ts (DOCX→text),
│   │                           domain.ts (defined-terms appendix), pagination.ts, outline.ts,
│   │                           sanitize/, docx/ (bridge.ts = OPC package layer over fflate+xmldom)
│   ├── packages/mcp-server/  — MCP server wiring; email tools, auth, UI templates
│   └── packages/n8n-nodes-adeu — Visual workflow node
├── skills/adeu-redlining/    — Open Agent Skill; SKILL.md + references/ (criticmarkup.md, mcp-tools.md, cli-fallback.md)
├── langchain/                — WIP LangChain toolkit (pre-PyPI)
├── desktop-extension/        — Claude Desktop .mcpb packaging
└── ecosystem/                — Vendor integration policy + placeholder for third-party wrappers
```

The README's "intelligent proxy" framing — **Read → Validate → Apply** — maps directly to the directory layout: `ingest.py`/`ingest.ts` (Read), the validators at the top of `engine.ts` plus `mapper.find_*` (Validate), and the `RedlineEngine.process_batch`/`apply_edits` paths (Apply).

## Pattern 1: CriticMarkup Wire Format Handling

**The asymmetry rule** (`skills/adeu-redlining/SKILL.md` lines 62-64; enforced in `node/packages/core/src/engine.ts` lines 161-170): LLMs **read** CriticMarkup syntax (`{++ins++}`, `{--del--}`, `{==anchor==}{>>comment<<}`, `{~~old~>new~~}`) from `read_docx` output but MUST NOT write it back. The validator inside `validate_edit_strings()` refuses any `new_text` containing `{++`, `{--`, `{>>`, or `{==` with an explicit error telling the LLM to use the separate `comment` field on the edit. This is the single most important behavioral primitive in the design.

**CriticMarkup builder** (`node/packages/core/src/markup.ts` `_build_critic_markup`, lines 232-284): the engine generates the markup AFTER trimming common prefix/suffix between target and new (`trim_common_context` in `diff.ts`) so that "John Smith" → "John Smyth" produces `{--Smith--}{++Smyth++}` rather than `{--John Smith--}{++John Smyth++}` — much smaller diffs in the LLM's view of the document. This minimization is critical for token cost on large documents.

**Markdown style markers as virtual characters**: `_strip_balanced_markers` (`markup.ts` lines 24-44) handles the case where target text is wrapped in `**bold**` or `_italic_`. It strips the markup before redlining (so you get `**{--Old--}{++New++}**` not `{--**Old**--}{++**New**++}`) and re-wraps. The Markdown markers are *virtual* — they don't exist as physical characters in the DOCX, they're projected by `ingest.py` from `<w:b/>` / `<w:i/>`. The mapper tracks them as `virtual` spans (AI_CONTEXT.md §3 "Virtual Text Contract") so the engine knows not to try to delete them.

**Smart-quote normalization** (`markup.ts` `_replace_smart_quotes`, lines 46-52): `"`/`"` and `'`/`'` are normalized to ASCII before matching. This catches the common LLM failure mode where the model copies text with curly quotes but the document has straight quotes (or vice versa).

**Fuzzy regex matching** (`markup.ts` `_make_fuzzy_regex`, lines 137-195): when an exact substring fails, the engine builds a tolerant regex that allows Markdown noise (`[*_]*`) and structural noise (list markers, paragraph breaks) at every token boundary of the target. The TS implementation explicitly avoids Python's atomic groups `(?>...)` because JS regex has no equivalent, and notes the catastrophic-backtracking risk it dodges by using character classes instead.

**Read-only structural projections** that look editable but aren't (`engine.ts` lines 172-256): `[^fn-id]` (footnotes), `[~text~](#_Ref)` (cross-references), `{#_BookmarkName}` (bookmarks), `[text](url)` (hyperlinks). The validator pre-counts these in target_text and new_text — if the counts differ, the edit is rejected with a clear reason ("Cannot insert footnote markers via text replace"). For hyperlinks, *changing the URL* is allowed silently (no redline emitted, just a rels rewrite) — this is the kind of distinction documented in `references/criticmarkup.md`.

## Pattern 2: DOCX Read/Write — Comments, Track Changes, and the OOXML Layer

**Comments-before-track-changes ordering, per AI_CONTEXT.md and `engine.ts` `_apply_single_edit_indexed`** (lines 2540-2700+): the engine first computes the `del_id` and `ins_id` for the tracked change, then attaches the comment markers (`<w:commentRangeStart>` / `<w:commentRangeEnd>` / `<w:commentReference>`) BEFORE wrapping content in `<w:ins>`/`<w:del>`. The comment anchors are SIBLINGS of the ins/del wrappers, not children — and the engine has helper `ascend_to_paragraph_child` (line 2495) specifically to find the right level to anchor at when the first/last run lives inside a tracked-revision wrapper.

**Modern Comments four-part architecture** (`comments.ts`): Word 2021+ stores comments across FOUR XML parts: `comments.xml` (the text), `commentsExtended.xml` (`w15:done` resolved flag, `w15:paraIdParent` for threading), `commentsIds.xml` (durable cross-machine IDs), `commentsExtensible.xml` (extensibility metadata). The `CommentsManager` class creates all four parts on first write, with explicit namespace declarations and rels wiring. Threading is implemented by `w15:paraIdParent` referencing the root comment's `w14:paraId`. This is the most underdocumented OOXML area — and adeu's implementation is the clearest reference I've seen in any OSS code.

**Empty-part lifecycle gotcha** (AI_CONTEXT.md §8 "Empty Comment Part Lifecycle"): empty comment parts are intentionally LEFT in place when all comments are removed, because deleting them across python-docx versions causes package corruption. The deletion path goes only as far as removing individual `<w:comment>` elements and their cross-references in the three companion parts.

**ID volatility** (`engine.ts` `_scan_existing_ids`, lines 424-433; `mapper.ts` `renumber_snapshot_ids`, lines 29-106): `Chg:N`/`Com:N` IDs are reassigned on every save. The mapper has an explicit renumbering pass that walks the document body for `w:ins`/`w:del`, then the comments part for `w:comment`, then re-syncs `w:commentReference`/`w:commentRangeStart`/`w:commentRangeEnd` IDs. The skill prompt drills this rule into the LLM: "ALWAYS call `read_docx` immediately before any batch that contains `accept`/`reject`/`reply`" (mcp-tools.md line 102).

**Paragraph-boundary deletion** is the documented hardest case (AI_CONTEXT.md §11 "Phase 2 OOXML Paragraph Merges"). When a `<w:del>` spans paragraph boundaries, the engine must coalesce adjacent paragraphs in REVERSE order (bottom-up) over `virtual_spans`, traversing DOM sibling pointers and jumping over invisible structural nodes (`w:bookmarkStart`, `w:bookmarkEnd`) without losing the parent reference. The "Safe Paragraph Acceptance" rule (same section): when accepting paragraph-break deletions, check whether visible content survives — if yes, strip only the `<w:del>` from `<w:pPr><w:rPr>` and preserve the paragraph container; if no, delete the whole paragraph.

**Run coalescing as a stability mechanism** (`engine.ts`; AI_CONTEXT.md §2 "Run Coalescing"): Word splits runs arbitrarily (spellcheck breaks "Agreement" into `<w:r>Agree</w:r><w:r>ment</w:r>`). The engine merges adjacent runs with identical styling before any edit operation to keep the diff clean. The rule: never merge a run containing `<w:br>`, `<w:tab>`, `<w:commentReference>`, or `<w:drawing>` — these are "immutable boundaries" because merging them destroys the special tag.

**OOXML serialization quirks**: `engine.ts` line 310-318 injects `xmlns:w16du` namespace at document root on EVERY load because lxml otherwise generates `ns0` aliases that corrupt downstream processors. The "Surgical Mode" (AI_CONTEXT.md §2): the engine never calls `normalize_docx` on init or save, never reflows whitespace via python-docx's `serialize_for_reading()` — it uses raw lxml `etree` with `pretty_print=False` and `remove_blank_text=False` to preserve Word's exact whitespace and produce minimal diffs.

## Pattern 3: Pattern-Based Text Anchoring + Validation Gate

**The fundamental find-and-replace primitive** (`mapper.ts` `find_all_match_indices`, lines 839-885): cascading fallback strategy with four levels:
1. Exact substring on the full projected text
2. Smart-quote-normalized substring
3. Markdown-formatting-stripped substring (so `**bold**` matches plain "bold")
4. Fuzzy regex (the `_make_fuzzy_regex` from `markup.ts` Pattern 1, allowing `[*_]*` between every token)

This is run against TWO mappers in sequence: `mapper` (raw view with CriticMarkup virtual chars) and `clean_mapper` (`new DocumentMapper(this.doc, true)` — accepted-state view). The engine tries the raw mapper first; on zero matches, it falls through to the clean mapper. This means an LLM can address text either with or without seeing the pending track-changes, and the system reconciles.

**The validation gate** (`engine.ts` `validate_edit_strings`, lines 153-291): runs as a single pass before ANY edit is applied. The refusals, in order:
- CriticMarkup syntax in `new_text` (per Pattern 1)
- Footnote markers (`[^fn-N]`) inserted/deleted (must use Word's References menu)
- Hyperlink markers (`[text](url)`) — only one editable per replace; counts must balance
- Cross-reference markers (`[~text~](#_Ref...)`) — strictly immutable
- Internal anchor markers (`{#_BookmarkName}`) — strictly immutable
- Heading depths > 6 (Markdown has 6, Word has 9 but renders 7-9 as inline; rejecting prevents silent broken styles)
- Read-only boundary text (`READONLY_BOUNDARY_START`, the structural appendix marker)

Each rejection includes the **edit index** (`Edit ${i + 1}`) so the LLM knows which item failed. This is small UX but high-value: in a batch of 12 edits, "Edit 7 Failed: heading depth 8 not supported" lets the LLM resubmit only Edit 7 instead of guessing.

**Ambiguity-as-error-with-resolution-hints** (`markup.ts` `format_ambiguity_error`, lines 375-441): when `match_mode: "strict"` (default) hits N>1 matches, the engine returns NOT a generic "ambiguous" error but a structured message: total match count, first 5 occurrences with 50 chars of pre/post context, and **literal text the model can copy** ("To resolve, re-send this edit using ONE of these strategies: 1. Set `match_mode: 'all'`...; 2. Set `match_mode: 'first'`...; 3. Provide more surrounding context..."). Comment from the code: "Without this, agents loop forever refining target_text/regex because they never learn that match_mode is the built-in escape hatch."

**The `_nearest_match_hint` heuristic** (`engine.ts` lines 341-376): when a regex target fails, the engine strips `^`/`$` anchors and `\(`/`\)` escapes and probes for a literal — if found, returns "Did you mean the literal '...'? It appears in the document. If you used a regex, drop the ^/$ anchors — they match the start/end of the ENTIRE document, not a line." The comment explains: "the common loop trap (observed in the field) is an anchored regex like `^\( x \)$` against a mid-document string." This is the kind of empirical tuning that's worth more than any single-shot benchmark.

**Three-tier match-mode resolution strategy** (`models.ts` `ModifyText`; documented in `search_and_targeted_write.md`):
- `strict` (default): N>1 raises BatchValidationError. Safest, requires LLM to provide enough context.
- `first`: linear-order first occurrence in the flat projected text — explicit opt-in.
- `all`: every occurrence; if even one match falls in a safety-blocked zone (foreign-author redline overlap), entire batch fails.

The `references/criticmarkup.md` repeatedly tells the LLM "**`target_text` must be unique by default**. Either add surrounding context, or explicitly set `match_mode`." This pushes the safety-vs-convenience decision to the LLM as a deliberate parameter rather than the engine silently picking.

## Pattern 4: Semantic Appendix Generation (Domain Visibility)

The `read_docx` tool projects a **structural appendix** below the body content, behind a `<!-- READONLY_BOUNDARY_START -->` marker (`domain.ts` and pagination.ts). It contains:

**Defined Terms** (`domain.ts` `extract_all_domain_metadata`, lines 45-180): extracted by *typography*, not English regex. The patterns:
- `leading_re = /^(?:[\d.\-()a-zA-Z]+\s*)?["“]([A-Z][A-Za-z0-9\s\-&'’]{1,60})["”]/` — leading quoted term at paragraph start, optionally after a section number
- `inline_re = /\([^)]*?["“]([A-Z][A-Za-z0-9\s\-&'’]{1,60})["”][^)]*?\)/g` — inline `(the "Agreement")` definition pattern

Terms are then usage-counted across the body. Terms with zero uses are dropped. Duplicate definitions raise an `[Error] Duplicate Definition` diagnostic. The README and skill highlight this as a key "domain visibility" feature so the LLM scans defined terms before editing capitalized phrases.

**Typo candidates** (per AI_CONTEXT.md §13 "High-Signal Diagnostics"): bounded Levenshtein distance ≤2 against defined terms, grouped by target. Pruned by stop-word filter, singular/plural exclusion, and for short acronyms (≤5 chars) max distance 1 + identical first letter. The pure-TS bounded Levenshtein implementation (`domain.ts` lines 7-34) explicitly avoids the rapidfuzz C-bindings the Python side uses, with early-termination on row minimum > maxDist.

**Cross-references and bookmarks** (`domain.ts` lines 87-114): walks the DOM for `w:bookmarkStart` (records `_Ref...` anchors), then `w:fldSimple` and `w:instrText` parsing the `REF <name>` field instruction to map references → targets. Output is `Cross-Reference Targets` with `anchored_to` (the bookmark's paragraph snippet) and `referenced_from` (the referring paragraph snippets).

**Pagination over the flat outline** (per AI_CONTEXT.md §13 + `outline.ts`): heading page ranges are calculated post-pagination by walking the flat `OutlineNode` array forward to find the next node at or below current level — not by descending the DOM tree. This eliminates a class of bugs where nested tables or text boxes mis-calculated section boundaries.

**Boundary validation** (`engine.ts` lines 276-287): the boundary check uses *resolved physical indices* (`find_all_match_indices`), not string match — so body edits that coincidentally contain text appearing in the appendix are still allowed. Only edits whose resolved start_idx is INSIDE the appendix range are rejected.

## Pattern 5: Atomic Edit Batch Model

**Flat discriminated union** (`models.ts` lines 1-53): `DocumentChange = ModifyText | AcceptChange | RejectChange | ReplyComment | InsertTableRow | DeleteTableRow`. Each has a `type` literal. All fields are top-level (flat) — no nested parameter objects, because per AI_CONTEXT.md §6 nested params hurt LLM accuracy and break CLI argument parsing. The hidden internal fields (`_match_start_index`, `_resolved_proxy_edit`, `_internal_op`, `_active_mapper_ref`) are private state the engine writes during pre-resolution; they're not part of the LLM-facing schema.

**No pure-insert / pure-delete operations exposed** (AI_CONTEXT.md §6 "Search & Replace First"): the LLM cannot send "insert text X at position Y" or "delete text X". Everything is a `ModifyText` with `target_text` and `new_text`. Empty `new_text` = delete. Empty `target_text` is supported only for the cell-anchor case (`{#cell:<id>}`). The reasoning: requiring anchor text forces the LLM to provide context that the fuzzy matcher can verify — pure insertions have no anchor and degrade to hallucination.

**Reverse-order, pre-resolved application** (`engine.ts` `apply_edits`, lines 1921-2101; AI_CONTEXT.md §11): the algorithm has four phases:
1. **Pre-resolve**: walk all edits, call `_pre_resolve_heuristic_edit` for each, which resolves `target_text` against the (raw|clean) mapper and records `_resolved_start_idx`. This phase touches NOTHING in the DOM — it's read-only.
2. **Sort descending**: edits sorted by `_resolved_start_idx DESC` (largest first).
3. **Overlap-rejection**: maintain `occupied_ranges`; skip and report edits whose [start, end) overlaps an already-applied range.
4. **Apply bottom-up**: each edit calls `_apply_single_edit_indexed`, which directly mutates the DOM at the pre-resolved offset.

Because edits apply bottom-up, earlier-in-document edits don't shift the indices of later-in-document edits. The comment in `engine.ts` explicitly calls out the side-effect: "bottom-most edits receive lower sequential IDs like `Chg:1`."

**Transaction model + dry-run** (`engine.ts` `takeSnapshot`/`restoreSnapshot`, lines 95-128): the engine clones all XML parts (deep cloneNode), preserves `pkg.unzipped`, the per-part `rels` map, and `current_id`. The `process_batch` `dry_run: true` mode takes a snapshot, runs the whole pipeline, returns the report, and unconditionally restores. The "Simulated dry-run sequentially" path for wet runs (line 1846+) takes a snapshot, runs edits one-at-a-time validating each against the document state AFTER prior edits, and if any single edit fails sequential validation, rolls back the whole batch.

**Reply-to-comment threading** (`engine.ts` `apply_review_actions`, lines 2103-2196; `comments.ts` `addComment`, lines 202-292): replies are first-class edits (`type: "reply"`, `target_id: "Com:5"`, `text: "..."`). The engine creates a new `<w:comment>` with the reply text and links it to the parent thread via `w15:paraIdParent` (Modern Comments) or `w15:p` attribute (legacy fallback). Reply IDs are session-bound like all other Com:N IDs.

**Atomic rollback example** — the `process_batch` outer wrapper sanitizes potentially-string-serialized changes from "double-serializing" LLM clients (lines 1623-1642) BEFORE entering the apply pipeline. This is a defensive convergence pattern: the engine has seen Gemini and other clients send `changes: ["{\"type\":\"modify\",...}", "..."]` instead of parsed arrays. Rather than crashing on `change.type` access, it JSON.parses each string and falls through if parsing fails.

## Outlook / Email — Findings

**adeu has email integration. It is OUTSIDE the OSS DOCX engine and behind a paid cloud backend.**

What's in the OSS repo (`node/packages/mcp-server/src/tools/email.ts`, ~750 lines; mirrored in `python/.../tools/email.py`):
- `search_and_fetch_emails` — POSTs to `${BACKEND_URL}/api/v1/emails/search` with filters (sender, subject, has_attachments, attachment_name, is_unread, days_ago, folder, mailbox_address). The backend is "Adeu Cloud" — a closed service.
- `create_email_draft` — POSTs Markdown body + optional attachments to `${BACKEND_URL}/api/v1/emails/drafts/new`. The backend server-side renders Markdown → styled HTML with inlined CSS for email-client compatibility.
- `list_available_mailboxes` — lists Microsoft/Google linked accounts.
- An async task pattern: backend returns `task_id` → tool polls `/api/v1/emails/tasks/{taskId}` with 5s sleeps × 10 attempts.

What it does NOT do:
- No native parsing of `.msg` (Outlook binary) or `.eml` (RFC 822 / MIME). The backend handles that opaquely.
- No native MIME boundary handling, no MAPI, no IMAP/POP3 client.
- No email-thread reconstruction in OSS code — the backend returns pre-grouped `messages[]` arrays.

What IS in OSS (interesting):
- `stripTags` (lines 178-212): a manual HTML→text reducer that loops on `<style>/<script>/<head>/<title>` stripping until stable, converts block-level closing tags to newlines, then strips all remaining tags, decodes named + numeric HTML entities (`&nbsp;`, `&#1234;`, `&#xABCD;`), and collapses triple newlines to paragraph breaks. Explicit comment about "matches Python MLStripper's structural suppression."
- `removeNestedQuotes` (lines 214-296): **localized** quote-block stripping. Recognizes "From:"/"Sent:" Outlook quote headers in 9 languages (Finnish, Swedish, German, French, Spanish, Portuguese, Italian, Dutch, Norwegian/Danish), plus "Forwarded message" markers in 11 locales, plus Gmail-style "On X wrote:" / "Le X a écrit:" / "Am X schrieb:" patterns. Cuts the body at the earliest match.
- Short-ID cache pattern (lines 54-123): `msg_<6hex>` short IDs minted from MD5 of provider IDs, persisted to `~/.adeu/mcp_id_cache.json` with 1000-entry LRU. Raises `StaleShortIdError` if an ID misses the cache (with explicit "may have been evicted, or it came from a different machine" hint to the LLM). `adeu_<numeric>` IDs are server-side references that survive cross-machine.
- The `process_attachments` helper (lines 553-594): downloads attachments to `<working_dir>/adeu_attachments/<short_id>/<filename>` (or `<tmpdir>/adeu_downloads/...`), with size cap (`max_attachment_size_mb`, default 10), and **leaves the `local_path` populated on the structured response** so downstream tools can pick up the saved DOCX directly.

**The actual email-to-DOCX pipeline** (inferred from the assembled response in `email.ts` `search_and_fetch_emails`): backend returns full HTML body + sanitized markdown + array of attachments (with base64 payloads), the MCP tool saves each attachment to disk, builds a markdown response with "Attachments Saved Locally" sections, and ends with **"You can now use tools like `read_docx`, `diff_docx_files`, or `finalize_document` on the local file paths."** This is a *coordination prompt* — the tool actively suggests the next tools the LLM should call.

**Conclusion for Spaarke**: adeu has nothing to teach us about MIME or `.msg` parsing. What it DOES teach us:
1. Email-to-DOCX is a **discovery + dispatch** pattern, not a separate edit domain. The DOCX surface owns the editing; email tools just retrieve and stage.
2. Multi-locale "remove quoted history" is real engineering effort — not trivial. Outlook/Gmail emit quote blocks differently across language settings.
3. Short-ID cache pattern (MD5 hash → short token → persisted to disk → with explicit eviction error semantics) is a useful template for any large opaque-ID surface (Graph item IDs, container IDs).
4. The DOCX tools and email tools share the **same MCP server**, with email gated by `if (!isDocxOnly)` (line 710 of index.ts). For Spaarke, this argues for unifying the artifact surfaces in one BFF facade rather than fragmenting per format.

## AI Integration Patterns

### 6.1 Prompt Engineering

**Where the prompts live**: `skills/adeu-redlining/SKILL.md` (the agent skill's "system prompt") + `references/criticmarkup.md`, `references/mcp-tools.md`, `references/cli-fallback.md` (loaded by the LLM only when relevant). Also the MCP tool **descriptions** themselves (e.g., `PROCESS_BATCH_OPERATIONS_DESC` in `mcp-server/src/index.ts` lines 86-89 is ~1500 characters of natural-language guidance baked into the MCP `description` field).

**Tool-description-as-prompt** (`mcp-server/src/index.ts` lines 86-89 quoted from real code): the description for `process_document_batch` doesn't just describe parameters — it tells the LLM the *philosophy* and *pitfalls*:
- "All changes evaluate against the ORIGINAL document state — do not chain dependent edits within one batch."
- "Each item in `changes` must specify a `type`: 1. 'modify': Search-and-replace. By default `target_text` must match uniquely (`match_mode`:'strict')..."
- "ID VOLATILITY: 'Chg:N' and 'Com:N' shift between document states. Always call `read_docx` immediately before any accept/reject/reply..."

Each rule is *behavioral*, not syntactic. The schema (Zod) covers types; the description covers when-to-use.

**The skill's "Execution path — pick once, then forget"** (SKILL.md lines 28-40): explicit cascading instruction: "1. MCP tools available → use them. 2. Bash available, no MCP → shell out to `uvx adeu`. 3. Neither → tell the user." With "Do not present these as options to the user mid-task. Pick the available path and proceed." This forecloses the failure mode where the LLM dithers asking the user which approach to use.

**Critical gotchas section** (SKILL.md lines 52-76): nine numbered "These are environment-specific facts that will trip you up if you assume Word behaves like plain text. Read this section every time." Each gotcha is a single sentence + 2-3 sentence elaboration. Reads like a postmortem distilled into prompt copy.

**Tactical anti-patterns**: SKILL.md and the `mcp-tools.md` reference both repeat "**Do not write CriticMarkup tags manually**" multiple times in different contexts. This is the highest-impact rule — repetition is deliberate.

**Token-budget management** built into `read_docx`: pagination (default 1 page) + outline mode + search filters. `references/mcp-tools.md` lines 22-23 instruct: "for any document longer than a few pages, the first call should be `mode='outline'`. Read the headings, decide which page or section to read in full, then call again with that page number." Cost-vs-coverage tradeoff pushed onto the LLM as a documented protocol.

### 6.2 Tool/Function Schemas

**Five surface tools** (consistent across Python + Node MCP servers):
1. `read_docx` — read with mode/page/outline/search params; returns Markdown+CriticMarkup
2. `process_document_batch` — apply the DocumentChange list; supports `dry_run`
3. `accept_all_changes` — convenience for "finalize"
4. `diff_docx_files` — custom `@@ Word Patch @@` sub-word diff (NOT unified diff)
5. `finalize_document` — strip metadata, lock document, prepare for distribution

Plus auth tools (`login_to_adeu_cloud`, `logout_of_adeu_cloud`) and email tools when cloud is enabled.

**Zod schemas with `.describe()` annotations** (`mcp-server/src/index.ts` ~line 728): each Zod field carries a human-readable `.describe()` string — these flow into the MCP tool schema and appear in the LLM's tool list. Example: `max_attachment_size_mb: z.coerce.number().optional().describe("Maximum attachment size in MB to download (default 10). Attachments larger than this are listed in the response but not downloaded. Raise this to fetch large files.")`. The instruction "Raise this to fetch large files" is doing prompt work — telling the LLM the recovery path when it hits a "skipped because too large" response.

**Discriminated union as MCP schema**: `process_document_batch`'s `changes` array is Zod-validated against the DocumentChange union. The Python side uses pydantic discriminated unions. This means the MCP tool schema, exposed via JSON-RPC `tools/list`, is rich enough for the LLM to know which fields go with which `type` — no ambiguity. Smithery had a deployment pain point here (AI_CONTEXT.md §4) where Anthropic's `mcpb pack` rejected tool schemas but Smithery's registry required them; resolved by `scripts/patch_smithery_mcpb.py` dynamically extracting schemas via JSON-RPC at build time.

**No read/write separation enforced in scopes**: there are read-only tools (`read_docx`, `diff_docx_files`, `list_available_mailboxes`) and write tools (`process_document_batch`, `finalize_document`, `create_email_draft`), but the MCP layer doesn't expose a permission scope concept. The `--docx-only` flag (line 710) disables cloud tools but only as an install-time choice.

### 6.3 Edit-Suggestion UX

**There is no built-in human-in-the-loop accept/reject UI**. The "review" pattern is documented at the *prompt* layer: "For destructive or finalization operations, run a dry-run first when the tool supports it (`dry_run: true` on `process_document_batch`)." (SKILL.md line 51). The output of `dry_run` is the per-edit report (lines 1893-1906) that includes `critic_markup` (the inline CriticMarkup preview around the edit context) and `clean_text` (the same with insertions accepted) — the LLM uses these for self-verification.

**The actual review surface** is Microsoft Word itself. Adeu produces native `<w:ins>`/`<w:del>`/`<w:comment>` XML and hands the file back. The user opens the modified DOCX in Word and uses Word's built-in Review pane. The Adeu engine's job ends at "produce reviewable DOCX." This is a deliberate scope choice — the README calls it a "Virtual DOM for Microsoft Word."

**Edit-report alignment with read** (`search_and_targeted_write.md` §5.4): the per-edit report in `process_batch`'s response mirrors the layout of `read_docx` search-match output. Same heading markers, same `### Edit N ✅ [applied] (p3)` page notation, same `**Path:**` breadcrumb. Per the spec: "This ensures the agent is able to correlate its write operations with its earlier observations." A symmetric LLM-facing audit trail.

**The MCP Apps UI** (`docs/MCP_APPS_PROTOCOL.md`, `node/packages/mcp-server/src/templates/markdown_ui.html`): for clients that support the MCP Apps protocol (Claude Desktop's renderer), `read_docx` returns a `ToolResult(content=Markdown, structured_content={html: ...})`. The LLM sees pure Markdown; the human user sees a styled iframe with the same content rendered. Dynamic resizing via `ResizeObserver` + `window.postMessage` JSON-RPC `ui/notifications/size-changed`. Vanilla JS only (no React, no D3) to bypass CSP restrictions and keep offline-capable.

### 6.4 Validation + Safety

**Three layers of safety**:
1. **Schema-level** (Zod / pydantic): malformed payloads rejected at the MCP boundary. Per-element validation in Python (`_normalize_changes` in `tools/document.py` lines 51-108) — one bad sub-edit doesn't forfeit the whole batch; rejects accumulate to a `rejected_notes` list returned to the LLM.
2. **Edit-string validation** (`engine.ts` `validate_edit_strings`): refuses CriticMarkup in `new_text`, structural-marker mismatches, heading-depth violations, boundary-zone targets. Surfaces per-edit error messages with the edit index.
3. **Apply-time safety** (`engine.ts` `_pre_resolve_heuristic_edit` + `apply_edits`):
   - **Foreign-author redline overlap**: edits that fall inside another user's pending `<w:ins>` (different `w:author`) and aren't `match_mode: "all"` cause that ins to be "unwrapped" first (the engine accepts the foreign author's change locally before applying its own). Or in strict cases, raises `BatchValidationError`. AI_CONTEXT.md §11 calls this "Nested Redline Strict Refusal" — multi-author overlap prevents silent destruction of the other author's work.
   - **Cross-paragraph regex match**: regex patterns matching across `\n\n` paragraph boundaries are rejected because applying them would corrupt the DOM (`search_and_targeted_write.md` §5.3.3 "Double-Sided Paragraph Merges").
   - **Overlapping edits within the batch**: tracked via `occupied_ranges`. The later-in-list edit is skipped (not the earlier — first-resolved wins) and reported with `_error_msg`.

**No content moderation / no PII scrubbing in OSS**. The Cloud backend (closed source) presumably does some — but the engine itself is content-neutral.

**No rate limits in OSS**. No cost guards. The MCP server is stateless; the LLM controls call frequency.

**Sanitization (`sanitize/transforms.ts`, 538 lines)**: the `finalize_document` tool strips author metadata, replaces `w:author` on remaining redlines with a generic name, optionally accepts all changes, optionally applies `<w:documentProtection w:edit="readOnly" w:enforcement="1"/>`. AI_CONTEXT.md §8 details the "Deep Part Ejection" rule: severing relationships from `pkg.rels` + `part.rels` + removing the part from `pkg._parts` — deleting the XML element alone is insufficient because python-docx repackages empty parts on save.

## Patterns Worth Adopting for Spaarke Compose

| Pattern | Spaarke R# | Where to apply |
|---|---|---|
| **CriticMarkup read-only / structured-payload write asymmetry** | R2 | The LLM consumes the document with inline `{++/--/==/>>}` markers; submits typed `DocumentChange[]` payloads. We can keep this exact split with TipTap's JSON or with a Markdown projection of our DOCX content. |
| **DocumentChange discriminated union (flat fields, no nesting)** | R2 | Our `ApplyEdits` operation should accept exactly this shape; reuse the `modify`/`accept`/`reject`/`reply`/`insert_row`/`delete_row` type literals so existing adeu LLM training carries over. |
| **`match_mode: strict/first/all` + structured ambiguity errors** | R2 | Adopt verbatim. Saves multi-turn correction loops. |
| **Validation gate with per-edit-index refusals** | R2 | Implement `IDocxEditValidator` returning `List<EditValidationError>` keyed by edit index, with the exact refusal categories adeu uses (CriticMarkup-in-new-text, heading depth >6, structural marker count mismatch, foreign-author overlap, cross-paragraph regex). |
| **Reverse-order pre-resolved batched apply** | R2 | Open XML SDK gives us DOM-style access — apply this pattern when implementing `BatchEditApplyService`. Pre-resolve all `target_text` against the body element offset, sort descending, apply bottom-up. |
| **Snapshot/restore transaction primitive** | R2 | Use `WordprocessingDocument.Clone()` (Open XML SDK has it) before any batch; restore on validation failure. Mirrors `takeSnapshot`/`restoreSnapshot` exactly. |
| **Modern Comments 4-part architecture** | R2 | Codify the four content types + their rels in a `CommentsManager` C# class. `Codeuctivity.OpenXmlPowerTools` may have helpers; if not, adeu's `comments.ts` is the cleanest reference. |
| **Comments-attached-as-siblings-of-ins/del-wrappers** | R2 | Reuse `ascend_to_paragraph_child` logic — anchor comment markers at paragraph-child level, not inside the tracked wrapper. |
| **Dual mapper (raw view + clean view) extraction** | R2 | When the LLM submits text from a snapshot the document has since diverged from, fallback-search against the accepted state, not just the raw state. |
| **Smart-quote + Markdown-formatting cascading match fallbacks** | R2 | Direct substring → quote-normalized → markdown-stripped → fuzzy regex. Four tiers, document each. |
| **`_nearest_match_hint` regex-anchor strip** | R2 | When a regex `^...$` fails, strip anchors and probe for literal — return a Did-you-mean. Tiny code, large LLM benefit. |
| **Defined-terms appendix with usage counts + typo diagnostics** | R3 | Don't ship in R2 — too speculative. But it's the right pattern for legal docs in R3+. Implement bounded Levenshtein in C# (System.Text comparator). |
| **Tool descriptions as prompt copy** | R2 | Our BFF playbook outputs / MCP descriptions should include behavioral guidance: "All changes evaluate against the ORIGINAL state — do not chain dependent edits within one batch." Not just parameter docs. |
| **Open Agent Skill packaging** | R3+ | If we want Compose accessible from Claude Code / Cursor / Windsurf without a custom MCP server, follow adeu's skill structure: `skills/spaarke-compose/SKILL.md` + numbered `references/*.md` loaded on demand. |
| **`docx_only` install flag separating CRUD-tool surface from coupled cloud tools** | R2 | If we ever expose a public MCP/Connector API, mirror the `if (!isDocxOnly)` gating pattern so customers can opt out of features they don't have backend support for. |
| **Edit-report layout symmetric with read-output layout** | R2 | Per-edit `pages`, `heading_path`, `occurrences_modified` mirroring `read` search-match output. Audit trail correlates write to prior read. |
| **Dry-run mode at the BFF endpoint level** | R2 | `POST /api/v1/compose/edit?dry_run=true` returns the same shape without persisting. Cheap to add when the engine already supports it. |

## Patterns to Avoid

- **Markdown as the canonical LLM interchange** for an editor surface. Adeu does it because they're chat-only; we're an editor where TipTap JSON / ProseMirror nodes ARE the LLM-friendly representation. Forcing markdown round-trip would lose schema information we already have. (Consider: project to CriticMarkup for *track-change rendering* in chat, but keep TipTap JSON in the editor surface.)
- **The 3021-line monolithic engine.ts**. Adeu's `engine.ts` mixes apply pipeline + DOM mutation helpers + validation + table edits + style heuristics + ID renumbering. Split these in our implementation: `RedlineEngine` (apply), `EditValidator` (validate), `DocxDomService` (mutation primitives), `RevisionIdAllocator` (IDs), `TableEditApplier` (tables).
- **Cross-language "Make Both Perfect" parity**. Adeu maintains TypeScript and Python in lockstep. For Spaarke, the BFF is C# .NET 8 and that's the only implementation we need — don't fork.
- **Custom `@@ Word Patch @@` diff format**. Adeu invented a sub-word diff format to avoid line-based diff issues with the LLM. We have no reason to invent a new format — use TipTap's transaction format or standard JSON Patch (RFC 6902) for our internal diffs.
- **The `live_word` Windows COM integration**. Adeu has 10 functions in `live_word_ops.py` that drive a running Word desktop instance via pywin32. The COM impedance mismatches (AI_CONTEXT.md §10 enumerates 11 known ones) are gnarly and Windows-only. We deliver via SPE + Word-for-Web; we never touch desktop COM.
- **`FREE_AGENT_PROBLEM.md`-style essay material in the engineering docs**. It's a position essay, not load-bearing. Skip the temptation to ship long position docs alongside code; ship `AI_CONTEXT.md`-style operational logs instead.
- **The Smithery-vs-Anthropic schema deadlock workaround** (AI_CONTEXT.md §4). Don't take on this distribution complexity unless we ship a true public MCP server. If we do, prefer one canonical registry.

## Open Questions Remaining

1. **How does adeu validate that comments-before-track-changes-ordering produces output Word for Web accepts?** The AI_CONTEXT.md mentions golden files; haven't traced them.
2. **What's the actual XML structure produced when a comment spans both an existing `<w:ins>` (paragraph-internal) and a new `<w:del>`?** We need this for our spike #1.
3. **How does the engine handle the case where two LLM-suggested edits write to the same physical paragraph but at different offsets?** Reverse-order apply works for unrelated paragraphs; same-paragraph overlap is handled by `occupied_ranges` (skipped) — but what's the LLM-facing message and recovery path?
4. **What does Adeu Cloud do to attach the redlined DOCX to an Outlook draft?** The OSS code only shows `multipart/form-data` POST to `/api/v1/emails/drafts/new`. The actual Outlook MAPI / Graph integration is closed source. We'd need to build this from scratch using Microsoft Graph for our Outlook integration.
5. **Does adeu handle Word's "Compare/Combine Documents" output (the auto-generated revisions from Word's compare feature)?** Doesn't appear so — likely treats them as ordinary `w:ins`/`w:del`. We may need explicit handling.

## Calibration

**High confidence (read the code, traced specific behavior)**:
- Pattern 1 (CriticMarkup asymmetry, builder logic, fuzzy regex)
- Pattern 3 (4-tier match fallback, validation gate refusal categories, ambiguity error format)
- Pattern 5 (DocumentChange union shape, reverse-order pre-resolution, snapshot/restore)
- The Outlook/email finding (read tools/email.ts thoroughly — it's a wrapper not native parser)
- The MCP tool surface (5 tools, schemas, descriptions)
- The agent skill's prompt structure

**Medium confidence (read part of the implementation, inferred rest from AI_CONTEXT.md and tests)**:
- Pattern 2 paragraph-boundary deletion / coalescing (read enough engine.ts sections to confirm reverse-order traversal exists; full algorithm only partly traced)
- Pattern 2 comment 4-part architecture (read comments.ts thoroughly but didn't verify by generating a sample DOCX and inspecting Word's render)
- Pattern 4 defined-terms extraction (read domain.ts ~50%; the heuristics for typo pruning come from AI_CONTEXT.md not code)
- Live Word COM impedance mismatches (read the spec in AI_CONTEXT.md §10 but didn't trace live_word_ops.py — we're not using this path anyway)

**Lower confidence / speculation**:
- The actual production usage / scale claims in the README — no public metrics
- Whether Adeu Cloud is the team's revenue model (looks like it: "Adeu Cloud" link in README, `BACKEND_URL` for paid features) — irrelevant to pattern extraction
- Whether the JIT steering / activation steering concepts in FREE_AGENT_PROBLEM.md are actually implemented anywhere in the adeu codebase (didn't find evidence — looks aspirational)

## Notable code surprise (signal of where they did interesting work)

**`_nearest_match_hint`** in `engine.ts` lines 341-376. ~30 lines of code that strip regex anchors, unescape common escapes, and probe for a literal match in `full_text`. The COMMENT explains: "the common loop trap (observed in the field) is an anchored regex like `^\( x \)$` against a mid-document string: ^/$ bind to the whole full_text, so it never matches even though the literal `( x )` is present." This is a tell — they observed LLMs producing this exact failure pattern enough times to write a targeted heuristic. It's the kind of empirical, narrow fix that signals **the engine has been used heavily by actual LLM clients in production**, not just unit-tested. Pair this with the long list of bug-numbered comments throughout `engine.ts` ("BUG-7: Unified single-pass validation...", "BUG-23-3: a prefix insertion whose new_text ends in a paragraph break..."), and you get the picture: adeu's documented operational rigor is real, not a sales artifact. Their `AI_CONTEXT.md` is the deepest single artifact about LLM-driven DOCX editing on the public internet I've found.
