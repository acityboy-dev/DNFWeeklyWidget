# DNF Weekly Widget

던전앤파이터 캐릭터들의 주간 콘텐츠 진행 상태를 한 화면에서 확인하는 Windows용 위젯입니다.

여러 캐릭터의 주간 콘텐츠 완료 여부와 획득 정보를 카드 형태로 표시하며, 프리셋과 필터를 이용해 캐릭터가 많은 경우에도 목록을 나누어 관리할 수 있습니다. 게임 클라이언트를 조작하지 않으며, 캐릭터 정보 조회에는 Neople OpenAPI를 사용합니다.

## 다운로드

실행 파일과 사용자용 안내는 별도의 배포 저장소에서 제공합니다.

[DNFWeeklyWidget 다운로드](https://github.com/acityboy-dev/DNFWeeklyWidget.Release/releases)

## 주요 구성

- **WPF 애플리케이션**: 메인 위젯, 캐릭터 카드, 설정, 프리셋 및 트레이 기능
- **Neople API 클라이언트**: 캐릭터 정보와 주간 콘텐츠 상태 조회
- **외부 서비스 연동**: 던담 모험단 캐릭터 목록과 정기점검 공지 조회
- **네이티브 업데이터**: C++ Win32 기반 ZIP 다운로드, SHA-256 검증 및 파일 교체
- **배포 스크립트**: self-contained 단일 파일 빌드, 코드 서명 및 배포 패키지 생성

## 개발 환경

- Windows 10/11 x64
- .NET 8 SDK
- Visual Studio 2022
- `Desktop development with C++` 워크로드 및 Windows 10/11 SDK

C++ 개발 환경은 네이티브 업데이터를 빌드할 때 필요합니다. WPF 애플리케이션만 빌드한다면 .NET 8 SDK로 충분합니다.

## 빌드

WPF 애플리케이션 Debug 빌드:

```powershell
dotnet build DNFWeeklyWidget.sln -c Debug
```

네이티브 업데이터 Release 빌드:

```powershell
msbuild Updater\Updater.vcxproj /t:Build /p:Configuration=Release /p:Platform=x64
```

`msbuild`가 PATH에 없다면 Visual Studio 2022 Developer PowerShell에서 실행합니다.

전체 Release 패키지 생성:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-Release.ps1 -Version 1.0.0
```

생성된 파일은 `artifacts/release`에, ZIP 패키지는 `artifacts/package`에 저장됩니다.

## 코드 서명

배포 스크립트는 인증서 저장소의 지문 또는 PFX 파일을 이용한 Authenticode 서명을 지원합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-Release.ps1 `
  -Version 1.0.0 `
  -SigningCertificateThumbprint "CERT_THUMBPRINT" `
  -RequireSigning
```

PFX 파일을 사용하는 경우 `-SigningCertificatePath`를 지정하고, 비밀번호는 기본적으로 `DNFWEEKLYWIDGET_SIGN_PASSWORD` 환경 변수에 설정합니다.

## 프로젝트 구조

- `MainWindow.xaml(.cs)`: 메인 UI, 카드 관리, 갱신 및 트레이 메뉴
- `SettingsWindow.xaml(.cs)`: 설정 UI와 옵션 적용
- `AppSettings.cs`: 설정 모델
- `SettingsPersistenceService.cs`: 설정 저장과 디바운스 처리
- `CharacterCardService.cs`: 캐릭터 카드 데이터 구성
- `NeopleApiClient.cs`: Neople OpenAPI 호출
- `DundamClient.cs`: 던담 모험단 캐릭터 목록 조회
- `WeeklyResetNoticeService.cs`: 정기점검 공지 조회
- `Updater/`: C++ Win32 네이티브 업데이터
- `scripts/Publish-Release.ps1`: Release 패키지 생성과 코드 서명

## 자동 업데이트

자동 업데이트는 Release 빌드에서만 활성화되며 Debug 빌드에서는 동작하지 않습니다.

메인 애플리케이션은 배포 저장소의 `update.json`에서 최신 버전을 확인합니다. 업데이트가 필요하면 `update.exe`가 패키지를 내려받아 SHA-256을 검증하고, 애플리케이션 종료 후 파일을 교체합니다.

## 사용자 데이터

설정과 캐릭터 목록은 다음 경로에 저장됩니다.

```text
%AppData%\DNFWeeklyWidget\settings.json
```

캐릭터 이미지 캐시는 다음 경로를 사용합니다.

```text
%AppData%\DNFWeeklyWidget\ImageCache
```

Neople API Key는 Windows DPAPI의 현재 사용자 범위로 암호화되어 `settings.json`에 저장됩니다. 암호화된 값은 동일한 Windows 사용자 계정에서만 복호화할 수 있습니다.

## 라이선스

이 프로젝트의 소스 코드는 [PolyForm Noncommercial License 1.0.0](LICENSE)에 따라 공개됩니다.

비상업적 개인 사용, 수정 및 직접 빌드는 허용됩니다. 상업적 이용과 유료 재배포는 허용되지 않습니다.
