using 'shared.bicep'

// No secret in this file: scripts/deploy.sh exports the values read from Key Vault (or generated on the first run).
param postgresAdminPassword = readEnvironmentVariable('AUXILIA_PG_ADMIN_PASSWORD')
// Set by scripts/deploy.sh: users and the workflow identity that write the secrets (Key Vault Secrets Officer).
param operatorObjectIds = json(readEnvironmentVariable('AUXILIA_OPERATOR_OBJECT_IDS', '[]'))
param automationPrincipalIds = json(readEnvironmentVariable('AUXILIA_AUTOMATION_PRINCIPAL_IDS', '[]'))
// Every environments/<name>.bicepparam, listed by scripts/deploy.sh (JSON array).
param environmentNames = json(readEnvironmentVariable('AUXILIA_ENVIRONMENTS', '[]'))
param deploySharedServices = readEnvironmentVariable('AUXILIA_DEPLOY_SHARED_SERVICES', 'false') == 'true'
// Satellite apps (ADR 0022 §11): deploy.sh keeps the running tags, --openwa/--agent-openwa/--site <sha> change them.
param openwaImageTag = readEnvironmentVariable('AUXILIA_OPENWA_IMAGE_TAG', '')
param agentOpenwaImageTag = readEnvironmentVariable('AUXILIA_AGENT_OPENWA_IMAGE_TAG', '')
param siteImageTag = readEnvironmentVariable('AUXILIA_SITE_IMAGE_TAG', '')
// Ports, session folder and plain settings of the satellite apps: set them here once their images are known, e.g.
// param openwaEnv = [{ name: 'PORT', value: '8080' }]
// param agentOpenwaSecrets = [{ secret: 'agent-openwa-llm-key', variable: 'LLM_API_KEY' }]
