using '../environment.bicep'

// One file per environment (ADR 0024): adding environments/<name>.bicepparam and running the Infrastructure workflow
// with that name provisions it. <name>: 2–9 lowercase letters/digits, never "shared".
param environmentName = 'prod'
param logRetentionDays = 180
param apiMaxReplicas = 2
param workerMaxReplicas = 2
// Unique among the environments (0–15).
param valkeyDatabase = 0
// Set by scripts/deploy.sh: users and the workflow identity that write the secrets (Key Vault Secrets Officer).
param operatorObjectIds = json(readEnvironmentVariable('AUXILIA_OPERATOR_OBJECT_IDS', '[]'))
param automationPrincipalIds = json(readEnvironmentVariable('AUXILIA_AUTOMATION_PRINCIPAL_IDS', '[]'))
// Set by scripts/deploy.sh (--image <sha>, or the tag currently running); empty = no application containers.
param imageTag = readEnvironmentVariable('AUXILIA_IMAGE_TAG', '')
