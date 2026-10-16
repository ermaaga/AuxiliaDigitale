// Shared resources of Auxilia Next on Azure (ADR 0022): network, Container Apps environment, PostgreSQL, registry,
// Key Vault and, once their secrets exist, RabbitMQ, Valkey, the psql job and the satellite apps (OpenWA,
// agent-openwa, showcase site: §11, each one only once its image tag is set).
// Deploy with scripts/deploy.sh shared [--what-if]; never by hand with secrets on the command line.
targetScope = 'subscription'

@description('Azure region of every resource.')
param location string = 'italynorth'
param resourceGroupName string = 'rg-auxilia-next-shared'
param tags object = {
  application: 'auxilia-next'
  environment: 'shared'
}

param vnetName string = 'vnet-auxilia-next-01'
param containerAppsEnvironmentName string = 'cae-auxilia-next-01'
param registryName string = 'acrauxilianext01'
@allowed(['Basic', 'Standard'])
param registrySku string = 'Basic'
param keyVaultName string = 'kv-auxilia-next-shared'
param identityName string = 'id-auxilia-next-shared'

param postgresServerName string = 'psql-auxilia-next-01'
param postgresVersion string = '18'
param postgresAdminLogin string = 'auxilia_admin'
@secure()
@description('Read by shared.bicepparam from AUXILIA_PG_ADMIN_PASSWORD, set by scripts/deploy.sh (Key Vault or generated).')
param postgresAdminPassword string

@description('Users who write secrets with scripts/secrets.sh (object ids, Key Vault Secrets Officer).')
param operatorObjectIds array = []

@description('RabbitMQ, Valkey and the psql job: only after scripts/secrets.sh has written their Key Vault secrets.')
param deploySharedServices bool = false
param rabbitmqImage string = 'docker.io/library/rabbitmq:4.2-alpine'
param valkeyImage string = 'docker.io/valkey/valkey:8.1-alpine'
param postgresClientImage string = 'docker.io/library/postgres:18-alpine'

// Satellite apps (ADR 0022 §11): images built in acrauxilianext01 by the ACR Tasks of their own repositories.
@description('Image tags (commit SHA) set by scripts/deploy.sh; empty = the app is not deployed.')
param openwaImageTag string = ''
param agentOpenwaImageTag string = ''
param siteImageTag string = ''
param openwaPort int = 8080
param openwaSessionPath string = '/sessions'
param openwaApiKeyVariable string = 'OPENWA_API_KEY'
@description('Plain environment variables of each app: [{ name, value }] (no secrets).')
param openwaEnv array = []
param agentOpenwaEnv array = []
param siteEnv array = []
param agentOpenwaPort int = 8080
param agentOpenwaExternal bool = false
@description('Key Vault secrets (kv-auxilia-next-shared) of agent-openwa: [{ secret, variable }].')
param agentOpenwaSecrets array = []
param sitePort int = 80

resource rg 'Microsoft.Resources/resourceGroups@2024-07-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module network 'modules/network.bicep' = {
  scope: rg
  name: 'network'
  params: {
    location: location
    tags: tags
    vnetName: vnetName
  }
}

module identity 'modules/identity.bicep' = {
  scope: rg
  name: 'identity-shared'
  params: {
    location: location
    tags: tags
    name: identityName
  }
}

module keyVault 'modules/key-vault.bicep' = {
  scope: rg
  name: 'key-vault-shared'
  params: {
    location: location
    tags: tags
    name: keyVaultName
    readerPrincipalIds: [identity.outputs.principalId]
    operatorObjectIds: operatorObjectIds
  }
}

module registry 'modules/registry.bicep' = {
  scope: rg
  name: 'registry'
  params: {
    location: location
    tags: tags
    name: registryName
    sku: registrySku
  }
}

module environment 'modules/container-environment.bicep' = {
  scope: rg
  name: 'container-environment'
  params: {
    location: location
    tags: tags
    name: containerAppsEnvironmentName
    subnetId: network.outputs.containerAppsSubnetId
  }
}

module postgres 'modules/postgres.bicep' = {
  scope: rg
  name: 'postgres'
  params: {
    location: location
    tags: tags
    serverName: postgresServerName
    version: postgresVersion
    vnetId: network.outputs.vnetId
    subnetId: network.outputs.postgresSubnetId
    administratorLogin: postgresAdminLogin
    administratorPassword: postgresAdminPassword
  }
}

module sharedServices 'modules/shared-services.bicep' = if (deploySharedServices) {
  scope: rg
  name: 'shared-services'
  params: {
    location: location
    tags: tags
    environmentId: environment.outputs.id
    identityId: identity.outputs.id
    keyVaultUri: keyVault.outputs.uri
    postgresFqdn: postgres.outputs.fqdn
    postgresAdminLogin: postgresAdminLogin
    rabbitmqImage: rabbitmqImage
    valkeyImage: valkeyImage
    postgresClientImage: postgresClientImage
  }
}

module sharedRegistryPull 'modules/registry-pull.bicep' = {
  scope: rg
  name: 'registry-pull-shared'
  params: {
    registryName: registry.outputs.name
    principalId: identity.outputs.principalId
  }
}

module satelliteApps 'modules/satellite-apps.bicep' = if (deploySharedServices) {
  scope: rg
  name: 'satellite-apps'
  params: {
    location: location
    tags: tags
    containerAppsEnvironmentName: environment.outputs.name
    containerAppsDefaultDomain: environment.outputs.defaultDomain
    registryLoginServer: registry.outputs.loginServer
    identityId: identity.outputs.id
    keyVaultUri: keyVault.outputs.uri
    openwaImageTag: openwaImageTag
    openwaPort: openwaPort
    openwaSessionPath: openwaSessionPath
    openwaApiKeyVariable: openwaApiKeyVariable
    openwaEnv: openwaEnv
    agentImageTag: agentOpenwaImageTag
    agentPort: agentOpenwaPort
    agentExternal: agentOpenwaExternal
    agentEnv: agentOpenwaEnv
    agentSecrets: agentOpenwaSecrets
    siteImageTag: siteImageTag
    sitePort: sitePort
    siteEnv: siteEnv
  }
  dependsOn: [sharedRegistryPull]
}

output resourceGroupName string = rg.name
output containerAppsEnvironmentName string = environment.outputs.name
output containerAppsDefaultDomain string = environment.outputs.defaultDomain
output containerAppsSubnetPrefix string = network.outputs.containerAppsSubnetPrefix
output registryLoginServer string = registry.outputs.loginServer
output keyVaultName string = keyVault.outputs.name
output postgresFqdn string = postgres.outputs.fqdn
output openwaInternalUrl string = deploySharedServices ? satelliteApps!.outputs.openwaUrl : ''
output siteUrl string = deploySharedServices ? satelliteApps!.outputs.siteUrl : ''
