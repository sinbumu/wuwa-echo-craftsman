# PaddleOCR 한국어 모델 다운로드 (PaddleOCRSharp 연동용)
# 사용: powershell -ExecutionPolicy Bypass -File scripts/download-paddle-korean-models.ps1

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$modelRoot = Join-Path $repoRoot "WutheringWavesEchoCraftsman\data\models"
New-Item -ItemType Directory -Force -Path $modelRoot | Out-Null

$recTarUrl = "https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/korean_PP-OCRv5_mobile_rec_infer.tar"
$dictUrl = "https://raw.githubusercontent.com/PaddlePaddle/PaddleOCR/main/ppocr/utils/dict/korean_dict.txt"

$tempDir = Join-Path ([IO.Path]::GetTempPath()) ("paddle-ko-" + [Guid]::NewGuid().ToString("n"))
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

try {
    Write-Host "1/2 한국어 rec 모델 다운로드..." -ForegroundColor Cyan
    $recTar = Join-Path $tempDir "korean_PP-OCRv5_mobile_rec_infer.tar"
    Invoke-WebRequest -Uri $recTarUrl -OutFile $recTar

    Write-Host "2/2 korean_dict.txt 다운로드..." -ForegroundColor Cyan
    $dictPath = Join-Path $modelRoot "korean_dict.txt"
    Invoke-WebRequest -Uri $dictUrl -OutFile $dictPath

    Write-Host "rec 모델 압축 해제..." -ForegroundColor Cyan
    tar -xf $recTar -C $tempDir
    $extractedRec = Get-ChildItem -Path $tempDir -Recurse -Directory |
        Where-Object { $_.Name -eq "korean_PP-OCRv5_mobile_rec_infer" } |
        Select-Object -First 1

    if (-not $extractedRec) {
        throw "압축 파일에서 korean_PP-OCRv5_mobile_rec_infer 폴더를 찾지 못했습니다."
    }

    $targetRec = Join-Path $modelRoot "korean_PP-OCRv5_mobile_rec_infer"
    if (Test-Path $targetRec) {
        Remove-Item -Recurse -Force $targetRec
    }
    Copy-Item -Recurse -Force $extractedRec.FullName $targetRec

    Write-Host ""
    Write-Host "완료. 아래 파일이 준비되었습니다:" -ForegroundColor Green
    Write-Host "  $targetRec"
    Write-Host "  $dictPath"
    Write-Host ""
    Write-Host "det 모델은 NuGet에 포함된 inference/PP-OCRv5_mobile_det_infer 를 자동 사용합니다." -ForegroundColor Yellow
    Write-Host "dotnet build 후 dotnet run 으로 실행하세요." -ForegroundColor Yellow
}
finally {
    if (Test-Path $tempDir) {
        Remove-Item -Recurse -Force $tempDir
    }
}
