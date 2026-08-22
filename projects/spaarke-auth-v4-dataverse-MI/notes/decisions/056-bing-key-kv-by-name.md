# Task 056 — Bing key resolution, and the fabricated-results defect found on the way

> 2026-08-22. FR-E7. Outcome: **resolution changed per the constraint; criterion 1 NOT met and the
> reason is structural, not an oversight. The valuable finding was elsewhere.**

---

## 1. The defect that mattered: web search was inventing its sources

`WebSearchHandler` answered a missing Bing API key like this:

```csharp
if (string.IsNullOrWhiteSpace(apiKey))
{
    var mockResults = GenerateMockResults(count);
    return BuildToolResult(tool, query, mockResults,
        degradationNote: null,          // <- nothing marks these as invented
        ...);
}
```

`GenerateMockResults` returns **fabricated search results with real-looking URLs** — a mix of
plausible `https://www.example.com/...` links and genuine `learn.microsoft.com` ones — which
`BuildToolResult` wraps in citation envelopes tagged `SourceType="web"` and the frontend renders as
citations. With `degradationNote: null` there is **nothing** distinguishing them from real results,
to the user or to the LLM consuming the tool output.

**This was not an edge case on dev — it was the only path web search ever took there.** Measured
2026-08-22: `spaarke-bff-dev` has **no `BingSearch__ApiKey` app setting** (the only Bing settings are
`BingGrounding__Enabled=false` and `BingGrounding__BingConnectionName=not-configured`), and
**`spaarke-spekvcert` contains no `BingSearch-ApiKey` secret**. The template references
`secrets/BingSearch-ApiKey`, which does not exist.

Feeding invented sources to an LLM and to a citation UI is a grounding hazard, and it is silent by
construction. **Fixed**: no key now returns **empty results plus an explicit degradation note**.

The second mock site — the concurrency-limit fallback — had the same problem more subtly: its note
read *"Results shown are from a fallback source"*, which describes a degraded-but-real source rather
than invented content. Also changed to empty + an honest note.

**This is not the fail-fast conversion FAILURE-MODES AP-7 warns about.** The graceful path is
preserved: the chat turn still succeeds, the tool still returns a result. It just stops inventing
evidence. Mock results remain reachable for local development behind an explicit
`BingSearch:UseMockResults` opt-in, defaulting to `false`.

## 2. The forcing function

New: **`tests/Spaarke.ArchTests/FabricatedResultGuardTests.cs`** (3 tests) —
`GenerateMockResults` must be invoked from exactly one call site, that site must be gated on the
opt-in, only the genuine-success path may pass `degradationNote: null`, and the three config keys
must stay declared as constants.

A structural guard rather than a behavioural test because the failure mode is invisible: a
behavioural test would have to already know to look for fabricated citations. This is the same
instrument as `CredentialGuardTests` / `CredentialCensusTests`, pointed at invented evidence instead
of credentials.

**Controls, per `tests/CLAUDE.md` "Structural fitness functions":**

- **Negative control — demonstrated, not assumed.** Seeding an ungated `GenerateMockResults(count)`
  in place of the gated ternary produced:
  *"The single GenerateMockResults call site must be gated on the explicit BingSearch:UseMockResults
  opt-in. Found: line 315: GenerateMockResults(count),"* — then reverted, 52/52 green.
- **Positive control — and it caught my own error.** The first version of the degradation-note rule
  banned `degradationNote: null` outright. It failed immediately on **line 365, the genuine success
  path**, which legitimately has nothing to disclose. The invariant is not "never null" but "the
  undisclosed path is unique and is not the fabricated one". **I fixed the test, not the code** —
  a guard that flags the code it protects is a guard that gets deleted rather than obeyed.

## 3. 🔴 Criterion 1 cannot be met by the pattern the constraint mandates

| | |
|---|---|
| **Criterion 1** | *"The Bing key value is never bound into configuration; only its secret name is."* |
| **Constraint** | *"Copy the LlamaParseClient KV-by-name pattern; do not invent a third resolution style."* |

