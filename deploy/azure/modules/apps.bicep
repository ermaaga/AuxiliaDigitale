// Application containers of one environment (ADR 0022): api, worker, web and the auxctl job, in the shared
// Container Apps environment, images from the shared registry, secrets as Key Vault references.
param location string
param tags object
param environmentName string
param containerAppsEnvironmentId string
param containerAppsDefaultDomain string
param registryLoginServer string
@description('Commit SHA built by the ACR Task (tag of auxilia/api|worker|web|auxctl).')
param imageTag string
param identityId string
param keyVaultUri string
@description('Networks of the ingress proxies whose X-Forwarded-For the API trusts (ForwardedHeaders:KnownNetworks).')
param forwardedNetworks array
param apiMaxReplicas int = 2
param workerMaxReplicas int = 2
param webClientId string = 'web-bff'
param consoleClientId string = 'console'

var apiName = 'ca-auxilia-api-${environmentName}'
var workerName = 'ca-auxilia-worker-${environmentName}'
var webName = 'ca-auxilia-web-${environmentName}'
var jobName = 'caj-auxilia-auxctl-${environmentName}'
var apiUrl = 'https://${apiName}.${containerAppsDefaultDomain}'
var webUrl = 'https://${webName}.${containerAppsDefaultDomain}'

// Queues consumed by the Worker (Auxilia.Contracts.Messages.V1.<Module> → auxilia.<module>, MessageRouting).
var workerQueues = ['documents', 'imports', 'mailbox', 'marketing', 'messaging', 'platform', 'reporting']

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

var backendSecrets = [
  kvSecret(keyVaultUri, 'catalog-connection', identityId)
  kvSecret(keyVaultUri, 'redis-connection', identityId)
  kvSecret(keyVaultUri, 'rabbitmq-connection', identityId)
  kvSecret(keyVaultUri, 'storage-connection', identityId)
  kvSecret(keyVaultUri, 'altcha-key', identityId)
]

var backendEnv = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
  { name: 'ConnectionStrings__Catalog', secretRef: 'catalog-connection' }
  { name: 'ConnectionStrings__Redis', secretRef: 'redis-connection' }
  { name: 'ConnectionStrings__RabbitMq', secretRef: 'rabbitmq-connection' }
  { name: 'AuxiliaLogging__Storage', value: 'azure-blob' }
  { name: 'AuxiliaLogging__AzureBlob__ConnectionString', secretRef: 'storage-connection' }
  { name: 'AuxiliaLogging__AzureBlob__Container', value: 'logs' }
  { name: 'Captcha__Altcha__Key', secretRef: 'altcha-key' }
]

var forwardedEnv = [for (network, index) in forwardedNetworks: {
  name: 'ForwardedHeaders__KnownNetworks__${index}'
  value: network
}]

var identityConfig = {
  type: 'UserAssigned'
  userAssignedIdentities: { '${identityId}': {} }
}

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiName
  location: location
  tags: tags
  identity: identityConfig
  properties: {
    environmentId: containerAppsEnvironmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      registries: registries
      secrets: backendSecrets
      // External: the browser connects to the SignalR hub directly (AUXILIA_API_PUBLIC_URL); WebSocket only.
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${registryLoginServer}/auxilia/api:${imageTag}'
          env: concat(backendEnv, forwardedEnv)
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          probes: [
            {
              type: 'Startup'
              httpGet: { path: '/health/live', port: 8080 }
              periodSeconds: 2
              failureThreshold: 60
            }
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080 }
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080 }
              periodSeconds: 15
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: apiMaxReplicas
        rules: [
          {
            name: 'http-requests'
            http: { metadata: { concurrentRequests: '30' } }
          }
        ]
      }
    }
  }
}

resource worker 'Microsoft.App/containerApps@2024-03-01' = {
  name: workerName
  location: location
  tags: tags
  identity: identityConfig
  properties: {
    environmentId: containerAppsEnvironmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      registries: registries
      secrets: backendSecrets
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: '${registryLoginServer}/auxilia/worker:${imageTag}'
          env: backendEnv
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      // Woken by messages: one KEDA rule per queue, in the vhost of this environment (from the AMQP URI).
      scale: {
        minReplicas: 0
        maxReplicas: workerMaxReplicas
        rules: [for queue in workerQueues: {
          name: 'queue-${queue}'
          custom: {
            type: 'rabbitmq'
            metadata: {
              queueName: 'auxilia.${queue}'
              mode: 'QueueLength'
              value: '5'
            }
            auth: [
              {
                secretRef: 'rabbitmq-connection'
                triggerParameter: 'host'
              }
            ]
          }
        }]
      }
    }
  }
}

resource web 'Microsoft.App/containerApps@2024-03-01' = {
  name: webName
  location: location
  tags: tags
  identity: identityConfig
  properties: {
    environmentId: containerAppsEnvironmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      registries: registries
      secrets: [
        kvSecret(keyVaultUri, 'web-client-secret', identityId)
        kvSecret(keyVaultUri, 'console-client-secret', identityId)
      ]
      ingress: {
        external: true
        targetPort: 3000
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'web'
          image: '${registryLoginServer}/auxilia/web:${imageTag}'
          env: [
            { name: 'AUXILIA_API_URL', value: apiUrl }
            { name: 'AUXILIA_API_PUBLIC_URL', value: apiUrl }
            { name: 'AUXILIA_PUBLIC_ORIGIN', value: webUrl }
            { name: 'AUXILIA_WEB_CLIENT_ID', value: webClientId }
            { name: 'AUXILIA_WEB_CLIENT_SECRET', secretRef: 'web-client-secret' }
            { name: 'AUXILIA_CONSOLE_CLIENT_ID', value: consoleClientId }
            { name: 'AUXILIA_CONSOLE_CLIENT_SECRET', secretRef: 'console-client-secret' }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          probes: [
            {
              type: 'Startup'
              tcpSocket: { port: 3000 }
              periodSeconds: 2
              failureThreshold: 60
            }
          ]
        }
      ]
      // One replica at most: BFF sessions live in the memory of the process (ADR 0022 §4).
      scale: {
        minReplicas: 0
        maxReplicas: 1
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

// auxctl inside the VNet: the release runs `migrate catalog` and `migrate tenants --all`; any other command with
// `az containerapp job start --args` (runbook docs/runbooks/azure-deployment.md).
resource auxctl 'Microsoft.App/jobs@2024-03-01' = {
  name: jobName
  location: location
  tags: tags
  identity: identityConfig
  properties: {
    environmentId: containerAppsEnvironmentId
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 3600
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: registries
      secrets: backendSecrets
    }
    template: {
      containers: [
        {
          name: 'auxctl'
          image: '${registryLoginServer}/auxilia/auxctl:${imageTag}'
          args: ['migrate', 'catalog']
          env: backendEnv
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
    }
  }
}

output apiUrl string = apiUrl
output webUrl string = webUrl
