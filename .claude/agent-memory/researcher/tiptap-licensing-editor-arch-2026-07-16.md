---
name: tiptap-licensing-editor-arch-2026-07-16
description: TipTap ecosystem state + free-MIT-vs-Pro licensing boundary + editor-framework consensus (mid-2026) for Spaarke Compose legal-doc authoring surface. Confirms MIT-base + custom ProseMirror plugins is sound; DOCX import/export is paid so must stay server-side OpenXML.
metadata:
  type: project
---

# TipTap licensing + editor architecture for Spaarke Compose (2026-07-16)

**Question**: For "Spaarke Compose" (legal-doc AI authoring on MIT TipTap base, HARD rule: no TipTap product features paid OR unpaid): (1) TipTap version/packaging state mid-2026, (2) exact MIT-vs-Pro licensing line for track changes/comments/collaboration/DOCX import-export, (3) is MIT-base + own ProseMirror plugins still the right path or has consensus shifted, (4) notable OSS ProseMirror doc editors + licenses + borrowable patterns.

**Findings**:

1. **Version/packaging**: Current line is **TipTap v3** (3.0 stable **2025-07-15** after ~2mo beta). Latest = **@tiptap/core v3.28.0, published 2026-07-15** (verified via GitHub API), MIT (repo `ueberdosis/tiptap` LICENSE spdx = MIT). ~9M npm downloads/mo. v3.x roadmap adds Markdown load/export + a Decorations API in the free core. **Key 2025 event (pre-my-cutoff, ambiguous year in some secondary sources)**: end of **June 2025** TipTap open-sourced 10 formerly-Pro extensions under MIT: Details/DetailsContent/DetailsSummary, Emoji, DragHandle, FileHandler, InvisibleCharacters, Mathematics, TableOfContents, UniqueID. These are now genuinely MIT and usable — they are NOT in the owner's forbidden-product list.

2. **Licensing boundary (precise)**: MIT/free = editor core (@tiptap/core, /react, /starter-kit), all StarterKit marks/nodes, the 10 open-sourced extensions above, and everything in ProseMirror itself. **Paid product bundles (NOT MIT — require active subscription, code not open)**: Track Changes (custom-priced add-on), Comments, Real-time Collaboration (Hocuspocus server), Content AI / in-line AI, **Conversion = DOCX/PDF/ODT/EPUB import-export**, Pages/page-based layouts, Version History/Snapshot. So for Compose: track-changes, comments, collab, AND docx conversion all fall on the paid side → must be built ourselves or sourced from independent MIT OSS. TipTap restructured pricing June 2025; the old free collaboration tier is gone (plans ~$49-59/mo annual and up).

3. **Architecture consensus mid-2026**: TipTap-on-ProseMirror is still the **default recommendation** for serious document/authoring products (CMS, docs, KB, collaborative authoring). Lexical (Meta) is the pick only when perf/mobile/large-doc scale dominates; Slate for fully custom doc models; ProseMirror-direct is the engine everyone builds on (TipTap, BlockNote, Milkdown, ProseKit all sit on it). **Our "MIT TipTap base + our own ProseMirror plugins" approach is sound and current, not dated** — this is exactly the borrow-the-headless-core, avoid-the-paid-bundles pattern. Because TipTap extensions ARE ProseMirror plugins, community MIT PM plugins drop in directly.

4. **OSS ProseMirror doc editors**: **SuperDoc** (`Harbour-Enterprises/SuperDoc`) — **AGPL-3.0** dual-licensed (+commercial), built on ProseMirror + Yjs + JSZip, OOXML-native (not contenteditable wrapper), ships comments, tracked changes, real pagination, section breaks, headers/footers. Very active (latest mcp-v0.17.1 2026-07-15, 7,200+ commits). Proves high DOCX fidelity is achievable on a ProseMirror base — but **AGPL makes code-borrowing hostile for our proprietary product; borrow patterns/architecture only, not code**. Others on PM: BlockNote (MPL-2.0), Milkdown (MIT), ProseKit. Server-side DOCX fidelity remains our OpenXML-SDK backend job (see [[openxml-docx-compose-r2-2026-06-29]] and [[adeu-architecture-study-2026-06-29]]).

**Recommendation for Compose**: keep the MIT TipTap v3 base; build track-changes/comments as our own ProseMirror plugins (ProseMirror decorations/marks + steps); do DOCX import/export server-side with DocumentFormat.OpenXml (already the plan) — do NOT buy TipTap Conversion. This satisfies the owner's no-product-features rule cleanly.

**Sources**:
- GitHub API `repos/ueberdosis/tiptap/releases` + `/license` (v3.28.0 2026-07-15, MIT) — most authoritative for version/license
- https://tiptap.dev/blog/release-notes/tiptap-3-0-is-stable (3.0 stable 2025-07-15)
- https://tiptap.dev/blog/release-notes/were-open-sourcing-more-of-tiptap (10 MIT extensions, June 2025)
- https://tiptap.dev/pricing (paid boundary: track changes, comments, collab, conversion, pages, version history, AI)
- https://news.ycombinator.com/item?id=44202103 (HN, open-sourcing announcement 2025-06-06)
- GitHub API `repos/Harbour-Enterprises/SuperDoc/license` = AGPL-3.0; repo built on ProseMirror/Yjs
- Secondary consensus: pkgpulse/velt/eddyter/buildpilot 2026 editor comparisons (TipTap = default, Lexical = perf)

**Open questions**:
- npm scope of the 10 open-sourced extensions post-move (are they under @tiptap/extension-* MIT or still @tiptap-pro scope?) — didn't confirm exact package names; verify before adding to package.json.
- Whether TipTap's free Markdown export (v3.x roadmap) has actually shipped as of 2026-07 or still pending.
