# Phase 5 Deploy Evidence — Task 050 (FR-16)

> **Date**: 2026-06-26
> **Target**: `spaarke-search-dev` in `spe-infrastructure-westus2`
> **Operator**: spaarke-dev autonomous execution

---

## Outcome

**8 of 8 canonical indexes deployed to dev with all post-deploy invariants passing.**

```
$ GET /indexes?$select=name (via REST)
spaarke-discovery-index
spaarke-files-index
spaarke-insights-index
spaarke-invoices-index
spaarke-playbook-embeddings
spaarke-rag-references
spaarke-records-index
spaarke-session-files
```

```
$ pwsh scripts/ai-search/Deploy-AllIndexes.ps1 -Environment dev -VerifyOnly
Verify-only mode: asserting invariants against deployed indexes...
  Verifier: spaarke-files-index... [OK]
  Verifier: spaarke-discovery-index... [OK]
  Verifier: spaarke-records-index... [OK]
  Verifier: spaarke-rag-references... [OK]
  Verifier: spaarke-insights-index... [OK]
  Verifier: spaarke-session-files... [OK]
  Verifier: spaarke-invoices-index... [OK]
  Verifier: spaarke-playbook-embeddings... [OK]
Verify-only: all invariants pass.
```

Exit code: **0**.

---

## Iteration History (3 fixes needed during first deploy)

### Iteration 1 — All 8 failed (HTTP 400)

**Cause**: Spaarke schemas contain `"// rationale": "..."` comment-keys for NFR-09 override documentation. Azure AI Search REST API rejects unknown properties.

**Fix**: Added regex-based comment-key stripper to `Deploy-AllIndexes.ps1` (function `Remove-JsonCommentKeys`).

**Initial attempt failed**: First implementation used JSON parse → walk-and-strip → serialize via `ConvertTo-Json`. PowerShell's `ConvertTo-Json` UNWRAPS single-element arrays into objects (PowerShell quirk). This made `vectorSearch.algorithms` become an object instead of an array → Azure rejected with "A null value was found for the property named 'algorithms'".

**Final fix**: Switched to text-based regex strip (preserves array semantics).

### Iteration 2 — 7/8 passed, `spaarke-insights-index` failed (HTTP 400)

**Cause**: insights-index uses a different comment-key convention (`"_comment_": "..."`) vs other schemas (`"// rationale": "..."`).

**Fix**: Extended the regex stripper to handle both `//*` and `_comment_` patterns.

### Iteration 3 — 7/8 passed, `spaarke-insights-index` failed (HTTP 400) again

**Cause**: `evidence.fields[0].refType` had `sortable=true` + `facetable=true` (per task 010 schema property policy). But `evidence` is `Collection(Edm.ComplexType)` — Azure forbids sortable/facetable on fields inside multi-valued parents.

**Fix**: Updated schema `infrastructure/ai-search/spaarke-insights-index.json` with NFR-09 override + sortable=false, facetable=false on refType.

### Iteration 4 — Final post-deploy verifier surfaced 1 catalog drift

**Cause**: `Deploy-AllIndexes.ps1` `$Catalog` declared `playbook-embeddings.VectorFields = @('contentVector')` but the actual schema uses `contentVector3072`. Catalog vs schema drift.

**Fix**: Updated catalog vector field name to `contentVector3072`.

---

## Lessons Learned

1. **Schema property policy + ComplexType collections**: NFR-09's "default-enable-everything-Azure-allows" stance has a sub-rule: fields inside `Collection(Edm.ComplexType)` parents CANNOT be sortable/facetable (Azure-restricted). Future schemas must apply override per NFR-09 § Override discipline.

2. **PowerShell ConvertTo-Json + single-element arrays**: `ConvertTo-Json` unwraps single-element arrays. Text-based JSON edit (regex strip) is safer than parse-walk-serialize when the JSON has single-element arrays anywhere.

3. **Two comment-key conventions in Spaarke schemas**: `//<rationale>` (most schemas) AND `_comment_` (insights — Bicep-influenced). Stripper must handle both.

4. **Catalog vs schema drift**: post-deploy invariant verifier catches drift between the script's `$Catalog` declarations and actual deployed schema. This is exactly what NFR-02 was designed for.

---

## Performance

- Total deploy runtime: ~15 seconds (well under NFR-12 target of 30 min)
- Each PUT: ~1 second
- Verifier: ~5 seconds for 8 indexes

---

## Cross-References

- `scripts/ai-search/Deploy-AllIndexes.ps1`
- `docs/architecture/AI-SEARCH-INDEX-CATALOG.md` §4 (8-index catalog)
- `infrastructure/ai-search/spaarke-insights-index.json` (refType fix)
- `projects/spaarke-ai-azure-setup-dev-r1/spec.md` FR-16
- `projects/spaarke-ai-azure-setup-dev-r1/notes/deploy-allindexes-validation.md` (Phase 3 dry-run validation)
