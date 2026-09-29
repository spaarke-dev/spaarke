---
name: superdoc-license-v2-buildvsbuy-2026-07-22
description: SuperDoc (Harbour Enterprises / superdoc-dev) license + v2 + byte-preservation verification for a buy-vs-build on an embedded proprietary commercial legal-drafting editor. Confirms AGPL-3.0 v1.45.0; no verifiable v2; not byte-preserving.
metadata:
  type: project
---

# SuperDoc buy-vs-build verification (2026-07-22)

**Question**: For embedding SuperDoc in a proprietary commercial SaaS legal editor (hard no-AGPL / no-per-seat constraint) — exact license (v1.x), is the rumored "SuperDoc v2" (July 28 2026) real + does it change the license, commercial terms, byte-preservation, architecture/API, AGPL §13 implications. Primary sources only.

**Findings (PRIMARY-SOURCE VERIFIED 2026-07-22)**:
1. **LICENSE = AGPL-3.0, confirmed 4 ways.** GitHub `superdoc-dev/superdoc` (org also resolves as `Harbour-Enterprises/SuperDoc` — redirect, same repo) spdx_id `AGPL-3.0`; LICENSE file = "GNU AFFERO GENERAL PUBLIC LICENSE Version 3"; root `package.json` + `packages/superdoc/package.json` both `"license": "AGPL-3.0"`; npm `@harbour-enterprises/superdoc` license AGPL-3.0. **Dual-licensed**: AGPLv3 OR SuperDoc Commercial License (README + docs.superdoc.dev/resources/license). Latest = **v1.45.0, published 2026-07-15** (2330 npm versions; dist-tag latest=1.45.0).
2. **v2 = NOT VERIFIABLE / no evidence.** No `v2.*` tag for the superdoc package (the `vscode-v2.x` tags are the separate VS Code extension's independent versioning). No v2 release, no v2 announcement/changelog/blog indexed. Homepage shows no v2 banner. Only "v2" hits are an internal **"v2 importer"** (import-pipeline rewrite, e.g. PR #3259 "wire citation handler to v2 importer") and "pagination v2" — internal component work, NOT a product v2 or license change. **Cannot confirm any July 28 2026 v2 or license change — reported as unverified.**
3. **Commercial license = QUOTE-ONLY.** Contact q@superdoc.dev; terms at superdocportal.dev. No published pricing, no published model (per-seat/OEM/flat) in any primary source. Unverified.
4. **Byte-preservation = PARTIAL, does NOT meet "untouched subtrees byte-identical".** Architecture parses the WHOLE docx into a ProseMirror model (super-converter → layout-adapter → layout-engine → DomPainter). Constructs the editor does NOT model are carried over byte-for-byte from the original zip; but MODELED content is re-serialized from the ProseMirror model on export. It's a **semantic round-trip** ("open in Word, nothing lost"), not a byte-preserving projection. Fails a hard byte-identical-untouched-content requirement.
5. **Architecture/API.** Standalone **ProseMirror** stack (NOT TipTap extensions) + Yjs + JSZip + Vite. Native tracked changes (w:ins/w:del), comments, reads pre-existing Word revisions/comments, programmatic edit, **Agent SDK via MCP** with **BYO-LLM** redlining (AI is open/host-controllable, not a closed feature). Bundle size not published (unverified).
6. **AGPL §13 real for SaaS embed.** §13 network-use clause triggers source-disclosure to remote users even without distributing a binary — embedding AGPL SuperDoc in a network-delivered proprietary product obligates offering complete corresponding source of the combined work. The copyleft trigger is real for this use case (standard interpretation, not legal advice).

**Bottom line**: For an embedded proprietary commercial product with hard no-AGPL / no-per-seat-commercial constraints — SuperDoc v1 (today) is **BLOCKED as a dependency** under AGPL; adoptable ONLY via the paid commercial license (quote-only, terms unverified, may itself be per-seat); otherwise **patterns-only** (its ProseMirror+canonical-OOXML+MCP-BYO-LLM design is a good reference). v2 changes nothing decision-relevant because it is unverifiable.

**Sources** (all primary):
- api.github.com/repos/superdoc-dev/superdoc (spdx AGPL-3.0, releases, tags); raw LICENSE + package.json (root + packages/superdoc)
- registry.npmjs.org/@harbour-enterprises/superdoc (license, dist-tags, 1.45.0 @ 2026-07-15)
- docs.superdoc.dev/resources/license; README.md (dual-license); superdoc.dev/changelog/2026-03-22-document-engine + search excerpt (byte-for-byte only for unmodeled constructs)

**Open questions**: Actual commercial pricing/model (quote-only). Whether a real product "v2" lands 2026-07-28 (unconfirmed as of 07-22). Exact list of which OOXML constructs SuperDoc models vs preserves-verbatim (determines fidelity blast radius).

**Related to**: [[legal-ai-docx-editing-comparison-2026-07-21]], [[browser-docx-editing-market-patterns-2026-07-21]], [[openxml-docx-compose-r2-2026-06-29]], [[ms-platform-multiformat-ai-editing-2026-07-22]]
