# End-to-end POC gap log

One row per gap or surprise, in the order found. Each gap has a short name and is listed once; later observations go in its row.

**Severity:**
- **B**: blocks the design;
- **M**: needs a design change;
- **L**: polish;
- **I**: information only.

**Package?:** whether the fix belongs in the formal Accelerator package (Y/N).

| # | Act.step | Surface | Expected | Observed | Gap | Sev | Fix in package? | Evidence |
|---|---|---|---|---|---|---|---|---|
| 1 | Prep 5 | Build | Room for more `nda-review` companions | `nda-review` is at 20 companion files, the gate limit, after adding `xlsx_lite.py`, `review_visuals.py` and `review-result.schema.json` | No room for the next engine or reference in one skill | M | Y: the K/E split (protocol v0.2) or merging engines | Build log 2026-11-05 |
| 2 | Prep 5 | Package | Every review leaves a machine-readable result | Before 0.1.2, `nda-review` wrote only the edit plan; per-topic ratings existed only in chat | Dashboards, cross-surface comparison and scoring need `review.json` | M | Y: keep `review.json` and the `rating`/`note` fields (review-result schema 0.2) | `review-result.schema.json` |
| 3 | Prep 1–4 | Word playbook | Playbooks hold positions only (G1) | The renderer's General guidelines contain MUST / MUST NOT workflow instructions | Conflicts with the "no workflow instructions" guidance (OD-37, gate G1) | M | Y: rewrite General guidelines as positions | `build/render_word_playbook.py` |
| 4 | Prep 1–4 | Kit | Tracked insertions of whole paragraphs mark the paragraph too | `make_kit.py` tracks the inserted text but not the paragraph mark | Rejecting all changes leaves an empty paragraph (kit only; the engines are not affected) | L | N | Fernhill negotiated example |
