# Custom Page Dialog (Quick Create) — Diagnosis & Fix Guide

This guide documents the issue observed when launching a **Custom Page** as a right-side dialog to host the **SDAP Universal Document Upload PCF**. It explains the behavior, the most likely root causes, and provides a concrete, production-ready fix plan that follows Spaarke ADRs (thin client launcher, no heavy plugins, PCF does the UX, BFF handles services).

## 1) Scenario and observed behavior

You launch a Custom Page dialog from a subgrid command. The dialog opens, the PCF constructor fires, but the page closes almost immediately with a generic client error. Console logs show:

- The button gathers valid context (entity name, record id, container id, display name).
- `navigateTo({ pageType: "custom", ... })` is invoked with those values.
- The PCF logs **empty** parameters on first render (`{ parentEntityName: '', parentRecordId: '', ... }`) and throws **Parent context not loaded**.
- The canvas host treats the exception as fatal and closes the dialog. The navigation promise resolves and the subgrid refresh runs.

**Key inference:** The Custom Page loads before `Param("…")` values have hydrated, and the PCF initializes (and throws) before the page bridges `Param("…")` keys into the PCF input properties. Any early `Back()`/`Exit()` guards in `App.OnStart` amplify this race by closing the dialog when params appear blank.

## 2) Root-cause analysis (what actually goes wrong)

- **Custom Page parameter hydration is asynchronous.** When launched via `navigateTo`, `Param("key")` can return blank during `App.OnStart`; the values typically arrive by the time the first `Screen.OnVisible` executes.
- **PCF lifecycle:** The control’s `constructor` and first `updateView` run immediately. If required inputs are missing and the code throws, the host closes the dialog.
- **Guarding too early:** Checking for required params in `App.OnStart` and calling `Back()`/`Exit()` when blank closes the dialog before the page gets a chance to hydrate.
- **Optional hardening:** Not passing `appId` can cause edge-resolution issues in some contexts; including it makes navigation unambiguous.

## 3) Design basis for the recommendations

- **Bridge parameters in the canvas layer**: Hydrate globals on the first `Screen.OnVisible`; copy each `Param("…")` into variables once the screen renders, then bind those globals to the PCF inputs. Avoid wiring `Param()` directly to the control.
- **Gate rendering**: Only render/mount the PCF after the globals contain non-empty values. If the first `updateView` fires before hydration, the control should simply wait.
- **Close intentionally**: Prefer `Exit(value)` from the Custom Page to close the dialog and return a small payload. Avoid `Back()` in Custom Pages.
- **Harden launch**: Include `appId` in `pageInput` so the platform resolves the page inside the correct app.

## 4) Recommended changes — summary

- Read parameters in `Screen.OnVisible` and stash them in globals; keep `App.OnStart` free of early guards.
- Bind PCF inputs to those globals and gate visibility on their presence.
- Make the PCF idempotent: initialize once when all inputs exist; never throw on missing inputs.
- Include `appId` in `navigateTo` and ensure the Custom Page is added to the model-driven app (hidden from nav).
- Use `Exit({ status: "uploaded", ... })` when the flow completes to return a result.

---

## 5) Step-by-step — Manual Custom Page updates (Canvas layer)

1. **Remove early closes**
   - Delete any `Back()`/`Exit()` calls from **App.OnStart**. Do not guard in `App.OnStart`.

2. **Hydrate globals on the first screen render**
  - On the document upload screen’s `OnVisible`:
     ```powerfx
     Set(varParentEntityName, Param("parentEntityName"));
     Set(varParentRecordId, Param("parentRecordId"));
     Set(varContainerId, Param("containerId"));
     Set(varParentDisplayName, Param("parentDisplayName"));
     Set(varInit, true);
     ```

3. **Optional: show a friendly message if context is missing**
   - Do not close; let the user see the error.
     ```powerfx
     If(
         varInit && (IsBlank(varParentRecordId) || IsBlank(varContainerId)),
         Notify("Context missing. Close and retry.", NotificationType.Error)
     );
     ```

4. **Gate the PCF render**
   - Put the PCF inside a container and set the container’s `Visible`:
     ```powerfx
     varInit && !IsBlank(varParentEntityName) && !IsBlank(varParentRecordId) && !IsBlank(varContainerId)
     ```

5. **Bind PCF inputs**
   - Map variables to manifest inputs:
     - `ParentEntityName` → `varParentEntityName`
     - `ParentRecordId` → `varParentRecordId`
     - `ContainerId` → `varContainerId`
     - `ParentDisplayName` → `varParentDisplayName`

