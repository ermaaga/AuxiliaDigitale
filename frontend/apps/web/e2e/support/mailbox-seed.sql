-- Connected accounts with copied data (N05, C-08), owned by tenant-mailbox.spec.ts: run by the global setup before every
-- run (the spec changes a colour and disconnects an account; events follow today's date). What a sync would have
-- copied — headers of a PEC mailbox and events of a Microsoft calendar — stands in for the providers, which the E2E API
-- does not reach (no Worker; reading live and the sync itself are covered by the backend tests with fake providers and a
-- GreenMail IMAP server). The secret is a placeholder: nothing in the suite decrypts it.
insert into directory.people(id, first_name, last_name, email, created_at)
values ('0199aaaa-0000-7000-8000-000000000191', 'Silvia', 'Rinaldi', 'silvia.rinaldi@example.test', now())
on conflict (id) do nothing;
insert into identity.users(id, person_id, user_name, email, language_code, is_active, password_format, security_stamp,
                           access_failed_count, lockout_count, created_at)
values ('0199aaaa-0000-7000-8000-000000000192', '0199aaaa-0000-7000-8000-000000000191', 'silvia.rinaldi',
        'silvia.rinaldi@example.test', 'it', true, 'Identity', '00000000000000000000000000000191', 0, 0, now())
on conflict (id) do nothing;
insert into identity.user_roles(user_id, role) values ('0199aaaa-0000-7000-8000-000000000192', 'Employee')
on conflict do nothing;

-- Folders, headers, events and sync runs go with their account (cascade).
delete from mailbox.connected_accounts where user_id = '0199aaaa-0000-7000-8000-000000000192';

insert into mailbox.connected_accounts(id, user_id, provider, address, display_name, color, has_mail, has_calendar,
                                       granted_scopes, imap_host, imap_port, imap_security, imap_user_name,
                                       protected_secret, status, connected_at, updated_at, mail_synced_at, calendar_synced_at)
values ('0199aaaa-0000-7000-8000-000000000193', '0199aaaa-0000-7000-8000-000000000192', 'Imap', 'studio.rinaldi@pec.it',
        null, 'teal', true, false, null, 'imaps.pec.aruba.it', 993, 'SslOnConnect', 'studio.rinaldi@pec.it',
        'e2e-placeholder', 'Active', now() - interval '7 days', now(), now() - interval '1 hour', null),
       ('0199aaaa-0000-7000-8000-000000000194', '0199aaaa-0000-7000-8000-000000000192', 'Microsoft',
        'silvia.rinaldi@outlook.example', 'Silvia (Outlook)', 'purple', false, true, 'openid offline_access Calendars.Read',
        null, null, null, null, 'e2e-placeholder', 'Active', now() - interval '7 days', now(), null, now() - interval '1 hour');

insert into mailbox.mail_folders(id, account_id, kind, provider_folder_id, name, updated_at)
values ('0199aaaa-0000-7000-8000-000000000195', '0199aaaa-0000-7000-8000-000000000193', 'Inbox', 'INBOX', 'INBOX', now()),
       ('0199aaaa-0000-7000-8000-000000000196', '0199aaaa-0000-7000-8000-000000000193', 'Sent', 'Posta inviata',
        'Posta inviata', now());

insert into mailbox.mail_messages(id, account_id, folder_id, provider_message_id, internet_message_id, thread_id,
                                  from_address, from_name, to_recipients, cc_recipients, subject, sent_at, received_at,
                                  is_read, size, has_attachments, attachment_names, synced_at)
values ('0199aaaa-0000-7000-8000-000000000197', '0199aaaa-0000-7000-8000-000000000193',
        '0199aaaa-0000-7000-8000-000000000195', '1:3', '<e2e-3@example.com>', null, 'mario.rossi@example.com',
        'Mario Rossi', array['studio.rinaldi@pec.it'], array[]::text[], 'Fattura di ottobre', now() - interval '1 hour',
        now() - interval '1 hour', false, 48213, true, array['fattura-ottobre.pdf'], now()),
       ('0199aaaa-0000-7000-8000-000000000198', '0199aaaa-0000-7000-8000-000000000193',
        '0199aaaa-0000-7000-8000-000000000195', '1:2', '<e2e-2@example.com>', null, 'posta-certificata@pec.aruba.it',
        null, array['studio.rinaldi@pec.it'], array[]::text[], 'CONSEGNA: Dichiarazione dei redditi',
        now() - interval '1 day', now() - interval '1 day', true, 9120, true, array['daticert.xml', 'postacert.eml'], now()),
       ('0199aaaa-0000-7000-8000-000000000199', '0199aaaa-0000-7000-8000-000000000193',
        '0199aaaa-0000-7000-8000-000000000195', '1:1', '<e2e-1@example.com>', null, 'giulia.conti@example.com',
        'Giulia Conti', array['studio.rinaldi@pec.it'], array[]::text[], 'Appuntamento di lunedì',
        now() - interval '2 days', now() - interval '2 days', true, 2048, false, array[]::text[], now()),
       ('0199aaaa-0000-7000-8000-00000000019a', '0199aaaa-0000-7000-8000-000000000193',
        '0199aaaa-0000-7000-8000-000000000196', '1:1', '<e2e-sent-1@example.com>', null, 'studio.rinaldi@pec.it', null,
        array['Mario Rossi <mario.rossi@example.com>'], array[]::text[], 'Documenti richiesti', now() - interval '3 hours',
        now() - interval '3 hours', true, 1024, false, array[]::text[], now());

-- Today at noon in the tenant time zone: inside every calendar view of the day.
insert into mailbox.calendar_events(id, account_id, provider_event_id, title, location, starts_at, ends_at, all_day,
                                    tentative, link, synced_at)
values ('0199aaaa-0000-7000-8000-00000000019b', '0199aaaa-0000-7000-8000-000000000194', 'e2e-event-1',
        'Riunione con il commercialista', 'Via Roma 1, Milano',
        ((now() at time zone 'Europe/Rome')::date + time '12:00') at time zone 'Europe/Rome',
        ((now() at time zone 'Europe/Rome')::date + time '13:00') at time zone 'Europe/Rome',
        false, false, 'https://outlook.live.com/calendar/item/e2e-event-1', now());
