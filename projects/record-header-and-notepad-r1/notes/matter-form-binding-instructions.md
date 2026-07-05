# Matter Form Binding — Maker Instructions (DEF-02)

> **Task**: Task 061 (Phase 6). Cannot be coded — requires maker access to Dataverse form editor.
> **Estimated time**: 20–30 min.
> **Prerequisite**: `MatterHeaderPcf_v1.0.11.0.zip` (or later) imported + published in the target environment.
> **Effect**: The `MatterHeaderPcf` control becomes visible on the Matter form's header section. Until this is done, R1's shipped PCF is INVISIBLE to end-users even though the solution is imported.

---

## Why manual

R1's spec (owner clarification O3) deferred the Matter form XML update to a "follow-on maker task." The form XML lives in the target environment's unmanaged solution and is edited via the maker portal (`make.powerapps.com`) — not via git/PR. Automating this step would require an owned-by-us "form-binding" solution that manages Matter's form component; this is out of R1's engineering scope.

---

## Prerequisites checklist

Before starting, verify:

- [ ] `pac auth list` shows the target environment as active (or you're signed in to `make.powerapps.com` for the target env)
- [ ] `MatterHeaderPcf` v1.0.11+ solution imported (Solutions → search "Matter Header PCF" — should be present)
- [ ] All customizations published (Publish all customizations button after import)
- [ ] `sprk_matter` form to bind to exists (typically the Main form — "Matter" or "Matter main form")
- [ ] Matter form's "MATTER INFORMATION" section (or wherever the header PCF should replace) is identified

---

## Step-by-step

### 1. Open the Matter form in the classic form designer

The modern designer's PCF-binding UX is unreliable for header-region controls. Use the classic path:

1. Navigate to `make.powerapps.com` → your target environment
2. Solutions → open the unmanaged solution that owns the Matter form (typically `Spaarke` or `SpaarkeMainSolution`)
3. Tables → sprk_matter → Forms → "Matter" (main form) → **⋯ menu → Edit form → Edit in classic**
4. The classic form designer opens in a new tab

### 2. Add the PCF to the header section

The MatterHeader PCF replaces the entire "MATTER INFORMATION" section on the form — it's designed as a single-cell section-filling control.

Preferred approach — bind to `sprk_matternumber` field's field-level PCF slot:

1. In the classic designer, click the `sprk_matternumber` field on the form (it should be in the header/summary section)
2. In the field properties panel, click **Controls** tab
3. Click **Add Control** → search for "Matter Header" → **Add**
4. Select the row for `Matter Header` in the controls list
5. In the "Choose format" area, toggle **Web** (and optionally **Phone** / **Tablet**) so it renders on all form factors
6. Confirm the **Bound field** property maps to `sprk_matternumber` (this is the R1 v1.0.1+ manifest requirement — see `notes/design-alignment-corrections.md`)
7. Optionally set:
   - **Header title**: `Matter` (default) — customize if desired
   - **Show version footer**: `No` (Yes = shows the `v1.0.11` badge — useful during QA, hide in prod)

### 3. Reflow the header section

The MatterHeader PCF replaces the visual rendering of the bound field. The other fields normally in the header section (Matter Number, Matter Name, etc.) are read from Dataverse by the PCF itself via `Xrm.WebApi.retrieveRecord` — they do NOT need to be individually placed on the form.

Recommended cleanup:
- Delete the header section's OTHER fields (Matter Name, Matter Type, Practice Area, Matter Description) from the form. The PCF displays them; having them ALSO show as raw Dataverse fields creates a duplicate stack.
- Keep the `sprk_matternumber` field (invisible-behind-PCF is fine — the PCF replaces its rendering)
- Leave the rest of the form (Tracking, Matter Health, other sections) untouched

### 4. Save and publish

1. Save the form (Save button, top-right)
2. Publish (top toolbar Publish button, or Save and Publish combo)
3. Wait for the "Publishing customizations" toast to complete

### 5. Verify

1. Navigate to a live Matter record (any existing sprk_matter record)
2. Confirm the header section renders the MatterHeaderPcf (5-field card + 3-icon toolbar sparkle/checkmark/annotation) instead of the raw Dataverse fields
3. Confirm the footer says `v1.0.11` (if `Show version footer = Yes`) or is absent (if `No`)
4. Sanity checks:
   - Click sparkle → AI Summary popover opens with proper white background + shadow (v1.0.11 FluentProvider portal fix)
   - Click checkmark → SmartTodo modal opens (85% × 85%). Note: DEF-11 in-flight will change this to a filtered Todo list.
   - Click annotation → Notepad modal opens (25% × 35%)
   - Click Matter Type or Practice Area → picker dropdown opens with top 10 unfiltered
   - Edit Matter Description → field goes dirty (form Save button lights up); click form Save → commits
   - Refresh page → edit persisted

---

## If it doesn't work

| Symptom | Cause | Fix |
|---|---|---|
| "Matter Header" doesn't appear in the Add Control search | Solution not imported/published | Import + publish `MatterHeaderPcf_v1.0.11.0.zip` |
| PCF renders but shows "—" for every field | Bound field mismatch | Confirm bound field is `sprk_matternumber`, not another field |
| Sparkle opens transparent popover with no background/shadow | Old version (pre-v1.0.11) still cached | Import v1.0.11+ solution; hard refresh browser (`Ctrl+Shift+R`) |
| Checkmark opens SmartTodo app but user wants filtered todos list | Not a bug — DEF-11 (in-flight in Phase 6) | Wait for Phase 6 task 065 completion; then upload the new PCF version |
| Notepad modal opens but is huge (70% × 80%) | Old version (pre-v1.0.7) still cached | Import v1.0.7+ solution; hard refresh |

---

## For other entities

Once `ProjectHeaderPcf`, `InvoiceHeaderPcf`, `EventHeaderPcf` etc. ship (per DEF-05, separate follow-on projects), the same procedure applies with:
- Different form (project/invoice/event's main form instead of Matter's)
- Different bound field (e.g., `sprk_projectnumber` on Project)
- Different section cleanup (delete the header section's raw fields for that entity)

The instructions above are entity-agnostic apart from the `sprk_matter` / `sprk_matternumber` names.
