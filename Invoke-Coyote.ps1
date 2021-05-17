Param([switch]$NoBuild)

function Invoke-Coyote() {
    dotnet coyote @args
    if ($LastExitCode){
        throw "ERROR: coyote $args exited with code $LastExitCode"
    }
}

set-alias coyote Invoke-Coyote

$jsonConfig = Get-Content "$psScriptRoot/coyote.rewrite.json" | ConvertFrom-Json

$outputPath = "$psscriptroot/$($jsonConfig.OutputPath)"

if (!$NoBuild) { ./build }

coyote rewrite "$psscriptroot/coyote.rewrite.json"
try {
    coyote test "$outputPath/FSharp.Core.UnitTests.dll" -m testConcurrentAccountCreation --iterations 100
}
catch {
    $outputText = "$outputPath/Output\FSharp.Core.UnitTests.dll\CoyoteOutput\FSharp.Core.UnitTests_0_0.txt"
    bat $outputText
    throw
}
finally {
    rm -Verbose "$outputPath/Output\FSharp.Core.UnitTests.dll\CoyoteOutput\*.*"
}
