// Identity of the Infrastructure workflow (.github/workflows/infrastructure.yml, ADR 0024): the only part of the
// infrastructure that is not deployed by that workflow, because it is the identity the workflow signs in with.
// Deployed once by an Owner of the subscription (runbook docs/runbooks/azure-deployment.md, "First setup"):
//   az deployment sub create --name auxilia-next-automation --location italynorth --parameters deploy/azure/automation.bicepparam
// Running it again is harmless (same names, same role assignments).
targetScope = 'subscription'

param location string = 'italynorth'
param resourceGroupName string = 'rg-auxilia-next-automation'
param identityName string = 'id-auxilia-next-infra'
param tags object = {
  application: 'auxilia-next'
  environment: 'automation'
}
@description('GitHub repository allowed to run the Infrastructure workflow through OIDC.')
param githubRepository string = 'ermaaga/AuxiliaDigitale'

// Roles the templates assign (environment.bicep, shared.bicep and their modules); the workflow may assign or remove
// only these, so it can never make anybody Owner or User Access Administrator.
var contributorRole = 'b24988ac-6180-42a0-ab88-20f7382dd24c'
var rbacAdministratorRole = 'f58310d9-a9f6-439a-9e8d-f62e7b41a168'
var assignableRoles = [
  contributorRole
  '7f951dda-4ed3-4680-a7ca-43fe172d538d' // AcrPull
  'acdd72a7-3385-48ef-bd42-f853fb3cdc7a' // Reader
  '4633458b-17de-408a-b874-0445c86b69e6' // Key Vault Secrets User
  'b86a8fe4-44ce-4948-aee5-eccb2c155cd7' // Key Vault Secrets Officer
  'e147488a-f6f5-4113-8e2d-b22465e65bf6' // Key Vault Crypto Service Encryption User
]
var roleList = join(assignableRoles, ', ')
var assignCondition = '((!(ActionMatches{\'Microsoft.Authorization/roleAssignments/write\'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {${roleList}})) AND ((!(ActionMatches{\'Microsoft.Authorization/roleAssignments/delete\'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {${roleList}}))'

resource rg 'Microsoft.Resources/resourceGroups@2024-07-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module identity 'modules/identity.bicep' = {
  scope: rg
  name: 'identity-infra'
  params: {
    location: location
    tags: tags
    name: identityName
    githubRepository: githubRepository
  }
}

// Creates the resource groups and every resource of shared.bicep and environment.bicep.
resource contributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, identityName, contributorRole)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', contributorRole)
    principalId: identity.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// Role assignments of the templates, limited to the roles above (Role Based Access Control Administrator + condition).
resource rbacAdministrator 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, identityName, rbacAdministratorRole)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', rbacAdministratorRole)
    principalId: identity.outputs.principalId
    principalType: 'ServicePrincipal'
    condition: assignCondition
    conditionVersion: '2.0'
  }
}

@description('Repository variable AZURE_CLIENT_ID_INFRA.')
output clientId string = identity.outputs.clientId
output principalId string = identity.outputs.principalId
