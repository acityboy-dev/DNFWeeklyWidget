# 자동 업데이트 배포

자동 업데이트는 Release 빌드에서만 동작한다. Visual Studio Debug 빌드에서는 업데이트 확인, 다운로드, `update.exe` 실행이 모두 비활성화된다.

메인 앱과 업데이터는 모두 self-contained 단일 파일 옵션으로 publish하며, 네이티브 DLL과 콘텐츠 파일은 필요에 따라 별도 파일로 생성된다. 모든 결과물은 `artifacts/release` 폴더 하나에 합쳐진다. ZIP과 `update.json`은 자동 생성하지 않는다.

## Release 저장소

공개 저장소 주소:

`https://github.com/acityboy-dev/DNFWeeklyWidget.Release`

저장소 `main` 브랜치 루트에는 최신 버전의 `update.json`을 둔다. 실제 ZIP은 같은 저장소의 GitHub Release 자산으로 업로드한다.

```json
{
  "version": "1.0.1",
  "packageUrl": "https://github.com/acityboy-dev/DNFWeeklyWidget.Release/releases/download/v1.0.1/DNFWeeklyWidget-1.0.1-win-x64.zip",
  "sha256": "ZIP_SHA256",
  "executable": "DNFWeeklyWidget.exe"
}
```

## 패키지 생성

저장소 루트에서 실행한다.

```powershell
.\scripts\Publish-Release.ps1 -Version 1.0.1
```

생성 위치: `artifacts/release`

배포 순서:

1. `artifacts/release` 폴더의 파일을 직접 ZIP으로 압축한다.
2. ZIP의 SHA-256을 계산해 `update.json`을 작성한다.
3. `DNFWeeklyWidget.Release` 저장소에 Release와 `update.json`을 반영한다.

## 업데이트 흐름

1. 앱 시작 시 로컬 어셈블리 버전과 원격 `update.json`의 버전을 비교한다.
2. 새 버전이면 ZIP을 임시 폴더에 다운로드하고 SHA-256을 검증한다.
3. 설치 폴더의 `update.exe`를 임시 폴더로 복사해 실행한다.
4. 앱은 종료되고, 업데이터가 프로세스 종료를 기다린다.
5. 업데이터가 ZIP을 안전한 임시 경로에 해제하고 설치 폴더 파일을 교체한다.
6. 교체 완료 후 `DNFWeeklyWidget.exe`를 다시 실행한다.

업데이터는 `.update-files.json`에 자신이 관리하는 파일 목록을 저장한다. 다음 업데이트에서는 이전 패키지에 있었지만 새 패키지에서 빠진 파일만 삭제하며, 사용자가 설치 폴더에 별도로 둔 파일은 삭제하지 않는다.

업데이트 실패 시 설치 폴더에 `update-error.log`가 생성될 수 있다.
