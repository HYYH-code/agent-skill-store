// -----------------------------------------------------------------------
// <copyright file="CliJsonContext.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json.Serialization;
using AgentSkillStore.Cli.Config;
using AgentSkillStore.Client;
using AgentSkillStore.Cli.Commands;

namespace AgentSkillStore.Cli.Json;

[JsonSerializable(typeof(CliConfig))]
[JsonSerializable(typeof(IReadOnlyList<SkillSummary>))]
[JsonSerializable(typeof(IReadOnlyList<SkillVersionSummary>))]
[JsonSerializable(typeof(IReadOnlyList<SubAgentSummary>))]
[JsonSerializable(typeof(IReadOnlyList<ApiKeySummary>))]
[JsonSerializable(typeof(SkillUploadResponse))]
[JsonSerializable(typeof(PublicationResponse))]
[JsonSerializable(typeof(IReadOnlyList<PublicationSummary>))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(SkillInstallRecord))]
[JsonSerializable(typeof(SkillInstallState))]
[JsonSerializable(typeof(InstallResult))]
[JsonSerializable(typeof(IReadOnlyList<SkillInstallRecord>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class CliJsonContext : JsonSerializerContext;
