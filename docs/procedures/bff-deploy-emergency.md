# BFF Emergency Deploy Procedure

> **Last Updated**: 2026-05-31
> **Owner**: ralph.schroeder@hotmail.com (project owner; sole approver)
> **Scope**: Deploying the BFF API (`src/server/api/Sprk.Bff.Api`) to production under a declared emergency, bypassing the standard CI gate (`Build & Test (Debug)`, `Build & Test (Release)`, `Code Quality`).
>
> **Authority**: This document IS the emergency path. It replaces the removed `skip-tests` workflow_dispatch input from [`deploy-bff-api.yml`](../../.github/workflows/deploy-bff-api.yml) (removed per FR-10 / D-02 of project `sdap-bff.api-test-suite-repair`).
>
> **Related**:
> - Root [`CLAUDE.md`](../../CLAUDE.md) §10 — BFF Hygiene (binding governance)
> - [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — BFF pre-merge checklist
> - [`projects/sdap-bff.api-test-suite-repair/spec.md`](../../projects/sdap-bff.api-test-suite-repair/spec.md) FR-11
> - [`projects/sdap-bff.api-test-suite-repair/decisions/D-02-ci-gate-strict.md`](../../projects/sdap-bff.api-test-suite-repair/decisions/D-02-ci-gate-strict.md)
> - ADR-029 — BFF Publish Hygiene

---

## Purpose

The BFF API CI gate is enforced for administrators (`enforce_admins: true` on the 3 required status checks). Casual bypass is intentionally removed. **This procedure is the only sanctioned path to deploy the BFF when the CI gate cannot be made to pass in the time the situation requires.** It exists for true emergencies — not for "tests are flaky" or "we don't have time this sprint."

---

## When emergency deploy is justified

An emergency deploy MAY be declared if and only if **at least one** of the following is true:

- **(a) Security CVE actively exploited** — a known CVE affecting Spaarke's BFF surface is being exploited in the wild or in this tenant, and the fix is ready to ship.
- **(b) Production outage requiring rollback** — the BFF is degraded or down in production, a known-good prior commit exists, and reverting it restores service.
- **(c) Data-integrity incident** — the BFF is producing or persisting incorrect data with downstream contamination risk, and the fix or revert must ship before the next normal-process PR could complete.

**Explicitly NOT acceptable** (any one of these disqualifies an emergency declaration):

- "Tests are broken and we don't want to fix them this sprint."
- "The fix is small / obviously correct."
- "CI is slow today."
- "The release timeline slipped."

If the situation does not match (a)/(b)/(c), the answer is to repair the failing CI checks via normal process — no exceptions.

---

## Approver allowlist

| Role | Approver | Contact |
|---|---|---|
| Project owner (sole approver) | ralph.schroeder@hotmail.com | — |

**Backup approver**: currently NONE. If the project owner is unavailable, emergency deploy CANNOT proceed via this procedure. (Owner-unavailability is an unresolved question per [`projects/sdap-bff.api-test-suite-repair/spec.md`](../../projects/sdap-bff.api-test-suite-repair/spec.md) Unresolved Questions. Revisit after the first emergency surfaces the gap.)

Approval MUST be recorded as a comment on the incident issue (see template below) — verbal/Teams/email approvals do not satisfy this procedure.

---

## Procedure

1. **Declare the incident** — open a GitHub issue using the [incident-issue template](#incident-issue-template) below. Label: `bff-emergency`. The issue is the primary audit record.
2. **Obtain owner approval** — the project owner adds a comment to the issue with the literal text: `Approved: emergency deploy authorized — <reason summary>`. No other approver is accepted.
3. **Deploy via canonical path** — run [`scripts/Deploy-BffApi.ps1`](../../scripts/Deploy-BffApi.ps1) (the standard production deploy script). Do NOT re-introduce, re-implement, or replicate the removed `skip-tests` workflow_dispatch input. Do NOT temporarily relax `enforce_admins` on master branch protection. Do NOT push a hotfix that disables the gate.
4. **File the follow-up-fix issue** — within 1 business day of the deploy, open a follow-up GitHub issue (see [5-business-day clause](#5-business-day-follow-up-fix-clause) below). Link it from the incident issue.
5. **Close the incident issue** — only after the deploy is verified healthy AND the follow-up-fix issue has been filed and linked.

---

## 5-business-day follow-up-fix clause

Every emergency deploy MUST be followed by a remediation issue:

- **Label**: `bff-emergency-followup`
- **Due**: 5 business days from the emergency deploy timestamp
- **Scope**: the follow-up MUST close the test gap or process failure that made the emergency necessary. Examples: add the missing test that would have caught the CVE; repair the failing tests that blocked the normal-process PR; restore the CI gate to green for the deployed commit.
- **Owner**: the project owner is responsible for ensuring the follow-up closes on time. If it slips past 5 business days, the project owner files a `process-debt` ledger entry explaining why.

The intent is asymmetric: the emergency procedure is fast (one approval + one deploy), but the follow-up obligation is firm. This keeps the casual-bypass pattern from re-emerging.

---

## Incident-issue template

Open a GitHub issue with this body (copy verbatim, fill in fields):

```markdown
# BFF Emergency Deploy — <one-line incident description>

**Severity**: <P0 — outage | P1 — security | P1 — data integrity>

**Impact**:
<who is affected, what is broken, scope (tenants/users/data)>

**Justification** (matches "When emergency deploy is justified"):
- [ ] (a) Security CVE actively exploited
- [ ] (b) Production outage requiring rollback
- [ ] (c) Data-integrity incident

<paragraph: specific facts establishing the justification>

**Approver**: ralph.schroeder@hotmail.com
**Approval timestamp**: <UTC ISO-8601>
**Approval comment link**: <link to the "Approved:" comment on this issue>

**Deploy details**:
- Commit SHA deployed: <full SHA>
- Deploy timestamp (UTC): <ISO-8601>
- Deployed via: `scripts/Deploy-BffApi.ps1`
- CI gate status at time of deploy: <which check(s) red>

**Follow-up-fix issue**: <link — filed within 1 business day, due 5 business days>

**Labels**: `bff-emergency`
```

The follow-up-fix issue should be labeled `bff-emergency-followup` and reference this incident issue.

---

## Maintenance

Any change to the [Approver allowlist](#approver-allowlist) (adding a backup approver, transferring ownership) MUST update this file in a normal-process PR AND be announced in the `spaarke-dev` repository (a pinned discussion or repo announcement). Do not change the allowlist via emergency deploy — the allowlist itself is never an emergency.

Any change to the [When emergency deploy is justified](#when-emergency-deploy-is-justified) criteria requires owner sign-off AND an update to [`projects/sdap-bff.api-test-suite-repair/decisions/D-02-ci-gate-strict.md`](../../projects/sdap-bff.api-test-suite-repair/decisions/D-02-ci-gate-strict.md) reassessment-trigger section.

---

## References

- Root [`CLAUDE.md`](../../CLAUDE.md) §10 — BFF Hygiene (binding governance; task 084 of project `sdap-bff.api-test-suite-repair` updates §10 to point to this document)
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — BFF pre-merge checklist
- [`projects/sdap-bff.api-test-suite-repair/spec.md`](../../projects/sdap-bff.api-test-suite-repair/spec.md) — FR-11 (emergency procedure requirement)
- [`projects/sdap-bff.api-test-suite-repair/decisions/D-02-ci-gate-strict.md`](../../projects/sdap-bff.api-test-suite-repair/decisions/D-02-ci-gate-strict.md) — D-02 locked decision (CI gate strict)
- [`.claude/adr/ADR-029-bff-publish-hygiene.md`](../../.claude/adr/ADR-029-bff-publish-hygiene.md) — ADR-029 (related size-baseline ratchet; informational)
