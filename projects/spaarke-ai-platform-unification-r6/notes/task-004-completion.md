# Task 004 Completion Notes — Seed Default SYS- Persona Row (FR-04)

> **Project**: spaarke-ai-platform-unification-r6 — Pillar 1
> **Phase**: A — Data-driven Foundation
> **Wave**: A-G1 (parallel with 002, 003)
> **Status**: ✅ Completed
> **Date**: 2026-06-07
> **Rigor**: STANDARD (data seed; no `.cs` modification)

---

## Summary

Seeded the single global SYS- AI persona row that the Pillar 1 resolver
(`IScopeResolverService.ResolvePersonaForChatAsync` — task 003) returns when no
tenant CUST- or playbook-attached override exists. The seeded `sprk_systemprompt`
field contains the VERBATIM text currently produced by
`PlaybookChatContextProvider.BuildDefaultSystemPrompt(null)` — the FR-04 binding
that guarantees zero behavior change at task 005 cutover.

---

## Deployment Evidence

| Field | Value |
|---|---|
| Environment | `https://spaarkedev1.crm.dynamics.com` (Spaarke Dev) |
| Entity | `sprk_aipersona` |
| Row id (`sprk_aipersonaid`) | `4fe49430-aa62-f111-ab0c-70a8a58ae145` |
| Created (action) | `created` on first run; `unchanged` on second run (idempotency proven) |

Independent Web API verification:

```
GET /api/data/v9.2/sprk_aipersonas?$filter=sprk_name eq 'SYS-DEFAULT'
     &$select=sprk_aipersonaid,sprk_name,sprk_personacode,sprk_scopetype,
              sprk_availableadhoc,sprk_tags,sprk_description,_sprk_parentpersonaid_value

Row count: 1
  id              : 4fe49430-aa62-f111-ab0c-70a8a58ae145
  name            : SYS-DEFAULT
  personacode     : SYS-DEF
  scopetype       : 100000000          (Global)
  availableadhoc  : True
  tags            : system,default,fallback,chat
  parentpersonaid : None                (root of inheritance chain)
```

---

## Verbatim Text Used

Source method: `PlaybookChatContextProvider.BuildDefaultSystemPrompt(string? playbookName)`
File: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs`
Branch: `playbookName == null` (the standalone-mode branch — FR-04 SYS-default fallback)
Lines: 541-569
Captured: 2026-06-07 from master HEAD

The named-playbook branch (lines 526-536) is a runtime-dynamic template (string
interpolation on `playbookName`) — NOT the SYS-default. It will continue to be
produced by the playbook-attached resolution path post-cutover.

```text
You are Spaarke AI, an intelligent assistant for legal professionals using the Spaarke platform.
You help with document analysis, matter management, legal research, financial analysis, and general questions about the user's work.

## Your Capabilities
You have access to powerful tools — use them proactively:

- **SearchDocuments**: Search the document index to find relevant content. Use this when the user asks about documents, contracts, agreements, filings, or any content stored in Spaarke.
- **SearchDiscovery**: Broad discovery search across all indexed documents. Use this when the user asks to find matters, projects, documents, or explore what's available.
- **GetKnowledgeSource**: Retrieve full content from a specific knowledge source. Use after SearchDocuments identifies a relevant source.
- **SearchKnowledgeBase**: Search the knowledge base for reference information, policies, and best practices.
- **GetAnalysisResult** / **GetAnalysisSummary**: Retrieve prior analysis results for documents that have been analyzed.
- **RefineText**: Help the user improve, rewrite, or restructure text.

## Instructions
- When the user asks about their matters, projects, or documents, **always use SearchDiscovery or SearchDocuments first** — don't say you can't access their data.
- When you find relevant documents, summarize what you found and offer to analyze further.
- If the user asks to analyze a document but none is loaded, suggest they upload one or help them search for it.
- Cite sources and document names when referencing search results.
- Be proactive — if a search returns relevant results, highlight key findings.
- Format responses in clear, readable Markdown with headings and structure.

