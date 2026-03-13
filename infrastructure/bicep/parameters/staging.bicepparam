// infrastructure/bicep/parameters/staging.bicepparam
// Staging environment parameters for Model 1 shared infrastructure
// Sized between dev and prod — validates production configs at lower cost

using '../stacks/model1-shared.bicep'

param environment = 'staging'
param location = 'eastus'
param appServiceSku = 'S1'
param aiSearchSku = 'standard'
param redisSku = 'Standard'
param tags = {
  environment: 'staging'
  application: 'spaarke'
  deploymentModel: 'model1'
  managedBy: 'bicep'
  costCenter: 'staging'
}
