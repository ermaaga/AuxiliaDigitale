// User-assigned managed identity, optionally trusted by the GitHub Actions workflows of one branch (OIDC, no stored
// secret). Branch, not GitHub environment: environments do not exist in private repositories on GitHub Free (ADR 0025).
param location string
param tags object
param name string
@description('owner/repo; empty = no federated credential.')
param githubRepository string = ''
@description('Git ref whose workflow runs may sign in (workflow_run and workflow_dispatch from the default branch).')
param githubRef string = 'refs/heads/main'

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: name
  location: location
  tags: tags
}

resource github 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = if (!empty(githubRepository)) {
  parent: identity
  name: 'github-${replace(replace(githubRef, 'refs/heads/', ''), '/', '-')}'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${githubRepository}:ref:${githubRef}'
    audiences: ['api://AzureADTokenExchange']
  }
}

output id string = identity.id
output principalId string = identity.properties.principalId
output clientId string = identity.properties.clientId
