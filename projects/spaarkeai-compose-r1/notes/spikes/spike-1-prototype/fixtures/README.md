# Spike #1 Fixtures

Sanitized legal-document fixtures for round-trip validation. NOT committed to git in this repo
to avoid bundling real legal text — operators populate locally before running the prototype.

## Required fixtures (per POML step 3)

| File | Type | Profile |
|---|---|---|
| `01-letter.docx` | Short letter | 1 page, single column, plain paragraphs, basic styling (bold, italic, signature block) |
| `02-long-agreement.docx` | Long agreement | ≥10 pages, headers + footers, basic numbering (single level), simple tables, page breaks |
| `03-multi-level-contract.docx` | Multi-level numbered contract | Nested clauses: 1, 1.1, 1.1.1; cross-references; defined-terms (Section 5.2(a)(iii) style); 20+ pages |

## How to populate

1. Source a sanitized DOCX matching each profile from internal corpus (or use redacted real samples).
2. Place under this folder with the filenames above.
3. Run `npm run dev` from `notes/spikes/spike-1-prototype/` and open the dev server.
4. Load each fixture via the file picker; observe the mammoth conversion report and the rendered editor.
5. Export back to DOCX (via the Export button — to be added when prototype is actually run) and open in Word to compare.

## Round-trip findings recorded in

`../spike-1-tiptap-docx-roundtrip.md` §3 (OOB Inventory) and §5 (Fixture-Specific Findings).
