param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$Thumbprint,
    [Parameter(Mandatory=$true)][string]$SignToolPath,
    [ValidateSet('CurrentUser','LocalMachine')][string]$CertificateStore = 'CurrentUser',
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not (Test-Path -LiteralPath $SignToolPath -PathType Leaf)) { throw 'Supply the Microsoft Windows SDK SignTool executable path.' }
$toolSignature = Get-AuthenticodeSignature -LiteralPath $SignToolPath
if ($toolSignature.Status -ne 'Valid' -or $toolSignature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') { throw 'SignTool must have a valid Microsoft signature.' }
$certificate = Get-ChildItem "Cert:\$CertificateStore\My" -CodeSigningCert | Where-Object Thumbprint -EQ $Thumbprint
if (-not $certificate -or -not $certificate.HasPrivateKey) { throw 'The selected code-signing certificate/private key is not available. Connect the token or configure your signing provider first.' }
if ($certificate.NotAfter -lt (Get-Date) -or $certificate.NotBefore -gt (Get-Date)) { throw 'Certificate is not currently valid.' }
if ($certificate.Subject -eq $certificate.Issuer) { throw 'A self-signed certificate is not suitable for this publicly trusted release.' }
$chain = [System.Security.Cryptography.X509Certificates.X509Chain]::new()
try {
    if (-not $chain.Build($certificate)) { throw 'Certificate chain validation failed. Check the certificate/provider configuration.' }
} finally { $chain.Dispose() }
# Rebuild before signing; never rebuild a signed binary afterwards.
& (Join-Path $PSScriptRoot 'build.ps1')
$release = Join-Path $PSScriptRoot ('release\signed-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path (Join-Path $release 'src'),(Join-Path $release 'results') -Force | Out-Null
foreach ($name in @('AmazonPincodeChecker-v3.exe','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll','pincode.txt','README.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $release
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\page.js') -Destination (Join-Path $release 'src')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'lib\webview2\LICENSE.txt') -Destination (Join-Path $release 'WebView2-LICENSE.txt')
$exe = Join-Path $release 'AmazonPincodeChecker-v3.exe'
$signArgs = @('sign','/sha1',$Thumbprint,'/s','My','/fd','SHA256','/tr',$TimestampUrl,'/td','SHA256')
if ($CertificateStore -eq 'LocalMachine') { $signArgs += '/sm' }
$signArgs += $exe
& $SignToolPath @signArgs
if ($LASTEXITCODE -ne 0) { throw 'Signing failed. The staging folder is not a signed release.' }
foreach ($binary in Get-ChildItem -LiteralPath $release -File | Where-Object Extension -In '.exe','.dll') {
    & $SignToolPath verify /pa /v $binary.FullName
    if ($LASTEXITCODE -ne 0) { throw ('Signature verification failed: ' + $binary.Name) }
}
$signature = Get-AuthenticodeSignature -LiteralPath $exe
if ($signature.Status -ne 'Valid' -or -not $signature.TimeStamperCertificate -or $signature.SignerCertificate.Thumbprint -ne $Thumbprint) { throw 'Publisher or timestamp verification failed.' }
Get-ChildItem -LiteralPath $release -File -Recurse | Get-FileHash -Algorithm SHA256 | ForEach-Object {
    $_.Hash + '  ' + $_.Path.Substring($release.Length+1)
} | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding UTF8
Write-Output "Signed and verified release: $release"
Write-Output 'Launch the EXE directly. Local signature verification does not guarantee that every Windows policy or antivirus will allow it.'
