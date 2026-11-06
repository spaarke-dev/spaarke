---
name: harvey-vault-legora-tabular-review-2026-11-06
description: "Harvey Vault review tables vs Legora Tabular Review as of 2026-11-06 (limits, cells, citations, flags, export, playbook, Copilot/Cowork ties) for comparison with the Cowork NDA-grid POC"
metadata:
  node_type: memory
  type: reference
  originSessionId: 41528e46-e931-47ff-9d73-c52c333003ab
  modified: 2026-11-06T18:32:18.873Z
---

## 2026-11-06: Harvey Vault review tables vs Legora Tabular Review

**Question**: How do Harvey Vault review tables and Legora Tabular Review work today, compared with the Cowork 16-NDA POC (6a compare-key-terms, 6b review-contract loop, 6c nda-library-review)?

**Findings**:
- Harvey (release notes help.harvey.ai/release-notes/category/review-tables, public, dated): 10,000 files per table, 100k files/100GB per vault (2025-09-22), 500MB per file (2026-02-11), unlimited columns (2025-03). Cells carry answer, reasoning, sentence-level citations (2026-04-01), verify/flag (multi-colour 2026-03-04), assign, comments, in-line edit, cell lock (2026-08-12), cell-level re-run (2026-06-10), conditional columns, manual-input columns, reference file as column context (2026-07-01), agent actions for bulk edits (2026-09-17), table sync to Vault folders (2026-11-04), export with a citations column (Aug 2025), pre-built workflows, admin-published custom workflows. No red/yellow/green playbook column documented; playbook review lives in the Word add-in. Harvey in Cowork (2026-06-16) = Vault list/retrieval via MCP (ask_about_vault); review tables not mentioned.
- Legora: public page says documents=rows, prompts=columns, cell shows reasoning and source documents, Mark as Reviewed, Lock Cells, Review Mode, comments on cell/row/column and activity sidebar (blog 2026-01-19), chat with Assistant, import from DMS/VDR. Limit: 10,000 cells per review (support article via search snippet; help center login-walled). Playbooks documented for Word add-in only, not Tabular Review. "Legora for Copilot" is in the Cowork partner catalog (Learn cowork-available-plugins, ms.date 2026-11-02): "Ask legal questions and get grounded answers with citations" = Q&A, not grid.
- Pricing: neither public. Harvey third-party estimates ~$1,200/seat/month, 20-25 seat minimum (competitor-sourced). Legora platform fee + credits per its terms.

**Sources**: help.harvey.ai/release-notes/category/review-tables; developers.harvey.ai/guides/review_tables; harvey.ai/blog/review-table-agent-actions (2026-09-17); harvey.ai/blog/the-brief-aug-2025; harvey.ai/blog/harvey-copilot-cowork-launch; legora.com/product/tabular-review; legora.com/blog/collaborating-in-tabular-review; learn.microsoft.com/copilot/cowork/cowork-available-plugins. Related: [[word-vs-cowork-plugin-kinds-harvey-legora-tuning-2026-11-02]], [[legal-ai-competitive-landscape-2026-07]].

**Open questions**: Legora export format and column-type list (help center login-walled); Harvey page limits; whether any vendor rates cells against a playbook in the grid; Harvey export format (spreadsheet implied).