6. **Close intentionally after success**
   - Wherever you complete the upload:
     ```powerfx
     Exit({ status: "uploaded", count: varUploadedCount })
     ```

7. **Save and publish**
   - Save the page; then **Publish** the model-driven app (required for runtime to pick up changes).

---

## 6) Step-by-step — Command button (launcher) updates

1. **Build pageInput with appId and pass parameters**:
   ```js
   const pageInput = {
     pageType: "custom",
     name: "sprk_documentuploaddialog_e52db", // logical name
     appId: Xrm.Utility.getGlobalContext().client.getAppId(),
     parameters: {
       parentEntityName,
       parentRecordId,
       containerId,
       parentDisplayName
     }
   };

   const navOptions = {
     target: 2,                 // dialog
     position: 1,               // right
     width: { value: 640, unit: "px" }
   };

   Xrm.Navigation.navigateTo(pageInput, navOptions)
     .then(result => {
       // result may be the value passed to Exit(...) or undefined
       // refresh subgrid here as needed
     })
     .catch(console.error);
   ```

2. **Case sensitivity**
  - Ensure the parameter keys exactly match the names used with `Param("…")` in the Custom Page.

3. **Add telemetry (optional)**
   - Log the `appId`, page name, and a correlation id to aid diagnostics.

---

## 7) Step-by-step — PCF control code changes

1. **Manifest inputs**
   - Define inputs (e.g., `parentEntityName`, `parentRecordId`, `containerId`, `parentDisplayName`) as `SingleLine.Text`.

2. **Idempotent initialization in `updateView`**
   ```ts
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
       return; // Wait for next update if not ready
     }

     // Optional: handle prop changes if you expect them
   }
   ```

3. **Do not throw on missing inputs**
   - Log a warning and return. Throwing in `updateView` will close the dialog.

4. **Close from the page, not the PCF**
   - Let the canvas page call `Exit(...)` after success; do not attempt to close from inside the PCF.

---

## 8) Validation & diagnostics

- **Visual check:** Add a temporary label to the page to display `Param("parentRecordId")` and confirm non-empty before the PCF shows.
- **Monitor:** Use Power Apps Monitor attached to the model-driven app session to observe parameter hydration and any control exceptions.
- **Publishing discipline:** After **any** Custom Page edit, **Publish** the model-driven app before retesting.
- **Result contract:** If you use `Exit({ status: ... })`, read the `result` in the `navigateTo(...).then(...)` and branch your client behavior (e.g., refresh subgrid on `status === "uploaded"`).

---

## 9) Rollout considerations

- **Feature flag the new dialog launcher** in the ribbon so you can fall back to the legacy quick-create until the new dialog passes QA.
- **Telemetry and correlation IDs** in both the launcher and PCF will speed up support if users encounter environment-specific issues.
- **Docs**: Store this guide alongside the page and control code; include snippets in the repo’s `docs/` and wire into your CI/CD guidance.

---

### Minimal snippets (copy/paste)

**Screen.OnVisible**
```powerfx
Set(varParentEntityName, Param("parentEntityName"));
Set(varParentRecordId, Param("parentRecordId"));
Set(varContainerId, Param("containerId"));
Set(varParentDisplayName, Param("parentDisplayName"));
Set(varInit, true);
```

**Container.Visible**
```powerfx
varInit && !IsBlank(varParentEntityName) && !IsBlank(varParentRecordId) && !IsBlank(varContainerId)
```

**Launcher (JS)**
```js
const pageInput = {
  pageType: "custom",
  name: "sprk_documentuploaddialog_e52db",
  appId: Xrm.Utility.getGlobalContext().client.getAppId(),
  parameters: { parentEntityName, parentRecordId, containerId, parentDisplayName }
};
const navOptions = { target: 2, position: 1, width: { value: 640, unit: "px" } };
Xrm.Navigation.navigateTo(pageInput, navOptions).then(result => {
  // handle Exit(...) value if provided
});
```

**PCF `updateView`**
```ts
public updateView(ctx: ComponentFramework.Context<IInputs>): void {
  const e = ctx.parameters.parentEntityName?.raw ?? "";
  const id = ctx.parameters.parentRecordId?.raw ?? "";
  const c = ctx.parameters.containerId?.raw ?? "";
  if (!this._initialized) {
    if (e && id && c) { this.bootstrap({ entity: e, recordId: id, container: c }); this._initialized = true; }
    return;
  }
}
```

---

**Result:** With these changes, the Custom Page dialog will stay open, receive parameters reliably, and the PCF will initialize deterministically once inputs are present—eliminating the “close on open” behavior and aligning with Spaarke’s pro-code ADRs.
