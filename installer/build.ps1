$ErrorActionPreference = "Stop"
$installer = $PSScriptRoot
$repo = Split-Path $installer -Parent
$project = Join-Path $repo "src\Verrdoss.App\Verrdoss.App.csproj"
$staging = Join-Path $installer "staging"
$csproj = Get-Content $project -Raw
if ($csproj -notmatch '<Version>([^<]+)</Version>') {
    throw "Version introuvable dans le projet."
}
$version = $Matches[1].Trim()

if (Test-Path $staging) {
    Remove-Item $staging -Recurse -Force
}
dotnet publish $project -c Release -r win-x64 --self-contained true -o $staging
if ($LASTEXITCODE -ne 0) {
    throw "La publication a échoué."
}

function Find-Iscc {
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    return $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

$iscc = Find-Iscc
if (-not $iscc) {
    winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements
    $iscc = Find-Iscc
}
if (-not $iscc) {
    throw "Inno Setup 6 est introuvable."
}

& $iscc "/DMyAppVersion=$version" (Join-Path $installer "verrdoss.iss")
if ($LASTEXITCODE -ne 0) {
    throw "La compilation de l'installateur a échoué."
}
Write-Host "Installateur : $(Join-Path $installer 'output\VerrdossSetup.exe')"
