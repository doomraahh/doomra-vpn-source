$ErrorActionPreference='Stop'
$flagWorkspace=Split-Path $PSScriptRoot -Parent
$flagCache=Join-Path $flagWorkspace 'cache'
New-Item -ItemType Directory -Force $flagCache | Out-Null
$flagCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$flagProgram=Join-Path $flagCache 'RenderFlagAsset.exe'
& $flagCompiler /nologo /utf8output /codepage:65001 /target:exe ('/out:'+$flagProgram) /r:System.Drawing.dll (Join-Path $PSScriptRoot 'RenderFlagAsset.cs')
if($LASTEXITCODE -ne 0){throw 'Flag renderer build failed'}
$flagAssets=Join-Path $flagWorkspace 'assets/custom-flags'
foreach($flagCode in @('us','nl','de','se','fi')) {
 & $flagProgram (Join-Path $flagAssets ($flagCode+'.svg')) (Join-Path $flagAssets ($flagCode+'.png'))
 if($LASTEXITCODE -ne 0){throw "Flag conversion failed: $flagCode"}
}
Add-Type -AssemblyName System.IO.Compression
$flagStream=[IO.File]::Open((Join-Path $flagWorkspace 'assets/custom-flags.zip'),[IO.FileMode]::Create)
$flagZip=[IO.Compression.ZipArchive]::new($flagStream,[IO.Compression.ZipArchiveMode]::Create)
try {
 foreach($flagPng in (Get-ChildItem -LiteralPath $flagAssets -Filter '*.png')) {
  $flagEntry=$flagZip.CreateEntry($flagPng.Name,[IO.Compression.CompressionLevel]::Optimal)
  $flagOutput=$flagEntry.Open(); $flagInput=[IO.File]::OpenRead($flagPng.FullName)
  try {$flagInput.CopyTo($flagOutput)} finally {$flagInput.Dispose();$flagOutput.Dispose()}
 }
} finally {$flagZip.Dispose();$flagStream.Dispose()}
