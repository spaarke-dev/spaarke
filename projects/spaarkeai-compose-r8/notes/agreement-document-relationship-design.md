# Agreement ↔ Document relationship — structure advice

> **Date**: 2026-09-04 · **Trigger**: owner question during the §GAPS-5 Review Summary discussion —
> *"a Document can be associated to an Agreement and other record types; an Agreement can have multiple
> documents — do we need both 1:N and N:1 or some other structure?"*
> **Status**: advice, not a decision. One question (§3) must be answered by the owner before building.
>
> **⚠️ NAMING — read this before anything else.** `sprk_document` uses the **`Related*`** convention
> (owner decision, §9). The ONE field being created is **`sprk_relatedagreement`**. §§1–2 were written
> before that decision and say `sprk_regardingagreement`; §8.4 said `sprk_agreement`. **Both are
> superseded — §9.1 row 8 is the authority.** Existing unprefixed fields (`sprk_matter`,
> `sprk_invoice`, …) are NOT renamed; they appear in §9.1 because they already exist, not because
> anything is being added to that pattern.
>
> **🔴 UPDATE (schema confirmed, §11): `sprk_RelatedAgreement` ALREADY EXISTS — nothing to create.**
> §9.1 row 8 said CREATE and was wrong. The bigger finding: the code declarations know **7 of 16**
> document links, so Compose create-on-save silently drops nine link types — including Agreement.
> **§11 supersedes §9.1.**

---

## 0. TL;DR

1. **You do not need "both 1:N and N:1" — those are one relationship seen from two ends.** Author it once.
2. **Do not invent a structure.** ADR-024's dual-field polymorphic pattern already covers "a Document hangs
   off one of several parent types", and ADR-024 **names `sprk_document` explicitly**. Agreement is already
   a proven ADR-024 parent (`sprk_regardingagreement` exists on both `sprk_memo` and `sprk_todo`).
3. **One question decides whether that is sufficient**: can a single Document belong to **more than one
   Agreement at the same time**? If yes, lookups cannot express it and you need an intersect entity.
4. For *"this was an NDA review of this Agreement"*, **do not denormalise onto `sprk_analysis`.** Resolve it
   server-side in the read path that already does exactly this kind of resolve.

---

## 1. "Both 1:N and N:1?" — no; that is one relationship

In Dataverse a relationship is authored **once**, as a lookup column on the child:

```
sprk_document.sprk_regardingagreement  →  sprk_agreement
```

- From **Agreement's** side that same relationship is **1:N** ("this agreement's documents" — the subgrid).
- From **Document's** side it is **N:1** ("the agreement this document belongs to" — the lookup field).

Same object, two viewpoints. Dataverse generates the Agreement-side view automatically from the child lookup.

⚠️ **Creating "both" would be a modelling bug**, not thoroughness: you would get two independent
relationships and two lookup columns that can hold contradictory values, with nothing enforcing agreement
between them. Author the lookup on Document; the Agreement side comes free.

---

## 2. "A Document can also be associated to other record types" → ADR-024, unchanged

This is exactly the problem [ADR-024](../../../.claude/adr/ADR-024-polymorphic-resolver-pattern.md) exists
for, and it already lists `sprk_document` as one of its polymorphic entities. The pattern:

