// One environment of Auxilia Next (prod, qa, demo, …) on the shared resources (ADR 0022, ADR 0024): identities,
// Key Vault, storage and, once the secrets and the images exist, the application containers. Parameters of each
// environment in environments/<name>.bicepparam.
// Deployed by the Infrastructure workflow (scripts/provision.sh <name>) or scripts/deploy.sh <name> [--image <sha>] [--what-if].
targetScope = 'subscription'

@description('Lowercase letters and digits, starting with a letter (checked by scripts/deploy.sh); the longest name that fits kv-auxilia-next-<name> is 9 characters.')
@minLength(2)
@maxLength(9)
param environmentName string
param location string = 'italynorth'
param resourceGroupName string = 'rg-auxilia-next-${environmentName}'
param tags object = {
  application: 'auxilia-next'
  environment: environmentName
}

param sharedResourceGroupName string = 'rg-auxilia-next-shared'
param containerAppsEnvironmentName string = 'cae-auxilia-next-01'
param registryName string = 'acrauxilianext01'
@description('Networks whose X-Forwarded-For the API trusts: the subnet of the Container Apps environment.')
param forwardedNetworks array = ['10.40.0.0/23']

@description('GitHub repository allowed to deploy this environment through OIDC.')
param githubRepository string = 'ermaaga/AuxiliaDigitale'
@description('GitHub environment of the release job (production requires a reviewer).')
param githubEnvironment string

@description('Days after which the daily log files are deleted (D-17).')
param logRetentionDays int
@description('Users who write secrets with scripts/secrets.sh (Key Vault Secrets Officer).')
param operatorObjectIds array = []
@description('Identity of the Infrastructure workflow (Key Vault Secrets Officer).')
param automationPrincipalIds array = []

@description('Valkey database of this environment (one per environment, ADR 0022 §2); read by scripts/secrets.sh.')
@minValue(0)
@maxValue(15)
param valkeyDatabase int

@description('Commit SHA of the images; empty = deploy only identities, Key Vault and storage.')
param imageTag string = ''
param apiMaxReplicas int = 2
param workerMaxReplicas int = 2

resource rg 'Microsoft.Resources/resourceGroups@2024-07-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

resource sharedRg 'Microsoft.Resources/resourceGroups@2024-07-01' existing = {
  name: sharedResourceGroupName
}

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  scope: sharedRg
  name: containerAppsEnvironmentName
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  scope: sharedRg
  name: registryName
}

module runtimeIdentity 'modules/identity.bicep' = {
  scope: rg
  name: 'identity-runtime'
  params: {
    location: location
    tags: tags
    name: 'id-auxilia-next-${environmentName}'
  }
}

module deployIdentity 'modules/identity.bicep' = {
  scope: rg
  name: 'identity-deploy'
  params: {
    location: location
    tags: tags
    name: 'id-auxilia-next-deploy-${environmentName}'
    githubRepository: githubRepository
    githubEnvironment: githubEnvironment
  }
}

// The release updates the container apps and starts the auxctl job of this resource group only.
module deployAccess 'modules/resource-group-access.bicep' = {
  scope: rg
  name: 'deploy-access'
  params: {
    principalId: deployIdentity.outputs.principalId
  }
}

module registryAccess 'modules/registry-access.bicep' = {
  scope: sharedRg
  name: 'registry-access-${environmentName}'
  params: {
    registryName: registry.name
    pullPrincipalId: runtimeIdentity.outputs.principalId
    readerPrincipalId: deployIdentity.outputs.principalId
  }
}

module keyVault 'modules/key-vault.bicep' = {
  scope: rg
  name: 'key-vault'
  params: {
    location: location
    tags: tags
    name: 'kv-auxilia-next-${environmentName}'
    readerPrincipalIds: [runtimeIdentity.outputs.principalId]
    operatorObjectIds: operatorObjectIds
    automationPrincipalIds: automationPrincipalIds
    cryptoUserPrincipalIds: [runtimeIdentity.outputs.principalId]
  }
}

module storage 'modules/storage.bicep' = {
  scope: rg
  name: 'storage'
  params: {
    location: location
    tags: tags
    name: 'stauxilianext${environmentName}'
    logRetentionDays: logRetentionDays
  }
}

module apps 'modules/apps.bicep' = if (!empty(imageTag)) {
  scope: rg
  name: 'apps'
  params: {
    location: location
    tags: tags
    environmentName: environmentName
    containerAppsEnvironmentId: containerAppsEnvironment.id
    containerAppsDefaultDomain: containerAppsEnvironment.properties.defaultDomain
    registryLoginServer: registry.properties.loginServer
    imageTag: imageTag
    identityId: runtimeIdentity.outputs.id
    identityClientId: runtimeIdentity.outputs.clientId
    keyVaultUri: keyVault.outputs.uri
    dataProtectionKeyUri: keyVault.outputs.dataProtectionKeyUri
    forwardedNetworks: forwardedNetworks
    apiMaxReplicas: apiMaxReplicas
    workerMaxReplicas: workerMaxReplicas
  }
  dependsOn: [registryAccess]
}

output resourceGroupName string = rg.name
output keyVaultName string = keyVault.outputs.name
output storageAccountName string = storage.outputs.name
@description('Set as variable AZURE_CLIENT_ID of the GitHub environment.')
output deployClientId string = deployIdentity.outputs.clientId
output githubEnvironment string = githubEnvironment
output valkeyDatabase int = valkeyDatabase
output apiUrl string = 'https://ca-auxilia-api-${environmentName}.${containerAppsEnvironment.properties.defaultDomain}'
output webUrl string = 'https://ca-auxilia-web-${environmentName}.${containerAppsEnvironment.properties.defaultDomain}'
