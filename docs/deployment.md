이제 앱의 기본 기능이 잡혔으므로, 깃허브 릴리즈(Release) 배포 및 포터블 앱(Portable App) 구동을 위한 사전 환경 세팅과 경로 리팩토링을 진행하려고 해. 

다음 요구사항을 바탕으로 프로젝트 구조와 파일 I/O 로직을 전면적으로 수정해 줘. 기능 로직은 유지하되, 파일 저장 방식과 빌드 설정만 최적화해야 해.

### 1. 파일 저장 경로 정책 개편 (포터블 방식)
- 설정 파일(`config.json`)과 이미지 에셋 폴더(`assets/`), 그리고 SQLite 데이터베이스(`history.db`)의 저장 위치를 시스템 절대 경로(AppData 등)가 아닌, **실행 파일(.exe) 바로 옆의 `data/` 폴더 내부**로 통일한다.
- 코드 내에서 경로를 결합할 때 절대 하드코딩하지 말고, 반드시 **`AppContext.BaseDirectory`를 기준**으로 삼는 상대 경로로 결합(`Path.Combine`)하도록 수정해 줘.
- 예시 경로 구조:
  - `[앱 실행 경로]/data/config.json`
  - `[앱 실행 경로]/data/history.db`
  - `[앱 실행 경로]/data/assets/`

### 2. 앱 시작 시 초기 폴더 및 파일 생성 로직 보완
- 앱이 켜질 때(`App.xaml.cs` 또는 초기화 단계), 실행 경로 옆에 `data/` 폴더와 `data/assets/` 폴더가 존재하는지 자동으로 검사(`Directory.Exists`)하는 로직을 추가해 줘.
- 만약 해당 폴더들이 없다면 즉시 안전하게 생성(`Directory.CreateDirectory`)하여, 최초 실행 시 경로 미존재로 인한 크래시(IOException)가 발생하지 않도록 방어 코드를 작성해 줘.

### 3. 프로젝트 파일 (.csproj) 빌드 속성 정의
- 단일 파일 빌드(Single-File) 및 외부 런타임 독립 실행(Self-Contained)이 기본 프로필로 작동할 수 있도록 `WutheringWavesEchoCraftsman.csproj` 파일의 `<PropertyGroup>` 내부에 다음 속성들을 추가하거나 최적화해 줘.
```xml
<PropertyGroup>
  <PublishSingleFile>true</PublishSingleFile>
  <SelfContained>true</SelfContained>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <ReadyToRun>true</ReadyToRun>
  
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  
  <ApplicationIcon>Assets\app_icon.ico</ApplicationIcon>
</PropertyGroup>
```
4. 파일 잠금(File Lock) 및 동시성 예외 방지
앞서 기획한 Import/Export 기능이나 설정 세이브/로드 시, 앱이 config.json이나 history.db 파일을 붙잡고 있어서 "프로세스가 파일에 접근할 수 없다"는 에러가 발생하지 않도록, 모든 파일 읽기/쓰기 스트림(FileStream, StreamReader/Writer)에 using 문을 철저히 적용하여 사용 직후 파일 락이 즉시 해제되도록 검토해 줘.

이 세팅이 완료되면 내가 터미널에서 dotnet publish 명령어를 통해 단일 .exe 파일을 정상적으로 뽑아낼 수 있는지 준비 작업을 마친 뒤 알려줘.


---

### 💡 팁: 에이전트 작업 완료 후 확인할 점
에이전트가 작업을 마치고 나면, 코드 내에서 설정을 읽고 쓰는 모든 함수(예: `SaveConfig()`, `LoadConfig()`)가 아래와 유사한 형태로 `AppContext.BaseDirectory`를 잘 활용하고 있는지 확인하시면 세팅이 완벽하게 끝난 것입니다.

```csharp
// 에이전트가 반영해야 하는 올바른 C# 경로 설정 예시
string dataFolderPath = Path.Combine(AppContext.BaseDirectory, "data");
string configPath = Path.Combine(dataFolderPath, "config.json");