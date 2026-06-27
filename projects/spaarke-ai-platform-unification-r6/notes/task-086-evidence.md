# Task 086 Evidence — Natural-Language Regression Test (NFR-11)

> **Task**: 086 / D-D-07
> **Wave**: Phase D-G2 (parallel: 084 / 085 / 086)
> **Status**: ✅ Complete
> **Date**: 2026-06-18
> **Rigor**: STANDARD (POML rigor-hint; validation-only; no production code surface)

---

## Scope (per POML <prompt>)

Binding regression suite for spec **FR-54** + **NFR-11**: natural-language requests
MUST continue to work alongside the new Pillar 8 slash vocabulary. R5 `CapabilityRouter`
3-tier classification (Layer 1 keyword → Layer 2 embedding → Layer 3 LLM) MUST remain
**UNCHANGED** by Pillar 8's parser layer.

Wave D-G1 closed `ConversationPane.tsx` such that:
- `parseCommandIntent(messageText)` runs first
- `decorateSoftSlashBody(...)` runs ONLY when `intent.isSoftSlash === true`
- `ReferenceResolver.resolveAll(...)` runs ONLY when `intent.references.length > 0`

For pure natural language, ALL three conditions are false → outbound BFF payload is
structurally identical to the pre-Pillar-8 baseline. This suite asserts that invariant
for the 4 binding NL inputs.

---

## Deliverables

| Item | Path |
|---|---|
| Test file (NEW) | `src/solutions/SpaarkeAi/src/components/conversation/__tests__/natural-language-regression.test.ts` |
| Evidence | `projects/spaarke-ai-platform-unification-r6/notes/task-086-evidence.md` (this file) |
| TASK-INDEX entry | row 086 🔲 → ✅ |
| current-task.md | 086 marked closed |

**Production code modified**: NONE. (Test-only per POML scope.)

---

## Test surface (4 NL inputs per POML)

| # | Input | NL equivalent of | Baseline routing |
|---|---|---|---|
| 1 | `"summarize this document"` | `/summarize` | CapabilityRouter Layer 1 keyword → SUM-CHAT playbook |
| 2 | `"draft a reply to the opposing counsel"` | `/draft` | CapabilityRouter Layer 1 keyword → draft-intent path |
| 3 | `"what's the matter status?"` | (none; conversational query) | CapabilityRouter no match → agent stays conversational (NFR-01) |
| 4 | `"make it shorter"` | (none; refinement) | CapabilityRouter no match → conversational refinement (NFR-01) |

---

## Coverage matrix

| Input | command:null | references:[] | isHardSlash:false | isSoftSlash:false | decorateBody no-op | input purity | NFR-01 inert |
|---|---|---|---|---|---|---|---|
| summarize this document | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | n/a (Layer 1 keyword) |
| draft a reply to the opposing counsel | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | n/a (Layer 1 keyword) |
| what's the matter status? | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| make it shorter | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

---

## Test execution

Command:
```
cd src/solutions/SpaarkeAi && npx jest src/components/conversation/__tests__/natural-language-regression.test.ts
```

Result:
```
Test Suites: 1 passed, 1 total
Tests:       50 passed, 50 total
Snapshots:   0 total
Time:        0.727 s
```

---

## Test structure breakdown (50 tests total)

| describe block | Test count | What it covers |
|---|---|---|
| Parser returns command:null for NL inputs | 16 | 4 inputs × 4 invariant assertions (command, references, slash flags, rawText) |
| toCommandIntent returns null for NL | 4 | 4 inputs × null return |
| decorateBody is a no-op for NL | 12 | 4 inputs × 3 invariants (no commandIntent, message preserved, field-set parity) |
| decorateBody is pure (no mutation) | 8 | 4 inputs × 2 purity invariants |
| NFR-01 conversational primacy | 4 | sanity + 2 conversational inputs × inert + parser-purity follow-up |
| Positive anchor (suite integrity) | 2 | `/summarize` DOES decorate; `/clear` does NOT |
| Canonical NFR-11 Intent shape | 4 | 4 inputs × exact Intent shape match |

---

## Acceptance criteria verification (per POML <acceptance-criteria>)

| Criterion | Status | Evidence |
|---|---|---|
| Regression test file exists; covers 4+ NL inputs | ✅ | File present; `NATURAL_LANGUAGE_CASES` enumerates exactly 4 |
| For each input, parser returns `command: null` + `references: []` | ✅ | 8 parametrized assertions (4 inputs × 2 fields), all green |
| For each input, chat-endpoint payload has no `commandIntent` set (mock fixture) | ✅ | 4 parametrized assertions via `decorateBody`, all green |
| Conversational follow-up ("make it shorter" after prior response) works without invoking any tool (NFR-01 preserved) | ✅ | `NFR-01 conversational primacy` describe block — verifies all parser + decoration state is inert |
| All tests green via Jest | ✅ | 50/50 passed; 0 failures |
| No regression in existing test suite | ✅ | Test is additive; uses public exports only (`parse`, `decorateBody`, `toCommandIntent`); does NOT modify production code |
| BFF publish-size delta = 0 MB | ✅ | Frontend test-only file; no `src/server/` changes; no NuGet changes |

---

## Constraints honored

| Constraint | Source | Status |
|---|---|---|
| Natural language still works alongside slashes | spec FR-54 | ✅ verified via 4 NL inputs |
| Backward compatibility — existing CapabilityRouter behavior preserved | spec NFR-11 | ✅ verified via field-set parity + decorateBody no-op |
| Conversational primacy — agent stays conversational for non-slash inputs | spec NFR-01 | ✅ verified for inputs #3 + #4 (inert Pillar 8 layer) |
| BFF publish-size delta = 0 MB (test-only) | ADR-029 | ✅ no BFF surface touched |
| No new ADRs | NFR-03 | ✅ test-only; no architectural surface change |

---

## Files NOT modified (per task scope)

- ❌ `ConversationPane.tsx` — owned by task 085 (parallel)
- ❌ Any Pillar 8 module (`CommandRouter.ts`, `SoftSlashRouter.ts`, `HardSlashExecutor.ts`, `ReferenceResolver.ts`, `intentMatcher.ts`) — already shipped in Wave D-G0/D-G1
- ❌ `CapabilityRouter.cs` — already extended by task 082
- ❌ Any other test file in `__tests__/`

---

## Notes for downstream (task 087 vertical-slice integration)

Task 087 gates on 084 + 085 + 086. From 086's perspective:
- The 4 binding NL inputs are now regression-protected at the unit/parser layer
- 087's vertical-slice should additionally verify that the BFF Layer 1 keyword path is reached
  and produces the expected playbook routing (out of scope for 086 — BFF integration concern)
- The positive anchor in 086 (`/summarize` does decorate) confirms the suite would catch a
  catastrophic regression in `decorateBody` that silently dropped ALL commandIntents

---

## Decisions made / surfaced

None. Pure validation-only task; no design decisions surfaced.

---

## Tool call count

~10 tool calls (well under 50-call stream-idle threshold).
