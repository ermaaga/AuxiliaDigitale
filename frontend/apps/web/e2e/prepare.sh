#!/usr/bin/env bash
# Seeds a fresh environment for the Playwright suite (P3-09): Catalog, tenants `demo` and `beta`, the two client
# applications, a tenant Administrator (`mario.rossi`) and a System user (`ops@example.test`). Writes the generated
# client secrets to apps/web/e2e/.env.e2e (git-ignored), read by playwright.config.ts.
#
# Needs: Postgres from deploy/compose.dev.yml (empty database), the backend built in Release
# (`dotnet build backend/Auxilia.slnx -c Release`). Run from anywhere: `pnpm --filter web e2e:prepare`.
# To start again: `docker compose -f deploy/compose.dev.yml down -v && docker compose ... up -d postgres`.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../../../.." && pwd)"
compose=(docker compose -f "$root/deploy/compose.dev.yml")
pg_user="${AUXILIA_PG_USER:-auxilia}"
pg_password="${AUXILIA_PG_PASSWORD:-auxilia-dev}"

export ConnectionStrings__Catalog="${E2E_CATALOG_CONNECTION:-Host=localhost;Database=auxilia_catalog;Username=$pg_user;Password=$pg_password}"
export AuxiliaLogging__Storage=none

auxctl() {
  dotnet run --project "$root/backend/src/Auxilia.MigrationRunner" -c Release --no-build -- "$@"
}

# The value printed after "shown only now...): " by auxctl (client secret, activation token).
secret_of() {
  sed -n 's/.*shown only now[^)]*): //p' | tail -n 1
}

auxctl migrate catalog > /dev/null
auxctl tenant provision --slug demo --name Demo > /dev/null
auxctl tenant provision --slug beta --name "Beta Studio" > /dev/null
web_secret="$(auxctl clients add --client-id e2e-web --name "E2E web" --type WebBff | secret_of)"
console_secret="$(auxctl clients add --client-id e2e-console --name "E2E console" --type PlatformConsole | secret_of)"
auxctl platform users add --email ops@example.test --name "Operations" > /dev/null

# A tenant Administrator (creating users from the app comes with B-05): its password is set by the global setup.
"${compose[@]}" exec -T postgres psql -v ON_ERROR_STOP=1 -q -U "$pg_user" -d auxilia_t_demo <<'SQL'
insert into directory.people(id, first_name, last_name, email, created_at)
values ('0199aaaa-0000-7000-8000-000000000001', 'Mario', 'Rossi', 'mario.rossi@example.test', now());
insert into identity.users(id, person_id, user_name, email, language_code, is_active, password_format, security_stamp,
                           access_failed_count, lockout_count, created_at)
values ('0199aaaa-0000-7000-8000-000000000002', '0199aaaa-0000-7000-8000-000000000001', 'mario.rossi',
        'mario.rossi@example.test', 'it', true, 'Identity', '00000000000000000000000000000001', 0, 0, now());
insert into identity.user_roles(user_id, role)
values ('0199aaaa-0000-7000-8000-000000000002', 'Administrator'), ('0199aaaa-0000-7000-8000-000000000002', 'Employee');

-- The client pages (B-04): an Administrator only and an Employee only, with their own passwords (set by the spec).
insert into directory.people(id, first_name, last_name, email, created_at)
values ('0199aaaa-0000-7000-8000-000000000011', 'Anna', 'Bianchi', 'anna.bianchi@example.test', now()),
       ('0199aaaa-0000-7000-8000-000000000021', 'Paola', 'Neri', 'paola.neri@example.test', now());
insert into identity.users(id, person_id, user_name, email, language_code, is_active, password_format, security_stamp,
                           access_failed_count, lockout_count, created_at)
values ('0199aaaa-0000-7000-8000-000000000012', '0199aaaa-0000-7000-8000-000000000011', 'anna.bianchi',
        'anna.bianchi@example.test', 'it', true, 'Identity', '00000000000000000000000000000011', 0, 0, now()),
       ('0199aaaa-0000-7000-8000-000000000022', '0199aaaa-0000-7000-8000-000000000021', 'paola.neri',
        'paola.neri@example.test', 'it', true, 'Identity', '00000000000000000000000000000021', 0, 0, now());
insert into identity.user_roles(user_id, role)
values ('0199aaaa-0000-7000-8000-000000000012', 'Administrator'), ('0199aaaa-0000-7000-8000-000000000022', 'Employee');

-- The profile page (B-05): an Employee of its own, since changing the password closes the user's other sessions.
insert into directory.people(id, first_name, last_name, email, created_at)
values ('0199aaaa-0000-7000-8000-000000000031', 'Laura', 'Verdi', 'laura.verdi@example.test', now());
insert into identity.users(id, person_id, user_name, email, language_code, is_active, password_format, security_stamp,
                           access_failed_count, lockout_count, created_at)
values ('0199aaaa-0000-7000-8000-000000000032', '0199aaaa-0000-7000-8000-000000000031', 'laura.verdi',
        'laura.verdi@example.test', 'it', true, 'Identity', '00000000000000000000000000000031', 0, 0, now());
insert into identity.user_roles(user_id, role) values ('0199aaaa-0000-7000-8000-000000000032', 'Employee');

-- The document pages (B-13): a client of its own, whose documents the spec uploads and deletes.
insert into directory.people(id, first_name, last_name, email, created_at)
values ('0199aaaa-0000-7000-8000-000000000041', 'Elena', 'Russo', 'elena.russo@example.test', now());
insert into directory.client_profiles(id, status, status_changed_at, created_at)
values ('0199aaaa-0000-7000-8000-000000000041', 'Inactive', now(), now());
insert into identity.users(id, person_id, user_name, email, language_code, is_active, password_format, security_stamp,
                           access_failed_count, lockout_count, created_at)
