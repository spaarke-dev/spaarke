# Decision 001 — `layoutJson` is `of-type="Multiple"`, not `SingleLine.Text`

> **Status**: DECIDED (2026-08-26) — retires the open question in task 001
> **Decided by**: first-UAT evidence, not the spike
> **Supersedes**: the `SingleLine.Text` choice shipped in v1.1.1's predecessor v1.1.0
> **Applies to**: `Spaarke.Records.RecordHeader` v1.1.1+

---

## 1. The open question task 001 left

Task 001 was an ergonomics spike on how a maker supplies the
`RecordHeaderConfiguration` v1.0 JSON. It was **skipped**, so v1.1.0 shipped the
spec's documented fallback:

> `of-type="SingleLine.Text"` is the outcome recorded for the task-001 ergonomics
> spike: spec.md names it the PROVEN fallback (the shipped `title` property above
> is the same of-type and round-trips in form XML today), and the spike is an
> ergonomics check, not a gate.
> — `control/ControlManifest.Input.xml`, v1.1.0

The reasoning was sound but rested on an untested premise: that `title` and
`layoutJson` are the same kind of value because they share an `of-type`. They are
not — `title` is a handful of characters and `layoutJson` is a JSON document.

## 2. What UAT found

Binding the control to the `sprk_project` main form in **spaarkedev1**, the
classic form designer **refused to save the value**:

> The layout json property cannot be more than 100 characters

`SingleLine.Text` is capped at **100 characters** in the classic form designer's
control-properties panel. That is below any usable configuration:

| Layout | Size |
|---|---:|
| Smallest config that does anything (`_version` + one field) | ~55 bytes |
| Realistic config (title + columns + 2 fields) | ~150 bytes |
| The 6-field `sprk_project` layout under test | **~310 bytes** |

So the property could not hold **any** realistic layout, and the ~55-byte
degenerate case is not a configuration anyone would author. `SingleLine.Text` is
not a viable carrier for this property at any size that matters.

## 3. Decision

Change `layoutJson` to **`of-type="Multiple"`** (Multiple Lines of Text).

```xml
<property name="layoutJson" display-name-key="Layout JSON"
          description-key="..."
          of-type="Multiple" usage="input" required="false" />
```

### Why `Multiple` and not `SingleLine.TextArea`

Microsoft's PCF property-element reference documents both:

| `of-type` | Documented capacity |
|---|---|
| `SingleLine.Text` | "This option simply displays text." (100 chars in the classic designer panel) |
| `SingleLine.TextArea` | "limit of **4000 characters** … the Multiple Lines of Text column is a better choice if large amounts of text are expected" |
| `Multiple` | "This column can contain up to **1,048,576 text characters**" |

4000 characters would in practice be enough for a header layout, but Microsoft
explicitly steers callers to `Multiple` for large text, and `Multiple` costs
nothing extra here. Taking the documented recommendation avoids revisiting this a
third time if layouts grow.

### Blast radius: none in code

`of-type="Multiple"` still generates a `StringProperty`:

```ts
// control/generated/ManifestTypes.d.ts
layoutJson: ComponentFramework.PropertyTypes.StringProperty;
```

so `context.parameters.layoutJson?.raw ?? null` in `control/index.ts` is
unchanged, and `resolveHeaderConfig` still receives `string | null`. **Zero
consumer code changed.**

## 4. What was verified, and how

### Verified mechanically (re-runnable)

| Check | Method | Result |
|---|---|---|
| `Multiple` is a legal `of-type` | `pac`'s own manifest validator during `npm run build:prod` — the `[build] Validating manifest...` step | PASS |
| Full production build | `npm run build:prod` | Succeeded |
| Generated types unchanged | `grep layoutJson control/generated/ManifestTypes.d.ts` | still `StringProperty` |
| Emitted manifest carries it | `out/controls/control/ControlManifest.xml` | `of-type="Multiple"` |
| Packed solution carries it | read `ControlManifest.xml` out of `RecordHeaderPcf_v1.1.1.0.zip` | `of-type="Multiple"` |
| No apostrophe in any attribute VALUE | regex over the manifest with comments stripped | clean |
| Documented capacity | MS Learn, PCF manifest-schema-reference / property, "Using of-type" table | 1,048,576 chars |

### NOT verified — needs an operator in the classic designer

The following cannot be established from the repo or the Web API, because the
100-character cap is a **form-designer UI validation**, not a manifest or
platform constraint. It leaves no artifact a build or an OData query can read:

1. That the designer's control-properties panel renders a **multi-line editor**
   for a `Multiple` input property (ergonomics — the original task-001 question).
2. That a ~310-byte value **round-trips** through form XML: save, publish,
   reopen, and read back byte-identical.
3. That the 100-character error is genuinely gone rather than replaced by a
   different cap.

**Operator step to close this out** (after importing
`Solution/bin/RecordHeaderPcf_v1.1.1.0.zip`): open the `sprk_project` main form
in the **classic** designer, paste the 6-field layout into Layout JSON, save +
publish, reopen, and confirm the value returns intact and the header renders the
configured layout. Until that is done, item 3 above is *expected* to pass but is
not evidence.

## 5. Why this is recorded as a decision rather than a spike result

The spike was an ergonomics check. UAT turned it into a **correctness** question
and answered it definitively: `SingleLine.Text` cannot carry the property at all.
No further spike is warranted — running one now would only re-derive a result the
error message already gave us.

**Do NOT revert `layoutJson` to `SingleLine.Text`.** A reverting change must
first explain how a >100-character layout is meant to be saved.
