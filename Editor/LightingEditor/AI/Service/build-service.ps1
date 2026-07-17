param(
    [string]$Python = "python"
)

$ErrorActionPreference = "Stop"
$serviceRoot = $PSScriptRoot
$sourceRoot = Join-Path $serviceRoot "Source"
$entryPoint = Join-Path $sourceRoot "dawn_tod_ai_service\entrypoint.py"
$lockFile = Join-Path $sourceRoot "requirements.lock"
$windowsRoot = Join-Path $serviceRoot "Windows"
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$buildRoot = [IO.Path]::GetFullPath((Join-Path $tempBase ("DawnTodAiServiceBuild-" + [guid]::NewGuid().ToString("N"))))

if (-not $buildRoot.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a build directory outside the system temporary directory."
}

try {
    $pythonIdentity = (& $Python -c "import platform, sys; print(f'{sys.version_info.major}.{sys.version_info.minor}|{platform.system()}|{platform.architecture()[0]}')").Trim()
    if ($LASTEXITCODE -ne 0 -or $pythonIdentity -ne "3.12|Windows|64bit") {
        throw "The reproducible build requires 64-bit Python 3.12 on Windows."
    }

    & $Python -m venv $buildRoot
    if ($LASTEXITCODE -ne 0) { throw "Failed to create the isolated build environment." }

    $venvPython = Join-Path $buildRoot "Scripts\python.exe"
    & $venvPython -m pip install --disable-pip-version-check --no-deps -r $lockFile
    if ($LASTEXITCODE -ne 0) { throw "Failed to install the locked dependencies." }

    Push-Location $sourceRoot
    try {
        & $venvPython -B -m pytest "tests" -c "pytest.ini"
        if ($LASTEXITCODE -ne 0) { throw "Python tests failed; the executable was not built." }
    } finally {
        Pop-Location
    }

    New-Item -ItemType Directory -Path $windowsRoot -Force | Out-Null
    $pyinstallerArgs = @(
        "--noconfirm",
        "--clean",
        "--onefile",
        # Uvicorn initializes standard streams; Unity launches this console binary hidden.
        "--console",
        "--name", "DawnTodAiService",
        "--paths", $sourceRoot,
        "--collect-submodules", "uvicorn",
        "--distpath", $windowsRoot,
        "--workpath", (Join-Path $buildRoot "work"),
        "--specpath", (Join-Path $buildRoot "spec"),
        $entryPoint
    )
    & $venvPython -m PyInstaller @pyinstallerArgs
    if ($LASTEXITCODE -ne 0) { throw "PyInstaller failed." }
} finally {
    $cacheDirectories = Get-ChildItem -LiteralPath $sourceRoot -Directory -Recurse -Force |
        Where-Object { $_.Name -eq "__pycache__" -or $_.Name -eq ".pytest_cache" } |
        Sort-Object { $_.FullName.Length }
    foreach ($cacheDirectory in $cacheDirectories) {
        if (-not (Test-Path -LiteralPath $cacheDirectory.FullName)) { continue }
        $resolvedCache = [IO.Path]::GetFullPath($cacheDirectory.FullName)
        if (-not $resolvedCache.StartsWith($sourceRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean a Python cache outside the service source directory."
        }
        Remove-Item -LiteralPath $resolvedCache -Recurse -Force
    }

    if (Test-Path -LiteralPath $buildRoot) {
        $resolvedBuildRoot = [IO.Path]::GetFullPath($buildRoot)
        if ($resolvedBuildRoot.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedBuildRoot -Recurse -Force
        }
    }
}
