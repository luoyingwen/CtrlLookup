$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet restore CtrlLookup.sln --locked-mode
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    dotnet run --project tests/CtrlLookup.Tests -c Release --no-restore
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    $output = Join-Path $PSScriptRoot 'dist/CtrlLookup'
    dotnet publish src/CtrlLookup -c Release --no-restore --self-contained false -o $output
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    dotnet publish tools/CtrlLookup.DictionarySetup -c Release --no-restore --self-contained false -o (Join-Path $output 'dictionary-setup')
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    Copy-Item -LiteralPath 'Setup-Dictionary.cmd', 'Setup-Dictionary.ps1' -Destination $output -Force
} finally {
    Pop-Location
}
