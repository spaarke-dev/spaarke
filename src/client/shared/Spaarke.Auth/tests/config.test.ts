import { resolveConfig } from "../src/config";

describe("resolveConfig", () => {
  it("returns defaults when no config provided", () => {
    const config = resolveConfig();
    expect(config.clientId).toBe("170c98e1-d486-4355-bcbe-170454e0207c");
    expect(config.authority).toBe(
      "https://login.microsoftonline.com/organizations",
    );
    expect(config.bffApiScope).toBe(
      "api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation",
    );
    expect(config.proactiveRefresh).toBe(false);
    expect(config.requireXrm).toBe(false);
  });

  it("overrides with user config", () => {
    const config = resolveConfig({
      clientId: "custom-id",
      proactiveRefresh: true,
      requireXrm: true,
    });
    expect(config.clientId).toBe("custom-id");
    expect(config.proactiveRefresh).toBe(true);
    expect(config.requireXrm).toBe(true);
    // Non-overridden values stay default
    expect(config.authority).toBe(
      "https://login.microsoftonline.com/organizations",
    );
  });

  it("reads clientId from window global", () => {
    (window as any).__SPAARKE_MSAL_CLIENT_ID__ = "from-window";
    const config = resolveConfig();
    expect(config.clientId).toBe("from-window");
    delete (window as any).__SPAARKE_MSAL_CLIENT_ID__;
  });

  it("prefers user config over window global", () => {
    (window as any).__SPAARKE_MSAL_CLIENT_ID__ = "from-window";
    const config = resolveConfig({ clientId: "from-user" });
    expect(config.clientId).toBe("from-user");
    delete (window as any).__SPAARKE_MSAL_CLIENT_ID__;
  });
});
