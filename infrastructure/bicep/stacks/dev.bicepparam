using './model2-full.bicep'

// Dev environment parameter overrides
// Lower tiers acceptable for development — cost savings ~60%

param customerId = 'spaarkedev1'
param environment = 'dev'
param location = 'westus2'
param dataverseUrl = 'https://spaarkedev1.crm.dynamics.com'

// Dev: P1v3 (matches live dev App Service SKU; slot availability requires
// Standard-tier-or-above -- B1 does not support deployment slots).
// Per auth-v4 §8 item 4 / DS-5 IaC-alignment sweep (task 109).
param appServiceSku = 'P1v3'

// Dev: basic search is sufficient for low-volume testing
param aiSearchSku = 'basic'

// Monitoring
param enableMonitoringDashboard = true
param alertNotificationEmail = ''

// AI Foundry (optional)
param enableAiFoundry = false
