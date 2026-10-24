-- Login and Catalog database of one environment on the shared server (ADR 0022 §2). Idempotent: run again after a
-- password rotation. Executed by the job caj-auxilia-psql-01 as the server administrator, once per environment, with
-- `-v env=<name>`; the password comes from Key Vault through the environment variable AUXILIA_PG_PASSWORD (never in
-- this file nor on a command line).
--
-- auxilia_<env> is the Catalog login and the provisioning login of its environment (D-02: CREATEDB, CREATEROLE).
-- On PostgreSQL >= 16 a CREATEROLE login may alter only the roles it created, so the tenant roles of one environment
-- cannot be changed by another one.

\getenv password AUXILIA_PG_PASSWORD
\set login 'auxilia_' :env
\set catalog 'auxilia_catalog_' :env

SELECT format('CREATE ROLE %I LOGIN CREATEDB CREATEROLE', :'login')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'login') \gexec
SELECT format('ALTER ROLE %I WITH LOGIN CREATEDB CREATEROLE PASSWORD %L', :'login', :'password') \gexec

-- The administrator must be a member to create a database owned by the login.
SELECT format('GRANT %I TO CURRENT_USER', :'login') \gexec

SELECT format('CREATE DATABASE %I OWNER %I', :'catalog', :'login')
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = :'catalog') \gexec

SELECT format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', :'catalog') \gexec
