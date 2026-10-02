# Log event registry

<!-- Generated from Auxilia.Diagnostics. Do not edit by hand:
     dotnet run --project backend/src/Auxilia.MigrationRunner -- diagnostics registry --output docs/log-event-registry.md -->

Every code is shown as `AUX-NNNNN` in logs, in the `errorCode` of API responses and in the UI (ADR 0006, ADR 0012).
A new code is the current max of its range + 1; codes are never reused or renumbered.

## Ranges

| Range | Name | Owner | Codes | Next code |
|---|---|---|---|---|
| 10000–10999 | Host | Host / Startup / Middleware | 16 | 10025 |
| 11000–11999 | Tenancy | Tenancy / Catalog | 31 | 11035 |
| 12000–12999 | Identity | Identity / Auth | 59 | 12060 |
| 13000–13999 | Directory | Directory (clients, employees) | 33 | 13034 |
| 14000–14999 | Cases | Cases (services, cases, payments) | 0 | 14001 |
| 15000–15999 | Scheduling | Scheduling | 0 | 15001 |
| 16000–16999 | Documents | Documents / Storage | 0 | 16001 |
| 17000–17999 | Requests | Engagement: requests | 0 | 17001 |
| 18000–18999 | Notifications | Engagement: notifications / realtime | 3 | 18004 |
| 19000–19999 | Marketing | Marketing | 0 | 19001 |
| 20000–20999 | Configuration | Configuration | 22 | 20023 |
| 21000–21999 | Localization | Localization | 11 | 21012 |
| 22000–22999 | Imports | Imports | 0 | 22001 |
| 23000–23999 | Bus | Message bus (Rebus / RabbitMQ) | 8 | 23009 |
| 24000–24999 | Cache | Cache / Redis | 4 | 24005 |
| 25000–25999 | Messaging | Messaging (outbound channels, accounts, templates) | 22 | 25023 |
| 26000–26999 | Jobs | Worker / recurring jobs (manual runs) | 4 | 26005 |
| 27000–27999 | Audit | Audit / Reporting / Export | 0 | 27001 |
| 28000–28999 | Runner | MigrationRunner / Legacy import | 5 | 28006 |
| 29000–29999 | Security | Security events | 25 | 29026 |

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
| AUX-10024 | Host.TooManyRequests | – | – | – | – |
| AUX-11004 | Tenancy.CrossTenantAttempt | – | Forbidden | – | – |
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
| AUX-11020 | Tenancy.TenantProvisioningRequested | – | – | Tenancy.RequestProvisioning (success) | – |
| AUX-11021 | Tenancy.TenantUpdated | – | – | Tenancy.UpdateTenant (success) | – |
| AUX-11022 | Tenancy.TenantPlanChanged | – | – | Tenancy.ChangeTenantPlan (success) | – |
| AUX-11023 | Tenancy.TenantModuleOverrideChanged | – | – | Tenancy.ChangeModuleOverride (success) | – |
| AUX-11024 | Tenancy.PlanNotFound | – | NotFound | – | – |
| AUX-11025 | Tenancy.ModuleNotFound | – | NotFound | – | – |
| AUX-11026 | Tenancy.CoreModuleNotConfigurable | – | Validation | – | – |
| AUX-11027 | Tenancy.ProvisioningNotDispatched | Warning | – | – | The provisioning of tenant {TenantSlug} was not queued (message bus unavailable): retry from the console |
| AUX-11028 | Tenancy.TenantNotProvisioning | – | Conflict | – | – |
| AUX-11029 | Tenancy.TenantArchived | – | Conflict | – | – |
| AUX-11030 | Tenancy.TenantLogLevelChanged | – | – | Tenancy.ChangeLogLevel (success) | – |
| AUX-11031 | Tenancy.LogLevelUntilInvalid | – | Validation | – | – |
| AUX-11032 | Tenancy.LogQueryInvalid | – | Validation | – | – |
| AUX-11033 | Tenancy.LogFilesUnavailable | Error | Failure | – | The log files of tenant {TenantSlug} could not be read |
| AUX-11034 | Tenancy.LogLevelSyncFailed | Warning | – | – | The log levels of the tenants could not be synchronised ({Step}): this node keeps the levels it knows |
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
| AUX-12030 | Identity.PlatformUserCreated | – | – | Identity.CreatePlatformUser (success) | – |
| AUX-12031 | Identity.PlatformCredentialsReset | – | – | Identity.ResetPlatformCredentials (success) | – |
| AUX-12032 | Identity.PlatformUserActivationChanged | – | – | Identity.SetPlatformUserActive (success) | – |
| AUX-12033 | Identity.PlatformEnrollmentStarted | – | – | Identity.BeginPlatformEnrollment (success) | – |
| AUX-12034 | Identity.PlatformAccountActivated | – | – | Identity.ActivatePlatformAccount (success) | – |
| AUX-12035 | Identity.PlatformTokensIssued | – | – | Identity.PlatformSignIn (success) | – |
| AUX-12036 | Identity.PlatformTokensRefreshed | – | – | Identity.PlatformRefreshTokens (success) | – |
| AUX-12037 | Identity.PlatformSessionEnded | – | – | Identity.EndPlatformSession (success) | – |
| AUX-12038 | Identity.PlatformTenantTokenIssued | – | – | Identity.IssuePlatformTenantToken (success) | – |
| AUX-12039 | Identity.PlatformUserEmailTaken | – | Conflict | – | – |
| AUX-12040 | Identity.PlatformAccessRequired | – | Forbidden | – | – |
| AUX-12041 | Identity.TwoFactorCodeInvalid | – | Validation | – | – |
| AUX-12042 | Identity.PasswordReused | – | Validation | – | – |
| AUX-12043 | Identity.PasswordExpired | – | Forbidden | – | – |
| AUX-12044 | Identity.CurrentPasswordInvalid | – | Validation | – | – |
| AUX-12045 | Identity.LoginMethodDisabled | – | Validation | – | – |
| AUX-12046 | Identity.LoginOtpRequested | – | – | Identity.RequestLoginOtp (success) | – |
| AUX-12047 | Identity.PasswordChangedByUser | – | – | Identity.ChangePassword (success) | – |
| AUX-12048 | Identity.SignedInWithOtp | – | – | Identity.SignInWithOtp (success) | – |
| AUX-12049 | Identity.ExpiredPasswordChanged | – | – | Identity.ChangeExpiredPassword (success) | – |
| AUX-12050 | Identity.PasswordResetByOperator | – | – | Identity.ResetPasswordByOperator (success) | – |
| AUX-12051 | Identity.UserAmbiguous | – | Conflict | – | – |
| AUX-12052 | Identity.InitialAdministratorCreated | – | – | Identity.CreateInitialAdministrator (success) | – |
| AUX-12053 | Identity.AdministratorAlreadyExists | – | Conflict | – | – |
| AUX-12054 | Identity.InvitationPending | Warning | – | – | The invitation of Administrator {UserId} was not sent ({ErrorCode}): it stays pending |
| AUX-12055 | Identity.AccountAlreadyActivated | – | Conflict | – | – |
| AUX-12056 | Identity.RolePermissionsChanged | – | – | Identity.SetRolePermissions (success) | – |
| AUX-12057 | Identity.RolePermissionsReset | – | – | Identity.ResetRolePermissions (success) | – |
| AUX-12058 | Identity.RolePermissionsInvalid | – | Validation | – | – |
| AUX-12059 | Identity.UserAccountUpdated | – | – | Identity.UpdateUserAccount (success) | – |
| AUX-13001 | Directory.SpecializationCreated | – | – | Directory.CreateSpecialization (success) | – |
| AUX-13002 | Directory.SpecializationUpdated | – | – | Directory.UpdateSpecialization (success) | – |
| AUX-13003 | Directory.SpecializationDeactivated | – | – | Directory.DeactivateSpecialization (success) | – |
| AUX-13004 | Directory.SpecializationInvalid | – | Validation | – | – |
| AUX-13005 | Directory.SpecializationNotFound | – | NotFound | – | – |
| AUX-13006 | Directory.SpecializationNameTaken | – | Conflict | – | – |
| AUX-13007 | Directory.SpecializationMembersAdded | – | – | Directory.AddSpecializationMembers (success) | – |
| AUX-13008 | Directory.SpecializationMemberRemoved | – | – | Directory.RemoveSpecializationMember (success) | – |
| AUX-13009 | Directory.SpecializationMemberInvalid | – | Validation | – | – |
| AUX-13010 | Directory.ClientCreated | – | – | Directory.CreateClient (success) | – |
| AUX-13011 | Directory.ClientUpdated | – | – | Directory.UpdateClient (success) | – |
| AUX-13012 | Directory.ClientDeleted | – | – | Directory.DeleteClient (success) | – |
| AUX-13013 | Directory.ClientSignInChanged | – | – | Directory.ChangeClientSignIn (success) | – |
| AUX-13014 | Directory.ClientAssignmentChanged | – | – | Directory.ChangeClientAssignment (success) | – |
| AUX-13015 | Directory.ClientSpecializationsChanged | – | – | Directory.ChangeClientSpecializations (success) | – |
| AUX-13016 | Directory.PersonInvalid | – | Validation | – | – |
| AUX-13017 | Directory.ClientNotFound | – | NotFound | – | – |
| AUX-13018 | Directory.FiscalCodeTaken | – | Conflict | – | – |
| AUX-13019 | Directory.ClientEmployeeRequired | – | Conflict | – | – |
| AUX-13020 | Directory.EmployeeInvalid | – | Validation | – | – |
| AUX-13021 | Directory.ClientSpecializationInvalid | – | Validation | – | – |
| AUX-13022 | Directory.EmployeeCreated | – | – | Directory.CreateEmployee (success) | – |
| AUX-13023 | Directory.EmployeeUpdated | – | – | Directory.UpdateEmployee (success) | – |
| AUX-13024 | Directory.EmployeeDeleted | – | – | Directory.DeleteEmployee (success) | – |
| AUX-13025 | Directory.EmployeeSignInChanged | – | – | Directory.ChangeEmployeeSignIn (success) | – |
| AUX-13026 | Directory.DefaultEmployeeChanged | – | – | Directory.ChangeDefaultEmployee (success) | – |
| AUX-13027 | Directory.EmployeeSpecializationsChanged | – | – | Directory.ChangeEmployeeSpecializations (success) | – |
| AUX-13028 | Directory.EmployeeAdministratorChanged | – | – | Directory.ChangeEmployeeAdministrator (success) | – |
| AUX-13029 | Directory.EmployeeNotFound | – | NotFound | – | – |
| AUX-13030 | Directory.EmployeeIsDefault | – | Conflict | – | – |
| AUX-13031 | Directory.DefaultEmployeeInactive | – | Conflict | – | – |
| AUX-13032 | Directory.EmployeeSpecializationInvalid | – | Validation | – | – |
| AUX-13033 | Directory.AdministratorInvalid | – | Validation | – | – |
| AUX-18001 | Notifications.RealtimeConnected | Debug | – | – | Realtime connection {ConnectionId} of user {UserId} joined its groups |
| AUX-18002 | Notifications.RealtimeConnectionRejected | Warning | – | – | Realtime connection {ConnectionId} rejected ({Reason}) |
| AUX-18003 | Notifications.RealtimePushFailed | Warning | – | – | Realtime push {EventName} to {Target} failed |
| AUX-20001 | Configuration.SettingNotFound | – | NotFound | – | – |
| AUX-20002 | Configuration.SettingScopeNotAllowed | – | Validation | – | – |
| AUX-20003 | Configuration.SettingValueInvalid | – | Validation | – | – |
| AUX-20004 | Configuration.StoredSettingIgnored | Warning | – | – | Stored value of setting {SettingKey} at level {SettingLevel} is not valid and is ignored |
| AUX-20005 | Configuration.SettingChanged | – | – | Configuration.SetSetting (success) | – |
| AUX-20006 | Configuration.SettingReset | – | – | Configuration.ResetSetting (success) | – |
| AUX-20007 | Configuration.BrandingImageInvalid | – | Validation | – | – |
| AUX-20008 | Configuration.BrandingImageTooLarge | – | Validation | – | – |
| AUX-20009 | Configuration.BrandingAssetNotFound | – | NotFound | – | – |
| AUX-20010 | Configuration.BrandingAssetChanged | – | – | Configuration.SetBrandingAsset (success) | – |
| AUX-20011 | Configuration.BrandingAssetRemoved | – | – | Configuration.RemoveBrandingAsset (success) | – |
| AUX-20012 | Configuration.CustomFieldCreated | – | – | Configuration.CreateCustomField (success) | – |
| AUX-20013 | Configuration.CustomFieldUpdated | – | – | Configuration.UpdateCustomField (success) | – |
| AUX-20014 | Configuration.CustomFieldDeleted | – | – | Configuration.DeleteCustomField (success) | – |
| AUX-20015 | Configuration.CustomFieldInvalid | – | Validation | – | – |
| AUX-20016 | Configuration.CustomFieldKeyTaken | – | Conflict | – | – |
| AUX-20017 | Configuration.CustomFieldNotFound | – | NotFound | – | – |
| AUX-20018 | Configuration.CustomFieldValuesInvalid | – | Validation | – | – |
| AUX-20019 | Configuration.GridLayoutChanged | – | – | Configuration.SetGridLayout (success) | – |
| AUX-20020 | Configuration.GridLayoutReset | – | – | Configuration.ResetGridLayout (success) | – |
| AUX-20021 | Configuration.GridNotFound | – | NotFound | – | – |
| AUX-20022 | Configuration.GridLayoutInvalid | – | Validation | – | – |
| AUX-21001 | Localization.ResourceKeyNotFound | – | NotFound | – | – |
| AUX-21002 | Localization.ResourceKeyExists | – | Conflict | – | – |
| AUX-21003 | Localization.LanguageNotFound | – | NotFound | – | – |
| AUX-21004 | Localization.ResourceValueInvalid | – | Validation | – | – |
| AUX-21005 | Localization.ResourceKeyCreated | – | – | Localization.CreateKey (success) | – |
| AUX-21006 | Localization.ResourceKeyUpdated | – | – | Localization.UpdateKey (success) | – |
| AUX-21007 | Localization.ResourceKeyDeleted | – | – | Localization.DeleteKey (success) | – |
| AUX-21008 | Localization.TranslationSet | – | – | Localization.SetTranslation (success) | – |
| AUX-21009 | Localization.TranslationRemoved | – | – | Localization.RemoveTranslation (success) | – |
| AUX-21010 | Localization.MissingKey | Warning | – | – | Translation key {ResourceKey} has no translation (language {LanguageCode}); the key is shown |
| AUX-21011 | Localization.TranslationNotFound | – | NotFound | – | – |
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
| AUX-25022 | Messaging.TestDeliveryFailed | Warning | Failure | – | Test message {OutboundMessageId} through account {MessagingAccountId} could not be delivered |
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
| AUX-29015 | Security.CrossTenantAttempt | Warning | – | – | Cross-tenant attempt: claim tenant {ClaimTenant}, requested tenant {RequestedTenant} |
| AUX-29016 | Security.RateLimitExceeded | Warning | – | – | Rate limit {Policy} exceeded by {PartitionKind} on {RequestMethod} {RequestPath} |
| AUX-29017 | Security.PlatformLoginFailed | Warning | – | – | Platform sign-in failed ({Reason}) for platform user {PlatformUserId} |
| AUX-29018 | Security.PlatformAccountLockedOut | Warning | – | – | Platform user {PlatformUserId} locked out until {LockoutEnd} |
| AUX-29019 | Security.PlatformSignedIn | Information | – | – | Platform user {PlatformUserId} signed in (session {SessionId}) |
| AUX-29020 | Security.PlatformTenantAccess | Information | – | – | Platform user {PlatformUserId} opened tenant {TenantSlug} |
| AUX-29021 | Security.PlatformCredentialsChanged | Information | – | – | Credentials of platform user {PlatformUserId} {Change} |
| AUX-29022 | Security.LoginOtpSent | Information | – | – | Sign-in code e-mailed to user {UserId} |
| AUX-29023 | Security.PasswordResetByOperator | Warning | – | – | Password of user {UserId} reset by an operator ({Mode}) |
| AUX-29024 | Security.RolePermissionsChanged | Warning | – | – | Permissions of role {Role} changed: granted {Granted}, revoked {Revoked} |
| AUX-29025 | Security.TenantLogLevelChanged | Warning | – | – | Debug logging of tenant {TenantSlug} set until {DebugUntil} (empty: disabled) |
