param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('BelmontsCurseGraphics.dll')]
    [string]$PluginDll,
    [string]$GamePath
)

$ErrorActionPreference = 'Stop'
$exeName = "Castlevania Belmont's Curse.exe"
$source = [IO.Path]::GetFullPath($PSScriptRoot)

function Test-GameFolder([string]$Path) {
    return ((Test-Path -LiteralPath (Join-Path $Path $exeName) -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $Path 'GameAssembly.dll') -PathType Leaf))
}

try {
    # Only the bundled loader and this mod are installed. Configs, saves, other
    # plugins and generated interop assemblies are never copied or removed.
    $required = @('winhttp.dll', 'doorstop_config.ini',
        'BepInEx/core/BepInEx.Core.dll', 'BepInEx/core/BepInEx.Unity.IL2CPP.dll',
        'dotnet/coreclr.dll', "BepInEx/plugins/$PluginDll")
    foreach ($relative in $required) {
        if (!(Test-Path -LiteralPath (Join-Path $source $relative) -PathType Leaf)) {
            throw "Missing $relative. Extract the WHOLE standalone ZIP before running Install.cmd."
        }
    }

    if (!$GamePath) {
        # Covers extraction beside the EXE, inside BepInEx, or inside plugins.
        $candidate = $source
        for ($i = 0; $i -lt 4 -and $candidate; $i++) {
            if (Test-GameFolder $candidate) { $GamePath = $candidate; break }
            $candidate = [IO.Path]::GetDirectoryName($candidate)
        }
    }
    if (!$GamePath) {
        Add-Type -AssemblyName System.Windows.Forms
        $picker = New-Object System.Windows.Forms.OpenFileDialog
        $picker.Title = 'Select the game EXE in Steam > Manage > Browse local files'
        $picker.Filter = "Castlevania Belmont's Curse|$exeName"
        try {
            if ($picker.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
                throw 'Installation cancelled. No files were changed.'
            }
            $GamePath = [IO.Path]::GetDirectoryName($picker.FileName)
        } finally { $picker.Dispose() }
    }
    $target = [IO.Path]::GetFullPath($GamePath)
    if (!(Test-GameFolder $target)) {
        throw 'Choose the main game folder containing the game EXE and GameAssembly.dll, not BepInEx or plugins.'
    }
    if (Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($exeName)) -ErrorAction SilentlyContinue) {
        throw 'Close the game before installing, then run Install.cmd again.'
    }

    Write-Host "Installing $PluginDll into $target"
    if ($source -ne $target -and $source.StartsWith((Join-Path $target 'BepInEx'), [StringComparison]::OrdinalIgnoreCase)) {
        Write-Host 'The ZIP was extracted inside BepInEx. Copying its files to the correct level.'
    }
    # Take a snapshot before copying: the source may be a subfolder of target.
    $files = @()
    foreach ($folder in @('BepInEx/core', 'dotnet', 'BelmontsCurseGraphics/licenses')) {
        $path = Join-Path $source $folder
        if (Test-Path -LiteralPath $path -PathType Container) {
            $files += @(Get-ChildItem -LiteralPath $path -Recurse -File -Force)
        }
    }
    foreach ($relative in @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version', "BepInEx/plugins/$PluginDll")) {
        $path = Join-Path $source $relative
        if (Test-Path -LiteralPath $path -PathType Leaf) { $files += Get-Item -LiteralPath $path -Force }
    }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($source.Length).TrimStart([char[]]'\/')
        $destination = Join-Path $target $relative
        if ([string]::Equals($file.FullName, $destination, [StringComparison]::OrdinalIgnoreCase)) { continue }
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
    foreach ($relative in $required) {
        if (!(Test-Path -LiteralPath (Join-Path $target $relative) -PathType Leaf)) { throw "Installation is incomplete: $relative" }
    }
    Write-Host "Installed: $(Join-Path $target "BepInEx/plugins/$PluginDll")"
    Write-Host 'Start the game through Steam. The first BepInEx launch may take a few minutes.'
    Write-Host 'If the mod does not load, share BepInEx/LogOutput.log from the game folder.'
    exit 0
} catch {
    Write-Host "Installation failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
