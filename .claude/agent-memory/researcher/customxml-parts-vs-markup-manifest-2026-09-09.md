---
name: customxml-parts-vs-markup-manifest-2026-09-09
description: Settles the "Word removes custom XML" scare — the 2010 i4i removal hit the in-body w:customXml MARKUP element, not the /customXml/itemN.xml package PARTS; plus CustomXmlParts requirement-set host floor, whether the unified JSON manifest can declare it, and the WordApi 1.4 host floor
metadata:
  type: project
---

Evidence pack for `spaarkeai-word-add-in-r1` task 019 (whether to stamp document identity into a custom XML part, and whether the manifest can declare it).

**Why:** the support article "Custom XML markup in Word" says Word *removes* custom XML on open. If that applied to the parts the Office.js `CustomXmlParts` API writes, the whole identity-stamp approach dies. It does not.

### The markup-vs-parts distinction is CONFIRMED (two different things, same name)
- **Removed (2010, i4i):** the in-body `w:customXml` element — the "pink tags". Open XML SDK models it as `DocumentFormat.OpenXml.Wordprocessing.CustomXmlBlock` / `CustomXmlRun`, doc string: *"When the object is serialized out as xml, it's qualified name is w:customXml."*
- **NOT removed:** the Custom XML Data Storage **part** (`/customXml/itemN.xml` + `itemPropsN.xml`), modeled as `DocumentFormat.OpenXml.**Packaging**.CustomXmlPart`. Different namespace, different concept (OPC part vs body element).
- The load-bearing quote is Microsoft's own i4i explainer (Gray Knowlton, GM Office product management, 2009-12-23, archived on Learn), **"Not Affected"** section: *"Content Controls and XML data stored within DOCX or DOCM files will not be affected by this change."*
- Corroboration that parts are alive today: Word VBA `Document.CustomXMLParts` ("the custom XML in the XML data store") still documented, no deprecation; `Word.Document.customXmlParts` was **added in WordApi 1.4 (2022)** — 12 years AFTER the markup removal; persisting-add-in-state doc (ms.date 2026-03-24): *"This data persists with the file, independent of the add-in."*
- The support article itself never says "part" or "data store" — it is silent, not contradictory. No Microsoft doc found stating parts are removed on open.
- **Real residual risk is the Document Inspector**, not Word's open path: its "Custom XML Data" module finds *"Custom XML data that might be stored within a document"* and Remove All strips it. A user can wipe the stamp. Design for that.

### Manifest facts
- `CustomXmlParts` requirement set: EXISTS, **unversioned → treat as 1.1** ("All of these API requirement sets are version 1.1, unless otherwise specified"). Hosts: Word web / Word Windows (M365 sub **or** perpetual Office 2016) / Word Mac / Word iPad. Word-only. (page ms.date 2025-10-10)
- Unified JSON manifest CAN declare it. `capabilities[].name` is a **free-form string, maxLength 128, required**; `minVersion`/`maxVersion` optional. Doc: *"The `"minVersion"` property is optional. If it isn't present, Office assumes version "1.1"."* Learn's own examples name Common-API sets (`TableBindings`, `OoxmlCoercion`, `SharedRuntime`, `AddinCommands`) — not just host sets. **No enumerated allow-list exists anywhere**, so this is schema-legal-by-construction, not enumerated-legal.
- `m365-app-prev` (= `manifestVersion: "devPreview"`, what the repo uses) shares ONE moniker range with 1.19→1.30 on the capabilities schema page — **zero devPreview-vs-1.x difference** for this property.
- Casing gotcha: Learn's own example writes `"DialogAPI"` while the real set is `DialogApi`. Use the exact string from the requirement-sets page: `CustomXmlParts`.

### WordApi 1.4 floor (unchanged from [[word-document-identity-desk-research-2026-09-09]])
Web Supported · Win 2208 (Build 15601.20148) · **volume-licensed/LTSC: Office 2024 = 2208 (15601.20148)** · Mac 16.64 (22081401) · iPad 16.64. Both `Document.customXmlParts` and `Document.settings` are annotated `[ API set: WordApi 1.4 ]`.
**Live repo defect still open:** `src/client/office-addins/word/manifest.json` declares WordApi **1.3**. Bumping 1.3→1.4 costs only pre-2022 builds.

Related: [[word-document-identity-desk-research-2026-09-09]], [[word-extensibility-platform-2026-09-01]]
