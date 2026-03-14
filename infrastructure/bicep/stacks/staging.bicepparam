using './model2-full.bicep'

// Staging environment parameter overrides
// Mirrors production tiers to validate deployment before promotion
// Sized for pre-release testing — same SKUs as prod, lower capacity

param customerId = 'spraakestg'
param environment = 'staging'
param location = 'eastus'
param dataverseUrl = 'https://spraakestaging.crm.dynamics.com'

// Staging: match production SKU tier to catch tier-specific issues
param appServiceSku = 'S1'
param aiSearchSku = 'standard'

// Monitoring
param enableMonitoringDashboard = true
param alertNotificationEmail = ''

// AI Foundry (optional — enable when testing Prompt Flow orchestration)
param enableAiFoundry = false
