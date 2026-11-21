# Out-of-Unity compile check for Love-at-First-Bite-VR (runtime + PlayMode/EditMode tests).
# Usage: powershell -NoProfile -File <this file> [-SkipTests]
# Scratch files (rsp, output dlls) go to $env:TEMP, never into the project.
param([switch]$SkipTests)

$proj    = 'C:\MY_STUFF\faculta\Love-at-First-Bite-VR'
$unity   = 'C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Data'
$dotnet  = "$unity\NetCoreRuntime\dotnet.exe"
$csc     = "$unity\DotNetSdkRoslyn\csc.dll"
$sa      = "$proj\Library\ScriptAssemblies"
$tmp     = $env:TEMP
$nunit   = "$unity\Resources\PackageManager\BuiltInPackages\com.unity.ext.nunit\net40\unity-custom\nunit.framework.dll"

$engineRefs = @(Get-ChildItem "$unity\Managed\UnityEngine" -Filter *.dll | ForEach-Object { $_.FullName })
$engineRefs += "$unity\NetStandard\ref\2.1.0\netstandard.dll"

function Invoke-Csc([string]$name, [string[]]$sources, [string[]]$refs, [string]$out) {
    $rsp = "$tmp\$name.rsp"
    $lines = @('-nologo', '-target:library', '-langversion:9.0', '-nostdlib+', '-noconfig', '-warn:0', "-out:$out")
    foreach ($r in $refs) { $lines += "-r:`"$r`"" }
    foreach ($s in $sources) { $lines += "`"$s`"" }
    Set-Content -Path $rsp -Value $lines -Encoding ASCII
    $result = & $dotnet $csc "@$rsp" 2>&1
    $errs = @($result | Where-Object { "$_" -match ': error ' })
    Write-Host "== $name : $($errs.Count) error(s)"
    $result | Where-Object { "$_" -match ': (error|warning) ' } | Select-Object -First 15 | ForEach-Object { Write-Host $_ }
    return $errs.Count
}

# Runtime assembly (LoveAtFirstBite.Runtime)
$runtimeSrc = @(Get-ChildItem "$proj\Assets\Scripts" -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\Tests\\' } | ForEach-Object { $_.FullName })
$runtimeRefs = $engineRefs + @("$sa\Unity.XR.Interaction.Toolkit.dll", "$sa\Unity.XR.CoreUtils.dll", "$sa\Hanzzz.MeshSlicerFree.dll", "$sa\Unity.Mathematics.dll", "$sa\Unity.Collections.dll", "$sa\Unity.InputSystem.dll")
$runtimeOut = "$tmp\LoveAtFirstBite.Runtime.check.dll"
$total = Invoke-Csc 'runtime_check' $runtimeSrc $runtimeRefs $runtimeOut

if (-not $SkipTests) {
    $testRefs = $runtimeRefs + @($runtimeOut, "$sa\UnityEngine.TestRunner.dll", "$sa\UnityEditor.TestRunner.dll", $nunit, "$unity\Managed\UnityEditor.dll", "$unity\NetStandard\compat\2.1.0\shims\netfx\mscorlib.dll")
    foreach ($mode in 'PlayMode', 'EditMode') {
        $src = @(Get-ChildItem "$proj\Assets\Scripts\Tests\$mode" -Recurse -Filter *.cs | ForEach-Object { $_.FullName })
        $total += Invoke-Csc "tests_${mode}_check" $src $testRefs "$tmp\LoveAtFirstBite.Tests.$mode.check.dll"
    }
}
Write-Host "TOTAL ERRORS: $total"
exit $total
