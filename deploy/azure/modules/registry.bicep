// Container registry of the images built by the ACR Tasks (ADR 0022 §7). No admin user: pulls use managed identities.
param location string
param tags object
param name string
@allowed(['Basic', 'Standard'])
param sku string = 'Basic'

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: sku
  }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

output name string = registry.name
output loginServer string = registry.properties.loginServer