## What You Know About
- Legal documents (contracts, agreements, court filings, memos, briefs)
- Matter management (case details, timelines, budgets, parties)
- Financial data (budgets, invoices, billing, cost analysis)
- Document comparison and review workflows
- Legal research and case law (when Bing Grounding is available)
```

Stored byte count: **2042 chars (LF)**. Canonical (C# 11 raw-string-normalized
from the source file) byte count: **2042 chars (LF)**. **Byte-identical**.

Verification script: `c:\tmp\verify-persona-text.py` reads the row from Web API,
applies C# 11 raw-string normalization to the source file (strip the common
leading whitespace indicated by the closing `"""` indent), and asserts byte
equality. Output: `IDENTICAL: True`.

---

## Row Shape (final)

| Field | Type | Value |
|---|---|---|
| `sprk_name` | String(200) | `SYS-DEFAULT` |
| `sprk_personacode` | String(10) | `SYS-DEF` |
| `sprk_description` | Memo(2000) | `Default Spaarke AI persona; fallback when no tenant or playbook override exists.` |
| `sprk_systemprompt` | Memo(100000) | 2042-char verbatim text (above) |
| `sprk_scopetype` | Picklist | `100000000` (Global) |
| `sprk_tags` | String(1000) | `system,default,fallback,chat` |
| `sprk_availableadhoc` | Boolean | `True` |
| `sprk_parentpersonaid` | Lookup→self | `null` (root of inheritance chain) |

---

## Decisions

1. **Source method location**: The task POML cited
   `SprkChatAgentFactory.cs` as the home of `BuildDefaultSystemPrompt()`, but the
   method actually lives in `PlaybookChatContextProvider.cs`. The factory calls
   into the context provider, which invokes the method. The C# code is
   unchanged; the POML's source citation has minor drift. Captured the correct
   source method per FR-04 binding.

2. **personacode value**: The user-provided runtime arguments suggested
   `SYS-DEFAULT-PERSONA@v1`, but the schema field `sprk_personacode` is
   `MaxLength=10` (deployed in task 001). Used `SYS-DEF` instead — consistent
   with the `sprk_personacode`/`sprk_skillcode`/`sprk_toolcode` short-code
   convention from existing scope entities. The full identity is carried by
   `sprk_name` (`SYS-DEFAULT`).

3. **playbookName branch handling**: Only the standalone-mode branch
   (`playbookName == null`) is seeded. The named-playbook branch is a runtime
   template (string interpolation) and will be produced by the playbook-attached
   resolution path in task 003/005 — no static-text seed is appropriate for it.

4. **Line-ending normalization**: PS here-string adopts host line endings
   (CRLF on Windows). The script normalizes to LF (`-replace "`r`n", "`n"`)
   before storing so the verbatim diff is stable across hosts. The C# source
   file is checked into the repo with LF (per `.gitattributes`); the canonical
   value runs through C# 11 raw-string normalization which strips the common
   leading whitespace determined by the closing `"""` indent.

5. **Idempotency model**: First run = `created`. Subsequent runs:
   - Read row by `sprk_name`
   - If row absent → POST (create)
   - If row present + (prompt match + shape match) → no-op (`unchanged`)
   - If row present + (prompt drift or shape drift) → PATCH (re-sync to spec)
   Drift-healing on re-run is intentional: if the C# source text changes in a
   future PR, re-running the seed updates the row in lockstep.

---

## Acceptance Criteria — all green

| # | Criterion | Evidence |
|---|---|---|
| 1 | SYS-DEFAULT row exists in `sprk_aipersona` table on Spaarke Dev | Web API GET returned 1 row, id `4fe49430-aa62-f111-ab0c-70a8a58ae145` |
| 2 | `sprk_systemprompt` matches `BuildDefaultSystemPrompt()` VERBATIM (diff = 0) | Python verification: stored 2042 chars == canonical 2042 chars; byte-identical post C# 11 raw-string normalization |
| 3 | `scopeType = Global`; `parentScopeId = null` | `sprk_scopetype = 100000000` (Global); `_sprk_parentpersonaid_value = null` |
| 4 | Seed script idempotent | Re-run output: `Action: unchanged`, same row id |
| 5 | Tenant CUST- rows can reference SYS-DEFAULT as `parentScopeId` | `sprk_parentpersonaid` self-lookup deployed in task 001; SYS-DEFAULT has null parent (root) so any CUST- row can lookup against this id |
| 6 | BFF publish-size delta = 0 MB | No `.cs` changed in this task; only PS script + data row + notes |
| 7 | Seed script committed per existing scope-seed conventions (ADR-027) | `scripts/Seed-AiPersonaDefault.ps1` cloned from `Seed-KnowledgeScopes.ps1` canonical exemplar |

---

## Artifacts

- **Seed script**: `scripts/Seed-AiPersonaDefault.ps1`
- **Source text reference**: `projects/spaarke-ai-platform-unification-r6/notes/task-004-system-prompt-source.txt`
- **Verification script**: `c:\tmp\verify-persona-text.py` (transient; not committed)
- **Dataverse row**: `sprk_aipersona(4fe49430-aa62-f111-ab0c-70a8a58ae145)` in Spaarke Dev

---

## Blocks / Unblocks

This task **unblocks task 005** (Wire `SprkChatAgentFactory.CreateAgentAsync` to
scope persona). Task 005 now has a row to resolve when no tenant/playbook
override exists — the FR-04 cutover can be behavior-preserving.
