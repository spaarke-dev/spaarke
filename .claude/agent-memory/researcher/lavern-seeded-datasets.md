---
name: lavern-seeded-datasets
description: Concrete details on AnttiHero/lavern's 5 seeded legal datasets — CUAD, MAUD, ACORD, UNFAIR-ToS, LEDGAR. Where they come from, how lavern uses them (FTS5 RAG), licensing, and Spaarke reusability.
metadata:
  type: reference
---

# Lavern seeded legal datasets — concrete findings

**Corrects an earlier finding** that lavern "ships no bundled legal data." Lavern bundles a **seed script** (not the data itself); the script fetches from remote sources on first run and indexes into a local SQLite FTS5 KB. See [[lavern-multi-agent-legal-system]] and [[lavern-followup-2026-05-20]] for system context.

## Storage model

- **No `data/` directory in the repo.** The only seed artifact in-repo is `scripts/seed-knowledge-base.ts` (884 lines).
- Data is **downloaded at first run** to `./data/seed-cache/` (gitignored) and then bulk-inserted into the lavern SQLite KB (`kb_collections` + `kb_documents` + `kb_chunks` tables) as global, system-owned collections (`user_id = '__system__'`).
- The KB is **FTS5 full-text** — no vectors, no embeddings.
- Each dataset has a `--<name>` flag for selective seeding, and a `--force` flag to re-seed.

## Per-dataset details (from seed script)

| Dataset | Source (fetched at runtime) | Format | License | Approx size |
|---|---|---|---|---|
| **CUAD** | `github.com/TheAtticusProject/cuad/raw/main/data.zip` → `train_separate_questions.json` (SQuAD format) | JSON (nested), unzipped locally | CC BY 4.0 | 510 contracts × 41 clause types; zip ~tens of MB |
| **MAUD** | HF Datasets Server `theatticusproject/maud` (paginated) | JSON rows | CC BY 4.0 | 152 merger agreements × 92 deal points |
| **ACORD** | `huggingface.co/datasets/theatticusproject/acord/resolve/main/ACORD%20Dataset...zip` (BEIR format: corpus.jsonl, queries.jsonl, qrels TSV) | JSONL + TSV, unzipped locally | CC BY 4.0 | 114 queries, 126K+ clause-pair relevance judgments |
| **UNFAIR-ToS** | HF Datasets Server `coastalcph/lex_glue` config `unfair_tos` (train+test+validation) | JSON rows | CC BY-SA 4.0 | 5.5K sentences, 8 unfair-clause labels |
| **LEDGAR** | HF Datasets Server `coastalcph/lex_glue` config `ledgar` | JSON rows | CC BY-SA 4.0 | 60K provisions, 98 clause types |

**ContractNLI** was previously seeded; removed 2026-05-11 because CC BY-NC-SA 4.0 is incompatible with lavern's Apache 2.0.

## "ACORD" disambiguation

This is the **Atticus Clause Retrieval Dataset** (atticusprojectai.org/acord) — a BEIR-format IR benchmark for clause retrieval. **NOT the ACORD insurance-forms standard** (ACORD.org). Source: `https://huggingface.co/datasets/theatticusproject/acord` and the seed-script comments at lines 467-471.

## How lavern USES them (the key question)

**Answer: (a) — loaded into the FTS5 knowledge base for runtime RAG retrieval.**

Specifically:
- Indexed as global `kb_collections` rows with `is_global=1`, owned by a `__system__` user — visible to every session's KB queries.
- Surfaced to agents via the `search_knowledge_base` MCP tool (`src/mcp/tools/knowledge-base.ts`) — FTS5 over `kb_chunks` with optional filters by `collection_id`, `doc_type` (`precedent` for CUAD/MAUD/ACORD, `regulation` for UNFAIR-ToS/LEDGAR), and `jurisdiction`.
- Each chunk stores rich metadata in JSON: e.g. CUAD chunks store `{clauseType, contractTitle, source:'CUAD'}`; UNFAIR-ToS chunks store `{unfairType, allLabels, source:'UNFAIR-ToS'}`; ACORD chunks store `{clauseCategory, associatedQueries[top 5], source:'ACORD'}`.

**They are NOT few-shot examples in prompts**, **NOT eval fixtures**, **NOT a clause-pattern library** for deterministic detectors. They are pure FTS5 retrieval corpora.

## Spaarke reusability assessment

**License compatibility for commercial product:**
- CUAD/MAUD/ACORD (CC BY 4.0) — usable in a commercial product with attribution.
- UNFAIR-ToS/LEDGAR (CC BY-SA 4.0) — ShareAlike clause. Indexing internally for RAG retrieval likely fine; redistributing modified versions inherits ShareAlike. Legal review required before bundling in any redistributed Spaarke artifact.

**Mapping to existing JPS scopes:**
- **CUAD's 41 clause types** → can enrich a future RedFlagDetector or a `KNW-clauses` reference corpus indexed in Azure AI Search.
- **MAUD's deal-point taxonomy** → useful only if Spaarke takes on M&A workflows (not in current roadmap).
- **UNFAIR-ToS (EU consumer law)** → directly feedable into a "ToS red flags" reference for any Spaarke playbook reviewing consumer-facing contracts.
- **LEDGAR (SEC filings)** → broad clause taxonomy; useful as background corpus for clause-type classification but lower signal than CUAD for legal-ops use cases.
- **ACORD** → an IR benchmark, not a content corpus per se; less directly useful unless Spaarke wants to benchmark its own retrieval.

**Integration effort estimate:**
- Hours to days per dataset if porting the seed-script ingestion pattern to Spaarke's stack: rewrite the lavern SQLite inserts → push to Azure AI Search with appropriate skillset/index schema. The script's structure (download → cache → parse → bulk insert) maps cleanly.
- Total effort to ingest all 5 into an Azure AI Search "global precedent corpus": ~1-2 weeks including license review, infra setup, and dedup against existing Spaarke knowledge.

## The reusable pattern (independent of the data)

The lavern seed script demonstrates a clean pattern worth borrowing even if Spaarke doesn't use lavern's data:
- **Single TypeScript seed script** with per-dataset functions (`seedCuad`, `seedMaud`, etc.), idempotent (skip-if-already-seeded), `--force` to re-seed.
- **Cache directory** for raw downloads so re-runs don't re-fetch.
- **System-owned, global collections** model for "reference data every tenant sees" vs. per-tenant uploads.
- **Per-chunk JSON metadata** carrying source-specific provenance (`source: 'CUAD'`, `clauseType`, etc.) so RAG hits are filterable and citable.

## Key file paths (lavern repo)
- `scripts/seed-knowledge-base.ts` — the entire ingestion logic (downloaded to `c:/tmp/seed.ts` during investigation)
- `src/mcp/tools/knowledge-base.ts` — read-only MCP tool exposing FTS5 search to agents (downloaded to `c:/tmp/kb.ts`)
- `src/knowledge-base/indexer.ts`, `src/knowledge-base/retriever.ts` — KB I/O layer
- `CONNECTORS.md`, `NOTICE` — license attributions (per WebFetch findings)

## Open questions
- The seed script comments say CUAD/ACORD downloads are "~tens of MB" but exact sizes weren't measured. Run the script to confirm if accurate sizing matters.
- Does the lavern KB schema include any vector column or only FTS5? Verified FTS5-only per [[lavern-multi-agent-legal-system]] but worth re-checking if Spaarke wants to mirror the schema.
