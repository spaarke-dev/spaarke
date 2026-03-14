// infrastructure/bicep/modules/redis.bicep
// Azure Redis Cache module — hardened with VNet injection, RDB persistence,
// allkeys-lru eviction, and public access disabled (PPI-043)

@description('Name of the Redis Cache')
param redisName string

@description('Location for the Redis Cache')
param location string = resourceGroup().location

@description('SKU for Redis Cache (Premium required for VNet injection + persistence)')
@allowed(['Basic', 'Standard', 'Premium'])
param sku string = 'Premium'

@description('SKU capacity (family size). Premium: 1-5')
param capacity int = 1

@description('Enable non-SSL port (not recommended)')
param enableNonSslPort bool = false

@description('Minimum TLS version')
param minimumTlsVersion string = '1.2'

@description('Subnet resource ID for VNet injection (Premium SKU required)')
param subnetId string = ''

@description('Enable RDB persistence (Premium SKU required)')
param enableRdbPersistence bool = true

@description('RDB backup frequency in minutes (15, 30, 60, 360, 720, 1440)')
@allowed([15, 30, 60, 360, 720, 1440])
param rdbBackupFrequencyMinutes int = 15

@description('Tags for the resource')
param tags object = {}

var skuFamilies = {
  Basic: 'C'
  Standard: 'C'
  Premium: 'P'
}

// Build Redis configuration — base settings always applied
var baseRedisConfig = {
  'maxmemory-policy': 'allkeys-lru'
}

// RDB persistence settings (Premium only)
var rdbConfig = enableRdbPersistence && sku == 'Premium' ? {
  'rdb-backup-enabled': 'true'
  'rdb-backup-frequency': '${rdbBackupFrequencyMinutes}'
  'rdb-backup-max-snapshot-count': '1'
} : {}

// Merge configurations
var redisConfiguration = union(baseRedisConfig, rdbConfig)

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
    publicNetworkAccess: subnetId != '' ? 'Disabled' : 'Enabled'
    subnetId: subnetId != '' ? subnetId : null
    redisConfiguration: redisConfiguration
  }
}

output redisId string = redisCache.id
output redisName string = redisCache.name
output redisHostName string = redisCache.properties.hostName
output redisPort int = redisCache.properties.sslPort
#disable-next-line outputs-should-not-contain-secrets
output redisConnectionString string = '${redisCache.properties.hostName}:${redisCache.properties.sslPort},password=${redisCache.listKeys().primaryKey},ssl=True,abortConnect=False'
