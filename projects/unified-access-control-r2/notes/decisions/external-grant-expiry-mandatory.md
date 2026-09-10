# Decision record — external grant expiry becomes MANDATORY, bounded, and renewable

> **Status**: **ACCEPTED in principle by the owner, 2026-09-08. NOT IMPLEMENTED.**
> Scope, cap length and backfill remain open; see § Open questions.
> **Origin**: the owner's reframe of task 023's escalation — *"from a data control should we make
> expireDate mandatory and with a limit (similar to app reg certs/keys)? is this a security feature,
> not a bug? what type of maintenance debt does this cause? we could have a notification to the owner
> to renew."*

---

## 1. Two questions, kept apart

| | Verdict |
|---|---|
| Was the task-023 defect (H1) a bug? | **Yes, unambiguously.** The upsert told an operator it had bounded access when it had written nothing. Fixed in `6ce9c52c2`, independent of anything below. |
| Should expiry be MANDATORY with a maximum lifetime? | **Yes — a security control, not a bug fix.** This record is that decision. |

Keeping them apart matters: the bug fix was required whatever policy we land on, and the policy is not
a "further fix" to it.

## 2. Why mandatory expiry is right here — and stronger than the app-registration analogy

Azure caps app-registration secret/certificate lifetime because a long-lived credential is a standing
risk whose blast radius grows with time and whose holder may change unobserved. `sprk_externalrecordaccess`
grants share that shape and add two properties that make the case **stronger**:

1. **The grantee is outside our control boundary.** They are clients, opposing counsel, vendors —
   people whose employment, affiliation and adversarial posture can change without any signal reaching
   us. A firm gets acquired, a paralegal moves to the other side of a matter, an engagement ends. We
   find out never.
2. **The business reason is inherently time-bound.** External access exists for a matter, a deal, a
   discovery window. Access that outlives its reason is the single most common access-review finding.
3. **Nobody revokes.** Revocation requires acting on a NON-event — "this person no longer needs this"
   fires no trigger, appears in no queue, and is nobody's job. Expiry converts that non-event into a
   scheduled one, which is the entire mechanism.

## 3. ⚠️ The asymmetry that must shape the design

**An expired app credential breaks a SYSTEM. An expired human grant breaks a PERSON.**

| | App credential | External access grant |
|---|---|---|
| Failure is | loud, immediate, monitored | silent until someone tries to work |
| Discovered by | alerting | the locked-out external user |
| Reported through | an on-call channel | a channel they may no longer have |
| Cost of a bad moment | a broken integration | a missed filing deadline |

**Therefore the renewal notification is a PRECONDITION, not an enhancement.** Shipping the mandate
without it converts a silent over-permission problem into a silent lockout problem, and that is not
obviously the better trade. **Do not ship the cap before the notification.**

## 4. The design

### 4.1 Expiry is matter-bound first, calendar-capped second

The primary expiry semantic should be the **matter lifecycle**, not the calendar:

> A grant expires when its root record closes, **or** after the configured maximum, **whichever comes first.**

`ProjectClosureEndpoint` already deactivates every active `sprk_externalrecordaccess` for a project on
closure, so the lifecycle half largely exists. The calendar cap is the **backstop** for roots that stay
open for years — not the primary control.

This is not a stylistic preference: it is the difference between most grants dying naturally at closure
and every grant requiring a human renewal decision. It is the main lever on § 5's toil.

### 4.2 The cap is tenant-configurable, not a compile-time constant

Practice areas differ genuinely — a 30-day discovery window and a multi-year corporate engagement are
both legitimate. A hard-coded constant guarantees either a cap so long it does nothing or one so short
it is routinely overridden. Configure it with a conservative default; the value is a tenant policy.

### 4.3 Notify the GRANTING internal user / matter owner — never the grantee

The external grantee **cannot renew their own access**, so notifying them produces only social pressure
on the internal owner to extend. The notification goes to the person who can actually decide.

Infrastructure already exists — this is an extension, not new plumbing:

| Need | Existing |
|---|---|
| Scheduled sweep for expiring grants | `IScheduledJob` (ADR-004), the same contract `PlaybookSchedulerJob` uses |
| Delivery | `Services/Notifications/` — `OutboxService`, `SignalRDeliveryService` |
| Grant query surface | `ExternalGrantLifecycle` + task 007's `ExpiryPredicate` |

### 4.4 Renewal extends from TODAY, not from the old expiry

Otherwise repeated renewals drift: renewing a 90-day grant a week late yields 83 days, and the error
accumulates. Extending from today also makes "renewed" mean a fresh, deliberate decision.

## 5. Maintenance debt — stated honestly

1. **Toil scales as (active external grants × 1/cap period).** 500 grants at a 30-day cap ≈ 6,000
   renewal decisions a year; at a 1-year cap, 500. **The cap length IS the debt dial** — it is the
   number worth arguing about, and § 4.1's matter-binding is what keeps it survivable.
2. 🔴 **The notification becomes load-bearing, and its failure mode is SILENCE.** If the sweep job
   stops, nothing looks wrong until people begin losing access. We would need to monitor the monitor —
   a heartbeat or a "notifications sent today" signal — because *silence* and *nothing due* are
   otherwise indistinguishable. **This is the least obvious and most important debt item.**
3. **Renewal fatigue degrades the control into ceremony.** One-click renewal gets clicked. Work added,
   control not exercised. Mitigation: make short extensions cheap and long ones require a stated
   reason.
