# Signs a file with the AVErs code signing certificate (Authenticode, SHA-256, RFC 3161 timestamp).
# Called by the Package target of VSAgentBridge.csproj for vsagentbridge.exe in Release builds.
# The key lives outside the repository; a missing key fails the build so an unsigned package cannot appear unnoticed.
param(
    [Parameter(Mandatory = $true)][string]$TargetPath,
    [string]$SigningDir = "D:\Hell\MYPROGS\_Signing"
)

$ErrorActionPreference = "Stop"

$pfx = Join-Path $SigningDir "AVErs-codesign.pfx"
$passwordFile = Join-Path $SigningDir "password.txt"
if (-not (Test-Path $pfx)) { throw "Signing key not found: $pfx" }
if (-not (Test-Path $passwordFile)) { throw "Signing key password not found: $passwordFile" }
if (-not (Test-Path $TargetPath)) { throw "File to sign not found: $TargetPath" }
$password = (Get-Content $passwordFile -Raw).Trim()

# Newest signtool from the installed Windows SDKs (bin\<version>\x64\signtool.exe).
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
    Select-Object -First 1
if (-not $signtool) { throw "signtool.exe not found; install a Windows SDK (Visual Studio Installer, Individual components)." }

& $signtool.FullName sign /f $pfx /p $password /fd sha256 /tr http://timestamp.digicert.com /td sha256 $TargetPath
if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE for $TargetPath" }