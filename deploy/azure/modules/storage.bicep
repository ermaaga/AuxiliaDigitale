// Storage account of one environment: document files (tenant setting documents.storage.provider = azure-blob) and
// the per-tenant daily logs of D-17, deleted after logRetentionDays by a lifecycle rule (retention outside the system).
param location string
param tags object
param name string
param logRetentionDays int

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
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
    // The application authenticates with a connection string (Azure.Identity is outside the allowlist, ADR 0022 §5).
    allowSharedKeyAccess: true
    publicNetworkAccess: 'Enabled'
  }
}

resource blobs 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource containers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [for container in ['documents', 'logs']: {
  parent: blobs
  name: container
  properties: {
    publicAccess: 'None'
  }
}]

resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'logs-retention'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: {
              blobTypes: ['appendBlob', 'blockBlob']
              prefixMatch: ['logs/']
            }
            actions: {
              baseBlob: {
                delete: { daysAfterModificationGreaterThan: logRetentionDays }
              }
            }
          }
        }
      ]
    }
  }
}

output name string = account.name
