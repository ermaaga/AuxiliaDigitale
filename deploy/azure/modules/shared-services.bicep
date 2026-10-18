// RabbitMQ, Valkey and the psql job shared by prod and qa (ADR 0022 §2, §4). Every secret is a Key Vault reference
// read through the shared identity; values are written by scripts/secrets.sh before this module is deployed.
param location string
param tags object
param environmentId string
param identityId string
param keyVaultUri string
param postgresFqdn string
param postgresAdminLogin string
param rabbitmqName string = 'ca-auxilia-rabbitmq-01'
param valkeyName string = 'ca-auxilia-valkey-01'
param psqlJobName string = 'caj-auxilia-psql-01'
param rabbitmqImage string
param valkeyImage string
param postgresClientImage string
param rabbitmqCpu string = '0.5'
param rabbitmqMemory string = '1Gi'
param valkeyCpu string = '0.25'
param valkeyMemory string = '0.5Gi'

func kvSecret(vaultUri string, secretName string, identity string) object => {
  name: secretName
  keyVaultUrl: '${vaultUri}secrets/${secretName}'
  identity: identity
}

// RabbitMQ: no disk. Users, vhosts (prod, qa) and permissions are loaded at every start from the definitions file
// (Key Vault secret rabbitmq-definitions, written by scripts/secrets.sh init); the Worker declares its queues.
resource rabbitmq 'Microsoft.App/containerApps@2024-03-01' = {
  name: rabbitmqName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    environmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        transport: 'tcp'
        targetPort: 5672
        exposedPort: 5672
      }
      secrets: [
        kvSecret(keyVaultUri, 'rabbitmq-definitions', identityId)
        {
          name: 'rabbitmq-conf'
          // Not a secret: a secret is the only way to mount a file from the template.
          #disable-next-line use-secure-value-for-secure-inputs
          value: loadTextContent('../rabbitmq/20-auxilia.conf')
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'rabbitmq'
          image: rabbitmqImage
          resources: {
            cpu: json(rabbitmqCpu)
            memory: rabbitmqMemory
          }
          volumeMounts: [
            {
              volumeName: 'config'
              mountPath: '/etc/rabbitmq/conf.d'
            }
          ]
        }
      ]
      volumes: [
        {
          name: 'config'
          storageType: 'Secret'
          secrets: [
            { secretRef: 'rabbitmq-conf', path: '20-auxilia.conf' }
            { secretRef: 'rabbitmq-definitions', path: 'definitions.json' }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [
          {
            name: 'tcp-connections'
            tcp: { metadata: { concurrentConnections: '50' } }
          }
        ]
      }
    }
  }
}

// Valkey: cache, SignalR backplane, deny-list; in memory only. prod and qa use different databases and channel
// prefixes (in their connection strings).
resource valkey 'Microsoft.App/containerApps@2024-03-01' = {
  name: valkeyName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    environmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        transport: 'tcp'
        targetPort: 6379
        exposedPort: 6379
      }
      secrets: [
        kvSecret(keyVaultUri, 'valkey-password', identityId)
      ]
    }
    template: {
      containers: [
        {
          name: 'valkey'
          image: valkeyImage
          command: ['sh', '-c']
          args: ['exec valkey-server --requirepass "$VALKEY_PASSWORD" --save "" --appendonly no --databases 16']
          env: [
            { name: 'VALKEY_PASSWORD', secretRef: 'valkey-password' }
          ]
          resources: {
            cpu: json(valkeyCpu)
            memory: valkeyMemory
          }
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [
          {
            name: 'tcp-connections'
            tcp: { metadata: { concurrentConnections: '100' } }
          }
        ]
      }
    }
  }
}

// psql inside the VNet (the server has no public endpoint): by default it runs postgres/bootstrap.sql, which creates
// the logins and Catalog databases of prod and qa; `az containerapp job start --command/--args` runs anything else.
resource psqlJob 'Microsoft.App/jobs@2024-03-01' = {
  name: psqlJobName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    environmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 3600
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      secrets: [
        kvSecret(keyVaultUri, 'postgres-admin-password', identityId)
        kvSecret(keyVaultUri, 'postgres-prod-password', identityId)
        kvSecret(keyVaultUri, 'postgres-qa-password', identityId)
        {
          name: 'bootstrap-sql'
          // Not a secret (the passwords come from environment variables): mounted as a file.
          #disable-next-line use-secure-value-for-secure-inputs
          value: loadTextContent('../postgres/bootstrap.sql')
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'psql'
          image: postgresClientImage
          command: ['psql']
          args: ['-v', 'ON_ERROR_STOP=1', '-d', 'postgres', '-f', '/scripts/bootstrap.sql']
          env: [
            { name: 'PGHOST', value: postgresFqdn }
            { name: 'PGUSER', value: postgresAdminLogin }
            { name: 'PGSSLMODE', value: 'require' }
            { name: 'PGPASSWORD', secretRef: 'postgres-admin-password' }
            { name: 'AUXILIA_PROD_PASSWORD', secretRef: 'postgres-prod-password' }
            { name: 'AUXILIA_QA_PASSWORD', secretRef: 'postgres-qa-password' }
          ]
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          volumeMounts: [
            {
              volumeName: 'scripts'
              mountPath: '/scripts'
            }
          ]
        }
      ]
      volumes: [
        {
          name: 'scripts'
          storageType: 'Secret'
          secrets: [
            { secretRef: 'bootstrap-sql', path: 'bootstrap.sql' }
          ]
        }
      ]
    }
  }
}

output rabbitmqHost string = rabbitmq.properties.configuration.ingress.fqdn
output valkeyHost string = valkey.properties.configuration.ingress.fqdn
