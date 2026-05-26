# Capability Routing Benchmark Report

> **Date**: 2026-05-17
> **Component**: `CapabilityRouter` (AIPU2-012/013/014)
> **Source**: `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs`

---

## 1. Architecture Summary

The `CapabilityRouter` implements a 3-layer routing strategy that maps user messages to AI capabilities:

| Layer | Mechanism | Cost | Latency Target |
|-------|-----------|------|----------------|
| **Layer 1** | Keyword substring matching (in-memory) | Zero | < 50ms (warn at 10ms) |
| **Layer 2** | GPT-4o-mini JSON classification | 1 LLM call (< 600 tokens) | < 500ms (timeout) |
| **Layer 3** | Broad superset fallback (all tools) | Zero | < 1ms |

**Invariant**: At most one LLM call per user turn. Layer 2 is only invoked when Layer 1 returns `Uncertain`.

### Layer 1 Algorithm

1. Lowercase the user message once.
2. For each capability, compute `hintScore = matchedHints / totalHints` via substring matching.
3. Add a weak secondary signal: `descScore = matchedDescWords / totalDescWords` (words >= 4 chars), weighted at 0.2x.
4. If an active playbook is set and the capability has a `PlaybookId`, multiply the raw score by 1.5 (playbook bias).
5. Compute confidence: `topScore / (topScore + secondScore + epsilon)`.
6. If confidence >= threshold (0.8 default, 0.65 for playbook-biased), return `Confident`; otherwise `Uncertain`.

### Layer 2 Algorithm

1. Build a compact classification prompt (system message with capability index + user turn).
2. Send to GPT-4o-mini with JSON response mode.
3. Parse `{"capabilities": [{"name": "...", "confidence": 0.0-1.0}]}`.
4. If top result confidence >= threshold, return `Confident` at Layer 2.
5. On timeout (500ms), rate limit (429), invalid JSON, or unknown capability name: fall through to Layer 3.

### Layer 3 Algorithm

1. Compute union of all tool names from enabled capabilities.
2. Cap at `MaxSupersetTools` (default: 12), sorted alphabetically.
3. Return `Fallback` result (IsConfident = false, Layer = 3).

---

## 2. Test Corpus Specification

### Capability Manifest (10 capabilities)

The benchmark uses a fixed manifest of 10 capabilities that represent the Spaarke platform domains:

| # | Capability Name | Keyword Hints | Description |
|---|----------------|---------------|-------------|
| 1 | `legal_research` | case law, legal precedent, court decision, statute, regulation, jurisdiction | Search legal databases for case law and precedents |
| 2 | `document_search` | find document, search files, locate file, document lookup | Search and retrieve documents from SharePoint Embedded |
| 3 | `document_analysis` | analyze document, document review, extract from document, parse document | Analyze document content using AI extraction |
| 4 | `invoice_processing` | invoice, payment, billing, expense, receipt, vendor payment | Process and classify financial invoices |
| 5 | `email_processing` | email, message, inbox, outbound email, correspondence | Process incoming and outgoing email communications |
| 6 | `knowledge_retrieval` | knowledge base, knowledge source, FAQ, help article | Retrieve answers from configured knowledge sources |
| 7 | `summarize_content` | summarize, summary, brief, overview, recap, digest | Generate concise summaries of documents or text |
| 8 | `semantic_search` | semantic search, meaning search, concept search, similar documents | AI-powered semantic similarity search across documents |
| 9 | `entity_lookup` | lookup entity, find record, search entities, query records, CRM lookup | Query Dataverse entities by name or criteria |
| 10 | `write_back` | update record, save changes, write back, modify entity, edit record | Write data back to Dataverse records |

### Corpus Size and Distribution

| Expected Layer | Count | Target % | Rationale |
|---------------|-------|----------|-----------|
| Layer 1 (keyword hit) | 70 | >= 60% | Most messages contain clear domain keywords |
| Layer 2 (LLM classify) | 25 | ~25% | Ambiguous, rephrased, or indirect messages |
| Layer 3 (fallback) | 10 | < 10% | Off-topic, meta-questions, gibberish |
| **Total** | **105** | **100%** | |

### Category Breakdown

**Layer 1 categories** (70 messages):
- `factual-lookup` (15): Direct questions with clear domain keywords
- `action-request` (15): Commands containing keyword hints
- `multi-keyword` (10): Messages hitting multiple hints for the same capability
- `playbook-biased` (10): Messages that benefit from playbook bias
- `description-boost` (10): Messages where description words add secondary signal
- `single-keyword` (10): Messages with exactly one keyword match

**Layer 2 categories** (25 messages):
- `paraphrased` (8): Intent is clear but no keyword hints match
- `ambiguous-cross-capability` (7): Keywords match two capabilities equally
- `indirect-intent` (5): Implicit requests requiring semantic understanding
- `jargon-variant` (5): Domain-specific synonyms not in keyword hints

**Layer 3 categories** (10 messages):
- `off-topic` (4): Completely unrelated messages
- `meta-question` (3): Questions about the system itself
- `gibberish` (2): Random or nonsensical input
- `empty-adjacent` (1): Only stop-words, no semantic content

