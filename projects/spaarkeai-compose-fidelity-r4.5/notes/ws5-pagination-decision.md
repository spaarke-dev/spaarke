# WS-5 — Pagination decision record: ship page/line in R4.5 vs fast-follow

> **Task**: 052 (WS-5 decision) · **Project**: spaarkeai-compose-fidelity-r4.5
> **Rigor**: STANDARD (decision synthesis; notes-only, no code, no BFF change, nothing linked)
> **Synthesizes**: task **050** (`notes/ws5-libreoffice-spike.md` — measured LibreOffice-headless divergence) + task **051** (`notes/ws5-word-service-eval.md` — Word-service path + NFR-03 licensing).
> **Resolves**: spec **FR-19(d)** (ship-in-R4.5 vs fast-follow), Success Criterion 5 / **F-5** (honest layout numbering).
> **Scope guard**: This task changes **no `Services/Compose/` source**, adds **no project/package reference**, and links **no paginator** into the BFF. It authors a synthesis + recommendation only.

---

## 0. Decision at a glance

| | |
|---|---|
| **Recommendation** | **DEFER — pagination is a FAST-FOLLOW, not shipped in R4.5.** |
| **What R4.5 ships for WS-5** | The honest fidelity scoping (F-5) + the two-path engine analysis + the NFR-03 licensing path. **No pagination engine, sidecar, or page/line feature.** |
| **Why DEFER is in-scope (not a scope expansion)** | Spec FR-19 + owner clarification scoped WS-5 as **"spike + decision only"**; pagination implementation was explicitly an *possible* fast-follow, never committed. DEFER honors that scope. A **SHIP** recommendation would have expanded scope and fired the root §6 escalation — it does not, because the analysis does not support SHIP (§4). |
| **Honest claim ceiling (F-5)** | Until an engine is chosen and built, the product makes **NO page/line claim at all**. When built: **"page/line as rendered by Word Online"** (Graph path) or **"approximate, engine-measured (~21% internal-break divergence on the CIPO corpus doc)"** (LibreOffice path). **Never "100% identical to your Word desktop."** |
| **Items requiring HUMAN SIGN-OFF at fast-follow time** | (1) AGPL-as-a-separate-service ambiguity under NFR-03; (2) Syncfusion "Community License" free-but-not-permissive. Both are licensing/policy calls (root §9) — left to the human, **not** resolved here. |

---

## 1. The honest fidelity ceiling (F-5) — restated

Page numbers and line numbers **are not stored in the `.docx`.** They are computed at **layout/render time** from page size, margins, font metrics, image/table flow, and the renderer's line-breaking rules. Task 050 confirmed this directly from the OOXML: `w:lnNumType` carries only display *rules* (`countBy`/`start`/`distance`/`restart`) — no content→line mapping; and `w:lastRenderedPageBreak` appears **only** in the 3 corpus docs actually opened/saved in Word (proportional to their page count), and is **absent** from all 5 synthetic never-rendered docs. Pagination is a render-time artifact, never derivable statically.

The consequence, in three tiers of reachable fidelity:

1. **Truly Word-identical layout** is guaranteed **only by Word's own layout engine** (the user's Word desktop). That path — self-run headless/desktop Word server-side — is **barred**: Microsoft KB257757 / "Considerations for server-side Automation of Office" makes it **not supported** (deadlocks on modal dialogs — task 050 *reproduced this first-hand*: `WINWORD.EXE` COM automation hung on the first corpus doc, zero output) **and** EULA-prohibited for serving unlicensed users. This tier is **off the table**, independent of NFR-03.
2. **Reachable ceiling = "Word Online-identical"** via Microsoft Graph `format=pdf` (the Office cloud renderer). This is the **closest available** and **materially closer than any open engine** — but it is **Word-for-the-web's** engine, **not bit-identical to a specific user's Word desktop** (server-side font substitution, line-breaking nuances — task 051 §1.4).
3. **"Close but diverges" = LibreOffice sidecar.** Measured (task 050) on real Word-authored docs: **2 of 3** matched Word's cached page count exactly; **1 of 3** (`Engagement Letter.docx`) diverged by one page (LibreOffice fit the signature block Word pushed to page 2). On the CIPO patent doc, page **count** matched (15) but **~21% (3 of 14) of internal page-break positions** landed on a different paragraph than Word — and the net page-count match is a property of that doc's paragraph-length distribution, **not a general guarantee**. Line-level Word ground truth was **not obtainable** in the dev environment at all.

