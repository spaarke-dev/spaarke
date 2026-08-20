using './model2-full.bicep'

// Dev environment parameter overrides
// Lower tiers acceptable for development — cost savings ~60%

param customerId = 'spaarkedev1'
param environment = 'dev'
param location = 'westus2'
param dataverseUrl = 'https://spaarkedev1.crm.dynamics.com'

// Dev: P1v3. Reconciled with live 2026-08-20 by spaarke-auth-v4-dataverse-MI task 001.
// This file declared 'B1', but the live spaarke-dev-plan has been P1v3 since the
// 2026-05-24 migration -- the parameter file had drifted, not the environment.
// P1v3 is also LOAD-BEARING, not just a cost decision: B1 does not support
// deployment slots, and the staged slot rollout is mandatory here because OBO
// fails CLOSED (see projects/spaarke-auth-v4-dataverse-MI/). Do not lower this
// to a slot-less tier without retiring the slot-based rollout first.
param appServiceSku = 'P1v3'

// Dev: basic search is sufficient for low-volume testing
param aiSearchSku = 'basic'

// Monitoring
param enableMonitoringDashboard = true
param alertNotificationEmail = ''

// AI Foundry (optional)
param enableAiFoundry = false
