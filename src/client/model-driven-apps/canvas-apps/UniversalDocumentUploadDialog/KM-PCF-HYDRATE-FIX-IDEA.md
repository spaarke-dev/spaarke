# PCF Hydration — Delta Fix Plan (Custom Page Quick Create)

This document focuses on **what changes versus your current setup** and gives exact patches to get hydration working in a **Custom Page (dialog)** that hosts a **PCF**.

---

## 1) What’s different from what you already have

- **Hydrate in `Screen.OnVisible`, not `App.OnStart`**  
  You currently read params (and sometimes guard/close) in `App.OnStart`. We move **all hydration** to `Screen.OnVisible` and **never** close from `App.OnStart`.

- **Dual-path parameter intake**  
  You assume discrete keys via `Param("key")`. We add a fallback to parse a single JSON **blob** under `Param("data")` with `ParseJSON` + `Text(...)` so both platform behaviors are supported.

- **Initialize variables early, render late**  
  We **declare variables in `App.OnStart`** (types only), but **gate the UI/PCF** with a single `Visible` expression until required values exist. This avoids “blank screen” and designer errors.

- **PCF becomes passive until inputs exist**  
  Your PCF throws when inputs are empty. We make `updateView` **non‑throwing** and **idempotent**, initializing once when inputs are present.

- **Launcher includes `appId` and normalized payload**  
  We add `appId` and ensure `pageInput.data` keys are **case-exact** and values are **plain strings** (no braces around GUIDs).

- **App publish discipline**  
  We call out that **publishing the app** (not just “Publish All Customizations”) is required after any Custom Page update.

---

## 2) Three must-change items (copy/paste)

### A) App.OnStart — declare only
```powerfx
// Do NOT read params or close the page here
Set(varInit, false);
Set(varParentEntityName, Blank());
Set(varParentRecordId,  Blank());
Set(varContainerId,     Blank());
Set(varParentDisplayName, Blank());
```

### B) Screen.OnVisible — dual-path hydration (direct keys + JSON blob)
```powerfx
If(
    Not(varInit),
    Set(varInit, true);

    // direct keys first
    Set(_pEntity,        Param("parentEntityName"));
    Set(_pId,            Param("parentRecordId"));
    Set(_pContainer,     Param("containerId"));
    Set(_pDisplayName,   Param("parentDisplayName"));

    // fallback: single JSON string in Param("data")
    Set(_raw, Param("data"));
    If(
        Or(IsBlank(_pEntity), IsBlank(_pId), IsBlank(_pContainer)),
        If(
            Not(IsBlank(_raw)),
            Set(_cfg, ParseJSON(_raw));
            Set(_pEntity,      Coalesce(_pEntity,      Text(_cfg.parentEntityName)));
            Set(_pId,          Coalesce(_pId,          Text(_cfg.parentRecordId)));
            Set(_pContainer,   Coalesce(_pContainer,   Text(_cfg.containerId)));
            Set(_pDisplayName, Coalesce(_pDisplayName, Text(_cfg.parentDisplayName)))
        )
    );

    // commit
    Set(varParentEntityName,  _pEntity);
    Set(varParentRecordId,    _pId);
    Set(varContainerId,       _pContainer);
    Set(varParentDisplayName, _pDisplayName)
);
```

### C) Gate render and bind PCF inputs
```powerfx
// Container.Visible (or PCF.Visible)
And(
  Not(IsBlank(varParentEntityName)),
  Not(IsBlank(varParentRecordId)),
  Not(IsBlank(varContainerId))
)

// PCF property bindings
parentEntityName   = varParentEntityName
parentRecordId     = varParentRecordId
containerId        = varContainerId
parentDisplayName  = varParentDisplayName
```

---

## 3) PCF patch (no throw, init once)

```ts
// In your control
private _initialized = false;

public updateView(ctx: ComponentFramework.Context<IInputs>): void {
  const entity    = ctx.parameters.parentEntityName?.raw ?? "";
  const recordId  = ctx.parameters.parentRecordId?.raw ?? "";
  const container = ctx.parameters.containerId?.raw ?? "";
  const name      = ctx.parameters.parentDisplayName?.raw ?? "";

  if (!this._initialized) {
    if (entity && recordId && container) {
      this.bootstrap({ entity, recordId, container, name });
      this._initialized = true;
    }
    return; // Wait for the next update instead of throwing
  }
}
```

---

## 4) Launcher patch (normalize payload + appId)

```js
const pageInput = {
  pageType: "custom",
  name: "sprk_documentuploaddialog_e52db", // exact logical name
  appId: Xrm.Utility.getGlobalContext().client.getAppId(),
  data: {
    parentEntityName,           // "sprk_matter"
    parentRecordId,             // "3a785f76-c773-f011-b4cb-6045bdd8b757" (no braces)
    containerId,                // "b!yLRdWEOA..."
    parentDisplayName           // "Matter 123"
  }
};

const navOptions = {
  target: 2,                   // dialog
  position: 1,                 // right
  width: { value: 640, unit: "px" }
};

Xrm.Navigation.navigateTo(pageInput, navOptions)
  .then(result => { /* handle Exit(...) or refresh grid */ })
  .catch(console.error);
```

---

## 5) Ten-minute validation script

1. **Publish the Custom Page** and **publish the model-driven app**. Hard-refresh the shell.
2. On the page, drop a label with:
   ```powerfx
   Concatenate(
     "E=", Param("parentEntityName"),
     " ID=", Param("parentRecordId"),
     " C=", Param("containerId"),
     " RAW=", Left(JSON(Param("data")), 120)
   )
   ```
   - If direct keys are blank but `RAW` shows JSON, your fallback path will parse it.
3. Confirm the PCF container becomes visible when vars are set.
4. Verify the PCF initializes once (log from `bootstrap`), and no exceptions are thrown on first render.
5. Complete the flow; from the page, close with:
   ```powerfx
   Exit({ status: "uploaded", count: varUploadedCount })
   ```
   and confirm your caller sees the result in `.then(result => ...)`.

---

## 6) Failure matrix (quick triage)

- **Label shows all blanks** → Wrong page name, missing `appId`, page not added to app, or app not published.  
- **Label shows JSON in RAW but keys blank** → Your fallback wasn’t present; add the `ParseJSON` path above.  
- **PCF never starts** → `Visible` gate false; check variable names and binding.  
- **Dialog closes** → PCF threw on first render; apply the non-throwing `updateView`.  
- **Works inline, fails in dialog** → Remove any `Back()/Exit()` guards from `App.OnStart`; keep logic in `OnVisible`.

---

## 7) Why this works

- It embraces both parameter delivery modes seen in the wild (discrete keys vs. single JSON blob).  
- It defers control initialization until inputs exist, removing timing races.  
- It combines publish discipline and payload normalization to avoid stale or mismatched shells.

**Outcome:** The Custom Page will hydrate variables deterministically and the PCF will initialize reliably for Quick Create dialogs.
