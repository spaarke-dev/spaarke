// Copy to config.js and fill in. config.js is spike-local (do not commit real IDs).
// Broker-only (NFR-02): the acquired token authenticates to the BFF ONLY — never exchanged downstream.
window.SPIKE_CONFIG = {
  // Multitenant workforce Entra app that already carries access_as_user + Teams redirect URIs.
  appClientId: "1e40baad-XXXX-XXXX-XXXX-XXXXXXXXXXXX", // TODO: real client id

  // https base URL of a running BFF reachable from the Teams client.
  bffBaseUrl: "https://spaarke-bff-dev.example.net", // TODO

  // A membership-bearing entity type to query via GET /api/users/me/memberships/{entityType}.
  entityType: "sprk_matter", // TODO if different

  // BFF audience/scope the workforce token must target (api://<bff-app-id>/<scope>).
  // If the BFF accepts the app's own App ID URI audience directly, set that here.
  bffScope: "api://XXXX/access_as_user", // TODO

  // Optional: query-string filters passed through to the endpoint (see MembershipEndpoints.cs).
  // roles: "owner,assignedAttorney",
  // identityTypes: "SystemUser,Contact",
  // limit: 50,
};
