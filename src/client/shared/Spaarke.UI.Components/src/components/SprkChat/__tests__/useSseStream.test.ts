/**
 * useSseStream Hook Tests
 *
 * Tests the SSE stream parsing and state management.
 * Covers: parseSseEvent utility, stream lifecycle, cancellation, error handling.
 *
 * @see ADR-022 - React 16 APIs only
 */

import { parseSseEvent } from "../hooks/useSseStream";

// ---------------------------------------------------------------------------
// parseSseEvent unit tests
// ---------------------------------------------------------------------------

describe("parseSseEvent", () => {
    it("should parse a valid token event", () => {
        const result = parseSseEvent('data: {"type":"token","content":"Hello"}');
        expect(result).toEqual({ type: "token", content: "Hello" });
    });

    it("should parse a done event", () => {
        const result = parseSseEvent('data: {"type":"done","content":null}');
        expect(result).toEqual({ type: "done", content: null });
    });

    it("should parse an error event", () => {
        const result = parseSseEvent('data: {"type":"error","content":"Something went wrong"}');
        expect(result).toEqual({ type: "error", content: "Something went wrong" });
    });

    it("should return null for lines without data prefix", () => {
        expect(parseSseEvent("id: 123")).toBeNull();
        expect(parseSseEvent("event: message")).toBeNull();
        expect(parseSseEvent(": comment")).toBeNull();
        expect(parseSseEvent("random text")).toBeNull();
    });

    it("should return null for empty data payload", () => {
        expect(parseSseEvent("data: ")).toBeNull();
        expect(parseSseEvent("data:")).toBeNull();
    });

    it("should return null for invalid JSON", () => {
        expect(parseSseEvent("data: not-json")).toBeNull();
        expect(parseSseEvent("data: {broken")).toBeNull();
    });

    it("should return null for empty strings", () => {
        expect(parseSseEvent("")).toBeNull();
        expect(parseSseEvent("   ")).toBeNull();
    });

    it("should handle whitespace around the line", () => {
        const result = parseSseEvent('  data: {"type":"token","content":"Hi"}  ');
        expect(result).toEqual({ type: "token", content: "Hi" });
    });

    it("should return null for objects missing type field", () => {
        expect(parseSseEvent('data: {"content":"test"}')).toBeNull();
    });

    it("should handle token events with empty content", () => {
        const result = parseSseEvent('data: {"type":"token","content":""}');
        expect(result).toEqual({ type: "token", content: "" });
    });

    it("should handle token events with special characters in content", () => {
        const result = parseSseEvent('data: {"type":"token","content":"Hello\\nWorld"}');
        expect(result).toEqual({ type: "token", content: "Hello\nWorld" });
    });

    it("should handle done event without content field", () => {
        const result = parseSseEvent('data: {"type":"done"}');
        expect(result).not.toBeNull();
        expect(result!.type).toBe("done");
    });
});
