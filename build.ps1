$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /out:AmazonPincodeChecker-v3.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:Microsoft.Web.WebView2.Core.dll /reference:Microsoft.Web.WebView2.WinForms.dll src\App.cs src\BrowserWorker.cs src\WorkQueue.cs
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
