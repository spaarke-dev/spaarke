/*
 * Teams workforce SSO acquisition (throwaway spike).
 *
 * Primary path: microsoftTeams.authentication.getAuthToken() — the standard Teams SSO
 * flow. Returns a workforce token for the app's exposed API (webApplicationInfo in the
 * manifest). Broker-only (NFR-02): we send it to the BFF only; no OBO, no downstream exchange.
 *
 * Production alternative (task 011): Nested App Auth (NAA) via @azure/msal-browser
 *   createNestablePublicClientApplication({ auth: { clientId, authority: workforce-multitenant } })
 *   then acquireTokenSilent({ scopes:[bffScope] }). Not required to close this spike.
 */

/** Initialize Teams JS and return the Teams context (theme, host, etc.). */
async function initTeams() {
  await microsoftTeams.app.initialize();
  return microsoftTeams.app.getContext();
}

/** Acquire a workforce SSO token via Teams. Throws on failure (surface as NO-GO signal). */
async function acquireWorkforceToken() {
  // getAuthToken uses the manifest's webApplicationInfo (resource = App ID URI).
  // A desktop-client popup/Conditional-Access block here is the documented go/no-go risk.
  const token = await microsoftTeams.authentication.getAuthToken({
    // silent by default; Teams shows a consent prompt on first run if needed.
  });
  return token;
}

/** Decode a JWT payload (display-only — NOT validation; the BFF validates). */
function decodeJwtClaims(jwt) {
  try {
    const payload = jwt.split(".")[1].replace(/-/g, "+").replace(/_/g, "/");
    const json = decodeURIComponent(
      atob(payload)
        .split("")
        .map((c) => "%" + ("00" + c.charCodeAt(0).toString(16)).slice(-2))
        .join("")
    );
    const claims = JSON.parse(json);
    return { oid: claims.oid, tid: claims.tid, aud: claims.aud, upn: claims.upn || claims.preferred_username };
  } catch {
    return { error: "could not decode token payload" };
  }
}

/** Call the BFF membership endpoint with the workforce token. Returns { status, body }. */
async function callMembership(token, cfg) {
  const qs = new URLSearchParams();
  if (cfg.roles) qs.set("roles", cfg.roles);
  if (cfg.identityTypes) qs.set("identityTypes", cfg.identityTypes);
  if (cfg.limit) qs.set("limit", String(cfg.limit));
  const suffix = qs.toString() ? `?${qs}` : "";
  const url = `${cfg.bffBaseUrl.replace(/\/$/, "")}/api/users/me/memberships/${encodeURIComponent(cfg.entityType)}${suffix}`;

  const res = await fetch(url, {
    method: "GET",
    headers: { Authorization: `Bearer ${token}`, Accept: "application/json" },
  });
  let body;
  try {
    body = await res.json();
  } catch {
    body = await res.text();
  }
  return { status: res.status, body };
}
