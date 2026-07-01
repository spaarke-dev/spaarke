---
name: openxml-docx-compose-r2-2026-06-29
description: Spaarke Compose R2 pre-design research — Open XML SDK status, editor↔Word two-way patterns, SharePoint Embedded implications for DOCX comments + track changes.
metadata:
  type: project
---

# Spaarke Compose R2 — OpenXML / DOCX research (2026-06-29)

**Question**: Three pre-design questions for Compose R2: (1) Open XML SDK license/status/viability for reading-writing `w:comment` + `w:ins`/`w:del`, (2) open-source patterns where a web editor and Word two-way exchange comments+track-changes, (3) SharePoint Embedded implications for programmatic DOCX modification.

**Findings (synthesis)**:

1. **Open XML SDK is unambiguously safe to use.** MIT (.NET Foundation), `DocumentFormat.OpenXml` 3.x line current, .NET 8 supported, comments + track changes are first-class (`CommentRangeStart`/`CommentRangeEnd`/`CommentReference`/`Comment` + `InsertedRun`/`DeletedRun` with Author/Date/Id). Latest releases 3.4.1 (Jan 2026) and 3.5.1 (Mar 2026) — actively maintained. **Pair with `Codeuctivity.OpenXmlPowerTools` or `Clippit`** for diff→redline; Microsoft's own `OfficeDev/Open-Xml-PowerTools` is archived but MIT and forks live. Avoid `DocX` (Xceed Community license, not MIT).

2. **Two-way editor↔Word landscape (2026) is small but real.** Best references: **SuperDoc** (AGPL-3.0 + commercial; OOXML-native browser+headless, ProseMirror+Yjs, comments+tracked changes+multiplayer — production-ready); **DOCX Editor / Eigenpal** (Apache-2.0 but archived June 2026); **adeu** (MIT, Python+TS, atomic-edit-transaction pattern, production); **drpedapati/docx-review** (MIT, .NET 8 + Open XML SDK CLI, the cleanest reference for our exact stack). The architectural consensus is **OOXML-native server-side parse/serialize, not HTML round-trip**. Gotchas to bookmark: apply comments BEFORE track changes; multi-run text matching is mandatory; Word silently rejects spec-legal patterns it doesn't like; paragraph-boundary `w:del` is the hardest edge case. Litera/Spellbook/Kira/ContractPodAi have no public engineering writeups — we know they ship the outcome, not how.

3. **SPE doesn't have its own comment/track-change model — single source of truth is the DOCX bytes.** Webhooks on `drives/{containerId}/root` work (4230-min max lifespan, must renew), changeType `updated`, payload names driveId not item — use delta query to enumerate. `driveItem:checkout`/`:checkin` supported on SPE with `FileStorageContainer.Selected` + container-type permissions. Programmatic PUT while Word for Web has active session returns **HTTP 423 Locked** and Graph has no API to detect lock state in advance — design a state machine around checkout/checkin. Use `If-Match` etag on writes to handle the 412 race.

**Recommendation**: Server-side `DocumentFormat.OpenXml` 3.x + `Codeuctivity.OpenXmlPowerTools` in BFF, drpedapati-style ordering (comments before track changes), atomic-edit-transaction model from adeu, webhook+delta for Word-roundtrip detection, checkout for AI-driven redline operations.

**Critical pre-R2 spikes** (5 × half-day): (1) SDK creates DOCX with `w:ins`+`w:comment` → SPE upload → Word for Web renders natively; (2) Word for Web user adds comment+track-change → webhook → BFF reads correctly via SDK; (3) checkout-collides-with-Word-for-Web user experience; (4) etag-race PUT vs Word autosave; (5) `w15:commentEx` modern threaded-comments coverage in SDK 3.x.

**Sources**:
- `c:/code_files/spaarke/projects/spaarkeai-compose-r2/research/openxml-docx-research.md` (full memo with citations)
- `github.com/dotnet/Open-XML-SDK` (LICENSE, releases, issues #1187, #1817)
- Microsoft Learn how-tos for comments + revisions + driveItem checkout + SPE webhooks
- `github.com/superdoc-dev/superdoc`, `github.com/dealfluence/adeu`, `github.com/drpedapati/docx-review`, `github.com/Codeuctivity/OpenXmlPowerTools`
- HN item 48228411 (SuperDoc launch discussion)
- `knowledge/sharepoint-embedded/SOURCE.md`

**Open questions**:
- Are item-level SPE webhooks supported, or only container-root? Tutorial only shows root.
- `w15:commentEx` (modern threaded comments) strongly-typed coverage in SDK 3.x — unverified, may require raw XML.
- Word-for-Web ↔ programmatic-checkout concurrency user-visible behavior — needs spike.
- Word's "undocumented quirks" (SuperDoc founders' phrase) — no Microsoft-side enumeration; empirical only.

**Related to**: [[lavern-multi-agent-legal-system]] (multi-agent legal pipeline reference; not DOCX-specific).
