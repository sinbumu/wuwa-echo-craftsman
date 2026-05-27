# Project: Wuthering Waves (명조) - wuwa-echo-craftsman (에코 깎는 노인)

## 1. Project Overview
본 프로젝트는 게임 '명조(Wuthering Waves)'의 에코(장비) 일괄 강화 및 부옵션 판별 과정을 자동화하는 Windows 10/11 전용 데스크톱 유틸리티 프로그램이다. 
**C# .NET 8.0 기반의 WPF(Windows Presentation Foundation) 앱**으로 구현하며, 시스템 트레이(System Tray)에 상주하여 동작한다. 사용자가 별도의 런타임(.NET 등)을 설치할 필요 없이 즉시 실행 가능한 **Self-contained Single-file(`.exe`)**로 배포하는 것을 목표로 한다.

## 2. Tech Stack
- **Language / Runtime:** C# / .NET 8.0
- **GUI Framework:** WPF (Windows Presentation Foundation)
- **Computer Vision:** `OpenCvSharp4`, `OpenCvSharp4.Extensions` (화면 매칭 및 전처리)
- **OCR Engine:** `Windows.Media.Ocr` (Windows 10/11 네이티브 API)
- **Input & Hook:** Win32 API `SendInput` (게임 클라이언트 입력 전송용), `RegisterHotKey` (글로벌 핫키)
- **Screen Capture:** `Graphics.CopyFromScreen` (DPI 보정 포함)
- **Database:** `Microsoft.Data.Sqlite` (NuGet 패키지)
- **Tray Icon:** `System.Windows.Forms.NotifyIcon` (WPF에서 `UseWindowsForms`로 연동)

## 3. Directory & Solution Structure
```text
WutheringWavesEchoCraftsman.sln
└── WutheringWavesEchoCraftsman/
    ├── App.xaml / App.xaml.cs             
    ├── Core/
    │   ├── EchoAutomator.cs               # Task/async 기반 자동화 상태 머신
    │   ├── VisionProcessor.cs             # OpenCvSharp4 템플릿 매칭 및 WinRT OCR 연동
    │   ├── InputController.cs             # Win32 SendInput 래퍼
    │   ├── ScreenCapturer.cs              # DPI 보정이 적용된 화면 캡처 유틸리티
    │   └── CalibrationManager.cs          
    ├── Models/
    │   ├── AppConfig.cs                   
    │   └── SubstatInfo.cs                 
    ├── Services/
    │   └── DatabaseService.cs             # SQLite 로컬 히스토리 CRUD
    ├── Views/
    │   ├── MainWindow.xaml / .cs          
    │   ├── HistoryWindow.xaml / .cs       
    │   ├── CalibrationWindow.xaml / .cs    # 캘리브레이션 상태 확인 및 개별 재설정 창
    │   └── CalibrationOverlay.xaml / .cs  
    └── Properties/
        └── PublishProfiles/               
```

## 4. Pre-requisites & Core Policies
- **화면 모드:** '전체 창모드(Borderless Windowed)'를 공식 지원 대상으로 한다. 독점 전체화면(Exclusive Fullscreen)은 캡처(블랙 스크린) 및 입력 호환성이 보장되지 않으므로 미지원 또는 실험적 지원으로 간주한다.
- **DPI Policy:** Windows 디스플레이 배율(DPI Scaling)에 영향을 받지 않도록 앱을 **DPI-Aware**로 설정(`app.manifest` 활용)하거나, 캡처/클릭 좌표 계산 시 DPI 배율 역산 보정 로직을 필수로 구현한다.
- **인게임 상태:** 에코 목록 화면, 유저 목표 필터링 적용 완료. 정렬은 **'레벨 순서 (오름차순)'**.

## 5. Core Features & UX Flow

### 5.1. GUI 및 안전장치 (Fail-Safe)
- `System.Windows.Forms.NotifyIcon`을 WPF 앱에서 연동하여 Windows 시스템 트레이에 상주.
- **Dry-Run (테스트) 모드:** 활성화 시 실제 `SendInput` 호출을 생략하고 로그로 검증.
- **글로벌 제어:** `F5` 시작, `F6` 강제 정지 (`CancellationTokenSource` 활용).
- **하드웨어 Fail-Safe:** 사용자가 마우스를 화면 모서리(0, 0)로 이동시키면 스레드 즉시 정지.

