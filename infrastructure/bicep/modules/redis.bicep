// infrastructure/bicep/modules/redis.bicep
// Azure Redis Cache module

@description('Name of the Redis Cache')
param redisName string

@description('Location for the Redis Cache')
param location string = resourceGroup().location

@description('SKU for Redis Cache')
@allowed(['Basic', 'Standard', 'Premium'])
param sku string = 'Basic'

@description('SKU capacity (family size)')
param capacity int = 0

@description('Enable non-SSL port (not recommended)')
param enableNonSslPort bool = false

@description('Minimum TLS version')
param minimumTlsVersion string = '1.2'

@description('Tags for the resource')
param tags object = {}

var skuFamilies = {
  Basic: 'C'
  Standard: 'C'
  Premium: 'P'
}

resource redisCache 'Microsoft.Cache/redis@2023-08-01' = {
  name: redisName
  location: location
  tags: tags
  properties: {
    sku: {
      name: sku
      family: skuFamilies[sku]
      capacity: capacity
    }
    enableNonSslPort: enableNonSslPort
    minimumTlsVersion: minimumTlsVersion
    redisConfiguration: {
      'maxmemory-policy': 'volatile-lru'
    }
  }
}

output redisId string = redisCache.id
output redisName string = redisCache.name
output redisHostName string = redisCache.properties.hostName
output redisPort int = redisCache.properties.sslPort
#disable-next-line outputs-should-not-contain-secrets
output redisConnectionString string = '${redisCache.properties.hostName}:${redisCache.properties.sslPort},password=${redisCache.listKeys().primaryKey},ssl=True,abortConnect=False'
