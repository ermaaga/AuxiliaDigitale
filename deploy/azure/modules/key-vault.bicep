// Key Vault with RBAC authorization and purge protection (ADR 0022 §5). Values are written by scripts/secrets.sh,
// never by a template: the container apps only hold Key Vault references.
param location string
param tags object
param name string
@description('Identities that read secrets (Key Vault Secrets User).')
param readerPrincipalIds array = []
@description('Users who write secrets with scripts/secrets.sh (Key Vault Secrets Officer).')
param operatorObjectIds array = []
@description('Service principals that write secrets: the identity of the Infrastructure workflow (Key Vault Secrets Officer).')
param automationPrincipalIds array = []
@description('Identities that wrap and unwrap the Data Protection key ring with the key below (Key Vault Crypto Service Encryption User).')
param cryptoUserPrincipalIds array = []

var secretsUserRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
var secretsOfficerRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
var cryptoServiceEncryptionUserRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'e147488a-f6f5-4113-8e2d-b22465e65bf6')

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

resource automation 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in automationPrincipalIds: {
  scope: vault
  name: guid(vault.id, principalId, secretsOfficerRole)
  properties: {
    roleDefinitionId: secretsOfficerRole
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]

// Key encryption key of the Data Protection key ring (#141, R4): generated inside the vault and never exported; Api,
// Worker and auxctl only ask the vault to wrap/unwrap with it. One per environment (runbook C6). Creating a key that
// already exists is a no-op, so a new deployment never adds versions.
resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2024-11-01' = {
  parent: vault
  name: 'dataprotection-kek'
  tags: tags
  properties: {
    kty: 'RSA'
    keySize: 3072
    keyOps: [
      'wrapKey'
      'unwrapKey'
    ]
  }
}

resource cryptoUsers 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in cryptoUserPrincipalIds: {
  scope: dataProtectionKey
  name: guid(dataProtectionKey.id, principalId, cryptoServiceEncryptionUserRole)
  properties: {
    roleDefinitionId: cryptoServiceEncryptionUserRole
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]

output name string = vault.name
output uri string = vault.properties.vaultUri
@description('Versionless identifier of the Data Protection key (DataProtection__KeyVaultKeyId).')
output dataProtectionKeyUri string = dataProtectionKey.properties.keyUri
