import {
  publishToken,
  readBridgeToken,
  clearBridgeToken,
} from "../src/tokenBridge";

describe("tokenBridge", () => {
  afterEach(() => {
    clearBridgeToken();
  });

  it("publishToken sets token on window", () => {
    publishToken("test-token-123");
    expect((window as any).__SPAARKE_BFF_TOKEN__).toBe("test-token-123");
  });

  it("readBridgeToken returns published token", () => {
    publishToken("bridge-token");
    expect(readBridgeToken()).toBe("bridge-token");
  });

  it("readBridgeToken returns null when no token published", () => {
    expect(readBridgeToken()).toBeNull();
  });

  it("clearBridgeToken removes the token", () => {
    publishToken("to-be-cleared");
    clearBridgeToken();
    expect(readBridgeToken()).toBeNull();
  });
});
