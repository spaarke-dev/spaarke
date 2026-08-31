using './model2-full.bicep'

// Dev environment parameter overrides
// Lower tiers acceptable for development — cost savings ~60%

param customerId = 'spaarkedev1'
param environment = 'dev'
param location = 'westus2'
param dataverseUrl = 'https://spaarkedev1.crm.dynamics.com'

// Dev: P1v3. Aligned with the live dev tier 2026-08-20 by spaarke-auth-v4-dataverse-MI task 001,
// concurred by r1 DS-5 IaC-alignment sweep (task 109).
//
// IMPORTANT CONTEXT -- this stack does NOT currently describe the running dev
// environment. With customerId='spaarkedev1' + environment='dev' it would create
// 'sprkspaarkedev1dev-api' / 'sprkspaarkedev1dev-plan'. The BFF actually serving dev
// is 'spaarke-bff-dev' on plan 'spaarke-dev-plan' in rg-spaarke-dev, which was created
// out-of-band and is not managed by this IaC. So this value is aspirational, not a
// reconciliation of drift. See ISS-001.
//
// P1v3 (was 'B1') because the tier is LOAD-BEARING, not just a cost choice: B1 does not
// support deployment slots, and the staged slot rollout is mandatory here because OBO
// fails CLOSED. If this stack is ever used to stand up a dev environment, a slot-less
// tier would silently make that rollout impossible.
param appServiceSku = 'P1v3'

// Dev: basic search is sufficient for low-volume testing
param aiSearchSku = 'basic'

// Monitoring
param enableMonitoringDashboard = true
param alertNotificationEmail = ''

// AI Foundry (optional)
param enableAiFoundry = false
