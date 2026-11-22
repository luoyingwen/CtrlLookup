$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet restore SelectionPreview.sln --locked-mode
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    dotnet run --project tests/SelectionPreview.Tests -c Release --no-restore
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    dotnet publish src/SelectionPreview -c Release --no-restore --self-contained false -o dist
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
