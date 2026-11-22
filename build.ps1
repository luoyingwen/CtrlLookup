$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet restore CtrlLookup.sln --locked-mode
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    dotnet run --project tests/CtrlLookup.Tests -c Release --no-restore
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    dotnet publish src/CtrlLookup -c Release --no-restore --self-contained false -o dist/CtrlLookup
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
