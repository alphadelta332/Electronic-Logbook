@{
    SchemaVersion = 1

    RepoAssets = @(
        @{ Path = 'AGENTS.md'; Required = $true; Classification = 'private-context' }
        @{ Path = 'TODO.md'; Required = $true; Classification = 'private-context' }
        @{ Path = 'docs/local/*.md'; Required = $false; Classification = 'private-reference' }
        @{ Path = 'regulations.md'; Required = $true; Classification = 'private-reference' }
        @{ Path = 'LOCAL_DEVICE_SETUP_HANDOVER.md'; Required = $true; Classification = 'public-guide' }
        @{ Path = 'release.local.json'; Required = $false; Classification = 'private-config' }
        @{ Path = 'mobile/src/ElectronicLogbook.Mobile/wwwroot/hosted-sync.local.json'; Required = $false; Classification = 'secret-config' }
        @{ Path = 'mobile/android/app/google-services.json'; Required = $true; Classification = 'private-config' }
        @{ Path = '.github/*.pem'; Required = $false; Classification = 'secret-key' }
        @{ Path = '.codex/bounded-roadmap-prompt.md'; Required = $true; Classification = 'private-automation' }
        @{ Path = '.codex/bounded-roadmap-result.schema.json'; Required = $true; Classification = 'private-automation' }
        @{ Path = '.codex/hooks.json'; Required = $false; Classification = 'private-automation' }
        @{ Path = '.codex/Invoke-BoundedRoadmapLoop.ps1'; Required = $true; Classification = 'private-automation' }
        @{ Path = '.codex/Request-BoundedRoadmapStop.ps1'; Required = $true; Classification = 'private-automation' }
    )

    LocalAppDataAssets = @(
        @{ Path = 'ElectronicLogbook/AndroidSigning/electronic-logbook-development.json'; Required = $true; Classification = 'signing-metadata'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/AndroidSigning/electronic-logbook-development.keystore'; Required = $true; Classification = 'secret-signing-identity'; Lifecycle = 'local-transfer' }
        # These permanent Preview filenames are retained legacy cryptographic identifiers.
        @{ Path = 'ElectronicLogbook/AndroidSigning/flightlogx-pilot.keystore'; Required = $true; Classification = 'secret-signing-identity'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/AndroidSigning/flightlogx-pilot-credentials.json'; Required = $true; Classification = 'secret-signing-credentials'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/AndroidSigning/flightlogx-pilot-signing.json'; Required = $true; Classification = 'signing-metadata'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/Google Auth/webclientid.txt'; Required = $false; Classification = 'public-oauth-identifier'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/Google Auth/androidclientid.txt'; Required = $false; Classification = 'public-oauth-identifier'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/Google Auth/previewandroidclientid.txt'; Required = $false; Classification = 'public-oauth-identifier'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/Resend/privatepilotauthdevapi.txt'; Required = $true; Classification = 'secret-api-credential'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/Resend/privatepilotauthapi.txt'; Required = $true; Classification = 'secret-api-credential'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/Supabase/access-token.txt'; Required = $true; Classification = 'secret-api-credential'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/Supabase/hosted-preview-projects.local.json'; Required = $false; RequirementGroup = 'hosted-project-metadata'; Classification = 'private-project-metadata'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/Supabase/hosted-pilot-projects.local.json'; Required = $false; RequirementGroup = 'hosted-project-metadata'; Classification = 'private-project-metadata-compatibility'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/Supabase/recovery-envelope/development.env'; Required = $true; Classification = 'secret-recovery-configuration'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/Supabase/recovery-envelope/private-pilot.env'; Required = $true; Classification = 'secret-recovery-configuration'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/ParticipantHandoffs'; Required = $false; Classification = 'private-participant-handoff'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/HostedBackup/recipient.pem'; Required = $false; Classification = 'public-backup-recipient'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/HostedBackup/recovery-receipt.json'; Required = $false; Classification = 'private-backup-verification'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/HostedBackup/r2-credentials.local.json'; Required = $false; Classification = 'secret-backup-storage-credentials'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/HostedBackup/source-connection.local.json'; Required = $false; Classification = 'secret-backup-source-credentials'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/HostedBackup/source-root.crt'; Required = $false; Classification = 'public-backup-source-ca'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/HostedBackup/daily-review.local.json'; Required = $false; Classification = 'private-backup-owner-review'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/HostedBackup/alert-delivery.local.json'; Required = $false; Classification = 'private-backup-alert-recipient'; Lifecycle = 'local-transfer' }
    )

    # Known local trees that must stay outside the operational transfer archive. This is
    # policy documentation consumed by transfer validation, not a deletion list.
    LocalAppDataExclusions = @(
        @{ Path = 'ElectronicLogbook/HostedBackup/github-plan-read-token.txt'; Lifecycle = 'fresh-authentication'; Reason = 'Recreate the dedicated Plan read token after transfer; never transfer monitoring authentication or reuse the broader CLI login.' }
        @{ Path = 'ElectronicLogbook/HostedBackup/github-actions-read-token.txt'; Lifecycle = 'fresh-authentication'; Reason = 'Reprovision Actions Read and required Metadata Read for only the private backup repository; never transfer the token or substitute Plan/owner credentials.' }
        @{ Path = 'ElectronicLogbook/HostedBackup/supabase-metadata-read-token.txt'; Lifecycle = 'fresh-authentication'; Reason = 'Reprovision only Preview-scoped organization settings and Edge Functions read access; never transfer monitoring authentication or reuse the owner token.' }
        @{ Path = 'ElectronicLogbook/HostedBackup/supabase-organization-read-token.txt'; Lifecycle = 'fresh-authentication'; Reason = 'Reprovision only organization settings read on the selected organization; no project capabilities, token transfer or owner credential substitution.' }
        @{ Path = 'ElectronicLogbook/HostedBackup/cloudflare-monitoring-read-token.txt'; Lifecycle = 'fresh-authentication'; Reason = 'Reprovision only Account Analytics Read and Workers R2 Storage Read on the backup account; no edit permissions or token transfer.' }
        @{ Path = 'ElectronicLogbook/HostedBackup/cloudflare-monitoring-credentials.local.json'; Lifecycle = 'fresh-authentication'; Reason = 'Owner-retained metadata/API/S3 credentials for the same read-only identity; never transfer or use as backup upload/restore credentials.' }
        @{ Path = 'ElectronicLogbook/AnalysisTools'; Lifecycle = 'regenerated-dependency'; Reason = 'Installed analysis packages and source caches are reproducible and machine-specific.' }
        @{ Path = 'ElectronicLogbook/AndroidDeviceBridge'; Lifecycle = 'deliberate-exclusion'; Reason = 'Device-specific IndexedDB backups remain on the source machine.' }
        @{ Path = 'ElectronicLogbook/Evidence'; Lifecycle = 'regenerated-output'; Reason = 'Generated local evidence is not an operational prerequisite.' }
        @{ Path = 'ElectronicLogbook/Temp'; Lifecycle = 'regenerated-output'; Reason = 'Temporary APK copies and extracted receipt-verification sources are reproducible diagnostic outputs, not development configuration.' }
        @{ Path = 'ElectronicLogbook/Gate1RetainedState'; Lifecycle = 'deliberate-exclusion'; Reason = 'Retained-device recovery snapshots must not be copied as configuration.' }
        @{ Path = 'ElectronicLogbook/Google Auth/client_secret_*.json'; Lifecycle = 'deliberate-exclusion'; Reason = 'Google client-secret downloads are not consumed by the updater or Android build.' }
        @{ Path = 'ElectronicLogbook/Google Auth/webclientsecret.txt'; Lifecycle = 'deliberate-exclusion'; Reason = 'The updater uses browser sign-in without a local Google client secret.' }
        @{ Path = 'ElectronicLogbook/Recovery Codes'; Lifecycle = 'deliberate-exclusion'; Reason = 'User recovery artifacts are not development credentials and require separate protected handling.' }
    )

    # State outside the transfer roots. These entries document security-sensitive local
    # prerequisites that must be recreated rather than archived.
    ExternalLocalStateExclusions = @(
        @{ Path = '%APPDATA%/xdg.config/.wrangler'; Lifecycle = 'fresh-authentication'; Reason = 'Cloudflare OAuth credentials must be recreated with wrangler login, never transferred.' }
        @{ Path = '%USERPROFILE%/.android/avd'; Lifecycle = 'deliberate-exclusion'; Reason = 'Android virtual devices and snapshots can contain live Google sessions; recreate and authenticate the recovery AVD once on each trusted development machine.' }
        @{ Path = 'GitHub/alphadelta332/flightlogx-preview-backups/ActionsSecrets'; Lifecycle = 'fresh-authentication'; Reason = 'Reprovision scoped source/upload/readback job secrets through GitHub; never archive them or copy owner/recovery credentials into the job.' }
        @{ Path = 'Cloudflare/flightlogx-backup-controls'; Lifecycle = 'fresh-authentication'; Reason = 'Reprovision the reservation token hash and approved read-only monitoring secrets; preserve the existing DAILY_ATTEMPTS SQLite namespace and its counter across deployments, never reset it during transfer.' }
        @{ Path = 'Cloudflare/flightlogx-backup-watchdog'; Lifecycle = 'fresh-authentication'; Reason = 'Reprovision only the existing restricted Resend sender and private recipient config; preserve the separate MISSING_MONITOR namespace, stable object, monitor-state and monitor-heartbeat across deployments/rotation. Recreate the private named service binding; never copy remote SQLite or enable schedules during transfer.' }
        @{ Path = '%TEMP%/elb-daily-controls-*'; Lifecycle = 'regenerated-output'; Reason = 'Synthetic local Cloudflare SQLite test evidence is retained locally, never transferred as operational state.' }
        @{ Path = '%TEMP%/elb-daily-watchdog-*'; Lifecycle = 'regenerated-output'; Reason = 'Synthetic isolated observer SQLite and local Worker harnesses are retained diagnostic output, not transferable operational state.' }
    )

    CodexAssets = @(
        @{ Path = 'skills/graphify'; Required = $false; Classification = 'custom-skill' }
    )

    ExcludedPathPatterns = @(
        '*/backup-private.encrypted.pem'
        '*/recovery.elbbackup'
        '*/bin/*'
        '*/obj/*'
        '*/node_modules/*'
        '*/build/*'
        '*/.gradle/*'
        '*/artifacts/*'
        '*/graphify-out/*'
        '*/bounded-roadmap-runs/*'
        '*/TestResults/*'
        '*/__pycache__/*'
        '*.pyc'
        '*/local.properties'
    )

    ForbiddenBundlePatterns = @(
        '*/auth.json'
        '*/sessions/*'
        '*/plugins/cache/*'
        '*/cache/*'
        '*/attachments/*'
        '*/generated_images/*'
        '*/logs*.sqlite*'
        '*/state*.sqlite*'
        '*/memories*.sqlite*'
        '*/goals*.sqlite*'
    )

    WingetPackages = @(
        @{ Id = 'Git.Git'; Name = 'Git'; Required = $true }
        @{ Id = 'GitHub.cli'; Name = 'GitHub CLI'; Required = $false }
        @{ Id = 'Microsoft.PowerShell'; Name = 'PowerShell 7'; Required = $false }
        @{ Id = 'Microsoft.VisualStudioCode'; Name = 'Visual Studio Code'; Required = $true }
        @{ Id = 'Microsoft.DotNet.SDK.10'; Name = '.NET SDK 10'; Required = $true }
        @{ Id = 'Microsoft.DotNet.SDK.8'; Name = '.NET SDK 8'; Required = $true }
        @{ Id = 'OpenJS.NodeJS.LTS'; Name = 'Node.js LTS'; Required = $true }
        @{ Id = '7zip.7zip'; Name = '7-Zip'; Required = $true }
        @{ Id = 'Docker.DockerDesktop'; Name = 'Docker Desktop'; Required = $false }
        @{ Id = 'EclipseAdoptium.Temurin.21.JDK'; Name = 'Temurin JDK 21'; Required = $true }
        @{ Id = 'Google.PlatformTools'; Name = 'Android Platform Tools'; Required = $true }
        @{ Id = 'Google.AndroidStudio'; Name = 'Android Studio'; Required = $false }
        @{ Id = 'PostgreSQL.PostgreSQL.17'; Name = 'PostgreSQL 17'; Required = $true }
        @{ Id = 'astral-sh.uv'; Name = 'uv'; Required = $false }
        @{ Id = 'Python.Python.3.14'; Name = 'Python 3.14'; Required = $false }
    )

    NpmGlobalPackages = @(
        @{ Package = 'wrangler@4.147.0'; Command = 'wrangler'; Required = $false }
        @{ Package = 'supabase@2.111.0'; Command = 'supabase'; Required = $true }
        @{ Package = 'firebase-tools@15.28.2'; Command = 'firebase'; Required = $true }
        @{ Package = '@openai/codex'; Command = 'codex'; Required = $false }
    )

    UvTools = @(
        @{ Package = 'graphifyy'; Command = 'graphify'; Required = $false }
    )

    VsCodeExtensions = @(
        @{ Id = 'ms-dotnettools.csdevkit'; Required = $true }
        @{ Id = 'ms-dotnettools.csharp'; Required = $true }
        @{ Id = 'ms-vscode.powershell'; Required = $true }
        @{ Id = 'openai.chatgpt'; Required = $false }
        @{ Id = 'yzhang.markdown-all-in-one'; Required = $false }
    )

    Environment = @{
        AndroidSdkRelativeToLocalAppData = 'Android\Sdk'
        JavaInstallRoot = 'C:\Program Files\Eclipse Adoptium'
        JavaDirectoryPattern = 'jdk-21*'
        UserPathEntries = @(
            '%JAVA_HOME%\bin'
            '%ANDROID_HOME%\platform-tools'
            '%ANDROID_HOME%\cmdline-tools\latest\bin'
            '%USERPROFILE%\.local\bin'
            '%APPDATA%\npm'
            'C:\Program Files\PostgreSQL\17\bin'
        )
    }

    Expected = @{
        DotNetSdkMajors = @(10, 8)
        NodeMajor = 24
        JavaMajor = 21
        AndroidPlatform = 'android-36'
        AndroidBuildTools = '35.0.0'
        AndroidRecoverySystemImage = 'system-images;android-35;google_apis_playstore;x86_64'
        AndroidRecoveryAvdName = 'ElectronicLogbook_Pixel_Play_API35'
        AndroidRecoverySnapshotName = 'flightlogx_google_authenticated_clean_v1'
        AndroidRecoveryResetScript = 'tools/Reset-FlightLogXAndroidRecoveryEmulator.ps1'
        SupabaseVersion = '2.111.0'
        FirebaseCliVersion = '15.28.2'
        WranglerVersion = '4.147.0'
        FirebaseProjectId = 'flightlogx-private-pilot'
        FirebaseAndroidPackageName = 'com.alphadelta.electroniclogbook'
        FirebaseAndroidBuildVariant = 'preview'
        HostedProjectMetadataFile = 'hosted-preview-projects.local.json'
        LegacyHostedProjectMetadataFile = 'hosted-pilot-projects.local.json'
        # The filenames are permanent legacy identifiers for the already-distributed signing identity.
        PreviewSigningKeystoreFile = 'flightlogx-pilot.keystore'
        PreviewSigningCredentialsFile = 'flightlogx-pilot-credentials.json'
        PreviewSigningMetadataFile = 'flightlogx-pilot-signing.json'
        OwnerEnrollmentScript = 'tools/Add-FlightLogXParticipant.ps1'
        AirportCatalogPublisherScript = 'tools/Publish-AirportCatalog.ps1'
        AirportCatalogPublisherSecretName = 'AIRPORT_CATALOG_PUBLISH_TOKEN'
        AirportCatalogSchedulerSource = 'supabase/activity-worker/worker.mjs'
        ParticipantHandoffDirectory = 'ElectronicLogbook\ParticipantHandoffs'
        PostgreSqlMajor = 17
        PostgreSqlBackupTools = @('pg_dump', 'pg_dumpall', 'pg_restore', 'initdb', 'pg_ctl')
        HostedBackupScript = 'tools/hosted-backup/cli.mjs'
        HostedBackupReplayProject = 'tools/hosted-backup/ReplayCheck/ReplayCheck.csproj'
        HostedBackupCompatibilityContract = 'tools/hosted-backup/preview-logical-contract.json'
        HostedBackupCompatibilityTestScript = 'tools/hosted-backup/preview-compatibility.integration.test.mjs'
        HostedBackupLogicalExportScript = 'tools/hosted-backup/supervised-logical-export.mjs'
        HostedBackupLogicalClientScript = 'tools/hosted-backup/logical-client.mjs'
        HostedBackupLogicalRestoreScript = 'tools/hosted-backup/logical-restore.mjs'
        HostedBackupDailyScript = 'tools/hosted-backup/daily-backup.mjs'
        HostedBackupDailyTestScript = 'tools/hosted-backup/daily-backup.test.mjs'
        HostedBackupDailyWorkflow = 'tools/hosted-backup/daily-workflow.json'
        HostedBackupDailyControls = @('tools/hosted-backup/daily-controls.mjs', 'tools/hosted-backup/daily-controls-worker.mjs', 'tools/hosted-backup/daily-controls.wrangler.json', 'tools/hosted-backup/daily-controls.test.mjs', 'tools/hosted-backup/daily-controls.integration.test.mjs', 'tools/hosted-backup/daily-cost-policy.mjs', 'tools/hosted-backup/daily-cost-policy.test.mjs', 'tools/hosted-backup/daily-request-controls.mjs')
        HostedBackupProviderCollector = @('tools/hosted-backup/daily-provider-collector.mjs', 'tools/hosted-backup/daily-provider-collector.test.mjs', 'docs/hosted-backup-provider-collector.md')
        HostedBackupMonitor = @('tools/hosted-backup/daily-monitor.mjs', 'tools/hosted-backup/daily-monitor.test.mjs', 'tools/hosted-backup/daily-monitor-observation.mjs', 'tools/hosted-backup/daily-monitor-observation.test.mjs', 'tools/hosted-backup/daily-alert-delivery.mjs', 'tools/hosted-backup/daily-alert-delivery.test.mjs', 'tools/hosted-backup/daily-watchdog.mjs', 'tools/hosted-backup/daily-watchdog-worker.mjs', 'tools/hosted-backup/daily-watchdog.wrangler.json', 'tools/hosted-backup/daily-watchdog.test.mjs', 'tools/hosted-backup/daily-watchdog.integration.test.mjs', 'docs/hosted-backup-monitor.md')
        HostedBackupRecoveryScript = 'tools/hosted-backup/Initialize-Recovery.ps1'
        HostedBackupRecoveryPowerShellMinVersion = '7.2'
        HostedBackupR2AccessScript = 'tools/hosted-backup/Initialize-R2Access.ps1'
        HostedBackupR2Policy = 'tools/hosted-backup/r2-policy.json'
        HostedBackupR2CheckScript = 'tools/hosted-backup/check-r2-access.mjs'
        HostedBackupSourceSetupScript = 'tools/hosted-backup/initialize-source.mjs'
        HostedBackupSupervisedExportScript = 'tools/hosted-backup/supervised-export.mjs'
        HostedBackupSupervisedVerifyScript = 'tools/hosted-backup/Test-SupervisedBackup.ps1'
        HostedBackupCurlCommand = 'curl.exe'
        ResendApiKeyFiles = @('privatepilotauthdevapi.txt', 'privatepilotauthapi.txt')
        RecoveryEnvelopeSecretFiles = @('development.env', 'private-pilot.env')
        DebugPackageId = 'com.alphadelta.electroniclogbook.dev'
    }

    ManualCheckpoints = @(
        'Install or sign in to Microsoft 365 and confirm desktop Excel opens.'
        'In Excel Trust Center, enable trusted macros and Trust access to the VBA project object model only for this trusted development environment.'
        'Launch Docker Desktop once, accept its terms, and allow WSL2 setup or a restart if requested.'
        'Run gh auth login on the new device; GitHub authentication is never transferred.'
        'Run firebase login on the new device; Firebase authentication is never transferred.'
        'For Supabase activity/airport scheduler maintenance, install wrangler@4.147.0 and run wrangler login; Cloudflare authentication is never transferred. Regenerate ignored Worker configuration from verified project metadata. Publish-AirportCatalog rotates the remote publisher-only secret in Supabase Edge secrets and Cloudflare; never transfer that secret.'
        'Open the Codex VS Code extension and sign in; Codex authentication and session databases are never transferred.'
        'Confirm Windows has a default HTTPS browser; updater Google sign-in returns through a temporary 127.0.0.1 loopback callback and needs no local Google client secret.'
        'Confirm the Google OAuth project that owns the Web client also has separate Android clients for every active package/signing-certificate pair; preserve existing clients when adding the permanent Preview identity.'
        'Review and accept Android SDK licenses, then pair the unlocked retained Android device with this computer using Wireless debugging on the same Wi-Fi network. Discover/reconnect with adb mdns services and adb connect; use USB only when explicitly chosen. Verify the hardware serial before installing.'
        'For repeated Google recovery tests, create the documented Google Play API 35 AVD, sign in once, and capture its clean machine-local snapshot; never transfer or share that AVD.'
        'Restart Windows or the terminal when an installer or environment-variable change requires it.'
    )
}
