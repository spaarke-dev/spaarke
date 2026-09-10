# Task 042 — standing-grant baseline levels (FR-25)

> **Status: STOPPED AT STEP 1 — escalation trigger 2 fired, legitimately.** Schema verification is
> COMPLETE and recorded below; no code has been written. The blocking question is at §4.

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
