/**
 * chipPreference.test.ts — D-043-01 (option c). Locks the learned-usage tracking + the
 * stated-overrides-learned merge that feeds the Suggested-Next-Steps display reorder.
 */

import {
  recordChipUsage,
  getLearnedBindingOrder,
  buildChipPreference,
} from "../chipPreference";

describe("chipPreference (D-043-01)", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it("ranks learned usage by count, then recency", () => {
    recordChipUsage("b-alpha");
    recordChipUsage("b-beta");
    recordChipUsage("b-beta"); // beta used twice → ranks first
    expect(getLearnedBindingOrder()).toEqual(["b-beta", "b-alpha"]);
  });

  it("ignores local:* action chips and empty ids (not grounded Bindings)", () => {
    recordChipUsage("local:send-as-email");
    recordChipUsage("");
    recordChipUsage("b-real");
    expect(getLearnedBindingOrder()).toEqual(["b-real"]);
  });

  it("uses learned usage when no stated order is supplied (the fallback)", () => {
    recordChipUsage("b-x");
    recordChipUsage("b-y");
    recordChipUsage("b-x");
    expect(buildChipPreference(null).preferredBindingOrder).toEqual(["b-x", "b-y"]);
  });

  it("lets a non-empty stated order OVERRIDE learned usage (option c precedence)", () => {
    recordChipUsage("b-x");
    recordChipUsage("b-x"); // learned would rank b-x first
    const pref = buildChipPreference(["b-y", "b-z"]);
    expect(pref.preferredBindingOrder).toEqual(["b-y", "b-z"]); // stated wins
  });

  it("returns an empty order when there is neither stated nor learned signal (server order)", () => {
    expect(buildChipPreference(null).preferredBindingOrder).toEqual([]);
    expect(buildChipPreference([]).preferredBindingOrder).toEqual([]);
  });
});
