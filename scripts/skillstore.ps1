#!/usr/bin/env pwsh
$project = Join-Path $PSScriptRoot "..\src\AgentSkillStore.Cli\AgentSkillStore.Cli.csproj"
& dotnet run --project $project -- @args
exit $LASTEXITCODE
