using 'environment.bicep'

param environmentName = 'qa'
param githubEnvironment = 'qa'
param logRetentionDays = 14
param apiMaxReplicas = 1
param workerMaxReplicas = 1
// Signed-in user running scripts/deploy.sh: writes the secrets (Key Vault Secrets Officer).
param operatorObjectIds = [readEnvironmentVariable('AUXILIA_OPERATOR_OBJECT_ID')]
// Set by scripts/deploy.sh (--image <sha>, or the tag currently running); empty = no application containers.
param imageTag = readEnvironmentVariable('AUXILIA_IMAGE_TAG', '')
