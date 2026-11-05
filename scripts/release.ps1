#requires -Version 5.1
<#
Builds, signs and verifies the customer APK for the version in ForgeLinkSms.csproj.
With -Publish it also uploads it to cloudcompassai.com (APK first, latest.json last) so
installed copies and new buyers get it. Without -Publish nothing leaves this machine.

  scripts\release.ps1 -Notes "What changed."             # build + verify only
  scripts\release.ps1 -Notes "What changed." -Publish    # ...and publish
#>
param(
    [Parameter(Mandatory)][string]$Notes,
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $root 'src\ForgeLinkSms\ForgeLinkSms.csproj'
$website = Join-Path (Split-Path $root -Parent) 'Cloud Compass Website'
$keys = Join-Path $env:USERPROFILE 'ForgeLink-Keys'
$buildTools = 'C:\Program Files (x86)\Android\android-sdk\build-tools\36.0.0'
$bash = 'C:\Program Files\Git\bin\bash.exe'
$expectedCert = '0f909e4b37eff9553ed87ecbc9f668f6e6dc2e9b3d6e7abc20e3ab0f70a62cee'

$xml = [xml](Get-Content $csproj -Raw)
$name = @($xml.Project.PropertyGroup.ApplicationDisplayVersion | Where-Object { $_ })[0]
$code = @($xml.Project.PropertyGroup.ApplicationVersion | Where-Object { $_ })[0]
Write-Host "Releasing ForgeLink $name (versionCode $code)"

$live = try { (Invoke-RestMethod 'https://cloudcompassai.com/downloads/forgelink-sms/latest.json').versionCode } catch { 0 }
if ([int]$code -le [int]$live) {
    throw "versionCode $code is not higher than the published $live. Raise ApplicationVersion in the csproj first."
}

dotnet test (Join-Path $root 'tests\ForgeLinkSms.Core.Tests\ForgeLinkSms.Core.Tests.csproj') --nologo -v q
if ($LASTEXITCODE) { throw 'Unit tests failed.' }

# MSBuild signing can't sign an .apk with the single password file, so build unsigned and sign by hand.
dotnet build (Join-Path $root 'src\ForgeLinkSms\ForgeLinkSms.csproj') -c Release -f net10.0-android -p:AndroidPackageFormat=apk -p:AndroidKeyStore=false --nologo -v q
if ($LASTEXITCODE) { throw 'Build failed.' }

$out = Join-Path $root 'src\ForgeLinkSms\bin\Release\net10.0-android'
$built = Join-Path $root 'src\ForgeLinkSms\obj\Release\net10.0-android\android\bin\com.forgelink.sms.apk'
$aligned = Join-Path $out "aligned-$code.apk"
$final = Join-Path $out "ForgeLink-SMS-$name-v$code.apk"
Remove-Item $aligned, $final -ErrorAction SilentlyContinue

& "$buildTools\zipalign.exe" -f -p 16 $built $aligned
if ($LASTEXITCODE) { throw 'zipalign failed.' }
& "$buildTools\apksigner.bat" sign --ks "$keys\forgelink-upload.jks" --ks-pass "file:$keys\upload-key-password.txt" --ks-key-alias forgelink-upload --min-sdk-version 24 --out $final $aligned
if ($LASTEXITCODE) { throw 'apksigner failed.' }
Remove-Item $aligned

$certs = & "$buildTools\apksigner.bat" verify --print-certs $final | Out-String
if ($certs -notmatch [regex]::Escape($expectedCert)) { throw 'Signed with an unexpected key. Android would refuse this as an update.' }
$badging = & "$buildTools\aapt.exe" dump badging $final | Select-Object -First 1
if ($badging -notmatch "versionCode='$code'" -or $badging -notmatch "versionName='$name'") { throw "APK version mismatch: $badging" }
Write-Host "Built and verified: $final"

if (-not $Publish) {
    Write-Host 'Not published. Re-run with -Publish to upload.'
    return
}

function ToPosix($p) { '/' + $p.Substring(0, 1).ToLower() + ($p.Substring(2) -replace '\\', '/') }
$safeNotes = $Notes -replace "'", "'\''"
& $bash -c "cd '$(ToPosix (Resolve-Path $website).Path)' && scripts/publish-app-release.sh forgelink-sms '$(ToPosix $final)' $code '$name' '$safeNotes' </dev/null"
if ($LASTEXITCODE) { throw 'Publish failed.' }

$m = Invoke-RestMethod 'https://cloudcompassai.com/downloads/forgelink-sms/latest.json'
$tmp = New-TemporaryFile
Invoke-WebRequest $m.apkUrl -OutFile $tmp
$hash = (Get-FileHash $tmp -Algorithm SHA256).Hash
Remove-Item $tmp
if ($m.versionCode -ne [int]$code -or $hash -ne $m.sha256) { throw 'Live check failed: manifest or APK on the site does not match.' }
Write-Host "Live and verified: $name ($code) at $($m.apkUrl)"
