---
name: law-dept-benchmarks-ocg-2026-11-03
description: Law-department size/spend (ACC 2025, CLOC 2025), firm rate benchmarks (WK Real Rate Report, TR), and published outside-counsel-guideline provisions (CNA, Allianz, Penn State, Missouri, Delaware DOJ, Affirm) used to build 6 synthetic OCG personas P1-P6
metadata:
  type: reference
---

**Question**: Benchmarks for synthetic outside-counsel guidelines (6 personas, rate caps fixed by invoices).
**Findings**: ACC 2025 PDF (acc.com/sites/default/files/2025-06/ACC_2025_Law_Department_Management_Benchmarking_Report.pdf) is fetchable with curl + pypdf (pip install --target scratchpad/lib pypdf; run python -I with sys.path.insert) and gives medians by revenue band: lawyers 2/12/38/70 (<1B/1-5B/5-20B/20B+), outside-counsel spend $364K/$3.3M/$13.8M/$44M, litigation (excl IP) $75K/$1.2M/$3M/$15.5M, lawyers per $B by industry (finance 7.7, IT 10.2, mfg 4, healthcare 3.4, retail 1.5), outside spend % revenue (mfg 0.21). WK Real Rate Report: 2024 mean partner $803 / associate $597 / paralegal $258 (via legal.io); full RRR PDFs are gated, WK site 403s WebFetch, but assets.contenthub.wolterskluwer.com sample PDFs download with curl (2022 sample has city litigation partner medians: Atlanta $525, Austin $643, Baltimore $585). Real insurer/university/state OCG text: CNA 2025 and Allianz Sep 2025 (cdn.sitetheory.io), Penn State 2025, UM System, Delaware DOJ 2025, Wyandotte KS, Affirm LSR, Boston Fed engagement letter. All use 0.1-hour increments; none found allowing 0.25. Insurer panel rates are not published (SC Insurance Reserve Fund: $230-240 senior, $100 paralegal).
**Sources**: see the 2026-11-03 research reply; main ones are the ACC PDF, cloc.org 2025 SOTI PDF, TR Midsize Law Firms Report 2025 PDF, wolterskluwer contenthub PDFs.
**Open questions**: no sourced litigation-share-of-spend by industry; no NACUA/higher-ed or state-agency spend medians (paid reports); firm-size rate tables (RRR 2024/2025) paywalled; Wolters Kluwer 2025-2026 LegalVIEW pages blocked (403).
