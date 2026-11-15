[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Push-Location $root
try {
    dotnet restore tests\ModeTray.Tests\ModeTray.Tests.csproj --configfile ModeTray\NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw "Restore failed: $LASTEXITCODE" }
    dotnet run --project tests\ModeTray.Tests\ModeTray.Tests.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Tests failed: $LASTEXITCODE" }
    dotnet build ModeTray\ModeTray.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
    Write-Output 'BUILD_AND_TEST_OK'
}
finally {
    Pop-Location
}
