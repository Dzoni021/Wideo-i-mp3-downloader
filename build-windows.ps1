$ErrorActionPreference = "Stop"

foreach ($runtime in @("win-x64", "win-x86", "win-arm64")) {
    dotnet publish .\OmniFetch.Windows.csproj `
      --configuration Release `
      --runtime $runtime `
      --self-contained true `
      -p:PublishSingleFile=true `
      -p:EnableCompressionInSingleFile=true `
      --output (Join-Path ".\publish" $runtime)

    $output = Join-Path $PWD "publish\$runtime"
    $ytRelease = Invoke-RestMethod "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest"
    $ytAsset = switch ($runtime) {
        "win-x86" { "yt-dlp_x86.exe" }
        "win-arm64" { "yt-dlp_arm64.exe" }
        default { "yt-dlp.exe" }
    }
    $yt = $ytRelease.assets | Where-Object name -eq $ytAsset | Select-Object -First 1
    Invoke-WebRequest $yt.browser_download_url -OutFile (Join-Path $output "yt-dlp.exe")

    $ffmpegRepo = if ($runtime -eq "win-x86") { "defisym/FFmpeg-Builds-Win32" } else { "BtbN/FFmpeg-Builds" }
    $ffmpegRelease = Invoke-RestMethod "https://api.github.com/repos/$ffmpegRepo/releases/latest"
    $ffmpegAsset = switch ($runtime) {
        "win-x86" { "ffmpeg-master-latest-win32-gpl.zip" }
        "win-arm64" { "ffmpeg-master-latest-winarm64-gpl.zip" }
        default { "ffmpeg-master-latest-win64-gpl.zip" }
    }
    $ffmpeg = $ffmpegRelease.assets | Where-Object name -eq $ffmpegAsset | Select-Object -First 1
    $archive = Join-Path $env:TEMP $ffmpegAsset
    Invoke-WebRequest $ffmpeg.browser_download_url -OutFile $archive
    $extract = Join-Path $env:TEMP ("omnifetch-ffmpeg-" + [guid]::NewGuid().ToString("N"))
    Expand-Archive $archive -DestinationPath $extract
    Copy-Item (Get-ChildItem $extract -Recurse -Filter "ffmpeg.exe" | Select-Object -First 1).FullName $output -Force
    Copy-Item (Get-ChildItem $extract -Recurse -Filter "ffprobe.exe" | Select-Object -First 1).FullName $output -Force
    @{ ytDlpVersion = $ytRelease.tag_name; ffmpegVersion = $ffmpegRelease.tag_name; updatedAtUtc = (Get-Date).ToUniversalTime().ToString("o") } |
      ConvertTo-Json | Set-Content (Join-Path $output "tools.json") -Encoding UTF8
    Copy-Item .\THIRD-PARTY-NOTICES.md $output -Force
    Compress-Archive -Path (Join-Path $output "*") -DestinationPath (Join-Path ".\publish" "OmniFetch-$runtime.zip") -Force
    Remove-Item $archive -Force -ErrorAction SilentlyContinue
    Remove-Item $extract -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Build je završen za win-x64, win-x86 i win-arm64 u folderu publish."
Write-Host "ZIP paketi već sadrže OmniFetch, yt-dlp, ffmpeg, ffprobe i tools.json."
