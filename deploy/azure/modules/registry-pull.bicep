// AcrPull on the shared registry for one identity (the shared identity pulls the images of the satellite apps).
param registryName string
param principalId string

var acrPullRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

resource pull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  name: guid(registry.id, principalId, acrPullRole)
  properties: {
    roleDefinitionId: acrPullRole
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
