# UAT Round 4 Feedback — email-communication-solution-r4 (2026-07-21)

Source: owner UAT on dev after W11 import (Attachments v1.2.0, Connections v1.4.0, Actions v1.2.0).
Wave: **W12**. Mostly polish + two follow-up bugs on W10/W11 + one investigation.

Title spec home = task-101 standard [`docs/standards/UI-DESIGN-STANDARDS.md`](../../../docs/standards/UI-DESIGN-STANDARDS.md).
This round REFINES the title padding: **less padding ABOVE the title (title closer to the section top), MORE padding BELOW the title** (breathing room before the first row). Asymmetric — update the standard.

---

## A. CommunicationConnections PCF (RELATED RECORDS) → v1.5.0

- **A12-1** Reduce top space/padding so the title sits closer to the top of the section boundary.
- **A12-2** Increase padding BELOW the title (more space before the first related-record line).
- **A12-3** Add MORE space between the record NUMBER field and the record NAME field (W11 B11-1 added some; owner wants more).
- **A12-4** The "matched by/because …" reason line (on-form collapsed card — kept in W11): INCREASE its font size and REMOVE the italics. (Do NOT remove it — restyle it.)

## B. CommunicationAttachments PCF → v1.3.0

- **B12-1** Reduce top space/padding so the title sits closer to the top of the section boundary.
- **B12-2** Increase padding BELOW the title (more space before the first attachment line).
- **B12-3** BUG (W11 A11-2 follow-up): the per-row upload icon is NOT showing green/red — make it **green if uploaded, red if not**. Investigate why the W11 upload-status detection/coloring isn't rendering distinct green/red (screenshot shows all icons the same dark color → detection likely returns undefined for all, or the color token isn't applied).
- **B12-4** REVERSES earlier decision #4: the `.eml` row currently shows an external "open in SharePoint" icon and opens externally (file-type detection routes it to download). It should open the **SAME in-modal preview** as the other attachments. Investigate whether the preview URL renders `.eml`; if it does, route `.eml` through `RichFilePreviewDialog` like the rest (remove the download special-case). If it genuinely can't render, report back.

## C. EmailComposer (New Email modal) — recipient lookup bug

- **C12-1** BUG (W10 task 103 follow-up): the To/Cc/Bcc contact autocomplete does NOT pick up the full email address. Selecting a contact suggestion inserts the NAME (e.g. "ralph") which fails validation ("Invalid email address: ralph") instead of the contact's email. Selecting a suggestion must resolve to the contact's **email address** (emailaddress1) as the recipient.

## D. Reply / Reply All / Forward — related-records inheritance (INVESTIGATE)

- **D12-1** A Reply / Reply All / Forward does NOT automatically set the Related records; the new email should **inherit the regarding/related records from the parent (source) communication**. Owner: "this can be either in how the email is created/sent OR maybe in the field mapping — investigate to see how this is best handled." Investigate the reply/forward seed path (CommunicationActions handlers → draft create → BFF) vs the Field Mapping Framework; recommend + implement the cleanest; document the choice. Likely: copy the source communication's `sprk_regarding*` fields onto the new draft at create time (inheritance, not re-derivation).

---

## Notes / open technical questions to resolve during execution
- B12-4: does `GET /api/documents/{id}/preview-url` return a renderable URL for a `.eml` document? (SPE/Graph may preview `message/rfc822`.) Determines whether B12-4 is a simple special-case removal.
- D12-1: confirm whether reply/forward drafts are created client-side (CommunicationActions) or via a BFF endpoint, and where the source communication's regarding fields are available at seed time.
