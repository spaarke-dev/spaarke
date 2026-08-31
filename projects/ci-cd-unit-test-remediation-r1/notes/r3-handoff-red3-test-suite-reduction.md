# Handoff: r3 RED-3 → `ci-cd-unit-test-remediation-r1` (BFF test-suite reduction)

> **From**: code-quality-and-assurance-r3 (COMPLETE) · **To**: ci-cd-unit-test-remediation-r1 (owns test-suite work)
> **Date**: 2026-08-15

## Why this is yours

r3's post-program review quantified the BFF unit-test debt and mapped it to your remit (CICD-083..085,
which targeted ≤3,500 BFF unit tests but was **not achieved** — the gap is ~7,000 tests). Rather than open a
parallel effort, r3 hands you the grounded evidence to fold into your next wave.

## The evidence (measured at HEAD)

- **BFF unit tests ≈ 10,415** vs the ADR-038 target **≤3,500** (integration-heavy ~70/30 shape).
- **~1,922 `Mock<` usages in `tests/unit`** vs ~603 in integration (inverted from the ideal). 332 unit test
  files use `Mock<`.
- **Verified-clean — do NOT chase**: `Mock<HttpMessageHandler>` (ADR-038 B1 ban) = **0** real usages (all
  hits are compliance comments); CS1998 async-without-await = 0. The debt is **B7/B9/B15-class**
  (all-mocks-trivial, pass-through, high setup-to-assertion), not the transport-mock class.

## Recommended approach (from the seed)

Full analysis: **`code-quality-and-assurance-r3/notes/red-item-analyses/RED-3-test-suite-reduction.md`**.
Summary: staged conservative `/test-diet` in **whole-suite mode**, deleting in waves by ban-class (B9 → B7 →
B15 → B6/B16), each wave its own PR with a full `dotnet test` after, backfilling one integration/contract
test at a KEEP path where a deletion exposes a real branch gap. PATH-VIOLATION-PROTECTED guard + reviewer
confirmation on every `git rm`.

## Coordination note

Sequence the God-class decomposition projects (`speadmin-decomposition-r1`, `chatendpoints-decomposition-r1`)
to land alongside/after your waves so their test churn is absorbed once, not twice.
