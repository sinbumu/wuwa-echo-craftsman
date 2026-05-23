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
1. 3초 딜레이 후 현재 화면을 캡처하여 WPF 무테두리 전체화면 윈도우 배경으로 띄움.
2. 드래그(RubberBand)를 통해 영역 지정.
3. **수집 타겟 (총 12종):**
   - **[ROI 4종]:** `roi_list`, `roi_level`, `roi_substat`, `roi_material`
   - **[Asset 8종]:** `template_plus_zero.png`, `icon_discard.png`, `icon_exp.png`, `btn_enhance_tab.png`, `btn_slot_plus.png`, `btn_enhance_confirm.png`, `btn_optimize_tab.png`, `btn_optimize_confirm.png`

### 5.3. 부옵션 필터링 및 OCR 정규화
- **정규화:** WinRT OCR 결과에서 공백/특수문자 제거 후 13종 표준 명칭으로 치환.
- 목표 유효 옵션 개수 충족 시 `C`(잠금), 미달 시 `Z`(폐기) 전송 (클릭 1회 보정). DB 기록.

### 5.4. 자동화 상태 머신 (Task-Driven State Machine)
- **상태 머신은 각 루프 시작 시 GUI에서 설정한 `remainingCount`를 확인하고, 0 이하이면 정상 종료한다.**
- **SEARCH:** `roi_list`에서 `template_plus_zero.png` 매칭 -> 매칭 좌표 클릭 -> `btn_enhance_tab.png` 클릭. (매칭 실패 시 **정상 종료**)
- **ENHANCE:** `roi_level` OCR 판독 -> 목표 레벨 도달 시 OPTIMIZE 이동. 미달 시 `btn_slot_plus` 클릭 -> `icon_discard` 탐색/클릭, 없으면 `icon_exp` 클릭 -> `btn_enhance_confirm` 클릭 -> 딜레이 후 루프. (재료 소진 시 **비상 종료**)
- **OPTIMIZE:** `btn_optimize_tab` 클릭. 이후 `btn_optimize_confirm` 매칭 실패, 버튼 비활성화 감지, 또는 `roi_substat` OCR 결과 갱신 중 하나를 완료 조건으로 삼아 개방 로직을 수행.
- **EVALUATE:** `roi_substat` OCR 검증 -> 필터 조건 판별 -> 잠금/폐기.
- **RETURN:** `ESC` 입력으로 에코 리스트 복귀. `remainingCount` 차감 후 SEARCH 재진입.

## 6. Instructions for the AI Agent (Strict Rules)
1. **PoC First (핵심 검증):** 본격적인 구조를 잡기 전, 독립된 PoC(Proof of Concept) 코드를 먼저 작성하여 다음 3가지를 완벽하게 검증하라.
   - `Graphics.CopyFromScreen`을 활용한 인게임 화면 캡처 가능 여부.
   - `System.Drawing.Bitmap` 객체를 WinRT `SoftwareBitmap`으로 변환하여 `Windows.Media.Ocr`로 텍스트를 추출하는 변환/인식 로직.
   - Win32 `SendInput`을 통한 인게임 클릭/키보드 이벤트 정상 수신 여부.
2. **Threading Model:** `Task.Run`을 이용해 UI 스레드와 매크로 스레드를 철저히 분리.
3. **Safe Packaging:** 런타임 오류 방지를 위해 초기 배포는 `PublishTrimmed=true`를 배제하고 `Self-Contained` 및 `PublishSingleFile=true` 조합으로 안정성을 우선 확보할 것.