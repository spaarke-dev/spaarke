---
name: navigateto-prepopulate-lookup-createfromentity-2026-08-17
description: How to pre-populate a lookup field on an OOB entityrecord CREATE form opened via Xrm.Navigation.navigateTo/openForm — the data/formParameters three-key convention (id/name/type) vs createFromEntity (relationship-mapped defaults); which is reliable
metadata:
  type: reference
---

# Pre-populating a lookup on a navigateTo/openForm CREATE form (2026-08-17)

Related: [[uci-navigate-to-specific-view-2026-08-14]]

**navigateTo `data` (entityrecord) == openForm `formParameters` == URL `extraqs` — same flat dictionary, same lookup rules.** The navigateTo Learn page defines `data` as "A dictionary object that passes extra parameters to the form. The parameters can be table columns with default values that are set on new forms" and links to the SAME "Set column values using parameters passed to a form" article that governs openForm formParameters and the URL `extraqs`. So it is a FLAT `attributeName -> value` dictionary, NOT a nested/typed structure.

**Lookup three-key convention (id / name / type) — CONFIRMED and applies to navigateTo data too.** To set a lookup you pass up to three sibling keys:
- `{fieldlogicalname}` = GUID (id)
- `{fieldlogicalname}name` = display text (suffix `name`)
- `{fieldlogicalname}type` = target table logical name (suffix `type`) — required for customer/owner/polymorphic; harmless/optional for a simple single-target lookup.
openForm Example 3 (email regardingobjectid) does exactly: `regardingobjectid` + `regardingobjectidname` + `regardingobjectidtype`="systemuser". For our case: `sprk_regardingmatter`, `sprk_regardingmattername`, `sprk_regardingmattertype`="sprk_matter" (all lowercase logical names).

**JSON.stringify([{id,entityType,name}]) under one key is UNSUPPORTED.** No Learn doc describes a stringified-array lookup shape for data/formParameters/extraqs. Invalid parameters throw ("Any attempt to pass an invalid parameter or value results in an error"). This is almost certainly why our current approach doesn't populate the field.

**DOC INCONSISTENCY / gotcha:** the field-values article prose says "You can't set the values for partylist or regarding lookups," yet openForm Example 3 sets `regardingobjectid` via id+name+type. Practical read: simple single-target lookup → id+name works (type optional); polymorphic/regarding → include the `type` key. If `sprk_regardingmatter` is a SIMPLE lookup to sprk_matter only (not polymorphic), id+name(+type) works. If it were truly polymorphic multi-target, the URL-param path is documented as unsupported.

**createFromEntity = relationship-MAPPED defaults, NOT an arbitrary field setter.**
- Location: navigateTo → in the `pageInput` (entity record object); openForm → in `entityFormOptions`. Type `Lookup` = `{ entityType, id, name? }` (all strings).
- Learn (both pages, verbatim): "Designates a record that provides default values based on **mapped column values**." It does NOT let you name a target field; it copies whatever the Dataverse attribute MAPPINGS on the parent→child relationship say to copy.
- Requires: a 1:N relationship parent(sprk_matter)→child(sprk_todo) AND explicit column mappings (map-entity-fields). The mapping of the parent's primary-key/lookup into `sprk_regardingmatter` must exist for that lookup to be pre-filled. Map-columns doc caveat: "If you create a record in any way other than from the associated view of the primary table, data is not mapped" — createFromEntity is the API that reproduces the associated-view mapping path.

**BOTTOM LINE / recommendation:** To deterministically set a SPECIFIC named lookup (`sprk_regardingmatter`) on the create form, use the `data` three-key convention (id + name + type), NOT createFromEntity. createFromEntity is indirect (depends on relationship + attribute mappings existing and correct) and can't target an arbitrary field by name. Gotchas: (1) the lookup MUST be on the form or the PCF's `getAttribute('sprk_regardingmatter')` returns null; (2) lowercase logical names; (3) drop the stringified-array shape.

**Sources:**
- https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/navigateto (data = dictionary; createFromEntity = Lookup, mapped column values)
- https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/set-field-values-using-parameters-passed-form (MOST authoritative for lookup id/name/type + suffix rules + "can't set partylist/regarding")
- https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/openform (createFromEntity in entityFormOptions; Example 3 regardingobjectid id+name+type)
- https://learn.microsoft.com/en-us/power-apps/maker/data-platform/map-entity-fields (mappings copy parent→child; "created in any way other than from the associated view... data is not mapped")

**Open questions:** Is `sprk_regardingmatter` a simple single-target lookup (works with id/name) or polymorphic (URL-param path documented-unsupported)? Verify field metadata in Dataverse before relying on the three-key approach.