| Layer | Fields | Purpose |
|---|---|---|
| Entity-specific lookups | `sprk_regardingmatter`, `sprk_regardingproject`, … **`sprk_regardingagreement`** | Real lookups → subgrid filtering, Advanced Find, security |
| Denormalised resolver | `sprk_regardingrecordid` / `…name` / `…number` / `…url` / `…recordtype` | Unified cross-entity views (a native "Regarding" lookup cannot be used in views or filters — ADR-024's stated rationale) |

**Exactly one entity-specific lookup is populated per record.** That is the pattern's core assumption, and
it is also its limit (see §3).

### What to build — small, and precedented

1. Add ONE Agreement lookup on `sprk_document` → `sprk_agreement`. ⚠️ **NAME PENDING — see §7**: Document
   uses `Related*` (`sprk_relatedmatter`), NOT the `Regarding*` convention assumed here. Never add both forms.
2. Ensure the resolver fields populate for it (`PolymorphicResolverService` / the Field Mapping framework
   already do this for the other parents — extend the mapping, do not write a new resolver).
3. Seed a `sprk_recordtype_ref` row for Agreement: `sprk_recordlogicalname = "sprk_agreement"`,
   `sprk_recorddisplayname = "Agreement"`, `sprk_regardingfield = "sprk_regardingagreement"`.

**Cost: one lookup + one reference row + a mapping entry.** No new entity, no new pattern — passes
CLAUDE.md §11 cleanly. And the identical extension was already done for `sprk_memo` and `sprk_todo` on
2026-08-25 (owner live-verified), so the path is proven.

⚠️ **Verify `sprk_document`'s CURRENT regarding set via Dataverse MCP `describe` first.**
`docs/data-model/field-mapping-reference.md` §Document lists **no** regarding lookups at all, while ADR-024
says Document is polymorphic. One of the two is stale — almost certainly the doc (CLAUDE.md §2: code wins,
docs lag), but confirm rather than assume, and fix whichever is wrong.

---

## 3. 🔴 The question that decides the structure — owner must answer

> **Can ONE Document belong to MORE THAN ONE Agreement at the same time?**

| Answer | Structure |
|---|---|
| **No** — a document has exactly one owning Agreement | **ADR-024 lookup is sufficient. Stop here.** §2 is the whole job. |
| **Yes** — a document genuinely relates to 2+ agreements simultaneously | Lookups **cannot express this**, and adding more lookups does not help. You need an intersect entity (§4). |

**This is not a hypothetical in legal.** Plausible cases: a master agreement referenced by many SOWs; one
NDA covering several transactions; an amendment or side letter that modifies two agreements; a shared
exhibit or schedule attached to a family of contracts.

**Do not answer it from the abstract.** Name a concrete document you have that must sit under two
agreements. If you cannot name one, the answer is "No" and §2 is done — per §11, the cost of doing nothing
must be a named failing behaviour, not a hypothetical.

---

## 4. If the answer is "Yes" — intersect entity **with attributes**, not native N:N

Dataverse offers a native N:N ("many-to-many relationship"). **Do not use it here.** A native N:N is a bare
join: it stores only the two ids.

An intersect entity — e.g. `sprk_agreementdocument` with lookups to both sides — can carry the thing that
actually matters in legal work: **the nature of the association.**

| Native N:N | Intersect entity |
|---|---|
| No attributes | `sprk_role` (executed original / exhibit / amendment / precedent / draft) |
| No ordering | Sequence for exhibits and schedules |
| No dates | Effective/superseded dates per association |
| No independent security | Its own ownership + security |
| Cannot extend later without migration | Add columns freely |

**In this domain the association is itself data**: "Document X is the *executed original* of Agreement A"
and "Document X is an *exhibit to* Agreement B" are different facts, and a bare join cannot tell them apart.
That asymmetry is the argument.

### Both layers, and they are not redundant

If you go here, keep **both**, because they answer different questions:

- **`sprk_regardingagreement` (ADR-024)** = *ownership*. Exactly one. Drives the subgrid, "where does this
  document live", security inheritance, the resolver fields.
- **`sprk_agreementdocument` (intersect)** = *references*. Zero-to-many. Everything else this document
  relates to, and how.

**Build ownership first. Add the intersect only when §3's concrete case exists** — building both up front is
the scope creep §11 is written to catch.

---

## 5. Back to the driving question: *"this was an NDA review of THIS Agreement"*

### Where the chain stands today

```
Summary (sprk_analysisoutput)  →  Analysis (AnalysisId FK)  →  Document (analysis.DocumentId)  →  Agreement (MISSING)
```

`sprk_analysis` carries a lookup to **`sprk_agreementtype`** — the *classification registry*
(`sprk_key = 'nda'`, lease, …) — and **NOT** to `sprk_agreement`, the record. So today the Summary can say
*"this was an NDA review"* but cannot name the Agreement. §2's lookup closes the last hop.

### Is 3-hop traversal acceptable? — Yes, and it should NOT be denormalised

The tempting fix is to copy `sprk_regardingagreement` onto `sprk_analysis` for a 1-hop read. **Recommend
against it:**

- The Agreement is a fact the **Document** owns. Copying it onto every Analysis duplicates that fact, and
  **re-parenting a document silently stales every Analysis's copy** — with no error, which is the same
  quiet-wrongness class as §GAPS.
- It buys little: Dataverse `$expand` is effectively single-level, so the read is 2 round trips either way.

**Do it server-side in the read path that already does this.** `GetReviewMemoWithMetadataAsync` **already**
resolves the analysis and pulls `DocumentName` for display. Adding an Agreement resolve to that same step is
a small, single-source-of-truth change — no schema denormalisation, no staleness.

That also gives the natural home for the **agreement-level roll-up** ("show me every review summary for
this Agreement"): Agreement → its Documents → their Analyses → latest Summary output. Only possible once §2
exists.

---

## 6. Recommended sequence

1. **Verify** `sprk_document`'s current regarding set + the ADR-024 resolver wiring (Dataverse MCP
   `describe`). Fix whichever of ADR-024 / field-mapping-reference is stale.
2. **Answer §3.** One question, concrete evidence required.
3. **Build §2** — lookup + `sprk_recordtype_ref` row + mapping entry. Small and precedented.
4. **Extend the read path** (§5) so the Summary can name its Agreement.
5. **Intersect entity (§4) only if §3 said yes**, and only when a real case exists.

Steps 1–4 are independent of §3's answer and can proceed immediately.

---

## 7. ⚠️ CORRECTION + follow-on: `sprk_document` uses `Related*`, not `Regarding*` (owner, 2026-09-04)

**§2 above is wrong in its field naming.** Owner: *"in events, communication, matters, projects, work
assignment we use the 'Regarding' lookup structure; but in Documents the lookup relationships are 'Related'
— e.g. `sprk_RelatedMatter`. Also there is inconsistency because we also have `sprk_Matter` and
`sprk_Invoice`."* Confirmed in code. Read §2's *pattern* advice as still correct and its *field names* as
placeholders pending the naming decision below.

### 7.1 The actual `sprk_document` link vocabulary

It is a **closed set, hard-coded in TWO places** that must agree:

| Source | Purpose |
|---|---|
| `Services/Communication/Engine/Rungs/AttachmentDocumentAssociationRung.cs:73` `DocumentLinkFields` | Email→document association candidates |
| `Services/Compose/ComposeService.cs:96` `DocumentAssociationLookupAttributes` | create-on-save copy-forward |

Both list the same six: `sprk_matter`, **`sprk_relatedmatter`**, `sprk_project`, **`sprk_relatedproject`**,
`sprk_invoice`, `sprk_workassignment`.

**Note the asymmetry**: matter and project have BOTH forms; invoice and work-assignment have only the
primary. A designed dual-field scheme would be uniform. This one is not — evidence of accretion.

### 7.2 Q1 — does Regarding-vs-Related matter, and is `RegardingResolver` Document-capable?

**For the entity-specific lookup name: NO, it genuinely does not matter.** `ResolverWriteHandler.ts:135`
resolves the write target from **Dataverse relationship metadata**:

```
?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity
   -> columnName: r.ReferencingAttribute        (ResolverWriteHandler.ts:152)
```

So the resolver discovers `sprk_relatedmatter` perfectly well. `RegardingResolverApp.tsx`'s docblock line
*"iterate `sprk_regarding{entityName}` field names"* describes the **typical result on today's hosts, not
the mechanism** — do not read it as a naming requirement (it reads as one, which is worth a doc fix).

**What DOES block Document as a resolver host: the five denormalised columns, which ARE hard-coded by name.**
`sprk_regardingrecordtype` / `…recordid` / `…recordname` / `…recordurl` (+ `…recordnumber`). Only
`regardingRecordNumberField` and `regardingRecordNameField` are re-bindable via manifest properties; the
rest are literals in `applyResolverFields`.

**Is it used on Documents today? No — and the direction is the opposite of what one might assume.** The
manifest names its hosts: *"Designed for `sprk_todo`, `sprk_event`, `sprk_invoice`, `sprk_communication`,
`sprk_kpiassessment` child forms."* `sprk_document` appears **only in `regardingTargets`** — Document is a
**parent you can point AT**, never the child doing the pointing.

> **So**: if you ever want the resolver ON the Document form, the prerequisite is the **resolver-field set**
> (and a `sprk_regardingrecordtype` discriminator), **not** renaming `Related*` → `Regarding*`. Renaming for
> consistency is a legitimate goal on its own — just do not expect it to unblock the resolver, and do not
> skip it believing the resolver requires it.

### 7.3 Q2 — why both `sprk_relatedmatter` AND `sprk_matter`? Code dependencies?

**Dependency: yes — exactly the two lists in §7.1. Semantics: none implemented anywhere.**

- Both lists map **both fields to the same target entity**; nothing branches on which is populated.
  `AttachmentDocumentAssociationRung`: *"'Related' links point at the same target entity as their primary
  counterpart (a related matter is still a matter)."*
- The intended distinction survives only in prose. `CrossPathLink.cs:39` calls the related family *"the
  confirmed related record this document points at"*; `RungKind.cs:50` lumps both as *"OWN
  matter/project/invoice links (`sprk_matter` / `sprk_relatedmatter` / …)"* — i.e. the two comments do not
  even agree with each other.
- **No code writes either field** on `sprk_document` (searched server + shared client). They are
  form/user-populated; code only READS them, and reads them identically. `ComposeService` copies whichever
  are non-empty, without distinguishing.

**Conclusion: the pair is redundant *in the code as written*.** That is not proof it is redundant in the
DATA — see the safe first step below.

### 7.4 Recommended cleanup sequence

1. **Do the free win first, independent of any naming decision: collapse the two hard-coded lists into ONE
   shared constant.** Today adding a document link (e.g. Agreement) requires editing two files in two
   subsystems; miss one and behaviour silently diverges — Compose stops copying a lookup forward, or the
   association engine stops following it, with no error. This is the same silent-omission class as §GAPS.
2. **Measure the data before merging the pair.** Query the org: how many `sprk_document` rows have
   `sprk_matter` set, how many `sprk_relatedmatter`, how many BOTH, and do they ever disagree? Rows where
   both are set to *different* matters are the ones that decide whether a distinction was being used in
   practice, whatever the code does.
3. **Then decide the target shape** — most likely one lookup per target entity, named consistently with the
   rest of the platform (`sprk_regarding*`) so Document stops being the odd one out. That is a rename +
   data migration, so it wants its own project, not a drive-by.
4. **For Agreement specifically (§2)**: add ONE lookup and match whatever convention step 3 lands on. If the
   cleanup is not imminent, follow the local convention (`sprk_relatedagreement`) rather than introducing a
   third style on the same table — and add it to the §7.4.1 shared constant.

⚠️ **Do not add `sprk_agreement` AND `sprk_relatedagreement`.** That would replicate the exact duplication
this section exists to retire.

---

## 8. RECOMMENDATION — how to address the Document lookup inconsistency

> Requested 2026-09-04. Read §7 first for the evidence. **Headline: do not lead with a rename.**

### 8.1 The premise needs correcting first: there is no single platform-wide convention to be consistent WITH

"Documents are inconsistent with the rest of the platform" is only half true. The `Regarding*` family is
**not** uniform — each polymorphic host declares **its own** field map, and they already disagree with each
other on purpose:

| Host | Declared map | Divergence |
|---|---|---|
| `sprk_communication` | `Services/Communication/Engine/RegardingFieldMap.cs` | `contact` → **`sprk_regardingperson`** |
| `sprk_event` | `Services/Ai/Nodes/ActionCore/TaskActionCore.cs` `RegardingFieldByEntity` | `contact` → **`sprk_regardingcontact`**, plus the schema's real misspelling **`sprk_regardingorganziation`** which the code comment says explicitly: *"do not 'fix' it in code"* |
| `sprk_document` | **TWO** lists (§7.1) | `Related*` + unprefixed |

`TaskActionCore` states the architecture outright:

> *"This is `sprk_event`'s OWN regarding family — it differs from the `sprk_communication` `RegardingFieldMap`
> (e.g. `contact` → `sprk_regardingcontact` here vs `sprk_regardingperson` there), so it is NOT reused."*

**So the real invariant is not a naming convention. It is: _every polymorphic host declares its link
vocabulary exactly once, in code, as the contract._** The field name is deliberately treated as *schema
data* — read from the declaration, never derived by string convention. That is why `RegardingResolver`
resolves from relationship metadata (§7.2) and why a misspelling is tolerated rather than renamed.

**Document's actual deviation is therefore not the `Related*` prefix.** It is that Document declares its
vocabulary **twice, in two subsystems, and both declarations are incomplete** (neither lists
`sprk_relatedcommunication`, which `CrossPathLink` treats as a member of exactly this family). Measured
against the real invariant, *that* is the defect — and it is the one that can silently bite.

### 8.2 Recommendation, in priority order

#### Tier 1 — Give Document ONE declared link vocabulary. Do this now.

Create a single `DocumentLinkFieldMap` mirroring `RegardingFieldMap`'s shape, and have both
`AttachmentDocumentAssociationRung` and `ComposeService` consume it.

- **Zero schema change, zero migration, zero user-visible impact.** Pure refactor.
- **Closes a live silent-failure path**: today a new document link must be added in two files in two
  subsystems. Miss one and Compose stops copying it forward, or the association engine stops following it —
  **with no error**. Same class as §GAPS-5, and the §GAPS work this session exists to stop recurring.
- **It is the prerequisite for Agreement** (§2): one edit instead of two-and-hope.
- While doing it, resolve `sprk_relatedcommunication`'s absence — either add it, or write the one line
  saying why an association-candidate scan deliberately excludes it (it almost certainly should, since the
  communication IS the thing being matched from; but that reasoning is currently nowhere).

#### Tier 2 — Retire the duplication, driven by DATA not by code reading.

The code says `sprk_matter` and `sprk_relatedmatter` are redundant (§7.3). The data may disagree. Query the
org for the distribution — and specifically **rows where BOTH are set to DIFFERENT records**, which is the
only evidence that a distinction was ever really used.

Note the union is ragged, so no family is a superset:

| Target | unprefixed | `related*` |
|---|---|---|
| matter | ✅ | ✅ |
| project | ✅ | ✅ |
| invoice | ✅ | ✗ |
| workassignment | ✅ | ✗ |
| communication | ✗ | ✅ (load-bearing — 12 call sites) |

**If the data shows no meaningful divergence, the minimal-risk consolidation is to deprecate only the two
genuinely redundant columns (`sprk_relatedmatter`, `sprk_relatedproject`)** — after migrating any rows where
only the `related*` form is populated. That leaves matter/project/invoice/workassignment unprefixed plus
`sprk_relatedcommunication`: **mixed naming, but zero redundancy, no new columns and no renames.** Given
§8.1, mixed naming is the platform norm, not a wart.

#### Tier 3 — A cosmetic rename to `sprk_regarding*`: only with a stated reason beyond tidiness.

It buys nothing functional (§7.2: the resolver reads metadata; the maps are per-host anyway), costs a rename
plus data migration plus solution/form/view rework, and would be chasing a uniformity §8.1 shows does not
exist. The codebase's own precedent — a preserved misspelling — is the house position on renaming schema for
neatness. **Recommend: don't, unless a concrete need appears.**

### 8.3 On putting `RegardingResolver` on the Document form — probably not needed

Separate decision from all of the above, and the prerequisite is the **five resolver columns + a
`sprk_regardingrecordtype` discriminator** (§7.2), NOT a rename.

But first ask whether the capability is wanted: the resolver's job is *a user picking a polymorphic parent
on a form*. A document's parent is normally set by the flow that creates it — upload, email capture, Compose
create-on-save (which copies the source record's links forward). If users do not hand-pick a document's
parent on the form, the resolver adds columns and a control for a workflow that does not occur.

**Recommend: defer until someone asks for hand-picking on the Document form.** §11 — name the failing
behaviour first.

### 8.4 What this means for the Agreement link (§2)

- Add **ONE** lookup. Never both forms.
- ~~**Name it `sprk_agreement`**, the unprefixed form — the more complete family (4 targets vs 2), and the
  form that survives Tier 2.~~ **SUPERSEDED by the owner decision in §9 (2026-09-04): Document keeps the
  `Related*` convention, so the field is `sprk_relatedagreement`.** The reasoning above was sound on
  completeness grounds but is overridden — `related` reads better as "the business record this document
  relates to", and it is what the newest field on the table (`sprk_relatedcommunication`) already uses.
  **§9.1 row 8 is the authority; this bullet is kept only so the reversal is visible rather than silent.**
- Add it to the Tier 1 `DocumentLinkFieldMap` in the same change, so Compose copy-forward and email
  association pick it up together rather than one silently lagging.

### 8.5 Suggested sequence

1. **Tier 1 refactor** (one shared map, both consumers, `sprk_relatedcommunication` question resolved) — safe, immediate, unblocks everything else.
2. **Add `sprk_agreement`** to schema + the shared map (§8.4) — closes the Summary→Agreement gap of §5.
3. **Extend the read path** so a Summary can name its Agreement (§5) — server-side resolve, no denormalisation.
4. **Tier 2 data query**, then the deprecation decision.
5. **Tier 3 / resolver-on-Document**: only on a stated need.

Steps 1–3 are independent of every open question and can start immediately.

---

## 9. DECIDED: Document keeps the `Related*` convention — the build checklist

> **Owner decision, 2026-09-04**: *"for the Document 'related/regarding' we'll keep the 'related'
> convention."* This CLOSES §8's Tier 3 (no rename to `Regarding*`). New Document links use `sprk_related*`.

### 9.1 The vocabulary — every row is ONE relationship, a lookup ON `sprk_document`

**Direction is the same for all of them**: the lookup column lives on **`sprk_document`** (the child) and
points at the parent — **N:1 from Document**. The parent's 1:N "Documents" subgrid is generated from it.
**Create one relationship per row. Never a second one in the opposite direction** (§1).

| # | Field on `sprk_document` | Target | Status | Evidence |
|---|---|---|---|---|
| 1 | `sprk_matter` | `sprk_matter` | **CONFIRM** | `DocumentLinkFields` + `DocumentAssociationLookupAttributes` |
| 2 | `sprk_relatedmatter` | `sprk_matter` | **CONFIRM** | both lists |
| 3 | `sprk_project` | `sprk_project` | **CONFIRM** | both lists |
| 4 | `sprk_relatedproject` | `sprk_project` | **CONFIRM** | both lists |
| 5 | `sprk_invoice` | `sprk_invoice` | **CONFIRM** | both lists |
| 6 | `sprk_workassignment` | `sprk_workassignment` | **CONFIRM** | both lists |
| 7 | `sprk_relatedcommunication` | `sprk_communication` | **CONFIRM** ⚠️ | `CrossPathLink` + 12 call sites — **absent from BOTH lists today** |
| 8 | **`sprk_relatedagreement`** | `sprk_agreement` | **CREATE** | new (§2) |

⚠️ **Row 7 is the one to look at hardest.** It is real, load-bearing and heavily used, yet neither existing
declaration knows about it. That is precisely the "declared twice, both incomplete" defect from §8.1 — and
it is already live, not hypothetical.

### 9.2 Do NOT create `sprk_relatedinvoice` / `sprk_relatedworkassignment`

Keeping the `Related*` convention for NEW fields does **not** mean back-filling it onto the old ones. Doing
so would mean 2 new columns + 4 data migrations + 4 retirements, and would buy **naming tidiness only** —
§8's Tier 3 reasoning applies unchanged. The map absorbs the mixed naming so no consumer ever sees it.

**Rule going forward**: new Document links are `sprk_related*`; existing names stay as they are until
§8 Tier 2's data query justifies touching them.

### 9.3 Fields that are lookups on `sprk_document` but must STAY OUT of the map

The map is the **record-association vocabulary** — "what business record is this document filed under". These
are lookups too, and including them would corrupt every consumer:

| Field | Target | Why excluded |
|---|---|---|
| `sprk_parentdocument` | `sprk_document` | attachment → parent email |
| `sprk_canonicaldocument` | `sprk_document` | dedup/canonical pointer |
| `sprk_containername` | `sprk_container` | storage location, not a business parent |
| `sprk_currentversionid` | `sprk_fileversion` | version pointer |
| `sprk_checkedoutby` / `sprk_checkedinby` | `systemuser` | lock state |

### 9.4 Design the map so row 7 cannot happen again

Make `DocumentLinkFieldMap` the **complete** vocabulary, and let each consumer declare its own subset
**explicitly, with a reason** — rather than by silent omission, which is how row 7 got lost:

- `ComposeService` copy-forward → **all** rows (a new Word doc should file alongside its source).
- `AttachmentDocumentAssociationRung` → probably **excludes `sprk_relatedcommunication`**, since the
  communication is the thing being matched *from*, so it is not a candidate. That is very likely correct —
  but it is currently expressed as *absence*, and absence is indistinguishable from an oversight. Write it
  as an explicit exclusion with that sentence attached.

### 9.5 ⚠️ Confirm intent: there is a lookup in the OPPOSITE direction too

`sprk_invoice` carries **`sprk_document`** ("Source Document" → `sprk_document`,
`docs/data-model/field-mapping-reference.md:153`). So Document↔Invoice has links **both ways**:

- `sprk_document.sprk_invoice` → "this document is filed under this invoice"
- `sprk_invoice.sprk_document` → "this invoice was extracted FROM this document"

**These are probably two legitimately different facts** (filing vs extraction provenance), not the §1
anti-pattern — the "Source Document" display name points that way, and invoice extraction is a real pipeline
here. **But confirm it**, because if they are ever meant to agree, nothing makes them, and they will drift
silently. If they ARE different facts, say so in the schema description so the next reader does not "tidy"
one away.

---

## 10. Q: is `sprk_document` baked into the SemanticSearch PCF?

**Yes — but as the ENTITY, never as a link field, so none of §9 affects it.**

The control is document-centric by design and hard-codes the entity logical name at 9+ sites:

| Location | Use |
|---|---|
| `SemanticSearchControl.tsx:277` | `sprk_document: 'document'` entity→label map |
| `:434` | `getDocumentTypeOptions('sprk_document', 'sprk_documenttype')` |
| `:764`, `:980` | `retrieveMultipleRecords('sprk_document', …)` |
| `:948`, `:1522` | record URL `etn=sprk_document` |
| `:1012`, `:1427`, `:1453` | `updateRecord('sprk_document', …)` |
| `:1360` | `deleteRecord('sprk_document', …)` |
| `services/DataverseMetadataService.ts:69–70` | defaults `entityName = 'sprk_document'`, `attributeName = 'sprk_documenttype'` |

**The only `sprk_document` COLUMNS it binds are `sprk_documentid`, `sprk_documenttype` and
`sprk_workspaceflag`.** Searched for a `sprk_document` *lookup field* reference — `_sprk_document_value`,
`sprk_document@odata.bind` — and there is **none**.

**Consequences:**
- Adding `sprk_relatedagreement` (§9.1 row 8): **no impact on this control.**
- Retiring `sprk_relatedmatter` / `sprk_relatedproject` later (§8 Tier 2): **no impact on this control.**
- The field named `sprk_document` that exists on **`sprk_invoice`** (§9.5) is **not referenced** by the
  control at all.
- What WOULD break it: renaming the `sprk_document` **entity**, or `sprk_documenttype` /
  `sprk_workspaceflag`. None of those are in scope here.

---

## 11. 🔴 SCHEMA TRUTH (owner screenshot, 2026-09-04) — §9.1 was wrong, and the real finding is bigger

The actual `sprk_document` column list supersedes everything inferred from code. **Three corrections:**

### 11.1 `sprk_RelatedAgreement` ALREADY EXISTS — there is nothing to create

§9.1 row 8 said "CREATE". Wrong. The column is there. The Agreement link needs **no schema work at all** —
only registration in the map (§8 Tier 1) and the read-path change (§5).

§9.2 was also wrong: it said *"do NOT create `sprk_relatedinvoice` / `sprk_relatedworkassignment`"* on the
premise they did not exist. **`sprk_RelatedInvoice` and `sprk_RelatedWorkAssignment` both exist.** The
advice ("don't create them") happens to still hold, but for the opposite reason.

### 11.2 The real finding: the code knows about **7 of 16** document links

Confirmed columns on `sprk_document` (schema names as displayed; **logical names are all lowercase** — use
those in code):

| `Related*` family (12) | Unprefixed (4) |
|---|---|
| `sprk_RelatedAgreement`, `sprk_RelatedCommunication`, `sprk_RelatedContact`, `sprk_RelatedInvoice`, `sprk_relatedmatter`, `sprk_RelatedOrganization`, `sprk_relatedproject`, `sprk_RelatedServiceRequest`, `sprk_RelatedToDo`, `sprk_relatedvendororg`, `sprk_RelatedWorkAssignment`, `sprk_RelatedEvent` | `sprk_Matter`, `sprk_Project`, `sprk_WorkAssignment`, `sprk_Invoice` |

**The two code declarations know only 6** (`sprk_matter`, `sprk_relatedmatter`, `sprk_project`,
`sprk_relatedproject`, `sprk_invoice`, `sprk_workassignment`), plus `sprk_relatedcommunication` known
separately to `CrossPathLink` = **7 of 16**.

**Nine links are invisible to both consumers**: Agreement, Contact, Invoice(related), Organization,
ServiceRequest, ToDo, VendorOrg, WorkAssignment(related), Event.

**This is a live functional defect, not a tidiness issue.** Concretely:
- **Compose create-on-save** copies forward only the 6 it knows. A PDF filed under an **Agreement**, Service
  Request, To Do, Event, Contact, Organization or Vendor Org produces a new Word document that **silently
  loses that filing**. The user sees a document that is simply not where they filed the original.
- **Email→document association** scans only the 6, so nine link types never surface a candidate.

§8.1 called Document's vocabulary "declared twice, both incomplete". The schema shows *how* incomplete — the
declarations have drifted more than half a table behind. **This raises Tier 1 from a refactor to a bug fix.**

⚠️ The screenshot's sort order suggests it may be partial (`Related Event` trails the alphabetical run).
**Enumerate the full column list via MCP `describe` before finalising the map.**

### 11.3 The unprefixed four: deprecate by WRITE, not by read — do not simply "ignore" them

Owner: *"we just need to ignore the `sprk_{recordtype}` (i guess); we can't remove them b/c of dependencies."*
Right on removal, but **"ignore" is unsafe as a first move**, because today's code **reads** all four, and
they may be the only place a link lives on existing rows. Stop reading them and every such document silently
loses its association — the same class of quiet loss as §11.2.

**Use the standard deprecation order:**

1. **Write** `sprk_related*` only. Nothing new lands in the unprefixed four.
2. **Read both**, preferring `sprk_related*` and falling back to the unprefixed. One helper in the map; no
   consumer needs to know.
3. **Measure** (§8 Tier 2): rows where only the unprefixed is set, and rows where both are set to
   *different* records.
4. **Migrate** the unprefixed-only rows into `sprk_related*`.
5. **Then** drop the read fallback. Leave the columns in place (dependencies) but unread and unwritten —
   mark them deprecated in the schema description so the next reader knows.

Steps 1–2 are safe immediately. Only after step 4 is "ignore" actually true.

---

## 12. Q: do we also need N:1 in the other direction (record → document)?

**No — and adding it would create the §1 modelling bug.**

`sprk_document.sprk_relatedagreement → sprk_agreement` **already gives both directions**:

| Question | Answered by | Cost |
|---|---|---|
| "Which documents belong to this Agreement?" | the **1:N subgrid**, generated from the same lookup | free |
| "Which Agreement is this document filed under?" | the lookup column | the column itself |

A second column `sprk_agreement.sprk_document → sprk_document` would be an **independent** relationship. It
can hold a value that contradicts the child lookup, and nothing reconciles them — you would have two answers
to one question and no rule for which wins.

### The ONE legitimate exception — and you already have an instance of it

Add a record→document lookup **only when it expresses a DIFFERENT fact** than "documents filed here", and
name it for that fact. The existing example is `sprk_invoice.sprk_document` — display name **"Source
Document"**, i.e. *the document this invoice was extracted FROM*. That is provenance, not filing, so it is
legitimately its own relationship (§9.5).

The test: **can the subgrid already answer it?** If yes, the column is redundant. The subgrid can list an
Agreement's documents; what it **cannot** express is *which one plays a particular role* — you cannot ask it
"which of these 40 is the executed original?"

So for Agreement, the only reason to add a record→document lookup is a **distinguished single document**,
e.g.:

- `sprk_agreement.sprk_executeddocument` → "the signed, executed copy"
- `sprk_agreement.sprk_currentdraft` → "the draft under negotiation"

**Recommendation**: do not add one now. If a distinguished-document need appears, add a **role-named** lookup
for that specific role — never a generic `sprk_document` on Agreement, which would just duplicate the
subgrid. And if several roles emerge at once, that is the §4 intersect-entity signal rather than a column
per role.
