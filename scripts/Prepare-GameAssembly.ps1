param(
    [string]$GameDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\Bopl Battle',
    [string]$BepInExCore = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\BoplBattle\profiles\Default\BepInEx\core"
)

$ErrorActionPreference = 'Stop'
$managedDirectory = Join-Path $GameDirectory 'BoplBattle_Data\Managed'
$sourceAssembly = Join-Path $managedDirectory 'Assembly-CSharp.dll'
$cecilAssembly = Join-Path $BepInExCore 'Mono.Cecil.dll'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$libraryDirectory = Join-Path $repositoryRoot 'lib'
$outputAssembly = Join-Path $libraryDirectory 'Assembly-CSharp.publicized.dll'

foreach ($requiredPath in @($sourceAssembly, $cecilAssembly)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required file was not found: $requiredPath"
    }
}

New-Item -ItemType Directory -Force -Path $libraryDirectory | Out-Null
Add-Type -Path $cecilAssembly

$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory($managedDirectory)
$readerParameters = New-Object Mono.Cecil.ReaderParameters
$readerParameters.AssemblyResolver = $resolver
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($sourceAssembly, $readerParameters)

function Set-TypePublic {
    param([Mono.Cecil.TypeDefinition]$Type)

    if ($Type.IsNested) {
        $Type.Attributes = [Mono.Cecil.TypeAttributes](
            ([int]$Type.Attributes -band (-bnot [int][Mono.Cecil.TypeAttributes]::VisibilityMask)) -bor
            [int][Mono.Cecil.TypeAttributes]::NestedPublic)
    }
    else {
        $Type.Attributes = [Mono.Cecil.TypeAttributes](
            ([int]$Type.Attributes -band (-bnot [int][Mono.Cecil.TypeAttributes]::VisibilityMask)) -bor
            [int][Mono.Cecil.TypeAttributes]::Public)
    }

    foreach ($field in $Type.Fields) {
        $field.Attributes = [Mono.Cecil.FieldAttributes](
            ([int]$field.Attributes -band (-bnot [int][Mono.Cecil.FieldAttributes]::FieldAccessMask)) -bor
            [int][Mono.Cecil.FieldAttributes]::Public)
    }

    foreach ($method in $Type.Methods) {
        $method.Attributes = [Mono.Cecil.MethodAttributes](
            ([int]$method.Attributes -band (-bnot [int][Mono.Cecil.MethodAttributes]::MemberAccessMask)) -bor
            [int][Mono.Cecil.MethodAttributes]::Public)
    }

    foreach ($nestedType in $Type.NestedTypes) {
        Set-TypePublic -Type $nestedType
    }
}

try {
    foreach ($type in $assembly.MainModule.Types) {
        Set-TypePublic -Type $type
    }
    $assembly.Write($outputAssembly)
}
finally {
    $assembly.Dispose()
}

Write-Host "Prepared build reference: $outputAssembly"
