# DNF Weekly Widget

던전앤파이터 여러 캐릭터의 주간 콘텐츠 진행 상태를 한 화면에서 확인하는 Windows용 WPF 위젯입니다.

게임 클라이언트를 조작하는 도구가 아니라, Neople OpenAPI와 일부 외부 조회 결과를 읽어 캐릭터별 주간 현황을 정리해 보여주는 개인용 관리 툴입니다.

## 주요 기능

- 캐릭터별 명성, 직업, 서버, 캐릭터 이미지 표시
- 레기온, 레이드 등 주간 콘텐츠 완료 여부 표시
- 중천장비, 서약, 결정 등 주간 획득 정보 표시
- 미완료 콘텐츠가 남은 캐릭터만 필터링
- 캐릭터 카드 드래그 정렬 및 삭제
- 프리셋별 캐릭터 목록과 카드 캐시 관리
- 던담 모험단 검색 결과를 이용한 캐릭터 일괄 추가
- 주간 초기화까지 남은 시간 표시
- 정기점검 공지를 확인해 해당 주 초기화 시각 보정
- 트레이 상주, 자동 갱신, 주기 갱신, 시작 시 자동 실행
- 앱 시작 시 업데이트 확인 및 네이티브 업데이터를 통한 자동 패치

## 사용 방법

1. [Neople Developers](https://developers.neople.co.kr/)에서 API Key를 발급받습니다.
2. 앱을 실행하고 `설정`에서 API Key를 입력합니다.
3. 서버와 캐릭터명을 선택해 캐릭터 카드를 추가합니다.
4. 여러 캐릭터를 한 번에 넣고 싶다면 던담 모험단 불러오기를 사용합니다.
5. `지금 갱신` 또는 `F5`로 최신 주간 상태를 조회합니다.
6. 필요에 따라 미완료 필터, 카드/줄 수, 표시할 주간 콘텐츠, 캐릭터 이미지 모드를 조정합니다.
7. 캐릭터 묶음을 나누고 싶다면 프리셋을 만들어 관리합니다.

## 설정과 표시 옵션

설정창은 좌측 탭으로 분류되어 있습니다.

- `일반`: API Key, 자동 갱신, 갱신 주기, 트레이/작업 표시줄, 시작 시 업데이트 확인, 윈도우 부팅 시 자동 실행
- `화면`: 테마, 저성능 모드, 캐릭터 이미지 표시 방식, 카드/줄 수
- `주간 콘텐츠`: 주간 획득 정보, 레기온, 레이드 표시 여부

일부 화면 옵션은 설정창에서 즉시 미리보기로 반영되며, 저장하지 않고 닫으면 원래 설정으로 돌아갑니다.

## 데이터 저장

사용자 설정과 캐릭터 목록은 다음 위치에 저장됩니다.

```text
%AppData%\DNFWeeklyWidget\settings.json
```

저장되는 내용:

- API Key
- 창 위치와 크기
- 테마와 표시 옵션
- 프리셋과 캐릭터 목록
- 캐릭터 카드 캐시
- 이미지 캐시 경로 정보

캐릭터 이미지는 다음 폴더에 캐시됩니다.

```text
%AppData%\DNFWeeklyWidget\ImageCache
```

주의: 현재 API Key는 `settings.json`에 평문으로 저장됩니다. 이 파일을 공개 저장소, 로그, 스크린샷 등에 노출하지 마세요.

## 업데이트 방식

Release 빌드에서는 앱 시작 시 다음 매니페스트를 확인합니다.

```text
https://raw.githubusercontent.com/acityboy-dev/DNFWeeklyWidget.Release/main/update.json
```

새 버전이 있으면 `update.exe`가 실행되어 ZIP 패키지를 다운로드하고 SHA-256을 검증한 뒤 앱 파일을 교체합니다.

업데이터는 C++ Win32 네이티브 단일 실행 파일입니다. 앱 실행 중에는 파일을 교체할 수 없으므로, 메인 앱을 종료한 뒤 설치 폴더의 대상 파일 옆에 `.update-new` 임시 파일로 직접 추출하고 교체합니다.

Visual Studio Debug 빌드에서는 자동 업데이트가 비활성화됩니다.

## 빌드

일반 개발 빌드:

```powershell
dotnet build DNFWeeklyWidget.sln -c Debug
```

Release 패키지 생성:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-Release.ps1 -Version 1.0.0
```

업데이터만 빌드:

```powershell
msbuild Updater\Updater.vcxproj /t:Build /p:Configuration=Release /p:Platform=x64
```

`msbuild`가 PATH에 없다면 Visual Studio 2022 Developer PowerShell에서 실행하세요.

## 릴리즈

배포 파일은 `DNFWeeklyWidget.Release` 저장소의 GitHub Release Asset으로 업로드합니다. 배포 저장소의 `main` 브랜치에는 최신 `update.json`만 유지합니다.

현재 Release Asset 설명은 짧고 건조한 변경 요약을 기준으로 작성합니다.

예시:

```text
점검시간 디버그 메뉴 제거 및 업데이터 진행창 표시 조정.

업데이트 테스트를 위해 manifest 1.0.1로 임시 게시
```

## 데이터 출처

- Neople OpenAPI: 캐릭터 검색, 상세 정보, 타임라인, 캐릭터 이미지
- 던담: 모험단명 기반 캐릭터 목록 조회
- 넥슨 DNF 공지사항: 정기점검 시작 시각 확인

외부 사이트의 HTML이나 응답 구조가 바뀌면 일부 조회 기능이 동작하지 않을 수 있습니다.
