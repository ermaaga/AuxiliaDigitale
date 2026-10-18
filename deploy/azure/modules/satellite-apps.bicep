// Applications outside this repository, shared by prod and qa (ADR 0022 §11): OpenWA (WhatsApp Web gateway),
// agent-openwa and the showcase site. Images come from the ACR Tasks of their own repositories
// (scripts/acr-task.sh satellite …); an app is created only once its image tag is set. Every app scales to zero.
param location string
param tags object
param containerAppsEnvironmentName string
param containerAppsDefaultDomain string
param registryLoginServer string
@description('Shared identity: AcrPull on the registry, Key Vault Secrets User on kv-auxilia-next-shared.')
param identityId string
param keyVaultUri string
param storageAccountName string = 'stauxilianextshared'

param openwaName string = 'ca-auxilia-openwa-01'
param openwaRepository string = 'auxilia/openwa'
@description('Commit SHA (or tag) of the OpenWA image; empty = app not deployed.')
param openwaImageTag string = ''
param openwaPort int = 8080
@description('Folder of the WhatsApp Web session inside the container (kept on Azure Files).')
param openwaSessionPath string = '/sessions'
@description('Environment variable that receives the API key (Key Vault secret openwa-api-key).')
param openwaApiKeyVariable string = 'OPENWA_API_KEY'
@description('Extra plain environment variables: [{ name, value }].')
param openwaEnv array = []
param openwaCpu string = '0.5'
param openwaMemory string = '1Gi'

param agentName string = 'ca-auxilia-agent-openwa-01'
param agentRepository string = 'auxilia/agent-openwa'
@description('Commit SHA (or tag) of the agent image; empty = app not deployed.')
param agentImageTag string = ''
param agentPort int = 8080
@description('true only if the agent receives calls from outside the environment (e.g. third-party webhooks).')
param agentExternal bool = false
param agentEnv array = []
@description('Key Vault secrets of kv-auxilia-next-shared passed as environment variables: [{ secret, variable }].')
param agentSecrets array = []
param agentCpu string = '0.25'
param agentMemory string = '0.5Gi'

param siteName string = 'ca-auxilia-site-01'
param siteRepository string = 'auxilia/site'
@description('Commit SHA (or tag) of the showcase site image; empty = app not deployed.')
param siteImageTag string = ''
param sitePort int = 80
param siteEnv array = []
param siteMaxReplicas int = 2
param siteCpu string = '0.25'
param siteMemory string = '0.5Gi'

var sessionShareName = 'openwa-sessions'
var openwaUrl = 'https://${openwaName}.internal.${containerAppsDefaultDomain}'

func kvSecret(vaultUri string, secretName string, identity string) object => {
  name: secretName
  keyVaultUrl: '${vaultUri}secrets/${secretName}'
  identity: identity
}

var registries = [
  {
    server: registryLoginServer
    identity: identityId
  }
]

var identityConfig = {
  type: 'UserAssigned'
  userAssignedIdentities: { '${identityId}': {} }
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: containerAppsEnvironmentName
}

// Azure Files keeps the WhatsApp Web session across restarts and scale to zero (no QR code scan each time).
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    // SMB mounts of Container Apps authenticate with the account key.
    allowSharedKeyAccess: true
    publicNetworkAccess: 'Enabled'
  }
}

resource files 'Microsoft.Storage/storageAccounts/fileServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    shareDeleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource sessionShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-05-01' = {
  parent: files
  name: sessionShareName
  properties: {
    shareQuota: 5
    accessTier: 'TransactionOptimized'
  }
}

resource sessionStorage 'Microsoft.App/managedEnvironments/storages@2024-03-01' = {
  parent: environment
  name: sessionShareName
  properties: {
    azureFile: {
      accountName: storage.name
      accountKey: storage.listKeys().keys[0].value
      shareName: sessionShare.name
      accessMode: 'ReadWrite'
    }
  }
}

// OpenWA: internal only (agent-openwa and, later, the WhatsApp adapter of the API call it), at most one replica
// because one WhatsApp session cannot be opened twice. Asleep = no incoming messages (choice of the user, §11).
resource openwa 'Microsoft.App/containerApps@2024-03-01' = if (!empty(openwaImageTag)) {
  name: openwaName
  location: location
  tags: tags
  identity: identityConfig
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      registries: registries
      secrets: [
        kvSecret(keyVaultUri, 'openwa-api-key', identityId)
      ]
      ingress: {
        external: false
        targetPort: openwaPort
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'openwa'
          image: '${registryLoginServer}/${openwaRepository}:${openwaImageTag}'
          env: concat([{ name: openwaApiKeyVariable, secretRef: 'openwa-api-key' }], openwaEnv)
          resources: {
            cpu: json(openwaCpu)
            memory: openwaMemory
          }
          volumeMounts: [
            {
              volumeName: 'session'
              mountPath: openwaSessionPath
            }
          ]
        }
      ]
      volumes: [
        {
          name: 'session'
          storageType: 'AzureFile'
          storageName: sessionStorage.name
          // Chromium writes symlinks (SingletonLock) and byte-range locks that plain SMB refuses.
          mountOptions: 'dir_mode=0777,file_mode=0777,mfsymlinks,nobrl'
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [
          {
            name: 'http-requests'
            http: { metadata: { concurrentRequests: '20' } }
          }
        ]
      }
    }
  }
}

resource agent 'Microsoft.App/containerApps@2024-03-01' = if (!empty(agentImageTag)) {
  name: agentName
  location: location
  tags: tags
  identity: identityConfig
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      registries: registries
      secrets: concat(
        [kvSecret(keyVaultUri, 'openwa-api-key', identityId)],
        map(agentSecrets, item => kvSecret(keyVaultUri, item.secret, identityId))
      )
      ingress: {
        external: agentExternal
        targetPort: agentPort
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'agent-openwa'
          image: '${registryLoginServer}/${agentRepository}:${agentImageTag}'
          env: concat(
            [
              { name: 'OPENWA_URL', value: openwaUrl }
              { name: 'OPENWA_API_KEY', secretRef: 'openwa-api-key' }
            ],
            map(agentSecrets, item => { name: item.variable, secretRef: item.secret }),
            agentEnv
          )
          resources: {
            cpu: json(agentCpu)
            memory: agentMemory
          }
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [
          {
            name: 'http-requests'
            http: { metadata: { concurrentRequests: '20' } }
          }
        ]
      }
    }
  }
}

resource site 'Microsoft.App/containerApps@2024-03-01' = if (!empty(siteImageTag)) {
  name: siteName
  location: location
  tags: tags
  identity: identityConfig
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      registries: registries
      ingress: {
        external: true
        targetPort: sitePort
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'site'
          image: '${registryLoginServer}/${siteRepository}:${siteImageTag}'
          env: siteEnv
          resources: {
            cpu: json(siteCpu)
            memory: siteMemory
          }
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: siteMaxReplicas
        rules: [
          {
            name: 'http-requests'
            http: { metadata: { concurrentRequests: '50' } }
          }
        ]
      }
    }
  }
}

output openwaUrl string = openwaUrl
output siteUrl string = 'https://${siteName}.${containerAppsDefaultDomain}'
