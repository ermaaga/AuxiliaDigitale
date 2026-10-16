// Container Apps environment shared by prod and qa (ADR 0022 §1): Consumption only, in the VNet, no Log Analytics.
param location string
param tags object
param name string
param subnetId string

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    vnetConfiguration: {
      infrastructureSubnetId: subnetId
      internal: false
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    zoneRedundant: false
    // No appLogsConfiguration: console logs are only streamed (az containerapp logs show), D-17 files go to storage.
  }
}

output id string = environment.id
output name string = environment.name
output defaultDomain string = environment.properties.defaultDomain
