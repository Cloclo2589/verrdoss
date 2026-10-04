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

$layout = Join-Path $installer "layout"
if (Test-Path $layout) {
    Remove-Item $layout -Recurse -Force
}
New-Item -ItemType Directory $layout | Out-Null
$shellFiles = @(
    "Verrdoss.exe",
    "Verrdoss.Context.comhost.dll",
    "Verrdoss.Context.dll",
    "Verrdoss.Context.runtimeconfig.json",
    "Verrdoss.Context.deps.json"
)
foreach ($name in $shellFiles) {
    $source = Join-Path $staging $name
    if (-not (Test-Path $source)) {
        throw "Fichier manquant pour le menu contextuel : $name"
    }
    Copy-Item $source $layout
}
$pack = Start-Process -FilePath (Join-Path $staging "Verrdoss.exe") -ArgumentList @("--pack-shell", $layout) -Wait -PassThru
if ($pack.ExitCode -ne 0 -or -not (Test-Path (Join-Path $layout "AppxManifest.xml"))) {
    throw "La préparation du menu contextuel a échoué."
}

$certDir = Join-Path $installer "certs"
$rootCer = Join-Path $certDir "Verrdoss.Root.cer"
$leafCer = Join-Path $certDir "Verrdoss.Shell.cer"
$pfx = Join-Path $certDir "Verrdoss.Shell.pfx"
if (-not (Test-Path $rootCer) -or -not (Test-Path $pfx)) {
    throw "Certificats introuvables dans installer\certs."
}
$signer = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq "CN=VerrDoss" -and $_.Issuer -like "CN=VerrDoss Root*" -and $_.HasPrivateKey } | Select-Object -First 1
if (-not $signer) {
    $password = ConvertTo-SecureString "VerrDoss-shell" -Force -AsPlainText
    $signer = Import-PfxCertificate -FilePath $pfx -CertStoreLocation Cert:\CurrentUser\My -Password $password
}
$kitRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
$signTool = Get-ChildItem $kitRoot -Recurse -Filter signtool.exe | Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
$makeAppx = Get-ChildItem $kitRoot -Recurse -Filter makeappx.exe | Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signTool -or -not $makeAppx) {
    throw "Le SDK Windows (signtool, makeappx) est introuvable."
}
$msix = Join-Path $staging "Verrdoss.Shell.msix"
& $makeAppx.FullName pack /d $layout /p $msix /o /nv
if ($LASTEXITCODE -ne 0) {
    throw "La création du paquet du menu a échoué."
}
& $signTool.FullName sign /fd SHA256 /sha1 $signer.Thumbprint /ac $rootCer $msix
if ($LASTEXITCODE -ne 0) {
    throw "La signature du menu a échoué."
}
Copy-Item $rootCer, $leafCer $staging -Force
Remove-Item $layout -Recurse -Force

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
