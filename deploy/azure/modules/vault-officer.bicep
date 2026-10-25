// Key Vault Secrets Officer of the Infrastructure workflow on an existing vault (ADR 0024). Deployed by scripts/lib.sh
// when the workflow cannot read a vault that a shell run created before it: the scripts read secrets before the
// templates run, and the templates are what grant the workflow its role. Same assignment name as key-vault.bicep, so
// the next template run finds this assignment instead of failing.
param vaultName string
param principalId string

var secretsOfficerRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')

resource vault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: vaultName
}

resource officer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, principalId, secretsOfficerRole)
  properties: {
    roleDefinitionId: secretsOfficerRole
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
