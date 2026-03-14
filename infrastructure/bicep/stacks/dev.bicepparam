using './model2-full.bicep'

// Dev environment parameter overrides
// Lower tiers acceptable for development — cost savings ~60%

param customerId = 'spaarkedev1'
param environment = 'dev'
param location = 'westus2'
param dataverseUrl = 'https://spaarkedev1.crm.dynamics.com'

// Dev: B1 is sufficient for 1-3 developers
param appServiceSku = 'B1'

// Dev: basic search is sufficient for low-volume testing
param aiSearchSku = 'basic'

// Monitoring
param enableMonitoringDashboard = true
param alertNotificationEmail = ''

// AI Foundry (optional)
param enableAiFoundry = false
