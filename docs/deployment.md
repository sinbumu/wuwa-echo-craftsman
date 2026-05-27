# 로컬 배포 및 GitHub Release

이 앱은 Windows x64 포터블 앱으로 배포한다. 인디 프로젝트 운영을 전제로 GitHub Actions 같은 CI 파이프라인은 사용하지 않고, 로컬 단말에서 publish/package/release를 수행한다.

설정과 사용자 데이터는 실행 파일 옆 `data/` 폴더에 생성되며, 릴리스 zip에는 앱 실행 파일과 필요한 런타임 산출물만 포함한다.

## 빠른 명령

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Task build
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Task publish
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Task package -Tag v1.0.0
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Task release -Tag v1.0.0
```

## 로컬 Publish

`publish`는 Windows x64 self-contained single-file exe를 만든다.

```text
artifacts/publish/win-x64/
```

## 로컬 Package

`package`는 publish 결과물을 zip으로 묶는다.

```text
artifacts/release/wuwa-echo-craftsman-win-x64-v1.0.0.zip
```

패키징 전에 `data/` 폴더 또는 `history.db`, `history.sqlite`, `history.sqlite3` 같은 런타임 데이터 후보가 포함되어 있으면 스크립트가 중단된다.

## 포터블 데이터 경로

앱은 `AppContext.BaseDirectory`를 기준으로 데이터를 저장한다.

```text
[앱 실행 경로]/data/config.json
[앱 실행 경로]/data/history.sqlite3
[앱 실행 경로]/data/assets/
```

`data/config.json`, `data/assets/`, `data/history.sqlite3`는 런타임 생성 데이터이므로 Git에 포함하지 않는다. 사용자끼리 공유할 설정은 앱 내부의 `설정 내보내기` 기능을 사용한다. 이 기능은 `config.json`과 `assets/*.png`만 포함하며 히스토리 DB는 제외한다.

## GitHub Release 로컬 배포

`release`는 로컬에서 zip을 만든 뒤 GitHub CLI로 Release에 업로드한다.

사전 준비:

```powershell
gh auth login
```

릴리스 실행:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Task release -Tag v1.0.0
```

동작:

1. Release 빌드 및 publish를 수행한다.
2. `artifacts/release/`에 zip을 만든다.
3. 로컬에 태그가 없으면 `git tag v1.0.0`을 생성한다.
4. 태그를 `origin`으로 push한다.
5. GitHub Release가 없으면 생성하고, 이미 있으면 zip asset을 덮어쓴다.

## Release 전 확인

- `dotnet build WutheringWavesEchoCraftsman.sln` 성공
- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Task publish` 성공
- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Task package -Tag v1.0.0` 성공
- publish 결과물의 `WutheringWavesEchoCraftsman.exe` 실행 확인
- 최초 실행 시 `data/`, `data/assets/`, `data/config.json`, `data/history.sqlite3` 생성 확인
- Release zip에 `data/history.sqlite3`가 포함되지 않았는지 확인