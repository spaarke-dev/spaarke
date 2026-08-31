# DEF-1 root cause — entity metadata never reached the resolver

> **Status**: FIXED in `Spaarke.Records.RecordHeader` v1.1.1 (2026-08-26)
> **Reported**: first UAT of v1.1.0 on the `sprk_project` main form, spaarkedev1
> **Severity**: total — every cell rendered an em-dash

---

## 1. Symptoms (all six from ONE chain)

The control rendered its configured 6-field layout, but:

1. every cell showed an em-dash
2. every renderer fell back to plain text
3. labels showed humanized logical names ("Openeddate", "Highpriority")
4. lookups were not editable (no targets)
5. option-set labels did not resolve
6. the AI sparkle did not appear

Symptoms 1–5 are one failure chain. **Symptom 6 is not a defect** — see §5.

## 2. The chain

```
entity metadata empty / untyped at runtime
  -> resolveHeaderConfig derives renderer 'text' for EVERY field
  -> RecordHeaderView.buildSelectFields selects a lookup by its BARE name
     (`_<name>_value` is emitted only for renderer === 'lookup')
  -> Dataverse 400s the WHOLE $select
  -> every field null -> every cell an em-dash
```

Live proof of the last two links, against spaarkedev1 (both re-runnable):

| `$select` contains | Result |
|---|---|
| `sprk_projecttype_ref` (bare) | **HTTP 400** — `Could not find a property named 'sprk_projecttype_ref'` |
| `_sprk_projecttype_ref_value` | **HTTP 200** |

Live-confirmed `sprk_project` types (so the renderers would all be correct once
metadata resolves): `sprk_projecttype_ref`=Lookup, `sprk_openeddate`=DateTime,
`sprk_highpriority`=Boolean, `sprk_projectdescription`=Memo,
`sprk_recordsummary`=Memo (exists).

## 3. Two independent root causes, both in `@spaarke/ui-components`

Both live in `services/XrmDataverseClient.ts` and both predate R2 — they came in
with the R1 DataGrid framework (commit `c1c428e92`) and were inherited. R2's own
diff only added `targets` + the cache.

### RC-1 — the label/type rescue call could never succeed

`fetchAttributeDisplayNames` issued:

```ts
xrm.WebApi.retrieveMultipleRecords('EntityDefinition', options)
```

`Xrm.WebApi` resolves its first argument to an entity **set** name through the
client's entity catalog. `entitydefinition` is not an entity, so this always
threw — and the call site wrapped it in `.catch(() => new Map())`, so the throw
was swallowed and the rescue map was **always empty**.

Three independent confirmations:

1. **Live** — `GET /api/data/v9.2/EntityDefinitions?$select=LogicalName,EntitySetName&$filter=LogicalName eq 'entitydefinition'`
   returns `{"value":[]}`. There is no such entity to resolve.
2. **In-repo** — `SemanticSearchControl/services/DataverseMetadataService.ts:222`:
   *"Uses direct fetch to the metadata API since
   `Xrm.WebApi.retrieveMultipleRecords` doesn't work with metadata entities like
   EntityDefinitions."*
3. **R2's own spec** (`spec.md`, Placement/decision table): EntityDefinitions is
   *"unreachable by `Xrm.WebApi`"* — the spec authors knew, and concluded the
   control would not need it. The inherited code needed it anyway.

Note the OData query itself was fine — issued as a direct `fetch` it returns
HTTP 200 with exactly the expected shape. Only the **transport** was impossible.

### RC-2 — `projectAttribute` parsed only Web-API shapes

`Xrm.Utility.getEntityMetadata` is the **Client API**, and its payload differs
from the Web API in every field that mattered. Microsoft documents this at
*learn.microsoft.com/…/xrm-utility/getentitymetadata*, section "Attribute
objects":

| Field | Client API (what we call) | Web API (what we parsed) |
|---|---|---|
| `AttributeType` | **Number** (`AttributeTypeCode`) | String (`"Lookup"`) |
| `DisplayName` | **String** (`"Project Type"`) | `{ UserLocalizedLabel: { Label } }` |
| `OptionSet` | array / key:value bag | `{ Options: [...] }` |

The old code was:

```ts
function normalizeAttributeType(attributeType: unknown): MetadataAttributeType {
  if (typeof attributeType !== 'string') {
    return 'String';        // <-- a NUMBER lands here
  }
  return attributeType as MetadataAttributeType;
}
```

so **every attribute of every entity** projected as `String` with no label, no
options and no targets. That alone produces symptoms 2–5, and via the chain in
§2, symptom 1.

