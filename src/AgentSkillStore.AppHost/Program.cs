// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
var builder = DistributedApplication.CreateBuilder(args);
var repositoryDirectory = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));
var dataPath = builder.Configuration["AgentSkillStore:DataPath"]
    ?? Path.Combine(repositoryDirectory, "data");

builder.AddProject<Projects.AgentSkillStore_Server>("agent-skill-store")
    .WithHttpEndpoint(port: 0, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("AGENTSKILLSTORE__SEEDDATA", builder.Configuration["AgentSkillStore:SeedData"] ?? "true")
    .WithEnvironment("AGENTSKILLSTORE__DATAPATH", dataPath);

builder.Build().Run();
