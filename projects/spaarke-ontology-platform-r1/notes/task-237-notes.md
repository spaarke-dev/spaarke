# Task 237 notes: ClosesTodo fails closed

- Predicate only (`InquiryDispositionService.ClosesTodo`, no I/O). `statecode`/`statuscode` keys match trimmed, case-insensitive.
  Number: integral via `TryGetInt64`, else `TryGetDouble` with `value == Math.Floor(value)` and within long range. String: trimmed,
  `long.TryParse(NumberStyles.Integer, InvariantCulture)`. Null: not closing. Any other kind, an unparseable string or a non-integral
  number on those keys: closing (the open-inquiry read then decides).
- No mapper coercion: `DataverseWriteItemMapper.MapCoreAsync` passes simple values through as sent (it only trims/lower-cases column
  names), so a string `"1"` reaches Dataverse as written; the guard cannot rely on a mapper normalising the type.
- Tests: new `InquiryDispositionClosesTodoTests` (35 cases: 21 must-fire, 14 must-not-fire; 18
  failed before the change). Added guard `Run_R4_TheRedactionKeepsEachEvidenceLinesKindAndRef_AndRemovesOnlyTextAndSource`
  (redaction keeps kind and ref; privilege derivation reads them, uac-r2 note on #3292); verified it fails when the redaction also drops them.
- Publish size: no package or file added to the BFF; delta not material.
