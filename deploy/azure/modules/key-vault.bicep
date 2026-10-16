// Key Vault with RBAC authorization and purge protection (ADR 0022 §5). Values are written by scripts/secrets.sh,
// never by a template: the container apps only hold Key Vault references.
param location string
param tags object
param name string
@description('Identities that read secrets (Key Vault Secrets User).')
param readerPrincipalIds array = []
@description('Users who write secrets with scripts/secrets.sh (Key Vault Secrets Officer).')
param operatorObjectIds array = []

var secretsUserRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
var secretsOfficerRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')

resource vault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    // Container Apps read Key Vault references over the public endpoint (they are not a trusted service).
    publicNetworkAccess: 'Enabled'
  }
}

resource readers 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in readerPrincipalIds: {
  scope: vault
  name: guid(vault.id, principalId, secretsUserRole)
  properties: {
    roleDefinitionId: secretsUserRole
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]

resource operators 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for objectId in operatorObjectIds: {
  scope: vault
  name: guid(vault.id, objectId, secretsOfficerRole)
  properties: {
    roleDefinitionId: secretsOfficerRole
    principalId: objectId
    principalType: 'User'
  }
}]

output name string = vault.name
output uri string = vault.properties.vaultUri