---

## 3. Measurement Methodology

### Layer Distribution Measurement

For each corpus message, run `RouteSync()` and record:
- `result.Layer` (1, 2, or 3 for fallback)
- `result.IsConfident` (true/false)
- `result.Confidence` (0.0-1.0)
- `result.SelectedCapabilities` (array)

Layer 1 tests can be run without an LLM. Layer 2 tests require a mock `IChatClient` or a live GPT-4o-mini endpoint. Corpus messages marked `expectedLayer: 2` are expected to be `Uncertain` at Layer 1.

**Pass criteria**:
- Layer 1 hit rate >= 60% of total corpus
- Layer 3 fallback rate < 10% of total corpus
- Zero misroutes: Layer 1 confident results must match the expected capability

### Latency Budget

| Layer | P50 Target | P95 Target | P99 Target | Hard Limit |
|-------|-----------|-----------|-----------|------------|
| Layer 1 | < 1ms | < 5ms | < 10ms | 50ms |
| Layer 2 | < 200ms | < 400ms | < 500ms | 500ms (timeout) |
| Layer 3 | < 0.1ms | < 0.5ms | < 1ms | N/A |

Measurement: `Stopwatch` around `RouteSync()` / `RouteAsync()` calls. Run each message 100 times for statistical validity (Layer 1 only; Layer 2 uses mocks for latency testing).

### Single-LLM-Call Verification

The invariant "at most one LLM call per user turn" is verified by:
1. Using a `Mock<IChatClient>` that counts invocations.
2. After each `RouteAsync()` call, assert the mock was called at most once.
3. When Layer 1 returns `Confident`, assert the mock was called zero times.

This is already covered by existing tests (Tests 13-17 in `CapabilityRouterTests.cs`) but the benchmark corpus extends coverage to 105 diverse messages.

---

## 4. Results Template

### Layer Distribution Results

| Metric | Target | Actual | Status |
|--------|--------|--------|--------|
| Layer 1 hit rate | >= 60% | ___ / 105 (___%) | |
| Layer 2 hit rate | ~25% | ___ / 105 (___%) | |
| Layer 3 fallback rate | < 10% | ___ / 105 (___%) | |
| Misrouted messages | 0 | ___ | |

### Layer 1 Keyword Router Accuracy

| Category | Total | Correct Layer | Correct Capability | Accuracy |
|----------|-------|---------------|-------------------|----------|
| factual-lookup | 15 | ___ | ___ | ___% |
| action-request | 15 | ___ | ___ | ___% |
| multi-keyword | 10 | ___ | ___ | ___% |
| playbook-biased | 10 | ___ | ___ | ___% |
| description-boost | 10 | ___ | ___ | ___% |
| single-keyword | 10 | ___ | ___ | ___% |

### Layer 1 Latency (100-iteration runs)

| Manifest Size | P50 | P95 | P99 | Max | Status |
|--------------|-----|-----|-----|-----|--------|
| 10 capabilities | ___ms | ___ms | ___ms | ___ms | |
| 50 capabilities | ___ms | ___ms | ___ms | ___ms | |

### Confidence Score Distribution

| Range | Count | Notes |
|-------|-------|-------|
| 0.95 - 1.00 | ___ | Strong single-capability match |
| 0.80 - 0.95 | ___ | Confident but not dominant |
| 0.65 - 0.80 | ___ | Playbook bias zone |
| 0.40 - 0.65 | ___ | Ambiguous (Layer 2 territory) |
| 0.00 - 0.40 | ___ | No meaningful match |

---

## 5. Corpus File Reference

Test corpus: [`notes/routing-benchmark-corpus.json`](routing-benchmark-corpus.json)

Each entry:
```json
{
  "id": 1,
  "message": "...",
  "expectedLayer": 1,
  "expectedCapability": "legal_research",
  "category": "factual-lookup"
}
```

---

## 6. Test Harness Reference

Benchmark test class: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Capabilities/CapabilityRouterBenchmarkTests.cs`

Run with:
```bash
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~CapabilityRouterBenchmarkTests"
```

---

## 7. Key Implementation Details (from code review)

- **Keyword matching**: Case-insensitive substring match (`string.Contains` with `StringComparison.Ordinal` after lowering both sides).
- **Description scoring**: Words < 4 characters are skipped (stop-word filter). Weight: 0.2x of hint score.
- **Playbook bias**: 1.5x multiplier on raw score when `CapabilityManifestEntry.PlaybookId.HasValue` and `activePlaybookName` is not null.
- **Confidence formula**: `topScore / (topScore + secondScore + 1e-9)`. Perfect ties yield ~0.5; single dominator yields ~1.0.
- **Default thresholds**: Confident >= 0.8; Playbook-biased >= 0.65.
- **Layer 2 prompt budget**: 600 tokens max (2400 chars). Up to 20 candidates in classification prompt.
- **ADR-015**: User message content is never logged or stored in OTEL spans.
