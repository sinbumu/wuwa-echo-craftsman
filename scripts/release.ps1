param(
    [ValidateSet("build", "publish", "package", "release", "clean")]
    [string]$Task = "publish",

    [string]$Tag = "local",

    [switch]$SkipTagPush
)

$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$SolutionPath = Join-Path $Root "WutheringWavesEchoCraftsman.sln"
$ProjectPath = Join-Path $Root "WutheringWavesEchoCraftsman\WutheringWavesEchoCraftsman.csproj"
$PublishDir = Join-Path $Root "artifacts\publish\win-x64"
$ReleaseDir = Join-Path $Root "artifacts\release"
$ReleaseName = "wuwa-echo-craftsman-win-x64"

function Invoke-DotNetBuild {
    dotnet build $SolutionPath --configuration Release
}

function Invoke-DotNetPublish {
    if (Test-Path $PublishDir) {
        Remove-Item $PublishDir -Recurse -Force
    }

    dotnet publish $ProjectPath `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishProfile=win-x64-single-file `
        -p:PublishSingleFile=true `
        -p:PublishReadyToRun=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishTrimmed=false `
        --output $PublishDir
}

function New-ReleasePackage {
    param([string]$PackageTag)

    if (-not (Test-Path $PublishDir)) {
        Invoke-DotNetPublish
    }

    New-Item -ItemType Directory -Force $ReleaseDir | Out-Null

    $safeTag = if ([string]::IsNullOrWhiteSpace($PackageTag)) { "local" } else { $PackageTag }
    $zipPath = Join-Path $ReleaseDir "$ReleaseName-$safeTag.zip"
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    $forbiddenFiles = Get-ChildItem $PublishDir -Recurse -File |
        Where-Object { $_.Name -match 'history\.(db|sqlite|sqlite3)$' -or $_.FullName -match '\\data\\' }
    if ($forbiddenFiles) {
        $names = ($forbiddenFiles | Select-Object -ExpandProperty FullName) -join [Environment]::NewLine
        throw "Release package contains runtime data candidates. Refusing to package:$([Environment]::NewLine)$names"
    }

    Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $zipPath -Force
    return $zipPath
}

function Test-GhCli {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI(gh)가 필요합니다. 설치 후 'gh auth login'을 먼저 실행하세요."
    }

    gh auth status | Out-Null
}

function Publish-GitHubRelease {
    param([string]$ReleaseTag)

    if ([string]::IsNullOrWhiteSpace($ReleaseTag) -or $ReleaseTag -eq "local") {
        throw "release 작업에는 TAG가 필요합니다. 예: make release TAG=v1.0.0"
    }

    Test-GhCli
    $zipPath = New-ReleasePackage -PackageTag $ReleaseTag

    git rev-parse --verify "refs/tags/$ReleaseTag" *> $null
    if ($LASTEXITCODE -ne 0) {
        git tag $ReleaseTag
    }

    if (-not $SkipTagPush) {
        git push origin $ReleaseTag
    }

    gh release view $ReleaseTag *> $null
    if ($LASTEXITCODE -eq 0) {
        gh release upload $ReleaseTag $zipPath --clobber
    }
    else {
        gh release create $ReleaseTag $zipPath `
            --verify-tag `
            --title $ReleaseTag `
            --notes "Windows x64 portable release. Extract the zip and run WutheringWavesEchoCraftsman.exe."
    }

    Write-Host "Release uploaded: $ReleaseTag"
}

switch ($Task) {
    "build" {
        Invoke-DotNetBuild
    }
    "publish" {
        Invoke-DotNetPublish
        Write-Host "Published to: $PublishDir"
    }
    "package" {
        Invoke-DotNetPublish
        $zipPath = New-ReleasePackage -PackageTag $Tag
        Write-Host "Packaged: $zipPath"
    }
    "release" {
        Invoke-DotNetPublish
        Publish-GitHubRelease -ReleaseTag $Tag
    }
    "clean" {
        if (Test-Path (Join-Path $Root "artifacts")) {
            Remove-Item (Join-Path $Root "artifacts") -Recurse -Force
        }
        if (Test-Path (Join-Path $Root "WutheringWavesEchoCraftsman\bin")) {
            Remove-Item (Join-Path $Root "WutheringWavesEchoCraftsman\bin") -Recurse -Force
        }
        if (Test-Path (Join-Path $Root "WutheringWavesEchoCraftsman\obj")) {
            Remove-Item (Join-Path $Root "WutheringWavesEchoCraftsman\obj") -Recurse -Force
        }
    }
}
