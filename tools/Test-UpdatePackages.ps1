param(
    [Parameter(Mandatory)][string]$PreviousRelease,
    [Parameter(Mandatory)][string]$NextRelease,
    [Parameter(Mandatory)][string]$TestDirectory
)
$ErrorActionPreference = 'Stop'
$testRoot = [IO.Path]::GetFullPath($TestDirectory)
if (Test-Path -LiteralPath $testRoot) { throw '請使用新的隔離測試資料夾。' }
$previousPath = [IO.Path]::GetFullPath($PreviousRelease)
$nextPath = [IO.Path]::GetFullPath($NextRelease)
foreach ($releasePath in @($previousPath, $nextPath)) {
    if (!(Test-Path -LiteralPath (Join-Path (Split-Path $releasePath -Parent) 'DO-NOT-PUBLISH.txt'))) {
        throw '只能啟動明確標示的隔離測試套件，不能用正式程式執行此驗收。'
    }
}
New-Item -ItemType Directory -Path $testRoot | Out-Null
Set-Content -LiteralPath (Join-Path $testRoot 'isolated-update-test.marker') -Value 'synthetic data only'
$installPath = Join-Path $testRoot 'app'
Expand-Archive -LiteralPath (Join-Path $previousPath 'ZeroZeroClipboard-win-x64-Portable.zip') -DestinationPath $installPath
$previousRoot = $env:ZEROZERO_UPDATE_TEST_ROOT
$previousFeed = $env:ZEROZERO_UPDATE_TEST_FEED
try {
    $env:ZEROZERO_UPDATE_TEST_ROOT = $testRoot
    $env:ZEROZERO_UPDATE_TEST_FEED = $nextPath
    $process = Start-Process -FilePath (Join-Path $installPath '零零快捷剪貼板.exe') -WorkingDirectory $installPath -WindowStyle Hidden -PassThru
    $acceptance = Join-Path $testRoot 'data/acceptance.json'
    $failure = Join-Path $testRoot 'data/acceptance-error.txt'
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if (Test-Path -LiteralPath $failure) { throw (Get-Content -LiteralPath $failure -Raw) }
        if (Test-Path -LiteralPath $acceptance) {
            Get-Content -LiteralPath $acceptance
            $observed = Get-Content -LiteralPath $acceptance -Raw | ConvertFrom-Json
            $feed = Get-Content -LiteralPath (Join-Path $nextPath 'releases.win-x64.json') -Raw | ConvertFrom-Json
            $expectedVersion = [version]$feed.Assets[0].Version
            $actualVersion = [version]$observed.Version
            if ($actualVersion.Major -ne $expectedVersion.Major -or $actualVersion.Minor -ne $expectedVersion.Minor -or
                $actualVersion.Build -ne $expectedVersion.Build -or !$observed.DraftPreserved -or !$observed.FormalPreserved) {
                throw '重新啟動的版本或資料不符，不能宣告升級成功。'
            }
            $jobPath = Get-ChildItem -LiteralPath (Join-Path $testRoot 'data/updates') -Directory | Select-Object -First 1
            if (!(Test-Path -LiteralPath (Join-Path $jobPath.FullName 'before-update.sqlite')) -or
                !(Test-Path -LiteralPath (Join-Path $jobPath.FullName 'previous/current/ZeroZeroClipboard.exe'))) {
                throw '缺少更新前備份或上一版程式。'
            }
            if ((Get-Content -LiteralPath (Join-Path $jobPath.FullName 'result.json') -Raw | ConvertFrom-Json) -ne 'applied') {
                throw '更新交接沒有完成。'
            }
            Write-Output 'PASS 真實套件替換、重新啟動、草稿保留、畫面恢復與備份保留'
            exit 0
        }
        Start-Sleep -Milliseconds 500
    }
    throw "驗收尚未完成，已保留資料與程序供檢查：$testRoot"
}
finally {
    $env:ZEROZERO_UPDATE_TEST_ROOT = $previousRoot
    $env:ZEROZERO_UPDATE_TEST_FEED = $previousFeed
}