**Therefore the product may make no unqualified "page/line 100% identical to Word" claim under any reachable path.** F-5 requires surfacing this ceiling, not hiding it.

---

## 2. Engine options — side-by-side (fidelity × licensing × ops)

| Engine / path | Word-divergence (fidelity) | License / NFR-03 verdict | Linked into BFF? | Ops cost | Net posture |
|---|---|---|---|---|---|
| **Self-run headless/desktop Word** | Bit-identical (the only truly-identical source) | N/A — **support + EULA prohibited** server-side (KB257757); reproduced hanging in 050 | Never | — | **OFF THE TABLE.** Not an NFR-03 question — a Microsoft support/licensing bar. |
| **LibreOffice headless (sidecar; or Gotenberg wrapper)** | **Close but diverges** — measured ~21% internal-break shift on CIPO; 1-page diverge on Engagement Letter; **largest** divergence-from-Word of the viable paths | **MPL-2.0 (+LGPLv3+)** — weak/file-level copyleft. **PERMITTED — separate process ONLY** (never linked). Gotenberg = Apache-2.0 wrapper, same out-of-process posture. **This is 050's measured engine.** | **FORBIDDEN to link**; permitted as **out-of-process sidecar** | **Full ops control, no external latency cliff** — but a heavyweight container (LibreOffice image is hundreds of MB), own CPU/mem/cold-start budget | **Most viable under NFR-03; lowest external dependency; lowest fidelity.** |
| **Microsoft Graph `format=pdf` (Office cloud renderer)** | **Highest reachable** = Word-Online-identical; still **not** Word-desktop-identical (§1.4). **Unbenchmarked** on the corpus by 050/051. | **Microsoft service, bundled M365 entitlement** — **links no paginator code at all** (remote HTTP call to a sanctioned MS service; KB257757 does **not** apply). **NFR-03-clean.** | **N/A — links nothing** | **Latency/throttling** (async-only; ~45 s timeouts observed even on small docs), **sensitivity-label 406 failures**, requires docs in a Graph drive (SPE — satisfied), and you must **extract page/line from the PDF yourself** (PdfPig, Apache-2.0, permissive-linkable) | **Highest fidelity + license-clean; lowest ops control.** Needs its own timed spike before selection. |
| **AGPL rendering services** (ONLYOFFICE Document Server, SuperDoc-derived) | Word-ish as a service | **AGPL-3.0** — strong/network copyleft. §13 extends copyleft to **network interaction**, so "separate service" is **NOT** automatically clean. **AMBIGUOUS as a sidecar — human sign-off required (§5).** | Forbidden to link; sidecar **disputed** | — | **Do not pursue without the §5 human ruling.** Barred pending sign-off. |
| **Commercial** (Aspose.Words, GemBox.Document, **Syncfusion DocIO/PDF**, Apryse/PDFTron) | High-fidelity (Aspose) | Proprietary EULA — **FORBIDDEN**, full stop (NFR-03 names Aspose/GemBox/Syncfusion explicitly). **Syncfusion's free "Community License" is free-of-charge but NOT permissive** — proprietary, eligibility-capped, revocable. **"Free" ≠ NFR-03-compliant — human sign-off flag (§5).** | Forbidden | — | **Not an option regardless of fidelity or price.** |
| **PdfPig** (page/line extraction from whichever engine's PDF) | n/a (extractor, not renderer) | **Apache-2.0** — permissive | **PERMITTED to link** (but co-locating in BFF still counts against NFR-04 publish size — prefer a small worker) | Low | Supporting lib for either viable path. Avoid iText 7 (AGPL) for extraction. |

### 2.1 The licensing path — reaffirmed (NFR-03 / NFR-04 / T-1 Path A)

- **Permissive-only for anything LINKED into the BFF.** No commercial (Aspose/GemBox/Syncfusion/Apryse) and no AGPL (iText/ONLYOFFICE/SuperDoc) paginator may be linked.
- **LibreOffice (MPL-2.0/LGPL) is permitted ONLY as a separate process/service** — never a linked library. This is ADR Tension **T-1 Path A** (documented project exception + spike).
- **The WS-5 engine is OUT of the BFF publish (NFR-04 / BFF Hygiene §10).** BFF publish ceiling is **≤60 MB compressed** (baseline ~49.63 MB incl. PDBs); WS-1..WS-4 add ~0 MB. Any paginator is a **separate-process sidecar/container** (LibreOffice/Gotenberg) or a **remote service** (Graph) with its own size + ops budget. The BFF links **no paginator** either way; `Services/Compose/` stays `byte[]`-in / projection-out (ADR-007 / §10).

---

## 3. The trade the two viable paths represent

The two NFR-03-clean paths trade **fidelity against operational control in opposite directions**:

- **Graph `format=pdf`** — higher fidelity (Word-Online-identical), lower ops control (async-only ~45 s timeouts, throttling, sensitivity-label 406s, external dependency). **Unbenchmarked** — a timed corpus-render spike is the missing measurement before it could be selected.
- **LibreOffice sidecar** — lower fidelity (measured ~21% internal-break divergence), full ops control (no external latency cliff, entirely Spaarke-operated), heavyweight container.

A hybrid worth the fast-follow's consideration (both NFR-03-clean): **LibreOffice sidecar for interactive/preview + Graph render for a final high-fidelity citation pass.** Not resolved here — it is fast-follow design work.

---

## 4. Recommendation — DEFER to a fast-follow

**Ship in R4.5: the honest F-5 scoping + the engine/licensing analysis (this record and its two inputs). Do NOT ship a pagination engine in R4.5. Implement pagination as a separate, later fast-follow.**

Rationale:

1. **Scope.** Spec FR-19 and the owner clarification scoped WS-5 as **"spike + decision only"** (spec §Owner Clarifications; TASK-INDEX high-risk note). Pagination implementation was named an *explicit possible fast-follow*, **not committed in R4.5**. DEFER delivers exactly the committed deliverable — a written decision record — and nothing beyond it.
2. **The measurements do not clear a ship bar.** The only *measured* engine (LibreOffice) diverges from Word at the margins even when page counts match (~21% internal-break shift on CIPO; 1-page diverge on the Engagement Letter). The *higher-fidelity* engine (Graph) is **entirely unbenchmarked** on the corpus — selecting it in R4.5 would require a timed render + throttling + sensitivity-label-failure spike that R4.5 has no room for. Neither path is ready to ship a page/line feature with confidence this cycle.
3. **Open licensing questions block a clean engine selection.** Two items (§5) require human sign-off before an AGPL-service or "free" commercial option could even be prototyped. A ship decision cannot precede those rulings (root §9 — licensing is security-sensitive; do not silently resolve).
4. **F-5 is satisfied by the scoping alone.** Success Criterion 5 asks that the product make no page/line "100%" claim beyond the chosen engine's guarantee. With no engine shipped, R4.5 makes **no page/line claim at all** — trivially honest — and this record fixes the exact claim ceiling for whenever the fast-follow selects an engine.
5. **No BFF/publish impact.** DEFER adds nothing to the BFF publish, links no paginator, and keeps `Services/Compose/` unchanged — consistent with NFR-04 and BFF Hygiene §10.

Because the recommendation is **DEFER**, the root §6 scope-expansion escalation **does not fire** — DEFER stays within R4.5's committed spike-only scope. (Had the analysis supported SHIP, this record would STOP here and escalate to the human before any implementation task were created — no such task is created under any outcome of this task.)

### 4.1 What the fast-follow inherits (hand-off, not a commitment)

- Pick the engine against the two measured/known trade-offs (§3) and the product's page/line accuracy bar.
- If **Graph**: run the missing timed corpus-render spike (latency, throttling, sensitivity-label failure handling, async job design) — the measurement 050/051 could not perform.
- If **LibreOffice/Gotenberg**: budget the ~21% internal-break divergence as the realistic "close-but-not-exact" page-boundary ceiling; page-level citations ("page 4") are safer than line-level given no line-level Word ground truth was obtainable.
- Either way: sidecar/service **out of the BFF publish** (NFR-04); use PdfPig (Apache-2.0), never iText (AGPL), for PDF page/line extraction.
- **Obtain the two §5 human sign-offs first.**
- Set the product's page/line claim to match the chosen engine's guarantee (§1) — never "100% identical to Word desktop."

---

## 5. Items left to HUMAN SIGN-OFF at fast-follow time (root §9 — NOT resolved here)

Per root CLAUDE.md §9 (licensing is security-sensitive; do not silently resolve an ambiguous license as permissive), these two carry forward from task 051 §2.3 **unresolved**, for explicit human ruling **before** any engine embodying them is prototyped:

1. **🔔 AGPL-3.0 "as a separate service" is genuinely ambiguous under NFR-03 as written.** NFR-03 bars an AGPL paginator *linked into* the BFF; a literal reading leaves a gap for an AGPL renderer run **out-of-process** (ONLYOFFICE / SuperDoc-derived service) that is "not linked." **But** AGPL §13 extends copyleft to **network interaction**, which can trigger a source-disclosure obligation even for a separate network service — a stricter regime than the MPL/LGPL that makes LibreOffice-out-of-process clean. *Analysis recommendation to the human* (task 051): treat AGPL as **forbidden even as a sidecar**, reading NFR-03's intent (avoid copyleft entanglement) as covering the network-service case. **Requires an explicit human ruling** — the rule's letter (linking) and spirit (no entanglement) diverge for AGPL-as-a-service. Not decided here.

2. **🔔 Syncfusion "Community License" is free-of-charge but NOT permissive.** It is a proprietary EULA (eligibility-capped by company size/revenue, revocable), **not** OSI-permissive — it fails NFR-03's "MIT/permissive only" test and is one of the three explicitly-named commercial bars. *Analysis recommendation to the human*: **do not** adopt it as a shortcut; "free" ≠ NFR-03-compliant. Flagged only because "free" tempts a wrong classification. Not decided here.

Everything else classifies cleanly (LibreOffice/Gotenberg = permitted out-of-process; Graph = service, links nothing; Aspose/GemBox/Apryse = clearly commercial-forbidden; PdfPig/PDFsharp = clearly permissive-linkable).

---

## 6. Acceptance-criteria self-check (052)

- [x] Synthesizes 050's measured LibreOffice divergence + 051's Word-service + licensing evaluation into a side-by-side comparison (fidelity, licensing, ops) — §2.
- [x] States the licensing path: permissive-only; LibreOffice out-of-process; separate-process sidecar out of the BFF publish; no commercial/AGPL linked — §2.1.
- [x] Fixes the honest page/line claim — no "100%" beyond the chosen engine's guarantee (F-5 / Success Criterion 5) — §0, §1.
- [x] Gives a clear SHIP-IN-R4.5 vs FAST-FOLLOW recommendation with rationale — §4 (**DEFER**).
- [x] **Negative**: recommendation is DEFER, so the §6 scope-expansion escalation correctly does **not** fire; **no pagination code/engine/sidecar is added to the BFF publish** under this task; no implementation task is created — §4, §0. (Had it been SHIP, this record would STOP + escalate.)
- [x] Two ambiguous licenses left to human sign-off at fast-follow time, not resolved — §5.
- [ ] TASK-INDEX.md → ✅ for 052: **owned by the main session** (sub-agent write boundary; root §3). Flagged for the caller to flip.

---

## 7. Deviations

None. No scope-expansion escalation fired (recommendation is DEFER, which is within R4.5's committed spike-only scope). TASK-INDEX.md / current-task.md left to the main session per the sub-agent write boundary.

## Sources

- Task 050 — `projects/spaarkeai-compose-fidelity-r4.5/notes/ws5-libreoffice-spike.md` (measured LibreOffice divergence; OOXML evidence; Word-COM hang reproduction).
- Task 051 — `projects/spaarkeai-compose-fidelity-r4.5/notes/ws5-word-service-eval.md` (Graph `format=pdf` path; KB257757; NFR-03 license taxonomy; the two §2.3 sign-off flags).
- Spec `projects/spaarkeai-compose-fidelity-r4.5/spec.md` — FR-19, NFR-03, NFR-04, Success Criterion 5, Owner Clarifications (WS-5 scope = spike + decision only).
- Design `projects/spaarkeai-compose-fidelity-r4.5/design.md` §5.5 (fidelity ceiling), §9 T-1 (licensing Path A).
- Root CLAUDE.md §6 (scope-expansion escalation), §6.5 (ADR conflict resolution / T-1 Path A), §9 (security-sensitive licensing sign-off), §10 (BFF Hygiene / NFR-04 sidecar).
