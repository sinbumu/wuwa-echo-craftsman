# 프로젝트 구조 개요

이 문서는 `wuwa-echo-craftsman`의 현재 코드 구조와 주요 동작 책임을 정리한다. 자동화의 세부 시나리오는 `docs/automation_scenario.md`, 배포 절차는 `docs/deployment.md`를 참고한다.

## 1. 프로젝트 개요

`wuwa-echo-craftsman`은 명조 에코 강화 반복 작업을 보조하는 Windows WPF 유틸리티다. 에코 목록에서 +0 에코를 찾고, 강화 화면에서 목표 레벨까지 강화한 뒤, OCR로 읽은 부옵션 이름을 기준으로 잠금 또는 폐기 판단을 수행한다.

현재 앱은 아래 흐름을 중심으로 동작한다.

1. 에코 목록 화면에서 `+0` 표시 템플릿을 이미지 매칭으로 찾는다.
2. 대상 에코를 선택하고 강화 화면으로 진입한다.
3. 설정에 따라 폐기 에코 재료를 먼저 사용하거나, 단계별 투입을 사용해 강화한다.
4. 강화 완료 후 현재 레벨과 부옵션 영역을 OCR로 읽는다.
5. 부옵션 조건을 만족하면 잠금, 조건 달성이 불가능하거나 목표 레벨에 도달했는데 조건 미달이면 폐기한다.
6. 에코 목록으로 돌아가 남은 반복 횟수만큼 다시 실행한다.

## 2. 기술 스택

- UI: WPF, WPF-UI
- 런타임: .NET 8, Windows 전용
- 이미지 매칭: OpenCvSharp4
- OCR: Windows.Media.Ocr
- 로컬 DB: SQLite, `Microsoft.Data.Sqlite`
- 입력 자동화: Win32 `SendInput`
- 배포: `win-x64` self-contained single-file publish

## 3. 상위 폴더 구조

```text
wuwa-echo-craftsman/
├─ WutheringWavesEchoCraftsman/   # WPF 앱 본체
├─ asset/                         # 앱 아이콘, 스플래시 이미지
├─ docs/                          # 설계/동작/배포 문서
├─ scripts/                       # 로컬 publish/package/release 스크립트
├─ README.md                      # 사용자/개발자용 간단 안내
└─ WutheringWavesEchoCraftsman.sln
```

실행 시 사용자 데이터는 실행 파일 기준 `data/` 폴더에 저장된다.

```text
data/
├─ config.json        # 앱 설정, 캘리브레이션 ROI, 에셋 경로
├─ assets/            # 캘리브레이션으로 저장한 템플릿 PNG
└─ history.sqlite3    # 자동화 결과 히스토리
```

`history.sqlite3`는 개인 기록이므로 설정 내보내기 대상에서 제외한다.

## 4. 앱 프로젝트 구조

```text
WutheringWavesEchoCraftsman/
├─ App.xaml(.cs)                  # 앱 시작, 스플래시, 트레이, 종료 정책
├─ Core/                          # 자동화/캡처/OCR/입력/핫키 핵심 로직
├─ Models/                        # 설정, ROI, 부옵션 정의/파서
├─ Services/                      # SQLite 히스토리 서비스
├─ Views/                         # WPF 화면과 코드비하인드
└─ WutheringWavesEchoCraftsman.csproj
```

## 5. 주요 Core 클래스

### `EchoAutomator`

자동화 상태 머신의 중심 클래스다.

주요 책임:

- 에코 목록에서 +0 에코 탐색
- +0 에코가 안 보이면 설정된 방향/강도로 마우스 휠 스크롤 후 재탐색
- 강화 화면 진입
- 폐기 에코 우선 사용 옵션 처리
- 단계별 투입 강화
- 현재 레벨 OCR
- 부옵션 OCR 및 조건 판정
- 조기 폐기 판정
- 잠금/폐기 키 입력과 목록 복귀
- 오버레이 업데이트 콜백 호출

### `VisionProcessor`

이미지 매칭과 OCR 전처리를 담당한다.

