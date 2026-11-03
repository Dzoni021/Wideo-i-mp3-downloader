$ErrorActionPreference = "Stop"

foreach ($runtime in @("win-x64", "win-x86", "win-arm64")) {
    dotnet publish .\OmniFetch.Windows.csproj `
      --configuration Release `
      --runtime $runtime `
      --self-contained true `
      -p:PublishSingleFile=true `
      -p:EnableCompressionInSingleFile=true `
      --output (Join-Path ".\publish" $runtime)
}

Write-Host "Build je završen za win-x64, win-x86 i win-arm64 u folderu publish."
Write-Host "Kopirajte yt-dlp.exe u isti folder kao OmniFetch.exe pre pokretanja."