values ('0199aaaa-0000-7000-8000-000000000042', '0199aaaa-0000-7000-8000-000000000041', 'elena.russo@example.test',
        'elena.russo@example.test', 'it', false, 'Identity', '00000000000000000000000000000041', 0, 0, now());
insert into identity.user_roles(user_id, role) values ('0199aaaa-0000-7000-8000-000000000042', 'Client');

-- The case pages (B-14): a client and a service with one folder, used only by tenant-cases.spec.ts.
insert into directory.people(id, first_name, last_name, email, created_at)
values ('0199aaaa-0000-7000-8000-000000000051', 'Marco', 'Ferri', 'marco.ferri@example.test', now());
insert into directory.client_profiles(id, status, status_changed_at, created_at)
values ('0199aaaa-0000-7000-8000-000000000051', 'Inactive', now(), now());
insert into identity.users(id, person_id, user_name, email, language_code, is_active, password_format, security_stamp,
                           access_failed_count, lockout_count, created_at)
values ('0199aaaa-0000-7000-8000-000000000052', '0199aaaa-0000-7000-8000-000000000051', 'marco.ferri@example.test',
        'marco.ferri@example.test', 'it', false, 'Identity', '00000000000000000000000000000051', 0, 0, now());
insert into identity.user_roles(user_id, role) values ('0199aaaa-0000-7000-8000-000000000052', 'Client');
insert into cases.services(id, name, description, price, currency, duration_days, is_active, is_deleted, created_at)
values ('0199aaaa-0000-7000-8000-000000000061', 'Dichiarazione E2E', 'Servizio delle prove E2E', 150.00, 'EUR', 30, true,
        false, now());
insert into cases.service_folders(id, service_id, name, sort_order, created_at)
values ('0199aaaa-0000-7000-8000-000000000062', '0199aaaa-0000-7000-8000-000000000061', 'Redditi', 0, now());

-- The appointment pages (B-17): an employee and a client in her charge who signs in, used only by tenant-appointments.spec.ts.
insert into directory.people(id, first_name, last_name, email, created_at)
values ('0199aaaa-0000-7000-8000-000000000081', 'Sara', 'Gallo', 'sara.gallo@example.test', now()),
       ('0199aaaa-0000-7000-8000-000000000071', 'Giulia', 'Conti', 'giulia.conti@example.test', now());
insert into identity.users(id, person_id, user_name, email, language_code, is_active, password_format, security_stamp,
                           access_failed_count, lockout_count, created_at)
values ('0199aaaa-0000-7000-8000-000000000082', '0199aaaa-0000-7000-8000-000000000081', 'sara.gallo',
        'sara.gallo@example.test', 'it', true, 'Identity', '00000000000000000000000000000081', 0, 0, now()),
       ('0199aaaa-0000-7000-8000-000000000072', '0199aaaa-0000-7000-8000-000000000071', 'giulia.conti',
        'giulia.conti@example.test', 'it', true, 'Identity', '00000000000000000000000000000071', 0, 0, now());
insert into identity.user_roles(user_id, role)
values ('0199aaaa-0000-7000-8000-000000000082', 'Employee'), ('0199aaaa-0000-7000-8000-000000000072', 'Client');
insert into directory.client_profiles(id, status, status_changed_at, employee_user_id, created_at)
values ('0199aaaa-0000-7000-8000-000000000071', 'Inactive', now(), '0199aaaa-0000-7000-8000-000000000082', now());

-- The request, notification and session pages (B-21): an employee and a client in her charge, used only by tenant-requests.spec.ts.
insert into directory.people(id, first_name, last_name, email, created_at)
values ('0199aaaa-0000-7000-8000-000000000091', 'Chiara', 'Lodi', 'chiara.lodi@example.test', now()),
       ('0199aaaa-0000-7000-8000-000000000101', 'Paolo', 'Greco', 'paolo.greco@example.test', now());
insert into identity.users(id, person_id, user_name, email, language_code, is_active, password_format, security_stamp,
                           access_failed_count, lockout_count, created_at)
values ('0199aaaa-0000-7000-8000-000000000092', '0199aaaa-0000-7000-8000-000000000091', 'chiara.lodi',
        'chiara.lodi@example.test', 'it', true, 'Identity', '00000000000000000000000000000091', 0, 0, now()),
       ('0199aaaa-0000-7000-8000-000000000102', '0199aaaa-0000-7000-8000-000000000101', 'paolo.greco',
        'paolo.greco@example.test', 'it', true, 'Identity', '00000000000000000000000000000101', 0, 0, now());
insert into identity.user_roles(user_id, role)
values ('0199aaaa-0000-7000-8000-000000000092', 'Employee'), ('0199aaaa-0000-7000-8000-000000000102', 'Client');
insert into directory.client_profiles(id, status, status_changed_at, employee_user_id, created_at)
values ('0199aaaa-0000-7000-8000-000000000101', 'Inactive', now(), '0199aaaa-0000-7000-8000-000000000092', now());
SQL

if [[ -z "$web_secret" || -z "$console_secret" ]]; then
  echo "prepare: auxctl did not print the client secrets" >&2
  exit 1
fi

umask 077
cat > "$here/.env.e2e" <<EOF
AUXILIA_WEB_CLIENT_ID=e2e-web
AUXILIA_WEB_CLIENT_SECRET=$web_secret
AUXILIA_CONSOLE_CLIENT_ID=e2e-console
AUXILIA_CONSOLE_CLIENT_SECRET=$console_secret
E2E_CATALOG_CONNECTION=$ConnectionStrings__Catalog
EOF
echo "prepare: environment ready ($here/.env.e2e)"
