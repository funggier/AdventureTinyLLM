param(
    [switch]$EnableOnline,
    [string]$Checkpoint = "",
    [int]$Port = 8765
)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$python = Join-Path $root ".venv\Scripts\python.exe"
if (-not (Test-Path -LiteralPath $python)) {
    throw "Missing local Python venv. Install dependencies first (see README.md)."
}
if ($Port -lt 1 -or $Port -gt 65535) { throw "Invalid port" }
$onlinePath = Join-Path $root "checkpoints\tiny-unity-online.pt"
$offlinePath = Join-Path $root "checkpoints\tiny-v1.pt"
if (-not $Checkpoint) {
    if (Test-Path -LiteralPath $onlinePath) { $Checkpoint = $onlinePath }
    elseif (Test-Path -LiteralPath $offlinePath) { $Checkpoint = $offlinePath }
}
if ($Checkpoint) {
    $Checkpoint = [System.IO.Path]::GetFullPath($Checkpoint)
    if (-not (Test-Path -LiteralPath $Checkpoint)) { throw "Checkpoint not found: $Checkpoint" }
    $env:ADVENTURE_TINY_CHECKPOINT = $Checkpoint
} else {
    Remove-Item Env:ADVENTURE_TINY_CHECKPOINT -ErrorAction SilentlyContinue
    Write-Warning "No checkpoint selected: service uses RANDOM weights."
}
$env:ADVENTURE_TINY_ONLINE_TRAIN = if ($EnableOnline) { "1" } else { "0" }
$env:ADVENTURE_TINY_ONLINE_OUTPUT = $onlinePath
Write-Output "AdventureTinyLLM API on http://127.0.0.1:$Port"
Write-Output "Checkpoint: $(if($Checkpoint){$Checkpoint}else{'random'})"
Write-Output "Update weights from real Unity outcomes: $EnableOnline"
Push-Location $root
try {
    & $python -m uvicorn adventure_tiny_llm.api:app --host 127.0.0.1 --port $Port
    if ($LASTEXITCODE -ne 0) { throw "Tiny API failed: exit $LASTEXITCODE" }
} finally {
    Pop-Location
}