### Why the regression test did not catch it

Task 020 found a test asserting `getEntityMetadata('sprk_event', ['Attributes'])`,
saw the source passing one argument, and **changed the test to match the
source** — annotating the one-argument call as intentional and sufficient. That
edit removed the only signal. The assertion is restored in spirit in
`XrmDataverseClient.metadataShape.test.ts`, and the misleading comment in
`XrmDataverseClient.test.ts` is corrected in place.

## 4. The fix — four layers

| # | Change | File |
|---|---|---|
| 1 | Map numeric `AttributeTypeCode` → type name; accept plain-string `DisplayName`; accept all three `OptionSet` shapes | `services/XrmDataverseClient.ts` |
| 2 | Delete the impossible `Xrm.WebApi` metadata-entity call (dead code by construction) | `services/XrmDataverseClient.ts` |
| 3 | `retrieveEntityMetadata(entity, attributes?)` — callers NAME the attributes they need, which is the documented way to guarantee a populated `Attributes` collection; cache key includes the set | `services/XrmDataverseClient.ts`, `services/IDataverseClient.ts`, `control/useHeaderFormMetadata.ts`, `components/RecordHeader/configResolution.ts` |
| 4 | A failed `$select` read degrades to an **unprojected** read instead of blanking the control | `hooks/useRecordFieldValues.ts` |

Layer 4 is the general defence the owner asked for. A `$select` is
all-or-nothing in OData, so one bad column name blanks an entire control — this
is now the **third** occurrence of that failure mode (RS-1 / task 040, then this
defect). Rather than keep fixing individual column names, the read now retries
once with no `$select`, which returns the full row including every decorated
`_<lookup>_value`. The wider payload is paid only on the error path.

Unknown attribute types now project as `'Unknown'` rather than `'String'`.
Behaviourally identical (every renderer switch falls through to its text
default), but it stops the code asserting a type it does not have — asserting
`String` for a Lookup is exactly what started this.

### NFR-05 was NOT carved out

Spec NFR-05: *"All Dataverse I/O via `Xrm.WebApi` / `Xrm.Page`. No
`@spaarke/auth`, no raw `fetch` to the Dataverse API."*

Re-pointing RC-1 at a direct `fetch` to `/api/data/v9.2/EntityDefinitions` would
have worked (verified live, HTTP 200, and it can return `Targets` in one request
via a derived-type `$select`) but would have needed an NFR-05 exception. It was
not necessary: `Xrm.Utility.getEntityMetadata` supplies type, label and options
once parsed correctly, so the impossible call is simply deleted. **No ADR or
spec exception is claimed by this fix.**

## 5. Symptom 6 (the sparkle) is unbuilt scope, not a defect

`TASK-INDEX.md` shows **034 — Sparkle → `sprk_recordsummary` — 🔲 not done**.
`RecordHeaderView.tsx` says so in its own header: *"the sparkle / `summaryField`
wiring is task 034, layered on this view."* `resolveHeaderConfig` passes
`summaryField` through but explicitly leaves the `RECORDSUMMARY_FIELD` default
and the FR-17 metadata-existence gate to 034.

So the sparkle was correctly absent in v1.1.0. `sprk_recordsummary` does exist on
`sprk_project` (live-confirmed), and v1.1.1 now requests it as part of the
metadata set when a layout names it — so 034 will find it typed and present when
it is built. **No fix applied; none warranted.**

## 6. Residual risk to watch at re-UAT

`Xrm.Utility.getEntityMetadata`'s documented attribute payload lists only
`AttributeType`, `DisplayName`, `EntityLogicalName`, `LogicalName` (+ `OptionSet`
for Boolean/Picklist/MultiSelect/State/Status). It does **not** document
`Targets`. `projectAttribute` probes `Targets`/`targets` and `@types/xrm` is
silent, so whether FR-15a's OOB lookup picker receives its target entity is
**unproven** — it cannot be established without running the client API in a live
form.

This does **not** affect the reported defect: lookup **display** works through
`_<name>_value` + its FormattedValue annotation regardless of targets. Only the
*edit* affordance depends on `targets[0]`.

**Check at re-UAT**: click a lookup cell. If the OOB picker does not open with
the right table, `Targets` is absent from the client-API payload and the
follow-up is a scoped NFR-05 exception to read
`EntityDefinitions(LogicalName='x')/Attributes/Microsoft.Dynamics.CRM.LookupAttributeMetadata?$select=LogicalName,Targets`
— verified live to work in a single request (HTTP 200).
