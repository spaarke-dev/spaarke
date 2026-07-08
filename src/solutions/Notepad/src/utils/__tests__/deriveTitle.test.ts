/**
 * Unit tests for deriveTitle utility.
 * Covers precedence, whitespace handling, multiline bodies, truncation, and maxLen behavior.
 */
import { deriveTitle } from "../deriveTitle";

describe("deriveTitle", () => {
  it("returns 'Untitled' for empty memo (no name, no body)", () => {
    expect(deriveTitle({})).toBe("Untitled");
    expect(deriveTitle({ sprk_name: "", sprk_memobody: "" })).toBe("Untitled");
    expect(deriveTitle({ sprk_name: null, sprk_memobody: null })).toBe("Untitled");
  });

  it("returns 'Untitled' for whitespace-only memo", () => {
    expect(deriveTitle({ sprk_name: "   ", sprk_memobody: "   \n\n  \t  " })).toBe("Untitled");
  });

  it("uses sprk_name when set and body is empty", () => {
    expect(deriveTitle({ sprk_name: "My Memo", sprk_memobody: "" })).toBe("My Memo");
  });

  it("uses sprk_name when set even if body has content (name wins)", () => {
    expect(
      deriveTitle({ sprk_name: "My Memo", sprk_memobody: "First line\nSecond line" })
    ).toBe("My Memo");
  });

  it("falls through to body when sprk_name is the default 'Untitled'", () => {
    expect(deriveTitle({ sprk_name: "Untitled", sprk_memobody: "First line" })).toBe("First line");
  });

  it("falls through to body when sprk_name is 'untitled' (case-insensitive)", () => {
    expect(deriveTitle({ sprk_name: "UNTITLED", sprk_memobody: "First line" })).toBe("First line");
  });

  it("uses first non-empty line of body when sprk_name is null", () => {
    expect(
      deriveTitle({ sprk_name: null, sprk_memobody: "First line\nSecond line" })
    ).toBe("First line");
  });

  it("skips leading blank lines and returns first non-empty line", () => {
    expect(
      deriveTitle({ sprk_name: null, sprk_memobody: "  \n\nHello\nWorld" })
    ).toBe("Hello");
  });

  it("falls through when sprk_name is whitespace-only", () => {
    expect(
      deriveTitle({ sprk_name: "   ", sprk_memobody: "Body content" })
    ).toBe("Body content");
  });

  it("truncates a long sprk_name to maxLen with ellipsis", () => {
    const longName = "A".repeat(100);
    const result = deriveTitle({ sprk_name: longName }, 60);
    expect(result).toHaveLength(60);
    expect(result.endsWith("…")).toBe(true);
    expect(result.slice(0, 59)).toBe("A".repeat(59));
  });

  it("truncates a long body first-line to maxLen with ellipsis", () => {
    const longLine = "B".repeat(100);
    const result = deriveTitle({ sprk_memobody: longLine }, 60);
    expect(result).toHaveLength(60);
    expect(result.endsWith("…")).toBe(true);
    expect(result.slice(0, 59)).toBe("B".repeat(59));
  });

  it("respects a custom maxLen of 20", () => {
    const line = "This is a fairly long first line that must be trimmed";
    const result = deriveTitle({ sprk_memobody: line }, 20);
    expect(result).toHaveLength(20);
    expect(result.endsWith("…")).toBe(true);
    expect(result).toBe("This is a fairly lo…");
  });

  it("does not truncate when content is exactly maxLen", () => {
    const exact = "A".repeat(60);
    expect(deriveTitle({ sprk_name: exact }, 60)).toBe(exact);
  });

  it("does not truncate when content is under default maxLen (60)", () => {
    expect(deriveTitle({ sprk_name: "Short title" })).toBe("Short title");
  });

  it("trims whitespace from body lines before returning", () => {
    expect(
      deriveTitle({ sprk_memobody: "   Hello world   \nSecond" })
    ).toBe("Hello world");
  });

  it("handles CRLF line endings", () => {
    expect(
      deriveTitle({ sprk_memobody: "First\r\nSecond\r\nThird" })
    ).toBe("First");
  });
});
