## Codex 릴리즈 런북

이 문서는 사용자가 새 버전 빌드와 릴리즈 배포를 요청했을 때 다른 Codex 세션이 그대로 따라갈 작업 절차다.

## 저장소

- 소스: `https://github.com/acityboy-dev/DNFWeeklyWidget`
- 배포: `https://github.com/acityboy-dev/DNFWeeklyWidget.Release`
- 배포 저장소 `main`: `update.json`과 사용자용 README 관리
- 바이너리 ZIP: Git 커밋이 아니라 GitHub Release Asset으로 업로드
- 배포 저장소 `main`은 보호 브랜치다. force push와 branch deletion을 허용하지 않는다.
- 배포 저장소는 개인 계정 저장소라 특정 사용자 push 제한을 branch protection으로 걸 수 없다. 직접 collaborator를 늘리지 않아 `acityboy-dev`만 쓰기 권한을 갖게 한다.

모든 새 커밋의 작성자와 커미터는 다음 값이어야 한다.

```text
acityboy-dev <dongbin25@gmail.com>
```

작업 전에 반드시 확인한다.

```powershell
git config user.name
git config user.email
```

값이 다르면 현재 저장소 로컬 설정을 수정한다.

```powershell
git config user.name "acityboy-dev"
git config user.email "dongbin25@gmail.com"
```

## 1. 버전 변경

`DNFWeeklyWidget.csproj`의 `<Version>`을 요청받은 버전으로 변경한다.

```xml
<Version>1.0.0</Version>
```

`IncludeSourceRevisionInInformationalVersion`은 `false`를 유지한다. 설정창 버전 표시는 이 값을 읽으므로 별도 하드코딩하지 않는다.

## 2. 빌드 검증

먼저 Debug 빌드로 컴파일 오류를 확인한다.

```powershell
dotnet build DNFWeeklyWidget.sln -c Debug
```

그다음 Release publish를 실행한다.

```powershell
.\scripts\Publish-Release.ps1 -Version 1.0.0
```

또는 루트의 `publish-release.bat 1.0.0`을 사용할 수 있다. 버전 인자를 생략하면 프로젝트의 `<Version>`을 읽는다.

SmartScreen 대응 릴리즈는 Authenticode 코드 서명이 필요하다. 코드 서명 인증서가 없으면 SmartScreen 경고를 즉시 제거할 수 없고, 서명 후에도 일반 OV 인증서는 평판이 누적될 때까지 경고가 남을 수 있다. EV 코드 서명 인증서는 초기 평판 측면에서 더 유리하다.

인증서가 현재 사용자 인증서 저장소에 있으면 thumbprint로 서명한다.

```powershell
.\scripts\Publish-Release.ps1 -Version 1.0.0 -SigningCertificateThumbprint "CERT_THUMBPRINT" -RequireSigning
```

PFX 파일을 쓰는 경우 비밀번호는 환경 변수에 넣고 실행한다.

```powershell
$env:DNFWEEKLYWIDGET_SIGN_PASSWORD = "PFX_PASSWORD"
.\scripts\Publish-Release.ps1 -Version 1.0.0 -SigningCertificatePath "C:\secure\codesign.pfx" -RequireSigning
```

스크립트는 ZIP 생성 전에 `artifacts/release`의 `exe`와 `dll`을 SHA-256 + RFC3161 timestamp로 서명한다. `-RequireSigning`을 지정하면 인증서 누락이나 서명 실패 시 릴리즈 빌드를 실패시킨다.

산출물은 `artifacts/release`에 생성된다. 최소한 다음 파일을 확인한다.

```text
DNFWeeklyWidget.exe
update.exe
```

메인 앱은 `win-x64`, self-contained, single-file 옵션으로 publish한다. 업데이터는 MSVC의 `/MT` 정적 런타임을 사용하는 C++ Win32 단일 실행 파일로 빌드한다. 메인 앱에 필요한 WPF 네이티브 DLL이 생성되면 ZIP에 모두 포함해야 한다.

## 3. 소스 저장소 반영

빌드가 성공한 뒤 변경 범위와 작성자를 확인한다.

```powershell
git diff --check
git status -sb
git diff --stat
git config user.name
git config user.email
```

관련 파일만 stage하고 한글 커밋명으로 커밋한 뒤 `main`에 push한다.

```powershell
git add <관련 파일들>
git commit -m "변경 내용을 요약한 한글 제목"
git push origin main
```

push 후 작성자를 다시 확인한다.

```powershell
git log -1 --format="%h | %an <%ae> | %cn <%ce> | %s"
```

## 4. ZIP 및 SHA-256 생성

