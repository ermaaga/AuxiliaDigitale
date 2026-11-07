// User-assigned managed identity, optionally trusted by the GitHub Actions workflows of one branch (OIDC, no stored
// secret). Branch, not GitHub environment: environments do not exist in private repositories on GitHub Free (ADR 0025).
param location string
param tags object
param name string
@description('owner/repo; empty = no federated credential.')
param githubRepository string = ''
@description('Git ref whose workflow runs may sign in (workflow_run and workflow_dispatch from the default branch).')
param githubRef string = 'refs/heads/main'
@description('Subject prefix of the repository with immutable ids (repo:<owner>@<owner id>/<repo>@<repo id>), the format GitHub sends when the repository uses immutable OIDC subjects (gh api repos/<owner>/<repo>/actions/oidc/customization/sub); empty = none.')
param githubImmutableSubjectPrefix string = ''

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

// Same branch, subject with the immutable owner and repository ids (a renamed or re-created repository does not match).
resource githubImmutable 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = if (!empty(githubImmutableSubjectPrefix)) {
  parent: identity
  name: 'github-${replace(replace(githubRef, 'refs/heads/', ''), '/', '-')}-immutable'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: '${githubImmutableSubjectPrefix}:ref:${githubRef}'
    audiences: ['api://AzureADTokenExchange']
  }
  dependsOn: [github]
}

output id string = identity.id
output principalId string = identity.properties.principalId
output clientId string = identity.properties.clientId
