# 자동 업데이트 배포

자동 업데이트는 Release 빌드에서만 동작한다. Visual Studio Debug 빌드에서는 업데이트 확인, 다운로드, `update.exe` 실행이 모두 비활성화된다.

메인 앱은 .NET self-contained 단일 파일로 publish한다. 업데이터는 C++ Win32 정적 링크 단일 실행 파일로 빌드하므로 .NET 런타임이나 별도 DLL이 필요 없다. 모든 결과물은 `artifacts/release` 폴더 하나에 합쳐진다. ZIP과 `update.json`은 자동 생성하지 않는다.

## Release 저장소

공개 저장소 주소:

`https://github.com/acityboy-dev/DNFWeeklyWidget.Release`

저장소 `main` 브랜치 루트에는 최신 버전의 `update.json`을 둔다. 실제 ZIP은 같은 저장소의 GitHub Release 자산으로 업로드한다.

```json
{
  "version": "1.0.1",
  "packageUrl": "https://github.com/acityboy-dev/DNFWeeklyWidget.Release/releases/download/v1.0.0/DNFWeeklyWidget-1.0.0-win-x64.zip",
  "sha256": "ZIP_SHA256",
  "executable": "DNFWeeklyWidget.exe"
}
```

## 패키지 생성

저장소 루트에서 실행한다.

```powershell
.\scripts\Publish-Release.ps1 -Version 1.0.0
```

생성 위치: `artifacts/release`

배포 순서:

1. `artifacts/release` 폴더의 파일을 직접 ZIP으로 압축한다.
2. ZIP의 SHA-256을 계산해 `update.json`을 작성한다.
3. `DNFWeeklyWidget.Release` 저장소에 Release와 `update.json`을 반영한다.

## 업데이트 흐름

1. 앱 시작 시 설정에서 자동 확인이 켜져 있으면 원격 `update.json`의 버전을 비교한다.
2. 새 버전이면 임시 복사한 `update.exe`가 현재/최신 버전을 표시하고 업데이트 여부를 묻는다.
3. 사용자가 동의하면 앱이 종료되고 업데이터가 진행률 창을 표시한다.
4. 업데이터가 ZIP을 다운로드하고 SHA-256 및 실행 파일을 검증한다.
5. ZIP 항목을 설치 대상 파일 옆의 `.update-new` 임시 파일로 직접 풀고 교체한다. 별도 staging 폴더는 만들지 않는다.
6. 교체 완료 후 `DNFWeeklyWidget.exe`를 다시 실행한다.

설정창은 자동 확인 옵션과 관계없이 최신 버전을 표시한다. 새 버전이 있으면 `업데이트` 버튼으로 확인 대화상자 없이 바로 업데이트할 수 있다.

업데이터는 `.update-files.json`에 자신이 관리하는 파일 목록을 저장한다. 다음 업데이트에서는 이전 패키지에 있었지만 새 패키지에서 빠진 파일만 삭제하며, 사용자가 설치 폴더에 별도로 둔 파일은 삭제하지 않는다.

업데이트 실패 시 설치 폴더에 `update-error.log`가 생성될 수 있다.
