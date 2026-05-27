# wuwa-echo-craftsman

명조 에코작을 조금 덜 귀찮게 하기 위한 Windows 자동화 유틸리티입니다. 에코 목록에서 +0 에코를 찾고, 설정한 목표 레벨까지 강화한 뒤, 부옵션 조건에 따라 잠금/폐기 판단을 보조합니다.

## 주요 기능

- 에코 강화와 옵티마이즈 과정을 자동화합니다.
- 목표 레벨, 반복 횟수, 옵티마이즈 시행 횟수를 앱에서 설정할 수 있습니다.
- 부옵션 조건을 설정해 잠금/폐기 판단을 자동화할 수 있습니다.
- 좌표와 이미지 에셋 캘리브레이션을 앱 안에서 관리합니다.
- 설정 내보내기/불러오기로 다른 사용자와 캘리브레이션 프로필을 공유할 수 있습니다.
- 다크 모드, 트레이 아이콘, 포터블 실행을 지원합니다.

## 다운로드 및 실행

1. GitHub Releases에서 최신 `wuwa-echo-craftsman-win-x64-*.zip` 파일을 다운로드합니다.
2. 원하는 폴더에 압축을 풉니다.
3. `WutheringWavesEchoCraftsman.exe`를 실행합니다.
4. 최초 실행 후 `자동화 설정 관리`에서 본인 화면에 맞게 좌표와 이미지를 캘리브레이션합니다.

앱 설정, 캘리브레이션 이미지, 히스토리는 실행 파일 옆 `data/` 폴더에 저장됩니다. 앱 폴더를 통째로 옮기면 설정도 함께 이동합니다.

## 사용 안내

자세한 사용법은 추후 별도 영상으로 안내할 예정입니다. 우선 앱 안의 버튼 순서대로 `자동화 설정 관리`를 완료하고, `부옵션 설정`에서 원하는 조건을 정한 뒤 `자동화 시작`을 사용하면 됩니다.

## 개발자용 명령

필요 환경:

- Windows
- .NET 8 SDK
- PowerShell

```powershell
dotnet restore
dotnet build WutheringWavesEchoCraftsman.sln
dotnet run --project WutheringWavesEchoCraftsman
```

## 로컬 배포 명령

단일 exe publish:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Task publish
```

릴리스 zip 패키징:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Task package -Tag v1.0.0
```

GitHub Release까지 로컬에서 올리려면 `gh auth login` 후 실행합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Task release -Tag v1.0.0
```

배포 상세 내용은 `docs/deployment.md`를 참고하세요.