주요 책임:

- OpenCV 템플릿 매칭
- 여러 후보 템플릿 매칭 결과 수집
- 일반 OCR 전처리
- 작은 텍스트용 OCR 후보 이미지 생성
- Windows OCR 호출

현재 레벨처럼 작은 흰 글씨는 OCR 실패가 잦기 때문에 확대, 여백 추가, 반전 등 여러 후보 이미지를 만들어 순차적으로 OCR을 시도한다.

### `InputController`

Win32 `SendInput` 기반 입력 전송을 담당한다.

주요 입력:

- 좌표 클릭
- 키 입력
- 마우스 휠 스크롤
- 드래그 입력

`DryRun`이 켜져 있으면 실제 입력 대신 로그만 남긴다.

### `ScreenCapturer`

가상 화면 전체 또는 지정 ROI를 `Bitmap`으로 캡처한다. 캘리브레이션, OCR, 이미지 매칭에서 공통으로 사용한다.

### `CalibrationManager`

`data/config.json`과 `data/assets/`를 관리한다.

주요 책임:

- 설정 로드/저장
- 누락된 ROI/asset 기본값 보정
- 캘리브레이션 에셋 저장
- 상대/절대 경로 해석

## 6. 주요 Models

### `AppConfig`

앱 설정의 루트 모델이다.

포함 항목:

- 실행 설정: 반복 횟수, 목표 레벨, 대기 시간
- 자동화 설정: 폐기 에코 우선 사용, 목록 스크롤 방향/강도
- 캘리브레이션 ROI: `Regions`
- 템플릿 에셋 경로: `Assets`
- 부옵션 조건: `SubstatRules`

`CalibrationTargets`에는 현재 필수 ROI와 asset 키가 정의되어 있다.

주요 ROI:

- `roi_list`
- `roi_enhance_tab`
- `roi_staged_auto_input`
- `roi_echo_material_input`
- `roi_echo_material_list`
- `roi_enhance_confirm`
- `roi_enhance_complete_close`
- `roi_current_level`
- `roi_substat`

주요 asset:

- `template_plus_zero.png`
- `template_discard_echo.png`

### `SubstatInfo`

부옵션 정의와 OCR 파싱을 담당한다.

현재 부옵션 조건 판정은 수치를 사용하지 않고 이름만 사용한다. 수치는 오버레이 표시와 고정/퍼센트 부옵션 구분 보조에만 사용한다.

특히 `공격력`, `방어력`, `HP`는 고정 수치와 퍼센트가 이름상 헷갈릴 수 있으므로 다음 방식으로 구분한다.

- `%` 문자가 OCR 결과에 있으면 퍼센트 부옵션으로 본다.
- `%`가 없더라도 값이 퍼센트 범위에만 들어가면 퍼센트 부옵션으로 본다.
- 값이 고정 수치 범위에만 들어가면 고정 수치 부옵션으로 본다.
- 수치 범위는 서로 겹치지 않는 현재 게임 데이터를 기준으로 한다.

## 7. 주요 Views

### `MainWindow`

앱의 메인 화면이다.

주요 기능:

- 자동화 시작/정지
- 설정 저장
- 캘리브레이션 창 열기
- 부옵션 설정 창 열기
- 히스토리 열기
- 설정 내보내기/불러오기
- 상세 설정 관리
- 자동화 시작 전 프리플라이트 점검

자동화 시작 전 점검은 다음을 확인한다.

- 필수 ROI 설정 여부
- 필수 템플릿 이미지 존재 여부
- 현재 보이는 목록의 +0 탐지 여부
- 폐기 에코 우선 옵션 사용 시 관련 재료 목록/폐기 아이콘 설정 여부

현재 보이는 화면에서 +0 에코가 탐지되지 않아도 즉시 실패하지 않는다. 자동화 본 흐름에서 설정된 방향으로 최대 3회 스크롤하며 다시 탐색한다.

### `CalibrationWindow` / `CalibrationOverlay`

캘리브레이션 관리와 드래그 캡처를 담당한다.

