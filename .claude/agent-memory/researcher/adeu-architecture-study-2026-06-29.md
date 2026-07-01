---
name: adeu-architecture-study-2026-06-29
description: Deep clean-room study of dealfluence/adeu repo (2026-06-29) for Compose R2 pattern extraction. Maps the 8 highest-leverage adeu patterns to Spaarke Compose R2-R5 phases; flags what NOT to copy.
metadata:
  type: project
---

# Adeu architecture study (2026-06-29)

**Question**: Detailed code-level review of `github.com/dealfluence/adeu` for Compose R2 pattern extraction — DOCX manipulation, Outlook/email handling, AI-embedding patterns. Clean-room study (read for patterns, not code).

**Findings**:

1. **Eight patterns worth adopting** (full mapping in memo): (a) CriticMarkup read-only / structured-payload write asymmetry, (b) flat DocumentChange discriminated union, (c) match_mode strict/first/all with structured ambiguity errors, (d) per-edit-index validation gate refusals, (e) reverse-order pre-resolved batched apply, (f) snapshot/restore transaction primitive, (g) Modern Comments 4-part XML architecture (comments + Extended + Ids + Extensible parts), (h) tool descriptions as prompt copy. All of these translate cleanly to `DocumentFormat.OpenXml` 3.x + `Codeuctivity.OpenXmlPowerTools` for our R2 spec.

2. **Outlook/email** — adeu HAS email integration but it's a wrapper around paid Adeu Cloud backend; no native MIME/.msg/.eml parsing in OSS. What IS in OSS: localized HTML quote-block stripping (9 languages for "From:/Sent:" Outlook patterns), short-ID cache pattern (MD5→msg_<6hex>→`~/.adeu/mcp_id_cache.json` with LRU+eviction error), and a coordination prompt ("You can now use `read_docx` on the local file paths") that chains the DOCX tools after attachment download. The DOCX engine and email tools share one MCP server with `if (!isDocxOnly)` gating — argues for unified BFF facade.

3. **Top AI-integration insight**: the LLM **reads** CriticMarkup (so it sees inline `{++/--/==/>>}` markers for tracked changes/comments) but MUST NOT write CriticMarkup — instead submits typed `target_text`/`new_text` pairs and the engine produces the OOXML. Collapses the LLM's task from "produce valid OOXML deltas" to "find-and-replace text," which is what LLMs are best at. This asymmetry is the single highest-leverage design choice.

4. **Surprise finding** — `_nearest_match_hint` in `engine.ts` lines 341-376: ~30 lines that strip regex anchors and probe for a literal when a regex target fails, returning a Did-you-mean. Code comment: "the common loop trap (observed in the field) is an anchored regex like `^\( x \)$` against a mid-document string." Plus inline BUG-7/BUG-23-3-style numbered comments throughout. Signals that adeu has seen heavy real-world LLM use in production, not just unit tests. AI_CONTEXT.md is ~700 lines of operational rigor — the deepest public artifact on LLM-driven DOCX editing I've found.

5. **What to avoid copying**: the Markdown intermediate (we have TipTap JSON), the 3021-line monolithic engine.ts (split per concern in C#), cross-language parity (we're C#-only), the custom `@@ Word Patch @@` diff format, live_word Windows COM integration, FREE_AGENT_PROBLEM.md-style position essays, the Smithery-vs-Anthropic schema deadlock workaround unless we ship a public MCP.

**Memo**: `c:/code_files/spaarke/projects/spaarkeai-compose-r2/research/adeu-architecture-study.md` (~3400 words, 8 sections + pattern adoption table + calibration)

**Sources**:
- `github.com/dealfluence/adeu` @ main (shallow clone /tmp/adeu)
  - README.md, AI_CONTEXT.md (~700 lines — the most valuable single file), docs/ARCHITECTURE.md, docs/spec.md, docs/FREE_AGENT_PROBLEM.md, search_and_targeted_write.md
  - skills/adeu-redlining/SKILL.md + references/criticmarkup.md + references/mcp-tools.md (the agent skill = system prompt)
  - node/packages/core/src/: models.ts (52 LOC), markup.ts (441 LOC), comments.ts (470 LOC), mapper.ts (1092 LOC, ~95% read), engine.ts (3021 LOC, ~75% read), domain.ts (~50% read), docx/bridge.ts, ingest.ts (partial)
  - node/packages/mcp-server/src/: index.ts (tool registrations), tools/email.ts (~750 LOC, full)
  - python parity confirmed via AI_CONTEXT.md §4 "Make Both Perfect" principle; didn't re-read identical Python port
- Related prior research: [[openxml-docx-compose-r2-2026-06-29]] (pre-design memo that flagged adeu as a Level 2 pattern source)

**Open questions** (still uncertain after this pass):
- How does adeu validate that comments-before-track-changes-ordering produces output Word for Web accepts? Mentioned golden files but didn't trace.
- What's the exact XML when a comment spans an existing `<w:ins>` + new `<w:del>`? Need this for our spike #1.
- Does adeu handle Word's "Compare/Combine Documents" auto-generated revisions distinctly? Doesn't appear to — likely treats as ordinary ins/del.
- Adeu Cloud's actual Outlook integration (MAPI/Graph) — closed source; we'll build from scratch using Microsoft Graph.

**Recommended follow-ups for Spaarke Compose**:
- R2 design: codify the 8 adoption patterns above as design constraints in `projects/spaarkeai-compose-r2/specs/`.
- R2 spike #1 update: include adeu's comment-anchor-as-paragraph-child-sibling pattern as a test case.
- R2/R3: borrow the localized-quote-block stripping from `email.ts` if Outlook integration lands in R4+.
- Knowledge base: add `knowledge/openxml/` SOURCE.md noting adeu's Modern Comments 4-part architecture (comments + Extended + Ids + Extensible) as the cleanest OSS reference.
