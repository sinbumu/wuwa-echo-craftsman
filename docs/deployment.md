# 배포 및 GitHub Release

이 앱은 Windows x64 포터블 앱으로 배포한다. 설정과 사용자 데이터는 실행 파일 옆 `data/` 폴더에 생성되며, 릴리스 zip에는 기본적으로 앱 실행 파일과 필요한 런타임 산출물만 포함한다.

## 로컬 Publish

```powershell
dotnet publish WutheringWavesEchoCraftsman `
  -p:PublishProfile=win-x64-single-file
```

산출물은 아래 경로에 생성된다.

```text
WutheringWavesEchoCraftsman/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/
```

## 포터블 데이터 경로

앱은 `AppContext.BaseDirectory`를 기준으로 데이터를 저장한다.

```text
[앱 실행 경로]/data/config.json
[앱 실행 경로]/data/history.sqlite3
[앱 실행 경로]/data/assets/
```

`data/config.json`, `data/assets/`, `data/history.sqlite3`는 런타임 생성 데이터이므로 Git에 포함하지 않는다. 사용자끼리 공유할 설정은 앱 내부의 `설정 내보내기` 기능을 사용한다. 이 기능은 `config.json`과 `assets/*.png`만 포함하며 히스토리 DB는 제외한다.

## GitHub Release 자동 배포

릴리스는 `.github/workflows/release.yml`에서 처리한다.

1. 릴리스 태그를 만든다.

```powershell
git tag v1.0.0
git push origin v1.0.0
```

2. GitHub Actions가 Windows x64 self-contained single-file publish를 실행한다.
3. publish 폴더를 `wuwa-echo-craftsman-win-x64-vX.Y.Z.zip`으로 압축한다.
4. 같은 태그의 GitHub Release를 생성하고 zip 파일을 업로드한다.

수동으로 실행하려면 GitHub Actions의 `Release` 워크플로에서 `workflow_dispatch`를 선택하고 기존 태그 이름을 입력한다.

## Release 전 확인

- `dotnet build WutheringWavesEchoCraftsman.sln` 성공
- `dotnet publish WutheringWavesEchoCraftsman -p:PublishProfile=win-x64-single-file` 성공
- publish 결과물의 `WutheringWavesEchoCraftsman.exe` 실행 확인
- 최초 실행 시 `data/`, `data/assets/`, `data/config.json`, `data/history.sqlite3` 생성 확인
- GitHub Release zip에 `data/history.sqlite3`가 포함되지 않았는지 확인