-- Logins and Catalog databases of prod and qa on the shared server (ADR 0022 §2). Idempotent: run again after a
-- password rotation. Executed by the job caj-auxilia-psql-01 as the server administrator; the passwords come from
-- Key Vault through the environment variables AUXILIA_PROD_PASSWORD and AUXILIA_QA_PASSWORD (never in this file).
--
-- auxilia_<env> is the Catalog login and the provisioning login of its environment (D-02: CREATEDB, CREATEROLE).
-- On PostgreSQL >= 16 a CREATEROLE login may alter only the roles it created, so the tenant roles of one environment
-- cannot be changed by the other one.

\getenv prod_password AUXILIA_PROD_PASSWORD
\getenv qa_password AUXILIA_QA_PASSWORD

SELECT 'CREATE ROLE auxilia_prod LOGIN CREATEDB CREATEROLE'
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'auxilia_prod') \gexec
SELECT format('ALTER ROLE auxilia_prod WITH LOGIN CREATEDB CREATEROLE PASSWORD %L', :'prod_password') \gexec

SELECT 'CREATE ROLE auxilia_qa LOGIN CREATEDB CREATEROLE'
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'auxilia_qa') \gexec
SELECT format('ALTER ROLE auxilia_qa WITH LOGIN CREATEDB CREATEROLE PASSWORD %L', :'qa_password') \gexec

-- The administrator must be a member to create a database owned by the login.
GRANT auxilia_prod TO CURRENT_USER;
GRANT auxilia_qa TO CURRENT_USER;

SELECT 'CREATE DATABASE auxilia_catalog_prod OWNER auxilia_prod'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'auxilia_catalog_prod') \gexec
SELECT 'CREATE DATABASE auxilia_catalog_qa OWNER auxilia_qa'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'auxilia_catalog_qa') \gexec

REVOKE CONNECT ON DATABASE auxilia_catalog_prod FROM PUBLIC;
REVOKE CONNECT ON DATABASE auxilia_catalog_qa FROM PUBLIC;
