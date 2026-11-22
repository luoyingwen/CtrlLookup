param([switch]$Force)

$ErrorActionPreference = 'Stop'
$database = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SelectionPreview\dictionary-v1.sqlite'
if ((Test-Path -LiteralPath $database) -and -not $Force) {
    Write-Output "Dictionary already installed: $database (use -Force to replace)."
    return
}

$revision = '39cfb9672c0906b63569e0453c2e6abfb9ab182a'
$sha256 = 'd1ecbdf9ee2b920afb6f430ca6b6bb9f559588cf0dff86e04bd255f9390843cc'
$uri = "https://raw.githubusercontent.com/skywind3000/ECDICT/$revision/ecdict.csv"
$tempCsv = Join-Path ([IO.Path]::GetTempPath()) ("SelectionPreview-ecdict-{0}.csv" -f [guid]::NewGuid().ToString('N'))
$project = Join-Path $PSScriptRoot 'tools\SelectionPreview.DictionarySetup\SelectionPreview.DictionarySetup.csproj'
try {
    Write-Output 'Downloading ECDICT (approximately 66 MB)...'
    Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $tempCsv
    if ((Get-FileHash -LiteralPath $tempCsv -Algorithm SHA256).Hash -ne $sha256) {
        throw 'ECDICT checksum mismatch. The installed dictionary was not changed.'
    }
    dotnet restore $project --locked-mode
    if ($LASTEXITCODE) { throw 'Dictionary setup dependencies could not be restored.' }
    $mode = if ($Force) { '--replace' } else { '--install' }
    dotnet run --project $project -c Release --no-restore -- $tempCsv $mode
    if ($LASTEXITCODE) { throw 'Dictionary installation failed.' }
} finally {
    if (Test-Path -LiteralPath $tempCsv) { Remove-Item -LiteralPath $tempCsv }
}
