# NFR-09 real-firm-template fidelity fixtures (task 003)

These are **genuinely Microsoft Word–authored** legal agreement templates used by the NFR-09
hardening gate (`Nfr09RealTemplateHardeningTests`) to re-run the S1/S1b Docxodus `WmlComparer`
fidelity harness on *production-representative* documents — not the synthetic spike fixtures.

They were selected (over synthetic/library-generated `.docx`) specifically because they carry real
Word serialization idioms the synthetic fixtures cannot reproduce:

| File | Source | Real-doc stressors (verified) |
|---|---|---|
| `commonpaper-cloud-service-agreement.docx` | Common Paper CSA + SLA v1.1 | 345 body paragraphs, **395 `w14:paraId`** (100% coverage, all unique), **6 tables incl. 3 nested**, 235 table-cell paragraphs, **9-level numbering definition** (ilvl 0–8), 3 headers + 3 footers + footnotes + styles |
| `commonpaper-mutual-nda.docx` | Common Paper Mutual NDA v1 | 56 body paragraphs, 71 `w14:paraId` (unique), 3 tables, footnotes + styles + header/footer (distinct profile: no auto-numbering) |

## Provenance + license

Both documents are **Common Paper** standard agreements, published free-to-use and modify under
**Creative Commons Attribution 4.0 (CC BY 4.0)**. They are **public standard templates, not client
documents** — so committing them as test fixtures is permitted (the task-003 "do not commit real
*client* documents" confidentiality constraint does not apply to public CC-licensed standards).

- Cloud Service Agreement: https://commonpaper.com/standards/cloud-service-agreement/
- Mutual NDA: https://commonpaper.com/standards/mutual-nda/
- License: https://creativecommons.org/licenses/by/4.0/ — © Common Paper, used under CC BY 4.0.

## Coverage note (residual)

These two real templates cover the primary S1/S1b stressors — **nested tables** and **deep
multi-level numbering** — on genuine Word OOXML. The one stressor neither uses is **cross-reference
fields** (`PAGEREF`/`REF`); that residual is flagged in the NFR-09 hardening report and is exercised
end-to-end by the browser-verified G-R3 UAT (task 082) on real documents.
