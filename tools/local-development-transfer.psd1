@{
    SchemaVersion = 1

    RepoAssets = @(
        @{ Path = 'AGENTS.md'; Required = $true; Classification = 'private-context' }
        @{ Path = 'TODO.md'; Required = $true; Classification = 'private-context' }
        @{ Path = 'regulations.md'; Required = $true; Classification = 'private-reference' }
        @{ Path = 'LOCAL_DEVICE_SETUP_HANDOVER.md'; Required = $true; Classification = 'public-guide' }
        @{ Path = 'release.local.json'; Required = $false; Classification = 'private-config' }
        @{ Path = 'mobile/src/ElectronicLogbook.Mobile/wwwroot/hosted-sync.local.json'; Required = $false; Classification = 'secret-config' }
        @{ Path = '.github/*.pem'; Required = $false; Classification = 'secret-key' }
        @{ Path = '.codex/bounded-roadmap-prompt.md'; Required = $true; Classification = 'private-automation' }
        @{ Path = '.codex/bounded-roadmap-result.schema.json'; Required = $true; Classification = 'private-automation' }
        @{ Path = '.codex/hooks.json'; Required = $false; Classification = 'private-automation' }
        @{ Path = '.codex/Invoke-BoundedRoadmapLoop.ps1'; Required = $true; Classification = 'private-automation' }
        @{ Path = '.codex/Request-BoundedRoadmapStop.ps1'; Required = $true; Classification = 'private-automation' }
    )

    LocalAppDataAssets = @(
        @{ Path = 'ElectronicLogbook'; Required = $true; Classification = 'secret-machine-state' }
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
        PostgreSqlMajor = 17
        RecoveryEnvelopeSecretFiles = @('development.env', 'private-pilot.env')
        DebugPackageId = 'com.alphadelta.electroniclogbook.dev'
    }

    ManualCheckpoints = @(
        'Install or sign in to Microsoft 365 and confirm desktop Excel opens.'
        'In Excel Trust Center, enable trusted macros and Trust access to the VBA project object model only for this trusted development environment.'
        'Launch Docker Desktop once, accept its terms, and allow WSL2 setup or a restart if requested.'
        'Run gh auth login on the new device; GitHub authentication is never transferred.'
        'Open the Codex VS Code extension and sign in; Codex authentication and session databases are never transferred.'
        'Confirm Windows has a default HTTPS browser; updater Google sign-in returns through a temporary 127.0.0.1 loopback callback and needs no local Google client secret.'
        'Review and accept Android SDK licenses, then authorize USB debugging on the unlocked Android device.'
        'Restart Windows or the terminal when an installer or environment-variable change requires it.'
    )
}
