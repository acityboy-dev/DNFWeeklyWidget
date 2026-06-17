param(
	[Parameter(Mandatory = $true)]
	[string]$Version,

	[string]$Configuration = "Release",
	[string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repoRoot "artifacts\release"

$releaseRepoRoot = Join-Path (Split-Path -Parent $repoRoot) "DNFWeeklyWidget.Release"
$updateJsonPath = Join-Path $releaseRepoRoot "update.json"

$zipName = "DNFWeeklyWidget-$Version-$Runtime.zip"
$zipPath = Join-Path $repoRoot $zipName

if (Test-Path $artifactsRoot) {
	Remove-Item -LiteralPath $artifactsRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $artifactsRoot | Out-Null

dotnet publish (Join-Path $repoRoot "DNFWeeklyWidget.csproj") `
	-c $Configuration `
	-r $Runtime `
	--self-contained true `
	-p:PublishSingleFile=true `
	-p:DebugSymbols=false `
	-p:Version=$Version `
	-o $artifactsRoot

if ($LASTEXITCODE -ne 0) {
	throw "DNFWeeklyWidget publish failed with exit code $LASTEXITCODE."
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
	throw "Visual Studio Installer의 vswhere.exe를 찾을 수 없습니다."
}

$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" |
	Select-Object -First 1

if (-not $msbuild) {
	throw "C++ 업데이터를 빌드할 MSBuild를 찾을 수 없습니다."
}

$updaterIntermediate = Join-Path $repoRoot "artifacts\obj\updater"
$updaterProjectExtensions = Join-Path $repoRoot "artifacts\obj\updater-extensions"

& $msbuild (Join-Path $repoRoot "Updater\Updater.vcxproj") `
	/t:Build `
	/p:Configuration=$Configuration `
	/p:Platform=x64 `
	/p:OutDir="$artifactsRoot\" `
	/p:IntDir="$updaterIntermediate\" `
	/p:MSBuildProjectExtensionsPath="$updaterProjectExtensions\"

if ($LASTEXITCODE -ne 0) {
	throw "Native updater build failed with exit code $LASTEXITCODE."
}

# 기존 zip 삭제
if (Test-Path $zipPath) {
	Remove-Item -LiteralPath $zipPath -Force
}

# 빌드 결과 전체 압축
Compress-Archive `
	-Path (Join-Path $artifactsRoot "*") `
	-DestinationPath $zipPath `
	-Force

# SHA256 계산
$sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()

# Release repo 존재 확인
if (-not (Test-Path $releaseRepoRoot)) {
	throw "Release repo 폴더를 찾을 수 없습니다: $releaseRepoRoot"
}

# update.json 생성/갱신
$packageUrl = "https://github.com/acityboy-dev/DNFWeeklyWidget.Release/releases/download/v$Version/$zipName"

$updateInfo = [ordered]@{
	version    = $Version
	packageUrl = $packageUrl
	sha256     = $sha256
	executable = "DNFWeeklyWidget.exe"
}

$updateJson = $updateInfo | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText(
	$updateJsonPath,
	$updateJson + [Environment]::NewLine,
	[System.Text.UTF8Encoding]::new($false))

Write-Host ""
Write-Host "Release output:"
Write-Host "  $artifactsRoot"

Write-Host ""
Write-Host "Package:"
Write-Host "  $zipPath"

Write-Host ""
Write-Host "SHA256:"
Write-Host "  $sha256"

Write-Host ""
Write-Host "update.json:"
Write-Host "  $updateJsonPath"