현재 캘리브레이션은 3단계 화면을 사용한다.

1. 에코 목록 화면
2. 에코 강화 화면
3. 에코 재료 목록 화면

재료 목록은 강화 화면과 동시에 보이지 않는 상태가 있을 수 있으므로 별도 단계로 분리되어 있다.

### `SubstatSettingsWindow`

잠금/폐기 판정에 사용할 부옵션 조건을 설정한다.

현재 정책:

- `사용`: 유효 부옵션 개수 계산에 포함
- `필수`: 반드시 등장해야 하는 부옵션
- 수치 조건은 사용하지 않음

### `AutomationOverlayWindow`

자동화 중 식별된 정보를 보여주는 플로팅 오버레이다.

표시 항목:

- 현재 식별된 에코 레벨
- 현재 식별된 부옵션 목록
- 이번 실행 히스토리

매크로 종료 후에도 유지되며, 사용자가 오버레이의 `X` 버튼을 누르거나 앱을 완전히 종료하면 닫힌다. 재료 목록이 우측에 뜨는 흐름을 고려해 좌측 하단에 배치한다.

### `HistoryWindow`

SQLite에 저장된 자동화 결과 히스토리를 표시한다.

## 8. 자동화 판정 정책

### 잠금 조건

아래 조건을 모두 만족하면 잠금한다.

- 필수 부옵션이 모두 등장함
- 사용 체크된 부옵션 중 유효 개수가 `RequiredValidSubstatCount` 이상임

수치 조건은 사용하지 않는다. OCR 수치 오인식으로 잘못 잠금/폐기하는 리스크를 줄이기 위해서다.

### 조기 폐기 조건

목표 레벨까지 강화하지 않아도 조건 달성이 불가능하면 즉시 폐기한다.

사용하는 값:

- 현재 유효 부옵션 개수
- 아직 나오지 않은 필수 부옵션 개수
- 현재 레벨 기준 공개됐어야 하는 부옵션 슬롯
- OCR이 인식하지 못한 현재 슬롯
- 목표 레벨까지 남은 공개 가능 슬롯

조기 폐기 기준:

- `현재 유효 부옵션 수 + 남은/미인식 슬롯 < 잠금에 필요한 유효 부옵션 수`
- `아직 나오지 않은 필수 부옵션 수 > 남은/미인식 슬롯`

OCR이 일부 슬롯을 못 읽은 경우, 그 슬롯은 아직 어떤 부옵션일 가능성이 있다고 보고 가능성 계산에 포함한다.

## 9. 설정 내보내기/불러오기

설정 내보내기는 공유 가능한 캘리브레이션 프로필을 zip으로 만든다.

포함:

- `config.json`
- `assets/*.png`

제외:

- `history.sqlite3`
- 실행 중 생성되는 개인 데이터

불러오기 시 zip entry 경로를 검사해 path traversal을 방지한다.

## 10. 배포 구조

프로젝트는 `win-x64` self-contained single-file publish를 기준으로 한다.

주요 설정:

- `PublishSingleFile=true`
- `SelfContained=true`
- `RuntimeIdentifier=win-x64`
- `PublishReadyToRun=true`
- `IncludeNativeLibrariesForSelfExtract=true`

로컬 배포 명령은 `scripts/release.ps1`를 사용한다. 자세한 내용은 `docs/deployment.md`를 참고한다.

## 11. 개발 시 주의점

- 자동화 입력은 실제 게임 화면에 영향을 주므로 변경 후 Dry-run과 단계 테스트를 먼저 확인한다.
- ROI/asset 키를 추가하면 `CalibrationTargets`, 캘리브레이션 안내, 문서를 함께 갱신한다.
- 부옵션 판정은 현재 이름 기반이다. 수치 OCR은 표시와 보조 분류에만 사용한다.
- OCR 실패 시 재료 소모로 이어질 수 있는 경로는 보수적으로 중단하거나 폐기 판단을 유보한다.
- 설정 공유 기능에서 히스토리 DB나 개인 기록을 포함하지 않는다.
