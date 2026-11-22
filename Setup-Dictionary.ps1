param([switch]$Force)

$ErrorActionPreference = 'Stop'
# Match the app's legacy directory so upgrades can reuse installed dictionaries.
$folder = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SelectionPreview'
$revision = '39cfb9672c0906b63569e0453c2e6abfb9ab182a'
$dictionaries = @(
    @{
        Direction = 'english'; Name = 'ECDICT'; Size = '66 MB'; File = 'dictionary-v1.sqlite'; Extension = '.csv'
        Uri = "https://raw.githubusercontent.com/skywind3000/ECDICT/$revision/ecdict.csv"
        Sha256 = 'd1ecbdf9ee2b920afb6f430ca6b6bb9f559588cf0dff86e04bd255f9390843cc'
    }
    @{
        Direction = 'chinese'; Name = 'CC-CEDICT'; Size = '4 MB'; File = 'dictionary-zh-v1.sqlite'; Extension = '.txt.gz'
        Uri = 'https://www.mdbg.net/chinese/export/cedict/cedict_1_0_ts_utf-8_mdbg.txt.gz'
    }
)
$pending = @(foreach ($dictionary in $dictionaries) {
    if ($Force -or -not (Test-Path -LiteralPath (Join-Path $folder $dictionary.File) -PathType Leaf)) { $dictionary }
})
if ($pending.Count -eq 0) {
    Write-Output 'Both dictionaries are already installed (use -Force to replace).'
    return
}

$installer = Join-Path $PSScriptRoot 'dictionary-setup\CtrlLookup.DictionarySetup.exe'
$useBundledInstaller = Test-Path -LiteralPath $installer -PathType Leaf
if (-not $useBundledInstaller) {
    $project = Join-Path $PSScriptRoot 'tools\CtrlLookup.DictionarySetup\CtrlLookup.DictionarySetup.csproj'
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw 'Dictionary installer is missing. Use the complete CtrlLookup publish folder or run this script from the source checkout.'
    }
    dotnet restore $project --locked-mode
    if ($LASTEXITCODE) { throw 'Dictionary setup dependencies could not be restored.' }
}
$mode = if ($Force) { '--replace' } else { '--install' }
$failed = @()
foreach ($dictionary in $pending) {
    $tempFile = Join-Path ([IO.Path]::GetTempPath()) ('CtrlLookup-{0}{1}' -f [guid]::NewGuid().ToString('N'), $dictionary.Extension)
    try {
        Write-Output "Downloading $($dictionary.Name) (approximately $($dictionary.Size))..."
        Invoke-WebRequest -UseBasicParsing -Uri $dictionary.Uri -OutFile $tempFile
        if ($dictionary.Sha256 -and (Get-FileHash -LiteralPath $tempFile -Algorithm SHA256).Hash -ne $dictionary.Sha256) {
            throw 'Source checksum mismatch. The installed dictionary was not changed.'
        }
        if ($useBundledInstaller) {
            & $installer $dictionary.Direction $tempFile $mode
        } else {
            dotnet run --project $project -c Release --no-restore -- $dictionary.Direction $tempFile $mode
        }
        if ($LASTEXITCODE) { throw 'Dictionary installation failed.' }
    } catch {
        $failed += $dictionary.Name
        Write-Warning "$($dictionary.Name): $($_.Exception.Message)"
    } finally {
        if (Test-Path -LiteralPath $tempFile) { Remove-Item -LiteralPath $tempFile }
    }
}
if ($failed.Count -gt 0) { throw "Could not install: $($failed -join ', ')." }
