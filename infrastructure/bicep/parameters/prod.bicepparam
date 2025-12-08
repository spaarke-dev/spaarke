// infrastructure/bicep/parameters/prod.bicepparam
// Production environment parameters for Model 1 shared infrastructure

using '../stacks/model1-shared.bicep'

param environment = 'prod'
param location = 'eastus'
param appServiceSku = 'P1v3'
param aiSearchSku = 'standard2'
param redisSku = 'Premium'
param tags = {
  environment: 'prod'
  application: 'spaarke'
  deploymentModel: 'model1'
  managedBy: 'bicep'
  costCenter: 'production'
}
