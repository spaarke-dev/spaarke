/**
 * composeDraftRouting.test.ts — R7-4/R7-5 (UAT 2026-07-21). Locks the pure detector that routes a
 * typed message to the `compose-draft-document` capability (substantial output → Compose tab)
 * instead of a chat answer. Mirrors the composeReviseRouting detector tests: pure input → boolean.
 */

import { detectDraftDocumentIntent } from "../composeDraftRouting";

describe("detectDraftDocumentIntent", () => {
  const yes = (m: string) => expect(detectDraftDocumentIntent(m).isDraftDocument).toBe(true);
  const no = (m: string) => expect(detectDraftDocumentIntent(m).isDraftDocument).toBe(false);

  it("routes verb + document-noun asks to Compose", () => {
    yes("write a brief on the chevron doctrine");
    yes("draft a memo responding to the termination notice");
    yes("prepare an analysis of the indemnification clause");
    yes("compose a legal opinion on the merger");
  });

  it("routes analyze-this-document asks to Compose", () => {
    yes("analyze this agreement");
    yes("analyse the attached contract");
  });

  it("honors an explicit 'in the open Compose tab' target even for email/letter wording (R7-5)", () => {
    // The screenshot case: an email-worded ask that explicitly names the Compose surface.
    yes("write me an engagement letter email in the open Compose tab");
    yes("draft the response in this document");
    yes("write it into the compose editor");
  });

  it("leaves plain email asks to the correspondence path", () => {
    no("draft an email to the client");
    no("send an email to opposing counsel");
    no("write an email summarizing the NDA findings");
  });

  it("does not fire on questions, chit-chat, or create-record asks", () => {
    no("what is the effective date");
    no("what's your analysis of the market"); // analyze verb but no document object
    no("create a matter from this file"); // create-record → surface launch, not draft
    no("summarize this file");
    no("");
    no("/clear");
  });
});
