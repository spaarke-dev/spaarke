// infrastructure/bicep/parameters/dev.bicepparam
// Development environment parameters for Model 1 shared infrastructure

using '../stacks/model1-shared.bicep'

param environment = 'dev'
param location = 'eastus'
param appServiceSku = 'S1'
param aiSearchSku = 'standard'
param redisSku = 'Standard'
param tags = {
  environment: 'dev'
  application: 'spaarke'
  deploymentModel: 'model1'
  managedBy: 'bicep'
  costCenter: 'development'
}
