# NDA-REVIEW Advisory Eval Harness

> **Origin**: `ai-advanced-capabilities-nda-r1` task 050 (spec NFR-01)
> **Grades**: task 020's NDA-REVIEW Action (`infra/dataverse/actions/nda-review.action.json`)
> **Standard measured against**: [`projects/ai-advanced-capabilities-nda-r1/notes/spaarke-nda-standard-baseline.md`](../../projects/ai-advanced-capabilities-nda-r1/notes/spaarke-nda-standard-baseline.md) (B1-B16)
> **ADR-038**: this is an OBSERVATION harness (reports scores). It is NOT part of the .NET
> unit/seam pyramid and does not use the 7 KEEP-path mechanism — it is the project's own
> graduation instrument for proving and guarding the north star (advisory output quality
> >= a strong general LLM, empirically, not asserted).

## What this is NOT

This directory is distinct from `tests/integration/contract/Eval/` (the C#/xUnit golden-utterance
**dispatch** eval — "does the right utterance route to the right capability?", owned by task 051
for the NDA card/NL-intent cases). This harness grades a different question: **given that
NDA-REVIEW was dispatched, is its output good and honestly cited?** Two different eval families,
two different mechanisms, both required for graduation (see task 051's note: "Both must be green
for graduation.").

It is also distinct from `infrastructure/ai-foundry/evaluation/legal-eval-config.yaml`, a
general-purpose legal-analysis eval (case/statute citation FORMAT checking, e.g. "347 U.S. 483").
This harness's `citation_accuracy` metric checks a different, stricter, machine-exact invariant:
verbatim NDA-quote-match + real-B-clause-match, per the ADR-039 advisory grounding rules baked
into `nda-review.action.json`'s systemPrompt.

## Layout

```
tests/eval/
├── legal-eval-config.yaml         # cases + expected signals + the NFR-01 rubric
├── README.md                      # this file
├── cases/
│   ├── nda-01-clean-mutual.md               # closed-set NDA #1 — clean baseline
│   ├── nda-02-narrow-ci-oneway.md            # closed-set NDA #2 — marking-only CI definition (B3)
│   ├── nda-03-hidden-restrictive-covenant.md # closed-set NDA #3 — smuggled non-solicit (B11) + no injunctive relief (B13)
│   ├── nda-04-short-term-broad-residuals.md  # closed-set NDA #4 — short confidentiality period (B8) + broad residuals incl. trade secrets (B10)
│   ├── nda-05-drafting-errors.md             # closed-set NDA #5 — drafting-integrity errors (B16) + missing backup carve-out (B9)
│   ├── nda-06-critical-landmine.md           # closed-set NDA #6 — strict liability (B5) + automatic injunction/indemnity (B13) + asymmetric forum (B15) + hidden non-compete (B11)
│   ├── neg-01-non-nda-lease.md               # negative case — non-NDA document (residential lease)
│   ├── neg-02-unreadable-input.txt           # negative case — garbled/OCR-failure input
│   └── neg-03-unauthorized-user.md           # authorization case — scenario descriptor (no documentText)
├── metrics/
│   ├── citation_accuracy.py        # the citation-accuracy metric (promptflow custom-metric convention)
│   └── load_eval_config.py         # parses + structurally validates legal-eval-config.yaml
└── fixtures/
    ├── sample-nda-review-output.json          # authored fixture output (1 correct + 2 deliberately-broken findings) graded against nda-02
    └── test_citation_accuracy_offline.py       # offline proof the metric runs + discriminates pass/fail (no Azure OpenAI call)
```

## The closed set (9 cases)

**6 NDAs**, each with documented, deliberately planted deviations from the Spaarke standard
(12 planted issues total across NDA-02..NDA-06 — NDA-01 is the clean baseline and is not counted):

| Case | Planted issue(s) | Expected `overallRisk` |
|---|---|---|
| NDA-01 | none (clean baseline; ≤1 trivial B16 drafting nit tolerated) | Low |
| NDA-02 | B3 — marking-only CI definition | High |
| NDA-03 | B11 — hidden non-solicit; B13 — no injunctive relief | High |
| NDA-04 | B8 — 6-month confidentiality survival; B10 — broad residuals incl. trade secrets | High |
| NDA-05 | B16 ×2 — inconsistent defined term + broken cross-reference; B9 — no backup carve-out | Medium |
| NDA-06 | B5 — strict liability; B13 — automatic injunction/indemnity; B15 — asymmetric forum; B11 — hidden non-compete | Critical |

**3 negative/authorization cases** (the POML's exhaustiveness constraint — not just happy-path):

| Case | What it tests | Expected behavior |
|---|---|---|
| NEG-01-non-nda | Non-NDA document (residential lease) | Decline / empty findings, Low risk — see "Known limitation" below |
| NEG-02-unreadable | Garbled/OCR-failure input | Graceful error upstream, or empty findings if it reaches the Action — never a fabricated analysis |
| NEG-03-unauthorized | Caller without the AI-analysis entitlement | 401/403 at the endpoint filter; the Action never executes |

## The advisory-quality rubric (NFR-01)

Full detail in `legal-eval-config.yaml`'s `rubric` block. Summary — **all four must hold**:

1. **Planted-issue coverage ≥ 90%** (≥ 11 of 12 caught: `standardRef` matches the planted B-clause
   AND `riskLevel` ≥ the planted severity floor).
2. **Citation accuracy ≥ 95%** (`metrics/citation_accuracy.py` — verbatim quote + real B1-B16 ref).
3. **Hallucination guard: 0 tolerance** — any High/Critical finding on NEG-01 is an automatic FAIL.
4. **Overall-risk-band accuracy ≥ 83%** (≥ 5 of 6 NDAs within ±1 severity step of the expected band).

**Why this is a comparison, not a vibe check**: the 12 planted issues are exactly the deviations a
competent legal reviewer — or a frontier general-purpose LLM given the same standard + document —
would be expected to flag. NDA-REVIEW's measured recall against this ground truth is the
reproducible proxy for "at least as good as a strong general LLM," standing in for a live
side-by-side run until Azure OpenAI access is available (see below). The same `cases:` list is
reusable verbatim for that future head-to-head comparison run.

## Known limitation (surfaced, not silently patched)

NEG-01 exposes a real gap: `nda-review.action.json`'s systemPrompt has no explicit "this is not an
NDA — decline" instruction; it only forbids *ungrounded* findings. A model applying the B1-B16
rubric literally to a non-NDA document could, in principle, flag many "missing clause" findings
that are technically grounded (the clauses really are absent) but conceptually wrong (the document
was never an NDA). The rubric's `hallucination_guard` dimension is the mechanical backstop that
would catch the worst form of this (any High/Critical finding on NEG-01 fails the rubric outright),
but a full fix — an explicit document-type gate in the Action's systemPrompt or a pre-flight
classifier — is an improvement to task 020's Action, not to this eval harness, and is out of scope
here (this task owns eval assets only, per its HARD RULES). Recommend a follow-on task if the live
run (below) shows NEG-01 producing spurious findings.

## Running the offline citation-accuracy proof (works today, no Azure OpenAI needed)

```bash
# Validate the eval config parses + is internally consistent
python tests/eval/metrics/load_eval_config.py

# Prove the citation_accuracy metric runs and correctly discriminates pass/fail against an
# authored fixture (1 correct finding + 2 deliberately broken findings)
python tests/eval/fixtures/test_citation_accuracy_offline.py

# CLI form of the metric against any (output.json, source-document) pair:
python tests/eval/metrics/citation_accuracy.py tests/eval/fixtures/sample-nda-review-output.json tests/eval/cases/nda-02-narrow-ci-oneway.md
```

Both scripts are plain Python (stdlib + PyYAML for the loader); no promptflow SDK install required
(`citation_accuracy.py` guards the `promptflow.core.tool` import and degrades to a no-op decorator
when the SDK isn't present — see the module docstring).

## Running the LIVE eval (ENV-BLOCKED in this repo — no Azure OpenAI credentials)

The live run calls the NDA-REVIEW Action once per case (via the BFF's generic Action-execution
endpoint, `/api/ai/analysis/execute`, actionCode `nda-review`, `documentText` = the case file
contents) against the Reasoning-tier Azure OpenAI deployment (task 013), captures the
`{overallRisk, flaggedSections[]}` output per case, then scores it against this config:

```bash
# 1. Execute NDA-REVIEW once per NDA case (6 calls) against a live, authenticated BFF instance:
for case in nda-01-clean-mutual nda-02-narrow-ci-oneway nda-03-hidden-restrictive-covenant \
            nda-04-short-term-broad-residuals nda-05-drafting-errors nda-06-critical-landmine; do
  curl -s -X POST "$BFF_BASE_URL/api/ai/analysis/execute" \
    -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
    -d "{\"actionCode\": \"nda-review\", \"documentText\": $(python -c "import json,sys;print(json.dumps(open('tests/eval/cases/${case}.md',encoding='utf-8').read()))")}" \
    -o "tests/eval/fixtures/live-${case}-output.json"
done

# 2. Also execute NEG-01 and NEG-02 the same way (NEG-03 is authorization-only; see its case file).

# 3. Score each output against its source document:
for case in nda-01-clean-mutual nda-02-narrow-ci-oneway nda-03-hidden-restrictive-covenant \
            nda-04-short-term-broad-residuals nda-05-drafting-errors nda-06-critical-landmine \
            neg-01-non-nda-lease; do
  python tests/eval/metrics/citation_accuracy.py \
    "tests/eval/fixtures/live-${case}-output.json" "tests/eval/cases/${case}.md"
done

# 4. Manually (or via a follow-on aggregation script) roll the per-case citation_accuracy scores
#    and the planted-issue coverage / overall-risk-band checks up into the four rubric dimensions
#    in legal-eval-config.yaml, and report pass/fail against each threshold.
```

**Expected passing output** (once Azure OpenAI credentials are available and this is actually
run): each `citation_accuracy.py` invocation prints a JSON report with `"score" >= 0.95` and exits
0; the aggregate rubric report shows all four dimensions passing (planted-issue coverage ≥ 90%,
citation accuracy ≥ 95%, zero High/Critical findings on NEG-01, risk-band accuracy ≥ 83%).

**Why this is env-blocked today**: no Azure OpenAI Reasoning-tier deployment credentials /
`$BFF_BASE_URL` / `$TOKEN` are available in this development environment (task 013's Reasoning
deployment provisioning is itself flagged ⛔ env-blocked in `current-task.md`). This harness is
fully authored and offline-verified (above); only the network call to Azure OpenAI is blocked.
