param([Parameter(Mandatory)][string]$Output)
$ErrorActionPreference = 'Stop'
$Output = [IO.Path]::GetFullPath($Output)
$workspace = Join-Path ([IO.Path]::GetTempPath()) ('switcher-build-' + [guid]::NewGuid().ToString('N'))
try {
    $launcher = Join-Path $workspace 'launcher'
    $projectRoot = Split-Path $PSScriptRoot -Parent
    $project = Join-Path $PSScriptRoot '..\Launcher\FifaStreetLauncher\FifaStreetLauncher.csproj'
    dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $launcher
    if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed.' }
    $zip = Join-Path $workspace 'Launcher.zip'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($launcher, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    New-Item -ItemType Directory -Force -Path (Split-Path $Output) | Out-Null
    $mark = Join-Path $projectRoot 'Launcher\FifaStreetLauncher\ui\switcher-mark.png'
    $icon = Join-Path $projectRoot 'Launcher\FifaStreetLauncher\switcher.ico'
    $art = Join-Path $PSScriptRoot 'switcher-setup-court.jpg'
    & $compiler /nologo /target:winexe /platform:x64 "/out:$Output" "/win32icon:$icon" "/resource:$zip,Launcher.zip" `
        "/resource:$icon,switcher.ico" "/resource:$mark,switcher-mark.png" "/resource:$art,switcher-court.jpg" `
        /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll `
        (Join-Path $PSScriptRoot 'SwitcherSetup.cs') (Join-Path $PSScriptRoot 'SwitcherSetupForm.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Setup compilation failed.' }
    "$((Get-FileHash -LiteralPath $Output).Hash.ToLowerInvariant())  $(Split-Path $Output -Leaf)" | Set-Content -LiteralPath "$Output.sha256" -Encoding ascii
}
finally {
    if (Test-Path -LiteralPath $workspace) {
        $resolved = (Resolve-Path -LiteralPath $workspace).Path
        if ($resolved -eq $workspace -and (Split-Path $resolved -Leaf).StartsWith('switcher-build-')) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}
