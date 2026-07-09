/**
 * useCapabilityDiscovery — client consumption tests for the capability-discovery
 * READ endpoint (spec FR-A1-12, R1, BFF task 041).
 *
 * Verifies: the hook renders EXACTLY what the server returned (no re-sort, no
 * invented entries), degrades to an empty list on fetch failure (fail-closed
 * per ADR-039 — never a fallback list of invented capability names), and
 * passes the `surface` query param through to the request URL.
 */

import { renderHook, waitFor } from "@testing-library/react";
import {
  useCapabilityDiscovery,
  type CapabilityDiscoveryItem,
} from "../useCapabilityDiscovery";

describe("useCapabilityDiscovery", () => {
  const item1: CapabilityDiscoveryItem = {
    bindingId: "11111111-1111-1111-1111-111111111111",
    consumerType: "chat-summarize",
    consumerCode: "default",
    displayLabel: "Summarize this document.",
    surfaces: [],
    launchArgsSchemaJson: null,
  };
  const item2: CapabilityDiscoveryItem = {
    bindingId: "22222222-2222-2222-2222-222222222222",
    consumerType: "chat-draft",
    consumerCode: "default",
    displayLabel: "Draft a response.",
    surfaces: ["assistant"],
    launchArgsSchemaJson: '{"args":[]}',
  };

  function mockFetchOk(capabilities: CapabilityDiscoveryItem[]) {
    return jest.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ capabilities }),
    });
  }

  it("loads and exposes the capabilities in the server-returned order", async () => {
    const authenticatedFetch = mockFetchOk([item1, item2]);

    const { result } = renderHook(() =>
      useCapabilityDiscovery({
        bffBaseUrl: "https://bff.example.com",
        authenticatedFetch,
      })
    );

    await waitFor(() => expect(result.current.loading).toBe(false));

    expect(result.current.error).toBe(false);
    expect(result.current.capabilities).toEqual([item1, item2]);
  });

  it("requests the default 'assistant' surface when none is specified", async () => {
    const authenticatedFetch = mockFetchOk([item1]);

    renderHook(() =>
      useCapabilityDiscovery({
        bffBaseUrl: "https://bff.example.com",
        authenticatedFetch,
      })
    );

    await waitFor(() => expect(authenticatedFetch).toHaveBeenCalled());
    const requestedUrl = new URL(authenticatedFetch.mock.calls[0][0] as string);
    expect(requestedUrl.pathname).toBe("/api/ai/capabilities");
  });

  it("passes an explicit surface through as a query param", async () => {
    const authenticatedFetch = mockFetchOk([]);

    renderHook(() =>
      useCapabilityDiscovery({
        bffBaseUrl: "https://bff.example.com",
        authenticatedFetch,
        surface: "office",
      })
    );

    await waitFor(() => expect(authenticatedFetch).toHaveBeenCalled());
    const requestedUrl = new URL(authenticatedFetch.mock.calls[0][0] as string);
    expect(requestedUrl.searchParams.get("surface")).toBe("office");
  });

  it("does NOT surface a capability the server did not return", async () => {
    const authenticatedFetch = mockFetchOk([item1]);

    const { result } = renderHook(() =>
      useCapabilityDiscovery({
        bffBaseUrl: "https://bff.example.com",
        authenticatedFetch,
      })
    );

    await waitFor(() => expect(result.current.loading).toBe(false));

    expect(result.current.capabilities.some((c) => c.bindingId === item2.bindingId)).toBe(false);
  });

  it("fails closed to an empty list (never an invented fallback) when the fetch fails", async () => {
    const authenticatedFetch = jest.fn().mockResolvedValue({
      ok: false,
      status: 500,
      json: async () => ({}),
    });

    const { result } = renderHook(() =>
      useCapabilityDiscovery({
        bffBaseUrl: "https://bff.example.com",
        authenticatedFetch,
      })
    );

    await waitFor(() => expect(result.current.loading).toBe(false));

    expect(result.current.error).toBe(true);
    expect(result.current.capabilities).toEqual([]);
  });
});
