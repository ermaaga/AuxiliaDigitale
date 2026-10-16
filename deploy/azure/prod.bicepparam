using 'environment.bicep'

param environmentName = 'prod'
param githubEnvironment = 'production'
param logRetentionDays = 180
param apiMaxReplicas = 2
param workerMaxReplicas = 2
// Signed-in user running scripts/deploy.sh: writes the secrets (Key Vault Secrets Officer).
param operatorObjectIds = [readEnvironmentVariable('AUXILIA_OPERATOR_OBJECT_ID')]
// Set by scripts/deploy.sh (--image <sha>, or the tag currently running); empty = no application containers.
param imageTag = readEnvironmentVariable('AUXILIA_IMAGE_TAG', '')
