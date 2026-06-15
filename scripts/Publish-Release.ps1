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

dotnet publish (Join-Path $repoRoot "Updater\Updater.csproj") `
	-c $Configuration `
	-r $Runtime `
	--self-contained true `
	-p:PublishSingleFile=true `
	-p:DebugSymbols=false `
	-o $artifactsRoot

if ($LASTEXITCODE -ne 0) {
	throw "Updater publish failed with exit code $LASTEXITCODE."
}

Write-Host "Release output: $artifactsRoot"
