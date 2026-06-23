# Task 037 — JPS Matching Metadata Backfill Payloads

> **Generated**: 2026-06-23 by task 037 execution
> **Schema**: `architecture/jpsmatchingmetadata-schema.json` (Draft 2020-12)
> **Field written**: `sprk_analysisplaybook.sprk_jps_matching_metadata` (MultilineText, added by task 031)
> **NFR-02 invariants**: name, GUID, output schema UNCHANGED — only `sprk_jps_matching_metadata` modified

---

## Summary

Spec §1.5 (as amended 2026-06-22) enumerates **5 production-bound playbooks** (not 6 — `Summarize New File(s)` was DROPPED per spec line 151 because no such DEV record exists; closest match `Summarize File` PB-015 is NOT a production-binding for this project and remains out of scope per the §1.5 enumeration).

POML task 037 + project CLAUDE.md still reference "6 production-bound playbooks" — that is stale wording; the authoritative count is **5** per spec §1.5 / §3.7 Out-of-Scope as updated 2026-06-22.

All 5 verified to have `sprk_jps_matching_metadata = NULL` before backfill (no overwrite risk).

---

## Per-Playbook Payloads

### 1. `summarize-document-for-chat@v1` (chat-pane Summarize)

- **`sprk_analysisplaybookid`**: `44285d15-1360-f111-ab0b-70a8a59455f4`
- **`sprk_playbookcode`**: (null — chat sibling has no admin code)
- **Purpose**: Single-AiAnalysis chat-driven summarization; structured DocumentAnalysisResult emitted with progressive FieldDelta SSE (R5 D2-02). Output destination: chat pane.

```json
{"documentTypes":["NDA","Contract","MSA","Lease","Agreement","Email","Memo","Brief","Document"],"intents":["summarize","summarize-for-chat","tldr","quick-summary"],"triggerPhrases":["summarize this","give me a summary","tldr","summarize this document","summarize this for chat","what's in this document"],"preferredOver":[],"outputDestination":"chat","scopeHints":[],"exclusionHints":[]}
```

### 2. `summarize-document-for-workspace@v1` (workspace-destination Summarize)

- **`sprk_analysisplaybookid`**: `302e6da6-f363-f111-ab0c-7ced8ddc4cc6`
- **`sprk_playbookcode`**: (null — workspace sibling has no admin code)
- **Purpose**: Same SUM-CHAT@v1 action as chat sibling, but routes destination=workspace + widgetType=structured-output-stream → StructuredOutputStreamWidget. Output destination: workspace tab.

```json
{"documentTypes":["NDA","Contract","MSA","Lease","Agreement","Email","Memo","Brief","Document"],"intents":["summarize","summarize-for-workspace","summarize-to-workspace","open-in-workspace"],"triggerPhrases":["summarize this in the workspace","open a summary in the workspace","summarize and show in workspace","analyze this document in the workspace"],"preferredOver":[],"outputDestination":"workspace","scopeHints":[],"exclusionHints":[]}
```

### 3. `Document Profile` (PB-002)

- **`sprk_analysisplaybookid`**: `18cf3cc8-02ec-f011-8406-7c1e520aa4df`
- **`sprk_playbookcode`**: `PB-002`
- **Purpose**: Auto-generated document profile on upload (TL;DR, summary, keywords, doc-type classification, entity extraction). Runs at ingest time, not chat-routed in normal flow — but indexable for routing fallback.

```json
{"documentTypes":["NDA","Contract","MSA","Lease","Agreement","Email","Brief","Memo","Document"],"intents":["profile","extract-metadata","classify-document","extract-entities","generate-profile"],"triggerPhrases":["profile this document","extract document profile","classify this document","what kind of document is this","generate a profile"],"preferredOver":[],"outputDestination":"side-effect","scopeHints":["pipeline:upload"],"exclusionHints":[]}
```

### 4. `Create New Matter Pre-Fill` (PB-008)

- **`sprk_analysisplaybookid`**: `2d660cad-d418-f111-8343-7ced8d1dc988`
- **`sprk_playbookcode`**: `PB-008`
- **Purpose**: Extracts Matter fields from uploaded legal documents to pre-fill Create-New-Matter wizard. NFR-07 preserved (45s timeout, `useAiPrefill` hook, `$choices`).

```json
{"documentTypes":["NDA","Contract","MSA","Agreement","Engagement Letter","Retainer"],"intents":["pre-fill-matter","create-matter","extract-matter-info","matter-prefill"],"triggerPhrases":["create a matter from this","matter pre-fill","new matter from this document","start a matter from this contract"],"preferredOver":[],"outputDestination":"form-prefill","scopeHints":["wizard:create-matter"],"exclusionHints":[]}
```

### 5. `Create New Project Pre-Fill`

- **`sprk_analysisplaybookid`**: `fc343e9c-3460-f111-ab0b-7c1e521b425f`
- **`sprk_playbookcode`**: (null — no admin code in current DEV state)
- **Purpose**: Extracts Project fields from uploaded legal documents to pre-fill Create-New-Project wizard. NFR-07 preserved.

```json
{"documentTypes":["NDA","Contract","MSA","Agreement","Brief","Engagement Letter","SOW","Statement of Work"],"intents":["pre-fill-project","create-project","extract-project-info","project-prefill"],"triggerPhrases":["create a project from this","project pre-fill","new project from this document","start a project from this"],"preferredOver":[],"outputDestination":"form-prefill","scopeHints":["wizard:create-project"],"exclusionHints":[]}
```

---

## Schema-validation rationale

Each payload above:
- `documentTypes` — non-empty array of strings (FR-12 gate satisfied)
- `intents` — non-empty array reflecting playbook semantics
- `triggerPhrases` — non-empty natural-language phrases for embedding-time boost (FR-10)
- `preferredOver` — empty (no tie-breaker conflicts identified in 5-playbook set)
- `outputDestination` — non-empty enum value matching schema (FR-12 gate satisfied)
- `scopeHints` — populated only where a concrete scope tag exists (Document Profile = upload pipeline; pre-fill = wizard)
- `exclusionHints` — empty (no negative routing signal needed for the 5 production-bound)

All 5 conform to schema constraints:
- `additionalProperties: false` — payloads use ONLY the 7 declared properties
- `outputDestination` enum: only `"chat"` / `"workspace"` / `"form-prefill"` / `"side-effect"` used (no `"both"` yet — pending FR-14a Both-routing implementation)
- `preferredOver` pattern: empty arrays, no pattern violations

## Discrepancy notes

1. **Count: 5 not 6** — Spec §3.7 Out-of-Scope (line 151, updated 2026-06-22) confirms the production-bound set was reduced from 6 to 5; `Summarize New File(s)` doesn't exist in DEV; PB-015 `Summarize File` is NOT in the production-bound list and remains untouched.
2. **POML/CLAUDE.md stale** — Task 037 POML metadata and project CLAUDE.md still say "6"; spec is authoritative. Recommend updating POML title + CLAUDE.md MUST NOT line in a follow-up doc-drift task.
3. **`Document Profile` destination = `side-effect`** — Document Profile runs at upload (ingest-time auto-profile), not user-triggered. `side-effect` is the most accurate enum match per schema (no user-visible chat/workspace render; persists profile metadata to the document record).
