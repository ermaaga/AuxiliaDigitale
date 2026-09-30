# Log event registry

<!-- Generated from Auxilia.Diagnostics. Do not edit by hand:
     dotnet run --project backend/src/Auxilia.MigrationRunner -- diagnostics registry --output docs/log-event-registry.md -->

Every code is shown as `AUX-NNNNN` in logs, in the `errorCode` of API responses and in the UI (ADR 0006, ADR 0012).
A new code is the current max of its range + 1; codes are never reused or renumbered.

## Ranges

| Range | Name | Owner | Codes | Next code |
|---|---|---|---|---|
| 10000–10999 | Host | Host / Startup / Middleware | 15 | 10024 |
| 11000–11999 | Tenancy | Tenancy / Catalog | 16 | 11020 |
| 12000–12999 | Identity | Identity / Auth | 29 | 12030 |
| 13000–13999 | Directory | Directory (clients, employees) | 0 | 13001 |
| 14000–14999 | Cases | Cases (services, cases, payments) | 0 | 14001 |
| 15000–15999 | Scheduling | Scheduling | 0 | 15001 |
| 16000–16999 | Documents | Documents / Storage | 0 | 16001 |
| 17000–17999 | Requests | Engagement: requests | 0 | 17001 |
| 18000–18999 | Notifications | Engagement: notifications / realtime | 0 | 18001 |
| 19000–19999 | Marketing | Marketing | 0 | 19001 |
| 20000–20999 | Configuration | Configuration | 6 | 20007 |
| 21000–21999 | Localization | Localization | 0 | 21001 |
| 22000–22999 | Imports | Imports | 0 | 22001 |
| 23000–23999 | Bus | Message bus (Rebus / RabbitMQ) | 8 | 23009 |
| 24000–24999 | Cache | Cache / Redis | 4 | 24005 |
| 25000–25999 | Messaging | Messaging (outbound channels, accounts, templates) | 21 | 25022 |
| 26000–26999 | Jobs | Worker / recurring jobs (manual runs) | 4 | 26005 |
| 27000–27999 | Audit | Audit / Reporting / Export | 0 | 27001 |
| 28000–28999 | Runner | MigrationRunner / Legacy import | 5 | 28006 |
| 29000–29999 | Security | Security events | 14 | 29015 |

## Codes

