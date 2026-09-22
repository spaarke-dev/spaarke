# Task 031 — ADR-028 Amendment A5: what was verified, and the two things the notes had wrong

> **Completed** 2026-09-04. Path **B**, deliberately **narrow**.
> Outputs: `.claude/adr/ADR-028-spaarke-auth-architecture.md` (A5 added + one A2 clause pointered), `.claude/CHANGELOG.md`.
> ⚠️ The amendment is numbered **A5**, not "A2 amendment" — ADR-028 already carried A1–A4. The task title's
> "ADR-028 A2 amendment" describes *what it amends*, not what it is called.

---

## 1. The escalation trigger was evaluated and did NOT fire

The POML trigger: *"If ADR-028's current text contains a rule that would ALSO have to change to
sanction impersonated root sets (beyond the A2 derivation sentence and A3's plane description) — i.e.
the amendment cannot stay narrow — STOP and escalate rather than widening it."*

**The amendment stayed narrow, and this is mechanically demonstrable.** Across the whole 428-line ADR,
`git diff` shows **exactly one modified line** (everything else in the diff is the additive A5 section):

```
- **MUST** resolve the workforce-authenticated caller to a **principal** — a `systemuser` (→ ADR-034 membership) or, …
```

That is the A2 derivation clause — precisely the sentence the task authorises changing. A3's plane
description needed **no** edit: A3 describes plane *selection* and the Tier-1/Tier-2 split, neither of
which depends on how a systemuser's root set is computed.

## 2. Both negative criteria verified by diff, not by assertion

| Criterion | How verified | Result |
|---|---|---|
| C3 — OBO prohibition (A2/A3) textually unchanged | The five `no OBO` sites (lines 50, 72, 83, 121, 251) checked present; diff checked for any removed OBO line | ✅ The only "removed" line containing *OBO* is the derivation clause, whose trailing phrase *"No Dataverse seat / OBO is required for read/download"* is preserved **byte-identically** in the replacement |
| C4 — CIAM/contact plane derivation unchanged | The contact branch of the modified line extracted from both sides of the diff and compared | ✅ **Byte-identical** |

**A self-caught deviation on C4.** I first added `; **unchanged by A5**` to the *contact* branch of that
clause. It was well-intentioned — it says out loud that A5 doesn't touch contacts — but it modified
text the criterion protects, to state something A5's own "Scope + preserved invariants" section already
says. **Reverted**, so the contact branch is byte-identical. The pointer on the *systemuser* branch was
kept: without it, a reader of A2 applies a rule A5 has superseded.

## 3. Two things the project's own notes get WRONG — both now recorded in the ADR

Verified in source, not taken from prose. (Consistent with this project's eleven prior instances.)

### 3a. `MSCRMCallerID` takes the `systemuserid`, NOT the AAD `oid`

`notes/access-model-decision.md` pairs the header with the AAD oid. That is **incorrect** — and the
live helper's own XML doc says so explicitly (`DataverseImpersonation.cs:20-21`):

> *"`notes/access-model-decision.md` pairs `MSCRMCallerID` with the AAD oid — that pairing is incorrect
> per MS Learn; `MSCRMCallerID` takes the `systemuserid`. This helper uses the correct pairing."*

The code is right; the note is wrong. Recorded as a MUST in A5 so the next implementer reads the
correct pairing in the ADR rather than the wrong one in the note.

### 3b. ⚠️ The fail-closed is in the READ METHOD, not in the helper

Design §4.4 says *"`RetrieveMultipleImpersonatedAsync` refuses `Guid.Empty` and cannot silently degrade
to app-only."* **That claim is true** — and it is true of the *read method*, which matters:

| Layer | Behaviour on `Guid.Empty` |
|---|---|
| `DataverseWebApiService.RetrieveMultipleImpersonatedAsync:978` | **throws** — *"refusing to issue an app-only query on the access-scoped read path (fail closed)"* ✅ |
| `DataverseImpersonation` (the header helper) | **adds no header** — the request proceeds **app-only** ⚠️ |

The helper is generic and deliberately permissive; the access-scoped read path is where the refusal
lives. So the guarantee holds **for the current call path** but is **not intrinsic to impersonation**:
a *new* impersonated call site that sets the header via the helper and skips the read method would
silently issue an **unscoped app-only query** — returning every row, on the exact path whose purpose is
to return fewer. That is a disclosure, and it would look like success.

A5 therefore requires any new access-scoped impersonated path to carry its **own** equivalent refusal,
and names the **NFR-04 negative canary** (task 034) as the standing guard — impersonated reads must
return *strictly fewer* rows than app-only, and **equality fails the build**, because equality is the
exact signature of this degradation.

## 4. The broker-only reading, and why it is in the ADR rather than in notes

Investigation 08 §6 condition 6 required this recorded in the ADR. The reasoning:

- Broker-only is defined by the code implementing it — `AccessibleRecordSetService.cs:22-24`:
  *"reads … APP-ONLY against the already-resolved principal. **No caller-token exchange (no OBO)**."*
- An impersonated read uses the **BFF's own app-only credential** plus a header naming the user to
  scope to. The caller's token is never exchanged, never forwarded, and need not exist at Dataverse.
- Therefore impersonation **satisfies** broker-only as written. It is not an exception to it.

It belongs in the ADR because a future reader meeting the word "impersonation" on a plane whose
defining invariant is "no OBO" must otherwise re-derive whether the two conflict — and may reasonably
guess wrong, in the direction of either blocking correct work or sanctioning an actual token exchange.

## 5. Concise-only, again

No `docs/adr/ADR-028-*.md` exists — re-confirmed 2026-09-04, consistent with the notes A2, A3 and A4
each left. The concise ADR is canonical for ADR-028 and now carries **A1–A5**. Creating a full
counterpart was declined for the same reason A2 declined it: scope creep contradicting the standing note.

## 6. What this unblocks

Task **035** (`ImpersonatedRootSetSource` + per-user cache) and task **036** (the FR-20 swap) can now
land sanctioned. 036 remains **blocked** independently by the NFR-04 canary having never run against a
real tenant (no non-admin canary user exists in dev) — that is open owner decision **B**, unchanged by
this amendment.
