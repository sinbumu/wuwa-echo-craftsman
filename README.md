# wuwa-echo-craftsman
명조 에코작 자동화 유틸리티입니다.

## Development

```powershell
dotnet restore
dotnet build WutheringWavesEchoCraftsman.sln
dotnet run --project WutheringWavesEchoCraftsman
```

## Publish

```powershell
make publish
make package TAG=v1.0.0
```

GitHub Release까지 로컬에서 올리려면 `gh auth login` 후 실행합니다.

```powershell
make release TAG=v1.0.0
```
