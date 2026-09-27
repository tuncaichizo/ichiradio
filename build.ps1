# Ichi Radyo — © 2026 Kripto Ichizo (github.com/tuncaichizo). Tüm hakları saklıdır. Bkz. LICENSE
# Ichi Radyo derleme. Windows'la gelen .NET Framework C# derleyicisini kullanır; ek kurulum gerekmez.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$exe = Join-Path $root 'IchiRadyo.exe'
$ico = Join-Path $root 'ikon.ico'
$src = Join-Path $root 'IchiRadyo.cs'

# x86: 32 bit süreç daha az RAM kullanır.
$opts = @('/nologo', '/target:winexe', '/optimize+', '/platform:x86',
    '/r:System.dll', '/r:System.Core.dll', '/r:Microsoft.CSharp.dll', '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll',
    "/resource:$root\harita.bin,harita.bin")  # ülke sınırları: node harita-hazirla.js ile üretilir

Get-Process IchiRadyo -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

# 1) ikonsuz derle, 2) exe ile ikon üret, 3) ikonla yeniden derle.
if (-not (Test-Path $ico)) {
    & $csc @opts "/out:$exe" $src
    if ($LASTEXITCODE) { throw 'Derleme başarısız' }
    Start-Process $exe -ArgumentList '--ikon', "`"$ico`"" -Wait
}
& $csc @opts "/win32icon:$ico" "/out:$exe" $src
if ($LASTEXITCODE) { throw 'Derleme başarısız (ikon)' }
Write-Host "Hazır: $exe"

