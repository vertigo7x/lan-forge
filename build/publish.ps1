param(
    [ValidateSet('Server', 'Client', 'All')]
    [string] $App = 'All',

    [string] $Runtime = 'win-x64',
    [string] $Configuration = 'Release',
    [string] $OutputRoot = 'artifacts'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

dotnet publish src/Shared -c $Configuration -o "$OutputRoot/shared" | Out-Null

if ($App -in 'Server', 'All') {
    dotnet publish src/Server -c $Configuration -r $Runtime --self-contained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -o "$OutputRoot/server"
    Write-Host "Servidor publicado en $OutputRoot/server"
}

if ($App -in 'Client', 'All') {
    dotnet publish src/Client -c $Configuration -r $Runtime --self-contained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -o "$OutputRoot/client"
    Write-Host "Cliente publicado en $OutputRoot/client"
}

Write-Host "Listo. Copia la carpeta '$OutputRoot/server' al PC organizador y '$OutputRoot/client' a los jugadores."
