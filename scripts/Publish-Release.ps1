param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Version,
    [string]$ProjectPath = "./UsbFileSync.App/UsbFileSync.App.csproj",
    [string]$ArtifactsRoot = "./artifacts/release",
    [switch]$SelfContained = $true,
    [switch]$PublishSingleFile = $true,
    [switch]$Sign,
    [string]$SigningCertificatePath,
    [string]$SigningCertificatePassword,
    [string]$TimestampServer = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

function Resolve-SignToolPath {
    $roots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "$env:ProgramFiles\Windows Kits\10\bin"
    ) | Where-Object { $_ -and (Test-Path $_) }

    foreach ($root in $roots) {
        $candidate = Get-ChildItem -Path $root -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($null -ne $candidate) {
            return $candidate.FullName
        }
    }

    return $null
}

function Get-ReleaseVersion {
    param([string]$RequestedVersion)

    if (-not [string]::IsNullOrWhiteSpace($RequestedVersion)) {
        return $RequestedVersion.Trim()
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_REF_NAME)) {
        return $env:GITHUB_REF_NAME.Trim()
    }

    return "dev"
}

function Get-NumericVersion {
    param([string]$ReleaseVersion)

    $match = [regex]::Match($ReleaseVersion, '^\d+(?:\.\d+){0,3}')
    if (-not $match.Success) {
        return "1.0.0.0"
    }

    $segments = $match.Value.Split('.')
    while ($segments.Count -lt 4) {
        $segments += '0'
    }

    if ($segments.Count -gt 4) {
        $segments = $segments[0..3]
    }

    return ($segments -join '.')
}

$resolvedVersion = Get-ReleaseVersion -RequestedVersion $Version
$normalizedVersion = $resolvedVersion.TrimStart('v', 'V')
$numericVersion = Get-NumericVersion -ReleaseVersion $normalizedVersion
$publishDirectory = Join-Path $ArtifactsRoot "$RuntimeIdentifier/publish"
$packageName = "UsbFileSync-$resolvedVersion-$RuntimeIdentifier.zip"
$packagePath = Join-Path $ArtifactsRoot $packageName

if (Test-Path $publishDirectory) {
    Remove-Item $publishDirectory -Recurse -Force
}

if (Test-Path $packagePath) {
    Remove-Item $packagePath -Force
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

$publishArguments = @(
    "publish",
    $ProjectPath,
    "-c", $Configuration,
    "-r", $RuntimeIdentifier,
    "-o", $publishDirectory,
    "-p:Version=$numericVersion",
    "-p:FileVersion=$numericVersion",
    "-p:AssemblyVersion=$numericVersion",
    "-p:InformationalVersion=$resolvedVersion",
    "-p:PublishSingleFile=$($PublishSingleFile.IsPresent.ToString().ToLowerInvariant())",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=embedded"
)

if ($SelfContained.IsPresent) {
    $publishArguments += @("--self-contained", "true")
}
else {
    $publishArguments += @("--self-contained", "false")
}

& dotnet @publishArguments

$executablePath = Join-Path $publishDirectory "UsbFileSync.exe"
if (-not (Test-Path $executablePath)) {
    throw "Expected published executable not found at '$executablePath'."
}

if ($Sign.IsPresent) {
    if ([string]::IsNullOrWhiteSpace($SigningCertificatePath) -or -not (Test-Path $SigningCertificatePath)) {
        throw "Signing was requested, but no PFX certificate path was provided."
    }

    if ([string]::IsNullOrWhiteSpace($SigningCertificatePassword)) {
        throw "Signing was requested, but no PFX certificate password was provided."
    }

    $signToolPath = Resolve-SignToolPath
    if ([string]::IsNullOrWhiteSpace($signToolPath)) {
        throw "Signing was requested, but signtool.exe could not be found."
    }

    & $signToolPath sign /fd SHA256 /td SHA256 /f $SigningCertificatePath /p $SigningCertificatePassword /tr $TimestampServer $executablePath
}

Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $packagePath -CompressionLevel Optimal

Write-Host "Published executable: $executablePath"
Write-Host "Release package: $packagePath"

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    Add-Content -Path $env:GITHUB_OUTPUT -Value "package_path=$packagePath"
    Add-Content -Path $env:GITHUB_OUTPUT -Value "publish_directory=$publishDirectory"
    Add-Content -Path $env:GITHUB_OUTPUT -Value "executable_path=$executablePath"
    Add-Content -Path $env:GITHUB_OUTPUT -Value "release_version=$resolvedVersion"
}