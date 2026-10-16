using 'shared.bicep'

// No secret in this file: scripts/deploy.sh exports the values read from Key Vault (or generated on the first run).
param postgresAdminPassword = readEnvironmentVariable('AUXILIA_PG_ADMIN_PASSWORD')
// Signed-in user running scripts/deploy.sh: writes the secrets (Key Vault Secrets Officer).
param operatorObjectIds = [readEnvironmentVariable('AUXILIA_OPERATOR_OBJECT_ID')]
param deploySharedServices = readEnvironmentVariable('AUXILIA_DEPLOY_SHARED_SERVICES', 'false') == 'true'
