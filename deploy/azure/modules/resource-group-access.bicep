// Contributor on one environment's resource group for its GitHub deploy identity (container apps and jobs only live
// there; role assignments need Owner, which the identity does not get).
param principalId string

var contributorRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')

resource contributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, principalId, contributorRole)
  properties: {
    roleDefinitionId: contributorRole
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