**These conflict.** `LlamaParseClient.ResolveApiKey()` reads `_configuration[secretName]` — the value
**is** in configuration; only the *key name* is indirected. Its own comment says so: *"the actual
secret value is injected into IConfiguration by Azure App Service / Key Vault at startup."*

And the indirection cannot fix that, because **App Service resolves a Key Vault reference INTO the
application's configuration** whichever naming scheme is used. The Bing key is *already* a Key Vault
reference in the template today.

The only in-repo pattern that genuinely keeps a secret out of `IConfiguration` is a runtime
`SecretClient` fetch (`KnowledgeDeploymentService.GetApiKeyFromKeyVaultAsync`) — which adds a Key
Vault round-trip to the web-search path, the latency this task's escalation trigger names.

**Decision: follow the constraint, and say plainly that criterion 1 is unmet.** The constraint is the
more specific instruction and names its reference implementation; the criterion appears to have been
written from an assumption about what that implementation does. Implementing `SecretClient` instead
would have met the criterion's words while violating an explicit constraint, and would have added a
per-search KV round-trip for a benefit nobody asked to trade latency for. **What the indirection does
buy is real but small**: the secret's *name* becomes per-environment configuration instead of a
hard-coded key, and a configured-but-unresolvable name now logs an actionable error naming the key
that failed, instead of looking like an absent-key no-op.

**Recommended for the owner**: either accept the resolution-style change as the deliverable and
re-word criterion 1, or schedule the `SecretClient` variant as its own task with the latency
trade-off made explicitly.

## 4. Census correction (again)

The POML names *"`_configuration["BingSearch:ApiKey"]` at WebSearchHandler.cs:283 and :504."*

There is **one** config read, at `:283`. Line `:504` is inside `CallBingApiAsync(..., string apiKey, ...)`,
which receives the value as a **parameter** — it is the consumer, not a second resolution site.
Minor, but it is the third POML in this workstream whose site count did not survive a grep (053: 2
claimed vs 7 real; 054: 3 claimed, 1 already done elsewhere).

## 5. Verification

| Criterion | Status | Evidence |
|---|---|---|
| The Bing key value is never bound into configuration; only its secret name is | ❌ **NOT MET — structurally impossible via the mandated pattern** | §3. Stated rather than quietly redefined |
| Web search still returns results | ✅ | Real-key path untouched; `dotnet build` clean, suite green. Behaviour changes **only** where there was no key — and there it stopped returning invented ones |
| Negative case: a missing secret produces an actionable error, not a silent empty result set | ✅ **and the real problem was worse** | It was a silent **fabricated** result set (§1). Now: empty + explicit degradation note; a configured-but-unresolvable secret name logs an error naming the key |
| LlamaParse is unchanged | ✅ | Not touched |
| Bing key retained (Group 1 carve-out, constraint) | ✅ | No attempt to eliminate it; genuinely third-party, no Entra option |
| Build + suites | ✅ | build 0 errors · unit **10,603 / 0** (97 skipped) · ArchTests **52 / 52** (+3) · auth seams 60/60 |
| Publish size (CLAUDE.md §10) | ✅ | **44.99 MB** incl. PDBs / 215 files — **delta 0.00 MB**, ceiling 60 |
| CVE | ✅ | No vulnerable packages; no package added |
| Live environment changes | ✅ **none** | Read-only Azure queries only |

## 6. Carried forward

- **Owner** — criterion 1 (§3): accept the resolution change, or schedule the `SecretClient` variant.
- **Task 033** — `BingSearch-ApiKey` does not exist in `spaarke-spekvcert`; the template's Key Vault
  reference to it is dead. **Fourth instance** of the workstream's recurring pattern, after
  `Analysis:PromptFlowKey` (055), `AiSafety:ContentSafety:ApiKey` (050) and
  `AiSearch:ApiKeySecretName` (053).
- **Task 090** — worth asking whether other tool handlers fabricate fallback content the way web
  search did. This one was found incidentally while changing an unrelated line; nothing systematically
  looks for it, and `FabricatedResultGuardTests` currently guards exactly one file.
