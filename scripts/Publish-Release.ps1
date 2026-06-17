param(
	[Parameter(Mandatory = $true)]
	[string]$Version,

	[string]$Configuration = "Release",
	[string]$Runtime = "win-x64",

	[string]$SigningCertificateThumbprint,
	[string]$SigningCertificatePath,
	[string]$SigningCertificatePasswordEnv = "DNFWEEKLYWIDGET_SIGN_PASSWORD",
	[string]$TimestampUrl = "http://timestamp.digicert.com",
	[switch]$RequireSigning
)

$ErrorActionPreference = "Stop"

function Find-SignTool {
	$kitRoots = @(
		"${env:ProgramFiles(x86)}\Windows Kits\10\bin",
		"${env:ProgramFiles}\Windows Kits\10\bin"
	)

	foreach ($kitRoot in $kitRoots) {
		if (-not (Test-Path $kitRoot)) {
			continue
		}

		$signTool = Get-ChildItem -LiteralPath $kitRoot -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
			Where-Object { $_.FullName -like "*\x64\signtool.exe" } |
			Sort-Object FullName -Descending |
			Select-Object -First 1

		if ($signTool) {
			return $signTool.FullName
		}
	}

	return $null
}

function Invoke-CodeSigning {
	param(
		[Parameter(Mandatory = $true)]
		[string]$TargetPath
	)

	$signTool = Find-SignTool
	if (-not $signTool) {
		throw "signtool.exe를 찾을 수 없습니다. Windows SDK를 설치하거나 PATH를 확인하세요."
	}

	$arguments = @(
		"sign",
		"/fd", "SHA256",
		"/tr", $TimestampUrl,
		"/td", "SHA256"
	)

	if ($SigningCertificateThumbprint) {
		$arguments += @("/sha1", $SigningCertificateThumbprint)
	}
	elseif ($SigningCertificatePath) {
		$arguments += @("/f", $SigningCertificatePath)
		$password = [Environment]::GetEnvironmentVariable($SigningCertificatePasswordEnv)
		if ($password) {
			$arguments += @("/p", $password)
		}
	}
	else {
		throw "서명 인증서가 지정되지 않았습니다. -SigningCertificateThumbprint 또는 -SigningCertificatePath를 사용하세요."
	}

	$arguments += $TargetPath

	& $signTool @arguments
	if ($LASTEXITCODE -ne 0) {
		throw "Code signing failed for $TargetPath with exit code $LASTEXITCODE."
	}
}

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

$signingRequested = [bool]$SigningCertificateThumbprint -or [bool]$SigningCertificatePath
if ($RequireSigning -and -not $signingRequested) {
	throw "-RequireSigning이 지정됐지만 서명 인증서가 지정되지 않았습니다."
}

if ($signingRequested) {
	$signingTargets = Get-ChildItem -LiteralPath $artifactsRoot -File |
		Where-Object { $_.Extension -in @(".exe", ".dll") }

	foreach ($target in $signingTargets) {
		Write-Host "Signing: $($target.FullName)"
		Invoke-CodeSigning -TargetPath $target.FullName
	}
}
elseif ($RequireSigning) {
	throw "서명이 필수로 설정됐지만 서명 대상이 구성되지 않았습니다."
}
else {
	Write-Host "Code signing skipped. Provide -SigningCertificateThumbprint or -SigningCertificatePath to sign release binaries."
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
