// AcrPush on the shared registry for one identity: the promotion identity (.github/workflows/promote.yml) copies the
// qa images to the tags main and <main commit SHA> with az acr import, without rebuilding them.
param registryName string
param principalId string

var acrPushRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8311e382-0749-4cb8-b61a-304f252e45ec')

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

resource push 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  name: guid(registry.id, principalId, acrPushRole)
  properties: {
    roleDefinitionId: acrPushRole
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
