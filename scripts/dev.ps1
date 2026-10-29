$ErrorActionPreference = "Stop"

$viteProcess = Start-Process -FilePath "npm" -ArgumentList "run dev" `
    -WorkingDirectory "$PSScriptRoot\..\src\WebApp" `
    -PassThru -NoNewWindow

Write-Host "Waiting for Vite dev server on http://localhost:5223..."
$timeout = 30
$elapsed = 0
while ($elapsed -lt $timeout) {
    try {
        $null = Invoke-WebRequest -Uri "http://localhost:5223" -TimeoutSec 1 -ErrorAction Stop
        Write-Host "Vite dev server is ready."
        break
    } catch {
        Start-Sleep -Seconds 1
        $elapsed++
    }
}
if ($elapsed -ge $timeout) {
    Write-Error "Vite dev server did not start within ${timeout}s"
    Stop-Process -Id $viteProcess.Id -ErrorAction SilentlyContinue
    exit 1
}

try {
    Set-Location "$PSScriptRoot\..\src\Murmur.UI"
    dotnet run -p:SkipFrontendBuild=true
}
finally {
    Stop-Process -Id $viteProcess.Id -ErrorAction SilentlyContinue
    Write-Host "Dev servers stopped."
}
