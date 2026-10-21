param(
    [string]$Version = '',
    [string]$OutputDirectory = '',
    [switch]$UpdateTestBuild
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$projectFile = Join-Path $projectRoot 'ClipboardApp/ClipboardApp.csproj'
[xml]$project = Get-Content -LiteralPath $projectFile -Raw
$projectVersion = [string]$project.Project.PropertyGroup.Version
if (!$Version) { $Version = $projectVersion }
if (!$UpdateTestBuild -and $Version -ne $projectVersion) {
    throw '正式發布版本必須與 ClipboardApp.csproj 的 Version 相同；請先更新專案版本。'
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '版本必須是三段正式版本號。' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $projectRoot "artifacts/releases/$Version" }
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) { throw "輸出資料夾已存在，請使用新的位置：$outputPath" }
$vpk = Join-Path $projectRoot '.build-tools/vpk.exe'
if (!(Test-Path -LiteralPath $vpk)) { throw '請先將 vpk 1.2.161 安裝到專案的 .build-tools 資料夾。' }
$toolVersion = (& dotnet tool list --tool-path (Join-Path $projectRoot '.build-tools')) -join "`n"
if ($LASTEXITCODE -ne 0 -or $toolVersion -notmatch '(?m)^vpk\s+1\.2\.161\s') { throw 'vpk 版本必須固定為 1.2.161。' }
$buildPath = Join-Path $outputPath 'build'
$releasePath = Join-Path $outputPath 'releases'
$packagePath = Join-Path $projectRoot '.build-packages'
$testProperty = if ($UpdateTestBuild) { '-p:UpdateTestBuild=true' } else { '-p:UpdateTestBuild=false' }
& dotnet publish (Join-Path $projectRoot 'ClipboardApp/ClipboardApp.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false "-p:Version=$Version" $testProperty --packages $packagePath -p:RestoreSources=https://api.nuget.org/v3/index.json -o $buildPath
if ($LASTEXITCODE -ne 0) { throw '主程式發布建置失敗。' }
$helperPath = Join-Path $outputPath 'handoff'
& dotnet publish (Join-Path $projectRoot 'ClipboardUpdateHandoff/ClipboardUpdateHandoff.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none --packages $packagePath -p:RestoreSources=https://api.nuget.org/v3/index.json -o $helperPath
if ($LASTEXITCODE -ne 0) { throw '交接程序發布建置失敗。' }
Copy-Item -LiteralPath (Join-Path $helperPath 'ClipboardUpdateHandoff.exe') -Destination $buildPath
& $vpk pack --packId ZeroZeroClipboard --packVersion $Version --packDir $buildPath --mainExe ZeroZeroClipboard.exe --packTitle '零零快捷剪貼板' --icon (Join-Path $projectRoot 'assets/00monstericon.ico') --runtime win-x64 --channel win-x64 --delta None --outputDir $releasePath
if ($LASTEXITCODE -ne 0) { throw '更新套件打包失敗。' }
if ($UpdateTestBuild) { Set-Content -LiteralPath (Join-Path $outputPath 'DO-NOT-PUBLISH.txt') -Value '隔離驗證版本，包含測試入口，不可上傳正式發布。' }
Get-ChildItem -LiteralPath $releasePath -File | Select-Object Name, Length
