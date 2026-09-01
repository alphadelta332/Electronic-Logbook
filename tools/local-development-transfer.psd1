@{
    SchemaVersion = 1

    RepoAssets = @(
        @{ Path = 'AGENTS.md'; Required = $true; Classification = 'private-context' }
        @{ Path = 'TODO.md'; Required = $true; Classification = 'private-context' }
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
        @{ Path = 'ElectronicLogbook/Resend/privatepilotauthdevapi.txt'; Required = $true; Classification = 'secret-api-credential'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/Resend/privatepilotauthapi.txt'; Required = $true; Classification = 'secret-api-credential'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/Supabase/access-token.txt'; Required = $true; Classification = 'secret-api-credential'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/Supabase/hosted-preview-projects.local.json'; Required = $false; RequirementGroup = 'hosted-project-metadata'; Classification = 'private-project-metadata'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/Supabase/hosted-pilot-projects.local.json'; Required = $false; RequirementGroup = 'hosted-project-metadata'; Classification = 'private-project-metadata-compatibility'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/Supabase/recovery-envelope/development.env'; Required = $true; Classification = 'secret-recovery-configuration'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/Supabase/recovery-envelope/private-pilot.env'; Required = $true; Classification = 'secret-recovery-configuration'; Lifecycle = 'local-transfer' }
        @{ Path = 'ElectronicLogbook/ParticipantHandoffs'; Required = $false; Classification = 'private-participant-handoff'; Lifecycle = 'local-transfer' }
    )

    # Known local trees that must stay outside the operational transfer archive. This is
    # policy documentation consumed by transfer validation, not a deletion list.
    LocalAppDataExclusions = @(
        @{ Path = 'ElectronicLogbook/AnalysisTools'; Lifecycle = 'regenerated-dependency'; Reason = 'Installed analysis packages and source caches are reproducible and machine-specific.' }
        @{ Path = 'ElectronicLogbook/AndroidDeviceBridge'; Lifecycle = 'deliberate-exclusion'; Reason = 'Device-specific IndexedDB backups remain on the source machine.' }
        @{ Path = 'ElectronicLogbook/Evidence'; Lifecycle = 'regenerated-output'; Reason = 'Generated local evidence is not an operational prerequisite.' }
        @{ Path = 'ElectronicLogbook/Gate1RetainedState'; Lifecycle = 'deliberate-exclusion'; Reason = 'Retained-device recovery snapshots must not be copied as configuration.' }
        @{ Path = 'ElectronicLogbook/Google Auth/androidclientid.txt'; Lifecycle = 'deliberate-exclusion'; Reason = 'No active local consumer reads this public identifier.' }
        @{ Path = 'ElectronicLogbook/Google Auth/client_secret_*.json'; Lifecycle = 'deliberate-exclusion'; Reason = 'Google client-secret downloads are not consumed by the updater or Android build.' }
        @{ Path = 'ElectronicLogbook/Google Auth/webclientsecret.txt'; Lifecycle = 'deliberate-exclusion'; Reason = 'The updater uses browser sign-in without a local Google client secret.' }
        @{ Path = 'ElectronicLogbook/Recovery Codes'; Lifecycle = 'deliberate-exclusion'; Reason = 'User recovery artifacts are not development credentials and require separate protected handling.' }
    )

    CodexAssets = @(
        @{ Path = 'skills/graphify'; Required = $false; Classification = 'custom-skill' }
    )

    ExcludedPathPatterns = @(
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
        SupabaseVersion = '2.111.0'
        FirebaseCliVersion = '15.28.2'
        FirebaseProjectId = 'flightlogx-private-pilot'
        FirebaseAndroidPackageName = 'com.alphadelta.electroniclogbook'
        HostedProjectMetadataFile = 'hosted-preview-projects.local.json'
        LegacyHostedProjectMetadataFile = 'hosted-pilot-projects.local.json'
        # The filenames are permanent legacy identifiers for the already-distributed signing identity.
        PreviewSigningKeystoreFile = 'flightlogx-pilot.keystore'
        PreviewSigningCredentialsFile = 'flightlogx-pilot-credentials.json'
        PreviewSigningMetadataFile = 'flightlogx-pilot-signing.json'
        OwnerEnrollmentScript = 'tools/Add-FlightLogXParticipant.ps1'
        ParticipantHandoffDirectory = 'ElectronicLogbook\ParticipantHandoffs'
        PostgreSqlMajor = 17
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
        'Open the Codex VS Code extension and sign in; Codex authentication and session databases are never transferred.'
        'Confirm Windows has a default HTTPS browser; updater Google sign-in returns through a temporary 127.0.0.1 loopback callback and needs no local Google client secret.'
        'Review and accept Android SDK licenses, then authorize USB debugging on the unlocked Android device.'
        'Restart Windows or the terminal when an installer or environment-variable change requires it.'
    )
}
