[CmdletBinding()]
param(
    [switch]$Test,
    [switch]$IntegrationTest
)

$ErrorActionPreference = 'Stop'

$windowsRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputDirectory = Join-Path $windowsRoot 'dist'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'

$compiler = $null
if (Test-Path -LiteralPath $vswhere) {
    $visualStudioPath = & $vswhere -latest -products * -property installationPath
    if ($visualStudioPath) {
        $candidate = Join-Path $visualStudioPath 'MSBuild\Current\Bin\Roslyn\csc.exe'
        if (Test-Path -LiteralPath $candidate) {
            $compiler = $candidate
        }
    }
}
if (-not $compiler) {
    throw '未找到 C# 编译器。请安装 Visual Studio 2022 桌面开发工具。'
}

$netHttp = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Net.Http.dll'
if (-not (Test-Path -LiteralPath $netHttp)) {
    throw '未找到 .NET Framework 的 System.Net.Http.dll。'
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $windowsRoot 'src') -Filter '*.cs' -File |
    Sort-Object Name |
    Select-Object -ExpandProperty FullName
$applicationPath = Join-Path $outputDirectory 'QuotaPeek.exe'
$applicationArguments = @(
    '/nologo',
    '/utf8output',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/debug:pdbonly',
    "/out:$applicationPath",
    "/win32manifest:$(Join-Path $windowsRoot 'app.manifest')",
    "/win32icon:$(Join-Path $windowsRoot 'assets\QuotaPeek.ico')",
    "/reference:$netHttp"
) + $sourceFiles

& $compiler @applicationArguments
if ($LASTEXITCODE -ne 0) {
    throw "QuotaPeek 编译失败，退出码：$LASTEXITCODE"
}

if ($Test) {
    $testPath = Join-Path $outputDirectory 'QuotaPeek.CoreTests.exe'
    $testArguments = @(
        '/nologo',
        '/utf8output',
        '/target:exe',
        '/platform:anycpu',
        "/out:$testPath",
        "/reference:$netHttp",
        (Join-Path $windowsRoot 'src\Models.cs'),
        (Join-Path $windowsRoot 'src\QuotaClient.cs'),
        (Join-Path $windowsRoot 'tests\CoreTests.cs')
    )
    & $compiler @testArguments
    if ($LASTEXITCODE -ne 0) {
        throw "核心测试编译失败，退出码：$LASTEXITCODE"
    }
    & $testPath
    if ($LASTEXITCODE -ne 0) {
        throw "核心测试失败，退出码：$LASTEXITCODE"
    }
}

if ($IntegrationTest) {
    $integrationDirectory = Join-Path $outputDirectory ('validation-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $integrationDirectory | Out-Null
    $integrationPath = Join-Path $integrationDirectory 'QuotaPeek.IntegrationTests.exe'
    $integrationArguments = @(
        '/nologo', '/utf8output', '/target:exe', '/platform:anycpu',
        '/main:QuotaPeek.Windows.IntegrationTests',
        "/out:$integrationPath", "/reference:$netHttp",
        (Join-Path $windowsRoot 'tests\IntegrationTests.cs'),
        (Join-Path $windowsRoot 'tests\LoopbackServer.cs')
    ) + $sourceFiles
    & $compiler @integrationArguments
    if ($LASTEXITCODE -ne 0) { throw "集成测试编译失败，退出码：$LASTEXITCODE" }
    & $integrationPath
    if ($LASTEXITCODE -ne 0) { throw "集成测试失败，退出码：$LASTEXITCODE" }
}

Write-Host "已生成：$applicationPath"
