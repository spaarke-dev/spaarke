---
name: word-rendering-pagination-nfr03-2026-07-28
description: WS-5 page/line pagination engine + NFR-03 licensing eval (Jul 2026). Page/line NOT in .docx (layout-time). Only reachable Word-native path = Graph format=pdf (Word-Online engine, NOT desktop-identical); headless/desktop Word server-side = KB257757 support+EULA prohibited. LibreOffice MPL-2.0 out-of-process only; AGPL-as-sidecar is a genuine ambiguity to escalate.
metadata:
  type: project
---

# Word-rendering / page-line pagination + NFR-03 licensing (2026-07-28)

**Question**: Compose R4.5 WS-5 task 051 — evaluate the Word-rendering-service path as the only true-Word-identical pagination source, and run the full NFR-03 (permissive-only) licensing analysis. Page/line numbers do NOT exist in the .docx (computed at layout/render time).

**Findings**:

1. **Only reachable Word-native path = Microsoft Graph `GET /drives/{id}/items/{id}/content?format=pdf`** (Office cloud renderer). Returns a PDF, NOT a structured page/line map — you extract page/line from the PDF (PdfPig Apache-2.0). Page mapping = strong; line mapping = strong ONLY if source has `w:lnNumType` on (Office renders visible line numbers into the PDF), else geometric inference. Precondition: doc must be in a Graph drive — Spaarke's SPE satisfies this. App-only read works. Cost: latency (community reports ~45s General_Timeout even on ~7MB docs; nominal 100MB ceiling but practical fails lower), throttling, async-only, `406 OfficeConversion_DocumentProtected` on sensitivity-labelled files. NO separate rendering-license SKU — bundled M365 entitlement; links NO code into BFF → NFR-03-clean.

2. **KEY HONESTY QUALIFIER**: Graph render = Word-*Online*'s engine, NOT Word-*desktop*. MS Q&A explicit: cloud renderer doesn't always match desktop pagination (server font substitution, line-breaking). So "true-Word-identical" is really "Word-Online-identical." The bit-identical path (self-run headless/desktop Word) is **KB257757-prohibited server-side** (unsupported + EULA doesn't cover serving unlicensed users) — off the table for support+licensing reasons, independent of NFR-03. Word Automation Services = on-prem SharePoint legacy, not cloud.

3. **NFR-03 licensing classification**: LibreOffice MPL-2.0(+LGPLv3) = weak/file-level copyleft, PERMITTED out-of-process ONLY (unmodified binary invocation taints nothing); Gotenberg (Apache-2.0 wrapper bundling LibreOffice) = clean way to package LibreOffice-as-sidecar. PdfPig/PDFsharp/pdfminer (Apache/MIT) = permissive, linkable (the PDF page/line extractor). Aspose/GemBox/Syncfusion/Apryse = commercial proprietary, FORBIDDEN to link. iText7/ONLYOFFICE-DocServer/SuperDoc = AGPL-3.0, FORBIDDEN to link.

4. **ESCALATION FLAGS (§9 human sign-off, NOT resolved)**: (a) **AGPL-as-a-separate-service is genuinely ambiguous** — NFR-03 bars AGPL "linked into BFF"; a sidecar isn't "linked," but AGPL §13 network-copyleft can trigger source-disclosure to remote users even for a separate service. Letter (linking) vs spirit (no copyleft entanglement) diverge → needs human ruling; recommend treating AGPL as forbidden even as sidecar. (b) **Syncfusion "Community License" is free-of-charge but NOT permissive** — proprietary revenue-capped EULA; "free" ≠ NFR-03-compliant.

5. **Sidecar framing (NFR-04)**: BFF links no paginator ever. LibreOffice/Gotenberg = separate container (hundreds of MB — precisely why it's out of the ≤60MB BFF publish). Graph path = MS's own service, zero self-hosted render infra; only in-house code is the PDF extractor (keep in a worker/function, not BFF publish). `Services/Compose/` stays byte[]-in/projection-out (ADR-007).

**Recommendation (INPUT to task 052, not the decision)**: Two viable NFR-03-clean paths trading fidelity vs ops-control oppositely — Graph (highest reachable fidelity, but latency/throttle/label-dependent, async-only) vs LibreOffice sidecar (full ops control, no external latency cliff, but largest divergence-from-Word; 050 measures it). Hybrid worth 052's look: LibreOffice for interactive preview + Graph for final citation pass. Product must make NO unqualified "page/line 100% identical to Word" claim.

**Sources**:
- support.microsoft.com/.../considerations-for-server-side-automation-of-office (KB257757 — headless Word unsupported + EULA)
- learn.microsoft.com Graph driveItem content?format=pdf; MS Q&A 5679108 (45s timeout), 323331 (size limits), 5523177 (406 sensitivity label)
- libreoffice.org/licenses (MPL-2.0 + LGPLv3+)
- Deliverable: projects/spaarkeai-compose-fidelity-r4.5/notes/ws5-word-service-eval.md

**Open questions**:
- Graph `format=pdf` real render latency/throttling on the WS-5 corpus — unbenchmarked (051 is eval-only; needs a timed spike like 050 does for LibreOffice if 052 leans Graph).
- The AGPL-as-sidecar ruling is pending human sign-off.

**Related**: [[tiptap-licensing-editor-arch-2026-07-16]] (SuperDoc AGPL, conversion is paid), [[docx-delta-fidelity-roundtrip-2026-07-16]] (render-engine landscape, permissive-lib scan), [[server-docx-authoring-numbering-2026-07-18]] (why paragraph numbering is deterministic but page/line is not).
