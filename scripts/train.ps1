param(
    [int]$Steps = 200,
    [int]$BatchSize = 32,
    [int]$Seed = 1234
)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$python = Join-Path $root ".venv\Scripts\python.exe"
if (-not (Test-Path $python)) { throw "Missing local .venv Python; see README." }
if ($Steps -lt 1 -or $BatchSize -lt 1) { throw "Steps/BatchSize must be positive" }
Push-Location $root
try {
    & $python -m adventure_tiny_llm.training --steps $Steps --batch-size $BatchSize --seed $Seed --output checkpoints/tiny-v1.pt
    if ($LASTEXITCODE -ne 0) { throw "Tiny native training failed" }
} finally {
    Pop-Location
}
