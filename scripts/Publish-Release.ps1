param(
	[Parameter(Mandatory = $true)]
	[string]$Version,
	[string]$Configuration = "Release",
	[string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repoRoot "artifacts\release"

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

Write-Host "Release output: $artifactsRoot"
