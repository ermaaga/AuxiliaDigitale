// Virtual network of the platform (ADR 0022): one subnet for the Container Apps environment, one for PostgreSQL.
param location string
param tags object
param vnetName string
param addressPrefix string = '10.40.0.0/16'
// Workload-profiles environments need at least a /27; a /23 leaves room for many replicas.
param containerAppsSubnetPrefix string = '10.40.0.0/23'
param postgresSubnetPrefix string = '10.40.2.0/28'

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [addressPrefix]
    }
    subnets: [
      {
        name: 'snet-cae'
        properties: {
          addressPrefix: containerAppsSubnetPrefix
          delegations: [
            {
              name: 'containerapps'
              properties: { serviceName: 'Microsoft.App/environments' }
            }
          ]
        }
      }
      {
        name: 'snet-psql'
        properties: {
          addressPrefix: postgresSubnetPrefix
          delegations: [
            {
              name: 'postgres'
              properties: { serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers' }
            }
          ]
        }
      }
    ]
  }
}

output vnetId string = vnet.id
output containerAppsSubnetId string = resourceId('Microsoft.Network/virtualNetworks/subnets', vnetName, 'snet-cae')
output containerAppsSubnetPrefix string = containerAppsSubnetPrefix
output postgresSubnetId string = resourceId('Microsoft.Network/virtualNetworks/subnets', vnetName, 'snet-psql')
