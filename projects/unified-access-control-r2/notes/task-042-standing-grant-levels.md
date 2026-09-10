# Task 042 — standing-grant baseline levels (FR-25)

> **Status: ✅ COMPLETE.** Escalation trigger 2 fired at step 1 and the owner answered **option B** on
> 2026-09-10. The live backfill was applied **first** so nobody lost access; the code then shipped.
> §4 records the decision as it was put; **§6 records what was done.**

---

## 1. Live schema verification (POML step 1) — all against `spaarkedev1`, 2026-09-10

| Object | Exists | Type | Notes |
|---|---|---|---|
| `contact.sprk_standinggrant` | ✅ | Boolean | |
| `contact.sprk_accesspermissiongrant` | ✅ | Picklist | |
| `sprk_organization.sprk_standinggrant` | ✅ | Boolean | |
| `sprk_organization.sprk_accesspermissiongrant` | ✅ | Picklist | 🔴 **not the name the design gives** — see §2 |

**Option values — identical on both entities, and exactly what spec FR-25 states:**

| Value | Label |
|---|---|
| `100000000` | View Only |
| `100000001` | Collaborate |
| `100000002` | Full Access |

So the POML's escalation trigger 1 (fields absent) does **NOT** fire. Every field FR-25 needs exists,
with the expected option values. No schema work is required.

---

## 2. 🔴 The design doc names the org field wrongly — 9th docs-vs-metadata instance

Design §10, quoted in this POML's own step 1, says the org-level fields are
`sprk_standinggrant` + **`sprk_accesspermissions`**.

Live metadata has **`sprk_accesspermissiongrant`** — *character-identical to the contact field*. There
is no `sprk_accesspermissions` on `sprk_organization` at all (full attribute list checked).

**This is good news for the implementation**: one logical name serves both entities, so the reader
needs one attribute constant, not two, and the contact/org variants differ only in entity + id.

