# Act 4 step 2 (Cowork half): summarize-redlines (2026-11-05 23:58, 32 credits)

Prompt: `Brightwater sent back their markup: "Brightwater Mutual NDA - Brightwater response (tracked).docx" in the Acme Company team site, Shared Documents/Legal Knowledge/Matters/Brightwater Systems/. Summarize their redlines.`

Routing: Legal for Cowork (summarize-redlines). Connectors: SharePoint, Work IQ. It also opened `Brightwater Mutual NDA - Clean Revised.docx` (Act 3 output) as the base to compare.

## Chat output (verbatim)

I've summarized Brightwater's tracked changes below. There's one problem to sort out first: the untracked text in their markup doesn't match your latest clean draft, so the four tracked changes may not be the only changes.

Redline summary

All the tracked changes in Brightwater Mutual NDA - Brightwater response (tracked).docx are by "Brightwater Legal (P. Raman)". There are four substantive changes. Together they narrow the confidentiality protection:
- They add a residuals clause.
- They allow use of aggregated data.
- They shorten how long the confidentiality obligations last.
- They move governing law and courts to Brightwater's home state.

There are no tracked changes or comments in headers, footers or footnotes.

| Topic | Change | Impact | Beneficiary | Source / locator |
|---|---|---|---|---|
| Confidentiality: use restriction | The flat ban on using the other party's information "for any purpose other than the Purpose" now has an exception. Each party may use "aggregated data that does not identify the other party or reveal its Confidential Information solely to improve the services it provides to the other party." | Allows some use of data derived from Confidential Information outside the Purpose. The exception is limited to non-identifying aggregated data, used only to improve services to the other party. | Mixed. It applies to both parties, but in practice it helps whichever party provides services. Under the Purpose that looks like Brightwater, as the sensor platform provider. | cl. 4.5 |
| Confidentiality: residuals | New section 8 "Residuals". Either party may use general ideas, concepts, know-how or techniques kept in the "unaided memory" of staff who had access. Memory counts as "unaided" unless the person deliberately memorized the information to use it later. | A significant carve-out. Information people remember can be used without breaching the NDA. Because "unaided" is defined so narrowly, the carve-out is hard to police. It sits awkwardly with the reverse-engineering ban in cl. 4.4. | Mixed. It applies to both parties, but it favors whichever party receives more technical information. Which party that is depends on the expected flow of information. | New cl. 8 / 8.1 |
| Term | The confidentiality period after each disclosure drops from three (3) years to two (2) years. The 2-year disclosure window and the trade-secret carve-out are unchanged. | Ordinary Confidential Information is protected for a shorter time. Trade secrets stay protected for as long as they remain trade secrets. | Unclear. It favors the party that receives more information, because its obligations end sooner. The answer depends on the expected flow of information. | cl. 9.1 |
| Governing law and courts | Governing law changes from Delaware to California, and the non-exclusive courts change from Delaware to California. | Disputes would be decided under California law, and California courts become the named forum. | Brightwater, a California corporation based in Oakland. Acme is a Delaware corporation. | cl. 13.3 |

Is this the right base version? The untracked text in Brightwater's markup doesn't match Brightwater Mutual NDA - Clean Revised.docx. Some examples:
- The clean draft has a section 10 "Non-Solicitation", which is missing from their version. As a result, Remedies, Protected Reporting, Trade Secrets and General each move up one number in their version.
- The clean draft defines Confidential Information with marking and written-notice wording. Their version uses a shorter definition.
- The clean draft already says California law in cl. 14.3, and has section 8 marked "Intentionally Omitted".
- Their version fills in the Effective Date as November 10, 2026. The clean draft leaves it blank.

So either Brightwater marked up an earlier draft, or they made other changes without tracking them. I've summarized only the tracked changes. That list may not cover everything that differs from Acme's latest draft. It's worth asking Brightwater which version they started from before you respond on the substance.

Drafting and formatting changes: the only tracked changes are the four wording changes above. There are no tracked formatting-only changes.
