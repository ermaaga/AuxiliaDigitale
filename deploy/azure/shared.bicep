// Shared resources of Auxilia Next on Azure (ADR 0022): network, Container Apps environment, PostgreSQL, registry,
// Key Vault and, once their secrets exist, RabbitMQ, Valkey and the psql job.
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

output resourceGroupName string = rg.name
output containerAppsEnvironmentName string = environment.outputs.name
output containerAppsDefaultDomain string = environment.outputs.defaultDomain
output containerAppsSubnetPrefix string = network.outputs.containerAppsSubnetPrefix
output registryLoginServer string = registry.outputs.loginServer
output keyVaultName string = keyVault.outputs.name
output postgresFqdn string = postgres.outputs.fqdn
