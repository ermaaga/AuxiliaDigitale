// PostgreSQL Flexible Server shared by prod and qa (ADR 0022 §3): free-tier shape (B1ms, 32 GB, no auto-grow),
// VNet integration only, password + Microsoft Entra authentication.
param location string
param tags object
param serverName string
param vnetId string
param subnetId string
param administratorLogin string
@secure()
param administratorPassword string
param version string = '18'
param skuName string = 'Standard_B1ms'
param skuTier string = 'Burstable'
@description('32 GB is the storage included in the free tier; above it the storage is billed.')
param storageSizeGB int = 32
param backupRetentionDays int = 7

// Private zone resolved by every resource of the VNet; the server name keeps resolving through it.
resource dnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: '${serverName}.private.postgres.database.azure.com'
  location: 'global'
  tags: tags
}

resource dnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: dnsZone
  name: 'vnet-link'
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: { id: vnetId }
  }
}

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: serverName
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuTier
  }
  properties: {
    version: version
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorPassword
    authConfig: {
      activeDirectoryAuth: 'Enabled'
      passwordAuth: 'Enabled'
      tenantId: tenant().tenantId
    }
    storage: {
      storageSizeGB: storageSizeGB
      autoGrow: 'Disabled'
    }
    backup: {
      backupRetentionDays: backupRetentionDays
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    network: {
      delegatedSubnetResourceId: subnetId
      privateDnsZoneArmResourceId: dnsZone.id
      publicNetworkAccess: 'Disabled'
    }
  }
  dependsOn: [dnsLink]
}

// The tenant model uses citext (trusted extension: the tenant database owner may create it once it is allow-listed).
resource extensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: server
  name: 'azure.extensions'
  properties: {
    value: 'CITEXT'
    source: 'user-override'
  }
}

output serverName string = server.name
output fqdn string = server.properties.fullyQualifiedDomainName
