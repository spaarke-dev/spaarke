# Flaky-Quarantined Ledger — `sdap-bff.api-test-suite-repair`

> **Purpose**: Track tests marked `[Trait("status", "flaky-quarantined")]` and Skip'd because they are non-deterministic with environmental causes (e.g., timing-sensitive, network-dependent, cross-test state leakage). Per project §6.2, these tests CANNOT remain `Failed` at project close — they are Skip'd with a fix-by date and environmental-cause description.
>
> **Schema**: Each row identifies the test, the non-determinism source, the environmental cause, and a fix-by date.

---

## Status (2026-05-31, task 028 close)

**Zero entries.** The 51 residual `Failed` tests at Phase 2+3 close were ALL assertion-level failures with deterministic causes (production-side DI binding gaps, endpoint behavior drift, sort-stability bugs, fixture text drift). None exhibited non-determinism or environmental sensitivity. Per task 028 disposition: all 51 went to `real-bug-pending-fix` via RB-T028-01..08; none went to `flaky-quarantined`.

If future Phase 4 tasks (e.g., task 084 triple-run validation) surface new non-deterministic failures, append entries below following the schema in the predecessor `sdap-bff-api-remediation-fix` project's flaky-ledger format.

---

## Entry template (for future use)

```markdown
## FQ-T<NNN>-<NN> — <one-line description of non-determinism>

| Field | Value |
|---|---|
| **Bug ID** | FQ-T<NNN>-<NN> |
| **Date filed** | YYYY-MM-DD |
| **Filing task** | Task <NNN> (<phase / sub-phase>) |
| **Test file** | <path to test .cs file> |
| **Tests Skip'd** | <list of test methods> |
| **Non-determinism source** | <timing / network / cross-test state leakage / shared singleton / etc.> |
| **Environmental cause** | <Windows-vs-Linux / under-load timing / CI vs local / etc.> |
| **Fix-by date** | YYYY-MM-DD (typical: 60-day target) |
| **Severity** | LOW / MEDIUM / HIGH |
| **Recommended remediation** | <specific environmental fix or test isolation strategy> |

### Detail

<diagnostic description + reproducibility info>
```

---

*This ledger is required at Phase 2+3 exit gate (per [`design.md`](../design.md) §6.2). Empty at task 028 close is a valid state.*

---

## Finalization (2026-05-31 by task 085)

- **Total entries**: 0
- **FR-28 satisfaction**: ✅ ledger exists with documented zero-entry rationale; schema reference preserved for future entries
- **§6.2 binding**: ✅ no flaky-quarantined disposition was used; all 51 residual unit Failed tests routed to `real-bug-pending-fix` per task 028's deterministic-cause classification
- **Task 084 triple-run validation**: did NOT surface any new non-deterministic failures; this ledger remains empty at project close
- **Reconciliation**: 0 entries contributes 0 to the exit ledger touched-test sum
