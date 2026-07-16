# UAT helper — mock the Association Engine decision (see the "multi" review UI)

The Connections PCF renders whatever the Association Engine wrote to the
`sprk_associationprovenance` JSON column at ingest time. A real inbound email often
matches only ONE record (so you see one slot). To exercise the **multi-association**
and **ambiguous** review UI without waiting for a naturally multi-matching email,
seed the provenance JSON by hand.

## Fastest way — browser console on the email record

1. Open a **test** `sprk_communication` record (one you don't mind overwriting).
2. F12 → Console. Paste + run the snippet below.
3. It writes a 4-slot provenance (Matter + Organization + Contact + an **ambiguous**
   Invoice with two competing matches) and refreshes the form. The Connections panel
   should now show 4 rows, an "Accept all", the ambiguous slot's "File here"
   alternatives, and ✨-suggested Create actions.

```js
(() => {
  const id = Xrm.Page.data.entity.getId().replace(/[{}]/g, "");
  const provenance = {
    version: 1,
    direction: "inbound",
    decision: {
      status: "Suggested", autoFiled: false, killSwitchEnabled: true,
      autoFileThreshold: 0.85, topDeterministicConfidence: 0.82,
      topConfidence: 0.82, aiInvolved: true,
      reason: "Multiple deterministic + semantic matches; awaiting review."
    },
    rungsFired: ["ThreadContinuity","ParticipantCorrelation","StructuralDetector","SemanticMatch"],
    candidates: [
      { field:"sprk_regardingmatter", targetEntity:"sprk_matter",
        targetId:"00000000-0000-0000-0000-000000000001",
        targetName:"Henderson v. Acme — Breach of Contract",
        reinforcedConfidence:0.82, deterministicConfidence:0.82, written:false, conflict:false,
        contributors:[
          {rung:"ThreadContinuity",confidence:0.70,provenance:"reply to a thread already filed to this matter"},
          {rung:"ParticipantCorrelation",confidence:0.55,provenance:"2 known matter participants on the thread"}
        ]},
      { field:"sprk_regardingorganization", targetEntity:"sprk_organization",
        targetId:"00000000-0000-0000-0000-000000000002",
        targetName:"Acme Corporation",
        reinforcedConfidence:0.78, deterministicConfidence:0.78, written:false, conflict:false,
        contributors:[{rung:"ParticipantCorrelation",confidence:0.78,provenance:"sender domain acme.com matches this organization"}]},
      { field:"sprk_regardingperson", targetEntity:"contact",
        targetId:"00000000-0000-0000-0000-000000000003",
        targetName:"Ralph Schroeder",
        reinforcedConfidence:0.70, deterministicConfidence:0.70, written:false, conflict:false,
        contributors:[{rung:"ParticipantCorrelation",confidence:0.70,provenance:"sender email matches this contact"}]},
      { field:"sprk_regardinginvoice", targetEntity:"sprk_invoice",
        targetId:"00000000-0000-0000-0000-000000000004",
        targetName:"INV-2026-0417",
        reinforcedConfidence:0.66, deterministicConfidence:0.66, written:false, conflict:true,
        contributors:[{rung:"StructuralDetector",confidence:0.66,provenance:"invoice number INV-2026-0417 detected in body"}]},
      { field:"sprk_regardinginvoice", targetEntity:"sprk_invoice",
        targetId:"00000000-0000-0000-0000-000000000005",
        targetName:"INV-2026-0418",
        reinforcedConfidence:0.61, deterministicConfidence:0.61, written:false, conflict:true,
        contributors:[{rung:"StructuralDetector",confidence:0.61,provenance:"invoice number INV-2026-0418 also referenced"}]}
    ],
    signals: [
      { category:"invoice", confidence:0.66, provenance:"invoice number detected", obligations:["payment-review"] },
      { category:"event",   confidence:0.60, provenance:"calendar invite detected", obligations:["calendar-response"] }
    ]
  };
  return Xrm.WebApi.updateRecord("sprk_communication", id, {
    sprk_associationprovenance: JSON.stringify(provenance),
    sprk_associationstatus: 100000003 // Suggested
  }).then(() => Xrm.Page.data.refresh(false)).then(() => console.log("mock provenance written"));
})();
```

## Notes

- **Display-only mock**: the GUIDs above are fake, so the slots render from `targetName`
  (no lookup needed). **Confirm/Accept-all will FAIL the write** because those records
  don't exist. To test the *write* path too, replace the fake `targetId`s with real
  record GUIDs from your env (and matching `targetName`s).
- The **ambiguous** slot (two `sprk_regardinginvoice` candidates with `conflict:true`)
  is what renders "Two possible matches — choose one" + the indented **File here**
  rows. That's the per-slot candidate list.
- Set `sprk_associationstatus` to `100000000` (Resolved) instead to see the **filed /
  read-only** rendering; `100000004` = Ambiguous.
- To reset a test record: run the same snippet with `sprk_associationprovenance: null`
  and `sprk_associationstatus: 100000001` (Pending Review).

## Provenance JSON schema (for authoring your own)

`ProvenanceDoc`: `{ version, direction, decision, rungsFired[], candidates[], signals[] }`
- `candidate`: `{ field, targetEntity, targetId, targetName?, reinforcedConfidence,
  deterministicConfidence, written, conflict, contributors[] }`
  - `field` uses the display slot key: `sprk_regardingmatter | sprk_regardingproject |
    sprk_regardingorganization | sprk_regardingaccount | sprk_regardingperson (Contact) |
    sprk_regardinginvoice | sprk_regardingservicerequest | sprk_regardingevent |
    sprk_regardingworkassignment`
  - two+ candidates on the SAME `field` with `conflict:true` → an **ambiguous** slot
  - `contributor.rung`: `ExplicitReference | ThreadContinuity | ParticipantCorrelation |
    StructuralDetector | SemanticMatch | AiClassification`
- `signal`: `{ category, confidence, provenance, obligations[] }` — `category:"invoice"`
  → Link Invoice ✨; `category:"event"` or `obligations:["calendar-response"]` → Create
  Event ✨; `obligations:["deadline-response"|"payment-review"]` → Create To Do ✨.
</content>
</invoke>