It is the **ninth** time on this project that a doc has disagreed with live metadata (task 021 recorded
the eighth — `Secure Project` singular vs the docs' plural). Task 026 attacked the *cause* of that
class; this instance post-dates it, in a file task 026 did not cover. **Design §10 needs correcting** —
listed in §5 below rather than fixed here, because §10 is a design doc and this task has not been
authorised to edit it.

---

## 3. What a standing grant confers TODAY — the blast radius

This is the fact that turns escalation trigger 2 from a formality into a real decision.

`AccessibleRecordSetService.MembershipTermRights` is a **constant**:

```
internal const AccessRights MembershipTermRights =
    AccessRights.Read | AccessRights.Write | AccessRights.Create;   // Collaborate-equivalent
```

So today, a contact with `sprk_standinggrant = true` receives **Read + Write + Create** on every
standing-derived record, **regardless of any baseline** — because nothing reads the baseline field
(register C-3: `sprk_accesspermissiongrant` had zero repo references).

### Live data — 2026-09-10

| Fact | Value |
|---|---|
| Contacts in dev | **9** |
| Contacts with a baseline set | **0** |
| Contacts with `sprk_standinggrant = true` | **2** |
| …of those, baseline EMPTY | **2 (100%)** |
| Organizations with `sprk_standinggrant = true` | **0** |

The two standing-grant contacts are `ralph.schroeder@spaarke.com` and `eyal.iffergan@spaarke.com` —
both active (`statecode = 0`), created 2026-03-16 and 2026-07-01. **Internal Spaarke addresses,
configured months apart** — i.e. deliberately set up, not obvious throwaway rows.

**Therefore every option below CHANGES live behaviour for those two people.** That is why this is not
a silent implementation choice.

---

## 4. 🔔 THE BLOCKING QUESTION — what does an EMPTY baseline mean?

Spec FR-25 defines behaviour only for a **populated** baseline. The POML's escalation trigger 2 says
exactly this and forbids picking silently:

> *"If a subject holds `sprk_standinggrant=true` but the baseline field is EMPTY, STOP and escalate the
> default (ViewOnly floor vs no contribution) — spec FR-25 only defines populated-baseline behavior;
> do not pick silently (CLAUDE.md §6)."*

⚠️ **The POML offers a binary, and it is the wrong binary.** Neither of its two options preserves
current behaviour, because today's constant is *Collaborate*, not View Only. A third option exists and
the POML does not name it:

| Option | Empty baseline ⇒ | Effect on the 2 live contacts | Character |
|---|---|---|---|
| **A** — View Only floor | `Read` | **Lose Write + Create** | Silent partial reduction |
| **B** — No contribution | nothing | **Lose ALL standing access** | Fail-closed; silent full reduction |
| **C** — Collaborate floor | `Read\|Write\|Create` | **No change** | Behaviour-preserving; defers the decision to backfill |

### The arguments, stated fairly

**For B (fail closed).** An unset field means nobody decided what this person should get, and
conferring write access off an undecided field is precisely the over-grant this project exists to
remove. NFR-01's instinct is fail-closed.
*Against*: NFR-01 is about read **faults** — FLS-stripped, missing record, transport error. An empty
picklist is a *successful* read of a legitimately-empty value, so B is a policy choice, not an NFR-01
requirement. And it removes access two active people have today, with no signal to them.

**For C (preserve behaviour).** Spec **Prerequisites** already say
*"`sprk_accesspermissiongrant` populated for existing contacts and organizations"* is an **environment
obligation**. That frames the empty state as *transitional*, which argues for changing nothing until
the backfill happens, then letting the populated value take over.
*Against*: it perpetuates blanket-Collaborate for as long as the backfill is outstanding — and
"transitional" states have a way of persisting. It also means the code's honest answer to "what level
does this standing grant carry?" is "we don't know, so: write access".

**For A (View Only floor).** A middle path: keeps read access working while removing the unearned
write bit.
*Against*: it is the option nobody can justify from a document. FR-25 does not specify it, and it
invents a level the source data does not carry — the same objection the code comment already raises
about `MembershipTermRights` being a constant.

### My recommendation

**B (no contribution), paired with a backfill *before* the code lands** — and if the backfill cannot
be scheduled first, then **C**, explicitly time-boxed.

Reasoning: B is the only option whose behaviour matches what the code can honestly claim, and the
population set is *two internal contacts and zero organizations* — the smallest blast radius this
decision will ever have. Deferring makes it strictly harder. But B must not ship *before* the backfill,
because losing access with no notice is an outage, and both affected accounts are internal Spaarke
people who would hit it during demos or UAT.

**Either answer is implementable in the same amount of work.** What is not acceptable is guessing.

---

## 5. Carried forward

| Item | Where |
|---|---|
| Correct design §10's org field name → `sprk_accesspermissiongrant` | design doc edit, not authorised here |
| The POML's escalation-2 binary should become a ternary (option C exists) | this POML |
| Backfill obligation for `sprk_accesspermissiongrant` on contacts + organizations | spec Prerequisites — already stated as an environment obligation, now with a measured count: **0 of 9 contacts populated** |
| Register E-7: `contact.sprk_standinggrant` is FLS-secured; the Field Security Profile membership is a per-environment prerequisite | must be surfaced in this task's PR when it resumes |

---

## 6. ✅ Resolution — owner chose **option B**, 2026-09-10

> *"if B is the right technical and long term answer then proceed; update the current users so they
> have access (or let me know if i need to do this manually)"*

### 6.1 The backfill went FIRST, and that ordering was the point

The recommendation was explicitly "B **paired with a backfill before the code lands**", because losing
access with no notice is an outage. So the data change preceded the code change:

| Contact | Before | After |
|---|---|---|
| `ralph.schroeder@spaarke.com` (`8e9918a9-9021-f111-88b5-7c1e520aa4df`) | `sprk_accesspermissiongrant` = **null** | **100000001 Collaborate** |
| `eyal.iffergan@spaarke.com` (`8cb95c16-e974-f111-ab0e-7ced8ddc4a05`) | **null** | **100000001 Collaborate** |

**Why Collaborate and not something else**: `MembershipTermRights` was the constant
`Read | Write | Create`, so Collaborate is the value that leaves their access **exactly unchanged**.
Anything else would have been an unrequested change smuggled in under a backfill. Full Access is a
one-field edit if delete rights are ever wanted.

**Revert**: PATCH `sprk_accesspermissiongrant` back to `null` on both ids. Written as data, not schema —
so this is outside the binding no-live-schema-mutation directive, and it was done with the owner's own
identity after an explicit instruction.

**Organizations needed nothing** — zero hold a standing grant.

### 6.2 Option B needed no special-case code

`ExternalAccessLevels.ToAccessRights` (task 032's single mapping) **already** fails closed on `null` and
on any value outside the enum. So "empty baseline contributes nothing" is what it did all along; the
work was to **not** special-case it. Two things it *did* need:

1. **`Rights == None` short-circuits the whole term.** `AccumulateTerm` would otherwise enter the record
   ids at `None` — *present in the accessible set while conferring nothing*. That is a different and
   worse answer than absent: it is the shape that makes a UI render a row the caller cannot act on. It
   also skips the membership walk entirely, since a term that can contribute nothing has no records
   worth enumerating (NFR-02).
2. **Provenance must not lie.** `Sources.StandingGrantMembership` stays `false` when the term
   contributed nothing.

### 6.3 The rename

`IContactStandingGrantReader` → **`ISubjectStandingGrantReader`** (POML-sanctioned). It reads
organizations now, so "Contact" in the name would have been false — the same docs-vs-reality drift this
project keeps repairing. One registration, per ADR-010.

A pleasant accident worth knowing: because `StandingGrantState` is a `readonly record struct`, an
**unconfigured Moq mock returns `default` = `(Held: false, Baseline: null)`** — i.e. exactly the
fail-closed value. Several existing tests that never set the reader up therefore stayed correct rather
than NRE-ing on a null `Task`.

### 6.4 The seam test broke, and that was the useful part

`StandingGrantRuntimeUnionSeamTests` failed on this change because its fixture set the flag with **no
baseline** — *the exact live state of both real contacts*. It was the in-repo mirror of the production
problem, discovered by the same change that fixed production. Rather than quietly editing the fixture,
the no-baseline case now has **its own seam-level assertion**
(`StandingGrant_WithNoBaseline_ConfersNoAccessEvenWhenTheFlagIsSet`) proving absence-not-zero-rights
through the real reader and the real composer.

### 6.5 Perturbations (mandatory)

| # | Break | Tests failed |
|---|---|---|
| 1 | Term contributes the old constant `MembershipTermRights` again | **2** |
| 2 | Empty baseline falls back to a Collaborate floor (option C) | **4** |
| 3 | Gate on `standing.Held` instead of `Rights != None` (records enter at zero rights) | **2** |

None failed zero. Perturbation 2 is the one that matters most: it proves the **owner's decision itself**
is pinned, not just the mapping arithmetic.

### 6.6 🔴 A finding, filed not fixed

**`contact.sprk_standinggrant` is FLS-secured (`IsSecured=True`); `sprk_organization.sprk_standinggrant`
is NOT (`IsSecured=False`).**

The contact flag is deliberately protected — it is a policy flag that confers access, and register E-7
makes the Field Security Profile membership a per-environment prerequisite. The organization flag has
no such protection, so anyone with write on `sprk_organization` can confer standing access on an entire
law firm. Task **043** consumes exactly that field for the org expansion term, which is when it starts
to matter.

Effect here was limited to log wording (an absent org attribute means "not set", not "FLS stripped it")
and nothing about the fail direction.