`artifacts/release`의 내용물 자체가 ZIP 루트에 오도록 압축한다. 상위 `release` 폴더를 ZIP 안에 넣지 않는다.

```powershell
$version = "1.0.0"
$zip = "artifacts\DNFWeeklyWidget-$version-win-x64.zip"
Compress-Archive -Path "artifacts\release\*" -DestinationPath $zip -CompressionLevel Optimal
$sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
$sha256
```

기존 동일 버전 ZIP이 있으면 새 압축 전에 삭제한다.

## 5. GitHub Release Asset 업로드

배포 저장소에 `v1.0.0` GitHub Release를 만들고 다음 ZIP을 Release Asset으로 업로드한다.

```text
DNFWeeklyWidget-1.0.0-win-x64.zip
```

다운로드 URL 형식은 다음과 같다.

```text
https://github.com/acityboy-dev/DNFWeeklyWidget.Release/releases/download/v1.0.0/DNFWeeklyWidget-1.0.0-win-x64.zip
```

Release는 현재 개발 단계에서는 prerelease로 생성한다. Asset 업로드가 완료되고 실제 파일 크기와 URL이 확인되기 전에는 `update.json`을 변경하지 않는다.

PowerShell에서 GitHub API로 한글 Release 설명을 보낼 때는 JSON 문자열을 `[Text.Encoding]::UTF8.GetBytes(...)`로 변환하고 Content-Type을 `application/json; charset=utf-8`로 지정한다. 문자열을 `-Body`에 바로 전달하면 한글이 `?`로 깨질 수 있다.

중요: ZIP을 `DNFWeeklyWidget.Release`의 Git 커밋에 추가하지 않는다. 과거처럼 `raw.githubusercontent.com` URL을 사용하지 않는다.

## 6. update.json 갱신

Release Asset 업로드 성공 후 배포 저장소의 `update.json`을 갱신한다.

```json
{
  "version": "1.0.0",
  "packageUrl": "https://github.com/acityboy-dev/DNFWeeklyWidget.Release/releases/download/v1.0.0/DNFWeeklyWidget-1.0.0-win-x64.zip",
  "sha256": "ZIP_SHA256",
  "executable": "DNFWeeklyWidget.exe"
}
```

배포 저장소 커밋 예시:

```powershell
git add update.json
git commit -m "1.0.0 업데이트 매니페스트 배포"
git push origin main
```

`v1.0.0` 태그는 배포 저장소의 최신 매니페스트 커밋을 가리키게 맞춘다. 태그를 이동해야 한다면 원격 태그도 명시적으로 갱신한다.

## 7. 최종 검증

다음을 모두 확인해야 릴리즈가 끝난 것이다.

1. 소스 저장소 `main`에 코드와 버전 변경이 push됨
2. 소스 저장소 최신 커밋 작성자가 `acityboy-dev <dongbin25@gmail.com>`임
3. GitHub Release `vX.Y.Z`가 존재함
4. ZIP이 Release Asset으로 존재하며 다운로드 가능함
5. Asset SHA-256과 `update.json.sha256`이 일치함
6. `update.json.packageUrl`이 Release Asset URL임
7. 배포 저장소 `main`에는 ZIP이 없고 `update.json`만 갱신됨
8. 배포 저장소 최신 커밋 작성자가 올바름
9. 두 로컬 작업 트리가 clean 상태임
10. 배포 저장소 `main` 보호 규칙이 유지됨
11. 배포 저장소 직접 collaborator가 불필요하게 추가되지 않음
12. SmartScreen 대응 릴리즈라면 `DNFWeeklyWidget.exe`와 `update.exe`가 유효한 Authenticode 서명을 포함함

원격 `update.json`도 직접 읽어 최종 내용을 확인한다.

## 실패 시 원칙

- Release Asset 업로드가 실패하면 `update.json`을 갱신하지 않는다.
- 잘못된 ZIP이나 해시를 배포했다면 먼저 올바른 Asset을 준비한 뒤 매니페스트를 갱신한다.
- 배포 저장소 `main` 보호 규칙을 우회하거나 해제하지 않는다. 긴급 수정도 `acityboy-dev` 계정으로만 처리한다.
- 작성자 이메일이 잘못된 커밋은 그대로 두지 않는다. 해당 커밋과 연결된 태그를 올바른 작성자로 재작성하고 `--force-with-lease`로 교체한다.
- 사용자가 만든 관련 없는 변경은 되돌리지 않는다.
- Debug 빌드에서는 자동 업데이트가 비활성화되어 있으므로 실제 업데이트 테스트는 이전 버전의 Release 빌드로 수행한다.
