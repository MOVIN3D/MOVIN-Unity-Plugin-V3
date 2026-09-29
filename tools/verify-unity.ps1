param(
    [Parameter(Mandatory=$true)][string]$unity,
    [Parameter(Mandatory=$true)][string]$workspace,
    [Parameter(Mandatory=$true)][string]$artifacts,
    [Parameter(Mandatory=$true)][ValidateSet('core','fresh','upgrade')][string]$mode,
    [Parameter(Mandatory=$true)][AllowEmptyString()][string]$legacy
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$config = Get-Content -LiteralPath (Join-Path $repo 'release.json') -Raw | ConvertFrom-Json
$editor = (Resolve-Path -LiteralPath $unity).Path
$destination = [IO.Path]::GetFullPath($workspace)
$archives = (Resolve-Path -LiteralPath $artifacts).Path
& python "$PSScriptRoot/package.py" verify --output $archives
if ($LASTEXITCODE -ne 0) { throw 'Package verification failed' }
$manifest = Get-Content -LiteralPath "$archives/release-manifest.json" -Raw | ConvertFrom-Json
if (Test-Path -LiteralPath $destination) { throw "Use a new, empty verification path: $destination" }
if ($mode -eq 'upgrade') { $legacy = (Resolve-Path -LiteralPath $legacy).Path }
$editor_version = Split-Path (Split-Path (Split-Path $editor -Parent) -Parent) -Leaf
if ($editor_version -notin $config.unity_versions) { throw "Editor version is not in release.json: $editor_version" }
$urp = if ($editor_version.StartsWith('6000.0.')) { '17.0.4' } else { $config.sample_urp }
$test_framework = if ($editor_version.StartsWith('6000.0.')) { '1.4.6' } else { '1.6.0' }
$deps = [ordered]@{
    'com.unity.modules.animation' = '1.0.0'
    'com.unity.modules.audio' = '1.0.0'
    'com.unity.modules.imgui' = '1.0.0'
    'com.unity.modules.jsonserialize' = '1.0.0'
    'com.unity.modules.physics' = '1.0.0'
    'com.unity.modules.ui' = '1.0.0'
    'com.unity.modules.uielements' = '1.0.0'
}
if ($mode -ne 'core') {
    $deps['com.unity.render-pipelines.universal'] = $urp
    $deps['com.unity.test-framework'] = $test_framework
}
New-Item -ItemType Directory -Path "$destination/Assets/Editor","$destination/Packages","$destination/ProjectSettings","$destination/Logs" | Out-Null
@{ dependencies = $deps } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$destination/Packages/manifest.json" -Encoding utf8
"m_EditorVersion: $editor_version" | Set-Content -LiteralPath "$destination/ProjectSettings/ProjectVersion.txt" -Encoding utf8
Copy-Item -LiteralPath "$PSScriptRoot/verification/import_check.cs" -Destination "$destination/Assets/Editor/import_check.cs"

function run_unity([string]$stage, [string[]]$options) {
    $log = "$destination/Logs/$stage.log"
    $arguments = @('-batchmode','-nographics','-projectPath',"`"$destination`"",'-logFile',"`"$log`"") + $options
    $process = Start-Process -FilePath $editor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    while (-not $process.WaitForExit(30000)) { Write-Output "Unity $editor_version $stage is running (PID $($process.Id))." }
    if ($process.ExitCode -ne 0) { throw "Unity $stage failed ($($process.ExitCode)); inspect $log" }
}

if ($mode -eq 'upgrade') {
    run_unity 'legacy-import' @('-importPackage',"`"$legacy`"",'-quit')
    run_unity 'legacy-scene' @('-executeMethod','import_check.legacy_scene','-quit')
    foreach ($folder in @('Scripts/Core', 'Tests')) {
        $old_folder = (Resolve-Path -LiteralPath "$destination/Assets/MOVIN/$folder").Path
        $backup = "$destination/legacy-$(Split-Path $folder -Leaf)-backup"
        if (-not $old_folder.StartsWith($destination + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Legacy backup escaped the verification workspace' }
        if (Test-Path -LiteralPath $backup) { throw "Backup already exists: $backup" }
        Move-Item -LiteralPath $old_folder -Destination $backup
        Move-Item -LiteralPath "$old_folder.meta" -Destination "$backup.meta"
    }
}
$core = Join-Path $archives "MOVIN-Unity-Plugin-Core-v$($config.version).unitypackage"
run_unity 'core-import' @('-importPackage',"`"$core`"",'-quit')
if ($mode -eq 'core') {
    run_unity 'core-check' @('-executeMethod','import_check.core','-quit')
    if (-not (Test-Path -LiteralPath "$destination/core-import-ok.json")) { throw 'Core import did not produce a success report' }
}
else {
    $samples = Join-Path $archives "MOVIN-Unity-Plugin-Samples-v$($config.version).unitypackage"
    run_unity 'samples-import' @('-importPackage',"`"$samples`"",'-quit')
    New-Item -ItemType Directory -Path "$destination/Assets/Editor/ReceiverTests" | Out-Null
    Get-ChildItem -LiteralPath "$repo/Assets/MOVIN/Tests/Editor" -Filter '*.cs' | Copy-Item -Destination "$destination/Assets/Editor/ReceiverTests"
    Copy-Item -LiteralPath "$PSScriptRoot/verification/package_import_tests.cs" -Destination "$destination/Assets/Editor/package_import_tests.cs"
    $xml = "$destination/Logs/tests.xml"
    run_unity 'tests' @('-runTests','-testPlatform','EditMode','-testResults',"`"$xml`"")
    if (-not (Test-Path -LiteralPath $xml)) { throw 'Unity test result is missing' }
    [xml]$result = Get-Content -LiteralPath $xml
    $expected = if ($mode -eq 'upgrade') { 67 } else { 66 }
    if ([int]$result.'test-run'.failed -ne 0 -or [int]$result.'test-run'.passed -lt $expected) { throw "Unity verification failed: $xml" }
}
@{
    version = $config.version
    commit = $manifest.commit
    unity = $editor_version
    mode = $mode
    passed = $true
    packages = @($manifest.packages.PSObject.Properties | ForEach-Object { @{ file = $_.Value.file; sha256 = $_.Value.sha256 } })
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$destination/verification.json" -Encoding utf8
Write-Output "Verified $mode installation in Unity ${editor_version}: $destination"