| Code | Event name | Log level | Error type | Operation | Message |
|---|---|---|---|---|---|
| AUX-10001 | Host.UnhandledException | Error | Failure | – | Unhandled exception while processing {RequestMethod} {RequestPath} |
| AUX-10010 | Host.ConcurrencyConflict | Warning | Conflict | – | Concurrency conflict in operation {Operation} |
| AUX-10011 | Host.PreconditionFailed | Warning | PreconditionFailed | – | If-Match precondition failed for {RequestMethod} {RequestPath} |
| AUX-10012 | Host.IdempotencyKeyReused | Warning | Conflict | – | Idempotency key reused with a different payload for {RequestMethod} {RequestPath} |
| AUX-10013 | Host.DatabaseTimeout | Error | Failure | – | Database command timed out in operation {Operation} |
| AUX-10014 | Host.RequestCancelled | Information | – | – | Request {RequestMethod} {RequestPath} cancelled by the client |
| AUX-10015 | Host.LogStorageUnavailable | – | – | – | – |
| AUX-10016 | Host.LogStorageRecovered | – | – | – | – |
| AUX-10017 | Host.EndpointNotFound | – | – | – | – |
| AUX-10018 | Host.MethodNotAllowed | – | – | – | – |
| AUX-10019 | Host.RequestInvalid | – | – | – | – |
| AUX-10020 | Host.ValidationFailed | – | Validation | – | – |
| AUX-10021 | Host.PostCommitActionFailed | Warning | – | – | A post-commit action of operation {Operation} failed |
| AUX-10022 | Host.AuthenticationRequired | – | – | – | – |
| AUX-10023 | Host.AccessDenied | – | – | – | – |
| AUX-11004 | Tenancy.CrossTenantAttempt | Warning | Forbidden | – | Cross-tenant attempt: claim tenant {ClaimTenant}, requested tenant {RequestedTenant} |
| AUX-11005 | Tenancy.TenantSlugInvalid | – | Validation | – | – |
| AUX-11006 | Tenancy.TenantTransitionNotAllowed | – | Conflict | – | – |
| AUX-11007 | Tenancy.CatalogValueInvalid | – | Validation | – | – |
| AUX-11008 | Tenancy.TenantRequired | – | Validation | – | – |
| AUX-11009 | Tenancy.TenantNotFound | – | NotFound | – | – |
| AUX-11010 | Tenancy.TenantUnavailable | – | – | – | – |
| AUX-11011 | Tenancy.TenantSuspended | – | – | – | – |
| AUX-11012 | Tenancy.TenantProvisioned | – | – | Tenancy.ProvisionTenant (success) | – |
| AUX-11013 | Tenancy.TenantWasSuspended | – | – | Tenancy.SuspendTenant (success) | – |
| AUX-11014 | Tenancy.TenantWasReactivated | – | – | Tenancy.ReactivateTenant (success) | – |
| AUX-11015 | Tenancy.TenantWasArchived | – | – | Tenancy.ArchiveTenant (success) | – |
| AUX-11016 | Tenancy.TenantAlreadyExists | – | Conflict | – | – |
| AUX-11017 | Tenancy.TenantSlugReserved | – | Validation | – | – |
| AUX-11018 | Tenancy.TenantDatabaseInvalid | – | Failure | – | – |
| AUX-11019 | Tenancy.ModulesSynchronized | – | – | Tenancy.SyncModules (success) | – |
| AUX-12001 | Identity.UserCreated | – | – | Identity.CreateUser (success) | – |
| AUX-12002 | Identity.InvalidCredentials | – | Unauthorized | – | – |
| AUX-12003 | Identity.AccountLocked | – | Forbidden | – | – |
| AUX-12004 | Identity.UserNameTaken | – | Conflict | – | – |
| AUX-12005 | Identity.PasswordTooWeak | – | Validation | – | – |
| AUX-12006 | Identity.UserNotFound | – | NotFound | – | – |
| AUX-12007 | Identity.PasswordChanged | – | – | Identity.SetPassword (success) | – |
| AUX-12008 | Identity.UserActivationChanged | – | – | Identity.SetUserActive (success) | – |
| AUX-12009 | Identity.UserRolesChanged | – | – | Identity.SetUserRoles (success) | – |
| AUX-12010 | Identity.UserAuthenticated | – | – | Identity.AuthenticateUser (success) | – |
| AUX-12011 | Identity.PersonNotFound | – | NotFound | – | – |
| AUX-12012 | Identity.UserValueInvalid | – | Validation | – | – |
| AUX-12013 | Identity.TokensIssued | – | – | Identity.SignIn (success) | – |
| AUX-12014 | Identity.TokensRefreshed | – | – | Identity.RefreshTokens (success) | – |
| AUX-12015 | Identity.RefreshTokenInvalid | – | Unauthorized | – | – |
| AUX-12016 | Identity.ClientInvalid | – | Unauthorized | – | – |
| AUX-12017 | Identity.SessionEnded | – | – | Identity.EndSession (success) | – |
| AUX-12018 | Identity.UserTokenInvalid | – | Validation | – | – |
| AUX-12019 | Identity.ActivationSent | – | – | Identity.SendActivation (success) | – |
| AUX-12020 | Identity.AccountActivated | – | – | Identity.ActivateAccount (success) | – |
| AUX-12021 | Identity.PasswordResetRequested | – | – | Identity.RequestPasswordReset (success) | – |
| AUX-12022 | Identity.PasswordReset | – | – | Identity.ResetPassword (success) | – |
| AUX-12023 | Identity.AccessTokenRevoked | – | – | – | – |
| AUX-12024 | Identity.SigningKeyRotated | – | – | Identity.RotateSigningKey (success) | – |
| AUX-12025 | Identity.UserEmailMissing | – | Validation | – | – |
| AUX-12026 | Identity.ClientApplicationAdded | – | – | Identity.AddClientApplication (success) | – |
| AUX-12027 | Identity.ClientIdTaken | – | Conflict | – | – |
| AUX-12028 | Identity.PermissionDenied | – | Forbidden | – | – |
| AUX-12029 | Identity.PermissionsSynchronized | Information | – | – | Permissions aligned with the modules: {Added} added, {Removed} removed, {Granted} default grants |
| AUX-20001 | Configuration.SettingNotFound | – | NotFound | – | – |
| AUX-20002 | Configuration.SettingScopeNotAllowed | – | Validation | – | – |
| AUX-20003 | Configuration.SettingValueInvalid | – | Validation | – | – |
| AUX-20004 | Configuration.StoredSettingIgnored | Warning | – | – | Stored value of setting {SettingKey} at level {SettingLevel} is not valid and is ignored |
| AUX-20005 | Configuration.SettingChanged | – | – | Configuration.SetSetting (success) | – |
| AUX-20006 | Configuration.SettingReset | – | – | Configuration.ResetSetting (success) | – |
| AUX-23001 | Bus.MessageHandled | – | – | Bus.HandleMessage (success) | – |
| AUX-23002 | Bus.DuplicateMessageSkipped | Information | – | – | Message {MessageId} already handled by {Handler}: skipped |
| AUX-23003 | Bus.MessageTenantMissing | Error | Validation | – | Tenant message {MessageType} {MessageId} has no tenant header |
| AUX-23004 | Bus.MessageTenantUnavailable | Warning | NotFound | – | Tenant {TenantSlug} of message {MessageType} {MessageId} is missing or not active |
| AUX-23005 | Bus.MessageRejected | Warning | – | – | Message {MessageType} {MessageId} rejected by {Handler} with {ErrorCode}: not retried |
| AUX-23006 | Bus.MessageRetryScheduled | Warning | – | – | Message {MessageType} {MessageId} failed; second-level retry {Attempt} in {DelaySeconds} s |
| AUX-23007 | Bus.MessageDeadLettered | Error | – | – | Message {MessageType} {MessageId} moved to the error queue: {Reason} |
| AUX-23008 | Bus.OutboxDispatchFailed | Warning | – | – | Outbox message {OutboxId} ({MessageType}) not sent; it stays pending |
| AUX-24001 | Cache.CacheBackendUnavailable | Warning | – | – | Redis cache unavailable; serving from memory and database for {BreakSeconds} s |
| AUX-24002 | Cache.CacheBackendRecovered | Information | – | – | Redis cache available again |
| AUX-24003 | Cache.InvalidationPublishFailed | Warning | – | – | Invalidation of cache tag {CacheTag} not published to the other nodes |
| AUX-24004 | Cache.InvalidationSubscriptionFailed | Warning | – | – | Subscription to the cache invalidation channel failed |
| AUX-25001 | Messaging.AccountCreated | – | – | Messaging.CreateAccount (success) | – |
| AUX-25002 | Messaging.AccountUpdated | – | – | Messaging.UpdateAccount (success) | – |
| AUX-25003 | Messaging.DefaultAccountChanged | – | – | Messaging.SetDefaultAccount (success) | – |
| AUX-25004 | Messaging.AccountActivationChanged | – | – | Messaging.SetAccountActive (success) | – |
| AUX-25005 | Messaging.SenderRulesChanged | – | – | Messaging.SetSenderRules (success) | – |
| AUX-25006 | Messaging.MessageQueued | – | – | Messaging.QueueMessage (success) | – |
| AUX-25007 | Messaging.MessageSent | – | – | Messaging.DeliverMessage (success) | – |
| AUX-25008 | Messaging.MessageFailed | Warning | – | – | Outbound message {OutboundMessageId} failed permanently with {ErrorCode} |
| AUX-25009 | Messaging.TestMessageSent | – | – | Messaging.SendTestMessage (success) | – |
| AUX-25010 | Messaging.AccountNotFound | – | NotFound | – | – |
| AUX-25011 | Messaging.NoAccountForMessage | – | Failure | – | – |
| AUX-25012 | Messaging.AccountSettingsInvalid | – | Validation | – | – |
| AUX-25013 | Messaging.ChannelNotAvailable | – | Failure | – | – |
| AUX-25014 | Messaging.TemplateNotFound | – | NotFound | – | – |
| AUX-25015 | Messaging.TemplateInvalid | – | Validation | – | – |
| AUX-25016 | Messaging.RecipientInvalid | – | Validation | – | – |
| AUX-25017 | Messaging.DefaultAccountMustBeActive | – | Conflict | – | – |
| AUX-25018 | Messaging.DeliveryAttemptFailed | Warning | – | – | Delivery attempt {Attempt} of outbound message {OutboundMessageId} failed; it will be retried |
| AUX-25019 | Messaging.OutboundMessageNotFound | – | NotFound | – | – |
| AUX-25020 | Messaging.SenderRuleInvalid | – | Validation | – | – |
| AUX-25021 | Messaging.AccountAuthenticationFailed | – | Failure | – | – |
| AUX-26001 | Jobs.JobRunSucceeded | – | – | Jobs.RunJob (success) | – |
| AUX-26002 | Jobs.JobNotFound | – | NotFound | – | – |
| AUX-26003 | Jobs.JobRunFailed | – | Failure | – | – |
| AUX-26004 | Jobs.JobAlreadyRunning | – | Conflict | – | – |
| AUX-28001 | Runner.DataMigrationApplied | Information | – | – | Data-migration {Key} applied in {DurationMs} ms: {Description} |
| AUX-28002 | Runner.DataMigrationFailed | Error | – | – | Data-migration {Key} failed |
| AUX-28003 | Runner.CatalogMigrated | – | – | Runner.MigrateCatalog (success) | – |
| AUX-28004 | Runner.TenantMigrated | – | – | Runner.MigrateTenant (success) | – |
| AUX-28005 | Runner.TenantMigrationFailed | – | Failure | – | – |
| AUX-29001 | Security.LoginFailed | Warning | – | – | Sign-in failed ({Reason}) for user {UserId} |
| AUX-29002 | Security.AccountLockedOut | Warning | – | – | User {UserId} locked out until {LockoutEnd} after {FailedAttempts} failed sign-ins |
| AUX-29003 | Security.LegacyPasswordUpgraded | Information | – | – | Legacy password of user {UserId} rehashed |
| AUX-29004 | Security.RolesChanged | Information | – | – | Roles of user {UserId} changed to {Roles} |
| AUX-29005 | Security.PasswordChanged | Information | – | – | Password of user {UserId} changed |
| AUX-29006 | Security.AccountActivationChanged | Information | – | – | User {UserId} can sign in: {IsActive} |
| AUX-29007 | Security.RefreshTokenReuse | Warning | – | – | Refresh token reused in session {SessionId} of user {UserId}: session revoked |
| AUX-29008 | Security.SessionEnded | Information | – | – | Session {SessionId} of user {UserId} ended: {Reason} |
| AUX-29009 | Security.PasswordResetRequested | Information | – | – | Password reset requested for user {UserId} |
| AUX-29010 | Security.PasswordResetCompleted | Information | – | – | Password of user {UserId} reset with a reset link |
| AUX-29011 | Security.AccountActivated | Information | – | – | User {UserId} activated the account |
| AUX-29012 | Security.ClientRejected | Warning | – | – | Token request rejected for client {ClientId}: {Reason} |
| AUX-29013 | Security.SigningKeyRotated | Information | – | – | Token signing key {KeyId} is now active |
| AUX-29014 | Security.PermissionDenied | Warning | – | – | User {UserId} denied {Permission} ({Reason}) |
