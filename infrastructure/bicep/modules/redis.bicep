// infrastructure/bicep/modules/redis.bicep
// Azure Redis Cache module — hardened with VNet injection, RDB persistence,
// allkeys-lru eviction, and public access disabled (PPI-043)
//
// FR-09 (spaarke-redis-cache-remediation-r1, task 020): parameter audit complete
// 2026-06-25. SKU shape retained as string+int (NOT migrated to object) because
// 3 in-tree callers (customer.bicep, stacks/model1-shared.bicep, stacks/model2-full.bicep)
// already pass sku+capacity as separate args; the object decomposition is computed
// internally below (family derived via skuFamilies map). See
// projects/spaarke-redis-cache-remediation-r1/notes/redis-bicep-audit.md for the
// full decision record.

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

@description('Redis server major version. Empty string lets Azure pick the default (currently 6.0). Set to "6" to pin major version. FR-09 (R1 task 020).')
param redisVersion string = ''

@description('Subnet resource ID for VNet injection (Premium SKU required). Also aliased as vnetSubnetId in FR-09; this module retains the existing name to preserve compatibility with 3 in-tree callers.')
param subnetId string = ''

@description('Optional static IP within the injected subnet (Premium VNet-injected SKU only). Empty string lets Azure assign one. FR-09 (R1 task 020).')
param staticIP string = ''

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
    redisVersion: empty(redisVersion) ? null : redisVersion
    publicNetworkAccess: subnetId != '' ? 'Disabled' : 'Enabled'
    subnetId: subnetId != '' ? subnetId : null
    staticIP: (subnetId != '' && staticIP != '') ? staticIP : null
    redisConfiguration: redisConfiguration
  }
}

output redisId string = redisCache.id
output redisName string = redisCache.name
output redisHostName string = redisCache.properties.hostName
output redisPort int = redisCache.properties.sslPort
@description('Primary access key (admin-key auth). Sensitive — do not log; deploy script extracts to KV. FR-09 (R1 task 020).')
#disable-next-line outputs-should-not-contain-secrets
output redisPrimaryKey string = redisCache.listKeys().primaryKey
#disable-next-line outputs-should-not-contain-secrets
output redisConnectionString string = '${redisCache.properties.hostName}:${redisCache.properties.sslPort},password=${redisCache.listKeys().primaryKey},ssl=True,abortConnect=False'