### 5.2. 오버레이 캘리브레이션 UX
1. 메인 화면의 **[초기 설정 시작]** 버튼은 일회성 마법사가 아니라 **초기 설정 관리 창**을 연다.
2. 초기 설정 관리 창은 20개 캘리브레이션 항목의 현재 저장 상태(미설정/저장됨), ROI 좌표, asset 파일 경로를 표로 보여준다.
3. 사용자는 전체를 다시 할 필요 없이 **선택 항목만 다시 캡처**, **선택한 화면 단계 전체 다시 캡처**, **전체 순차 캘리브레이션** 중 하나를 실행할 수 있다.
4. 캘리브레이션 캡처는 **4단계 화면 준비 -> 3초 후 캡처 -> 드래그 수집** 방식으로 진행한다.
5. 각 단계 시작 전 WPF 안내 팝업으로 사용자가 어떤 인게임 화면을 준비해야 하는지 구체적으로 설명한다.
6. 드래그(RubberBand)를 통해 영역 지정.
7. **수집 타겟 (총 20종):**
   - **[1/4 에코 목록 화면]:** `roi_list`, `template_plus_zero.png`, `roi_enhance_tab`
   - **[2/4 에코 강화 기본 화면]:** `roi_expected_level`(강화 후 예상 레벨 OCR), `roi_slot_plus`, `roi_enhance_confirm`, `roi_enhance_complete_close`, `roi_optimize_tab`
   - **[3/4 에코 강화 재료 리스트 화면]:** 강화 화면에서 재료 슬롯/투입 영역을 클릭해 우측 재료 목록을 연 뒤 `roi_material`, `icon_discard.png`, `roi_exp_material_1`~`roi_exp_material_4`
   - **[4/4 에코 옵티마이즈 화면]:** `roi_substat`, `roi_optimize_count`, `roi_optimize_minus`, `roi_optimize_plus`, `roi_optimize_confirm`, `roi_optimize_complete_close`

### 5.3. 부옵션 필터링 및 OCR 정규화
- **정규화:** WinRT OCR 결과에서 공백/특수문자 제거 후 13종 표준 명칭으로 치환.
- 목표 유효 옵션 개수 충족 시 `C`(잠금), 미달 시 `Z`(폐기) 전송 (클릭 1회 보정). DB 기록.

### 5.4. 자동화 상태 머신 (Task-Driven State Machine)
- **상태 머신은 각 루프 시작 시 GUI에서 설정한 `remainingCount`를 확인하고, 0 이하이면 정상 종료한다.**
- **SEARCH:** `roi_list`에서 `template_plus_zero.png` 매칭 -> 매칭 좌표 클릭 -> `roi_enhance_tab` 중앙 클릭. (매칭 실패 시 **정상 종료**)
- **ENHANCE:** +0 에코 전제이므로 먼저 `roi_slot_plus` 중앙 클릭 -> `roi_material`에서 `icon_discard` 탐색/클릭, 없으면 `roi_exp_material_1`~`roi_exp_material_4` 중 설정된 음파통 영역을 순차 중앙 클릭 -> `roi_expected_level` OCR 판독. 목표 레벨 이상이면 `ESC` 1회로 재료 선택장을 닫고 `roi_enhance_confirm` 중앙 클릭 -> 잠시 대기 -> 강화 완료 오버레이를 `roi_enhance_complete_close` 중앙 클릭으로 닫고 OPTIMIZE 이동. 재료 클릭 5회 이상에도 예상 레벨이 증가하지 않으면 오류 기록 후 정지.
- **OPTIMIZE:** `roi_optimize_tab` 중앙 클릭 -> `roi_optimize_count` OCR 판독 -> 목표 옵티마이즈 횟수에 맞도록 `roi_optimize_minus`/`roi_optimize_plus` 중앙 클릭 반복 -> 목표 횟수 확인 후 `roi_optimize_confirm` 중앙 클릭 -> 잠시 대기 -> 옵티마이즈 완료 오버레이를 `roi_optimize_complete_close` 중앙 클릭으로 닫고 `roi_substat` OCR 결과 갱신을 완료 조건으로 삼아 개방 로직을 수행. 일정 횟수 안에 목표 시행 횟수를 맞추지 못하면 오류 기록 후 정지.
- **EVALUATE:** `roi_substat` OCR 검증 -> 필터 조건 판별 -> 잠금/폐기.
- **RETURN:** `ESC` 입력으로 에코 리스트 복귀. `remainingCount` 차감 후 SEARCH 재진입.

## 6. Instructions for the AI Agent (Strict Rules)
1. **PoC First (핵심 검증):** 본격적인 구조를 잡기 전, 독립된 PoC(Proof of Concept) 코드를 먼저 작성하여 다음 3가지를 완벽하게 검증하라.
   - `Graphics.CopyFromScreen`을 활용한 인게임 화면 캡처 가능 여부.
   - `System.Drawing.Bitmap` 객체를 WinRT `SoftwareBitmap`으로 변환하여 `Windows.Media.Ocr`로 텍스트를 추출하는 변환/인식 로직.
   - Win32 `SendInput`을 통한 인게임 클릭/키보드 이벤트 정상 수신 여부.
2. **Threading Model:** `Task.Run`을 이용해 UI 스레드와 매크로 스레드를 철저히 분리.
3. **Safe Packaging:** 런타임 오류 방지를 위해 초기 배포는 `PublishTrimmed=true`를 배제하고 `Self-Contained` 및 `PublishSingleFile=true` 조합으로 안정성을 우선 확보할 것.
4. **Input Compatibility:** 마우스 이동은 `SendInput`의 absolute/virtual desktop 좌표 이동 이벤트로 보내고, 클릭 down/up 사이에는 최소 50ms 수준의 지연을 둔다. 앱은 `requireAdministrator` manifest로 실행 권한을 맞춘다.