# SRFR-032 Inventory

## CREATE-mode detection pattern in RegardingResolverApp.tsx

**Existing pattern** (from Wave 3 SRFR-030 in `handlePickerSelect`, line 307):

```ts
if (!writeCtx.hostRecordId) {
  (window as unknown as { __sprk_regarding_pending__?: Record<string, unknown> })
    .__sprk_regarding_pending__ = { ... };
}
```

CREATE-mode is detected via **absence of `hostRecordId`** on the write context, which is populated from `getHostRecordId()` walking `Xrm.Page.data.entity.getId()`. On CREATE forms this returns undefined; on UPDATE forms it returns a 36-char GUID.

**Decision**: Reuse this exact pattern. Do NOT introduce `context.mode.getFormType()` or `contextInfo.entityRecord.recordType` as a third pattern. The `hostRecordId` gate is the only CREATE/UPDATE branching in the file and covers both `ResolverWriteHandler.applyRegardingSelection` (which uses the same gate to decide `updateRecord` vs return payload) and the seam write here.

## Record-number source

Wave 2 (SRFR-020) widened `applyResolverFields` return type to `IApplyResolverFieldsResult { recordNumber: string | null, recordNumberSourceField: string | null }`. The value is populated when:
- Metadata resolves a source-field via `sprk_recordtype_ref.sprk_regardingrecordnumberfield`
- The target record's value is a non-empty string

Otherwise `recordNumber: null` (NFR-06 graceful-blank for Contact/Account and unknown entities).

The shared `applyResolverFields` is called from `handlers/ResolverWriteHandler.applyRegardingSelection`. Currently `applyRegardingSelection` returns `IResolverWriteResult` with `success | catalogEntry | payload | error` — it discards the `recordNumber` on the awaited result.

**Change**: Widen `IResolverWriteResult` to include `recordNumber: string | null`, forward the value from `applyResolverFields`'s result.

## Presave contract (from webresource v1.2.0)

Per `sprk_todo_regarding_presave.js` line 37-48 docstring and `textKeyForField('sprk_regardingrecordnumber')` case:

```js
window.__sprk_regarding_pending__ = {
  hostEntity, entityType, entitySet, navProp, recordId, recordName, recordUrl,
  recordNumber,       // ← NEW in v1.2.0 (target's business-key from sprk_regardingrecordnumberfield)
  lookupAttribute,
};
```

If `recordNumber` key is absent → the presave loop at line 200 (`if (!(sourceKey in pending)) continue;`) skips it gracefully. Backward-compat verified in Wave 4 log.

## Type declaration

The seam is currently typed inline:
```ts
window as unknown as { __sprk_regarding_pending__?: Record<string, unknown> }
```

**Decision**: Extend the inline shape to include `recordNumber?: string | null` (keeps the shape local to the writer — same pattern as SRFR-030; no shared `.d.ts` exists for the seam today).

## Task scope

1. **Widen `IResolverWriteResult`** to include `recordNumber: string | null`. Forward from `applyResolverFields` return.
2. **Extend the CREATE seam payload** in `RegardingResolverApp.handlePickerSelect` to include `recordNumber` from `result.recordNumber`. Behavior on UPDATE (has `hostRecordId`): unchanged — no seam touch.
3. **Extend tests** — 4 new tests: (a) CREATE-mode with `recordNumber` value flows into seam; (b) UPDATE-mode leaves seam untouched; (c) CREATE-mode with `recordNumber: null` (graceful-blank) — seam includes `recordNumber: null`; (d) contract parity — asserted key set matches FR-A5-04 spec docstring.

## Divergences from POML

- POML Step 5 (Payload Type Declaration): shared `.d.ts` doesn't exist; the seam type is inline. Keep inline. Extending declared prop is trivial and stays with the writer.
- POML Step 4 (UPDATE Branch — No Bridge Write): verified — the existing `if (!writeCtx.hostRecordId)` guard already gates the write. No changes needed. Tests will assert this.
