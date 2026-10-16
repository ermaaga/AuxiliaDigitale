// Roles of an environment's identities on the shared registry (deployed into the shared resource group).
param registryName string
@description('Runtime identity of the environment: pulls the images (AcrPull).')
param pullPrincipalId string
@description('Deploy identity of the environment: waits for the images of a commit (Reader).')
param readerPrincipalId string

var acrPullRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
var readerRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'acdd72a7-3385-48ef-bd42-f853fb3cdc7a')

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

resource pull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  name: guid(registry.id, pullPrincipalId, acrPullRole)
  properties: {
    roleDefinitionId: acrPullRole
    principalId: pullPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource read 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  name: guid(registry.id, readerPrincipalId, readerRole)
  properties: {
    roleDefinitionId: readerRole
    principalId: readerPrincipalId
    principalType: 'ServicePrincipal'
  }
}
