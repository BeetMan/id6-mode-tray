[CmdletBinding()]
param(
    [string]$OutputDirectory = 'artifacts\ID6ModeTray'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Push-Location $root
try {
    $artifactsBase = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts')).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $artifactsRoot = $artifactsBase + [System.IO.Path]::DirectorySeparatorChar
    $output = [System.IO.Path]::GetFullPath((Join-Path $root $OutputDirectory)).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    if ($output.Equals($artifactsBase, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "OutputDirectory must be a child of $artifactsBase, not the artifacts root itself."
    }
    if (-not $output.StartsWith($artifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "OutputDirectory must be under $artifactsRoot"
    }

    & (Join-Path $PSScriptRoot 'build-and-test.ps1')
    if ($LASTEXITCODE -ne 0) { throw "Build and test failed: $LASTEXITCODE" }

    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Recurse -Force
    }

    dotnet publish ModeTray\ModeTray.csproj -c Release --no-restore -o $output
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }

    $files = Get-ChildItem -LiteralPath $output -Recurse -File |
        Sort-Object { $_.FullName.Substring($output.Length).TrimStart('\') }
    $manifestFiles = [System.Collections.Generic.List[object]]::new()
    $hashLines = [System.Collections.Generic.List[string]]::new()
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($output.Length).TrimStart('\').Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        $manifestFiles.Add([ordered]@{ path = $relative; bytes = $file.Length; sha256 = $hash })
        $hashLines.Add("$hash *$relative")
    }

    $manifest = [ordered]@{
        project = 'ID6 Mode Tray'
        targetFramework = 'net10.0-windows'
        selfContained = $false
        requiredRuntime = 'Microsoft.WindowsDesktop.App 10.0.0 or later'
        distribution = 'Local validation only; M2Mqtt redistribution is not licensed by this project.'
        files = $manifestFiles
    }
    $manifestPath = Join-Path $output 'release-manifest.json'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    $hashLines.Add("$manifestHash *release-manifest.json")
    $hashLines | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding UTF8

    $zipPath = Join-Path $root 'artifacts\ID6ModeTray.zip'
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zipPath -CompressionLevel Optimal
    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    "$zipHash *ID6ModeTray.zip" | Set-Content -LiteralPath "$zipPath.sha256" -Encoding UTF8

    Write-Output "Published: $output"
    Write-Output "Archive: $zipPath"
    Write-Output "Archive SHA256: $zipHash"
    Write-Output 'PUBLISH_RELEASE_OK'
}
finally {
    Pop-Location
}