4. **Backfill is a mass-lockout event.** Existing unbounded grants must be either grandfathered or
   force-expired on a staged schedule with lead-time notice. This is the riskiest single step and needs
   its own plan, not a migration script.
5. **`DATE ONLY` + `ge` semantics.** `sprk_expiresdate` is Date Only and task 007 compares
   `>= today` in UTC, so a grant lapses at UTC midnight — mid-afternoon in Sydney. Pin this
   deliberately rather than discovering it in an incident.

## 6. This dissolves the task-023 escalation

Task 023 left open: *should clearing an expiry (date → null) be expressible?* The request cannot
distinguish an omitted field from an explicit `null`, and both readings are unsafe in opposite
directions.

**If expiry is mandatory, the question disappears.** There is always an expiry, `null` unambiguously
means "leave unchanged", and there is no clear-operation to design. That is a cleaner resolution than
answering it on its own terms — and it is why 023 stays `completed-with-escalation` until FR-33 is
scheduled, rather than being closed with a contract patch we would then remove.

## 7. Open questions for scoping

1. **Cap default** — what is the conservative default, and is it per-tenant only or also per-practice-area?
2. **Scope** — external grants only (recommended), or internal POA shares (tasks 060/061) too? The
   arguments differ: internal users are inside the control boundary and covered by joiner/leaver process.
3. **Backfill** — grandfather existing unbounded grants, or force-expire on a staged schedule? How much
   lead time?
4. **Renewal authority** — can any user with Write on the root renew, or only the original granter /
   matter owner?
5. **Lead time** — how far ahead does the first notification fire, and how many reminders?

## 8. Not started

No code, no schema, no job. `spec.md` **FR-33** records the requirement; this file records the
reasoning. Tracked as a deferred issue for scheduling.

---

## 9. ✅ OWNER ANSWERS — 2026-09-10. §7 is closed.

| # | Question | Answer |
|---|---|---|
| 1 | Cap default | **Open → see §9.2.** The owner asked what the cap *is* before answering; recommendation recorded there. |
| 2 | Scope | **External grants ONLY.** Internal POA shares (tasks 060/061) are out. |
| 3 | Backfill | **Update whatever grants exist. Keep it simple — we are in dev. Build the best technical solution.** No staged force-expiry schedule, no grandfathering machinery. |
| 4 | Renewal authority | **Anyone with Write** on the root. Not restricted to the original granter or matter owner. |
| 5 | Lead time / notifications | 🔴 **NO notifications. Manage with a RENEWAL REPORT.** Proactive notifications are a deliberate future addition. |

### 9.1 🔴 Answer 5 overrides §3's precondition — recorded, not glossed

§3 says, in bold: ***"Do not ship the cap before the notification."*** The owner has substituted a
**renewal report** for the notification. That is a legitimate substitution and the constraint is
**satisfied conditionally**, not waived — because the *function* of the precondition was never "send an
email", it was **"the internal user who can renew learns before access lapses."**

- A **notification is push**: the information arrives whether or not anyone went looking.
- A **report is pull**: it discharges the same duty *only if someone reads it on a cadence shorter than
  the cap*.

**So the report is not optional and not cosmetic — it IS the control.** Two obligations follow, and they
are design constraints on FR-33's implementation, not nice-to-haves:

1. **The report must live where someone actually looks** (a workspace widget / a view that is part of a
   routine), not an endpoint nobody calls. A report that exists and is unread is exactly the silent
   lockout §3 warns about, with an audit trail proving we knew.
2. **The cap must be long relative to the report cadence.** This is the non-obvious consequence:
   **choosing report-only argues for a LONGER cap, not a shorter one.** Under push notification a short
   cap is tolerable because every expiry is announced. Under pull, every expiry is a surprise unless the
   report was read in time, so a short cap multiplies the chance of a silent lockout.

**Residual, stated for the record**: with report-only, an unread report means an external user is locked
out with no warning — the precise failure §3 exists to prevent. Acceptable in **dev**. It should be
re-examined before production, and the owner has already flagged notifications as the intended
follow-on, so this is a sequencing decision rather than a gap.

### 9.2 Cap default — recommendation (answering the owner's clarifying question)

**What the "cap" is**: the maximum calendar lifetime an external grant may carry — the ceiling on its
`sprk_expiresdate`. It is deliberately the **backstop, not the primary mechanism**: per §4.1 expiry is
**matter-bound first, calendar-capped second**, so most grants die naturally when their root closes. The
cap only bites for roots that **never** close — which are precisely the stale grants worth catching.

**Recommended: 90 days, tenant-configurable, ONE dimension (tenant-level only).**

- **Why 90 and not 30**: §9.1's consequence. Report-only means every lapse is unannounced; a 30-day cap
  under pull-based renewal is a lockout generator. 90 days also keeps §5's toil sane — the toil figure
  scales as (active grants × 1/cap), so 30 days would be ~4× the renewal decisions of 90.
- **Why 90 and not a year**: a cap long enough to outlive most engagements does nothing at all, which is
  the failure mode §4.2 names explicitly.
- **Why NOT per-practice-area** (the owner's sub-question): that is a second configuration dimension with
  no concrete failure attached to its absence today — CLAUDE.md §11 question 3 rejects exactly this
  ("future flexibility"). §4.2's argument for tenant-configurability is real and stands; the
  practice-area split is speculative until a specific practice area demands a different number. Add the
  dimension when that happens; the config shape should not preclude it.

⚠️ **Not yet ratified** — this is a recommendation answering a clarifying question, so the number itself
still needs the owner's yes.
