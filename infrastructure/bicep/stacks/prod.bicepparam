using './model2-full.bicep'

// Production environment parameter overrides
// Uses template defaults (S1, Standard Redis C1, standard AI Search with 2 replicas)
// Sized for 15-50 concurrent users, ~200 analyses/day

param customerId = 'spraakeprod'
param environment = 'prod'
param location = 'eastus'
param dataverseUrl = 'https://spraakeprod.crm.dynamics.com'

// Production: use template defaults (S1 App Service, standard AI Search)
// Explicitly listed here for clarity — these match the template defaults
param appServiceSku = 'S1'
param aiSearchSku = 'standard'

// Monitoring
param enableMonitoringDashboard = true
param alertNotificationEmail = ''

// AI Foundry (optional — enable when Prompt Flow orchestration is needed)
param enableAiFoundry = false
