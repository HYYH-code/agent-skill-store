// -----------------------------------------------------------------------
// <copyright file="EnterprisePackageUploadService.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.IO.Compression;
using System.Text.Json;
using AgentSkillStore.Server.Models;
using YamlDotNet.RepresentationModel;

namespace AgentSkillStore.Server.Services;

public sealed class EnterprisePackageUploadService
{
    private const long MaxPackageBytes = 100L * 1024 * 1024;
    private const long MaxFileBytes = 50L * 1024 * 1024;
    private const int MaxFileCount = 2000;
    private const int MaxCompressionRatio = 100;
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixSymlinkType = 0xA000;
    private readonly SkillUploadService _uploadService;

    public EnterprisePackageUploadService(SkillUploadService uploadService)
    {
        _uploadService = uploadService;
    }

    public async Task<SkillUploadResult> UploadAsync(
        IFormFile package,
        SkillName name,
        SkillVersionString version,
        string? category,
        string expectedRiskLevel,
        string expectedNetworkPolicy,
        CancellationToken ct)
    {
        if (package.Length <= 0 || package.Length > MaxPackageBytes)
            return SkillUploadResult.Failed("Package is empty or exceeds the 100 MB limit.");

        if (!package.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return SkillUploadResult.Failed("Enterprise publication requires a ZIP archive.");

        try
        {
            await using var packageStream = package.OpenReadStream();
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count == 0 || archive.Entries.Count > MaxFileCount)
                return SkillUploadResult.Failed("ZIP file count is outside the allowed range.");

            byte[]? skillMarkdown = null;
            var resources = new List<SkillArchiveResource>();
            long expandedBytes = 0;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;
                var normalizedPath = entry.FullName.Replace('\\', '/');
                if (Path.IsPathRooted(normalizedPath) || normalizedPath.StartsWith('/') || normalizedPath.Contains("..", StringComparison.Ordinal))
                    return SkillUploadResult.Failed($"Unsafe archive path: {entry.FullName}");
                var unixType = (entry.ExternalAttributes >> 16) & UnixFileTypeMask;
                if (unixType == UnixSymlinkType)
                    return SkillUploadResult.Failed($"Symbolic links are not allowed: {entry.FullName}");
                if (entry.Length < 0 || entry.Length > MaxFileBytes)
                    return SkillUploadResult.Failed($"File exceeds the 50 MB limit: {entry.FullName}");
                if (entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > MaxCompressionRatio)
                    return SkillUploadResult.Failed($"Suspicious compression ratio: {entry.FullName}");
                expandedBytes += entry.Length;
                if (expandedBytes > MaxPackageBytes)
                    return SkillUploadResult.Failed("Expanded ZIP exceeds the 100 MB limit.");

                await using var entryStream = entry.Open();
                using var buffer = new MemoryStream();
                await entryStream.CopyToAsync(buffer, ct);
                var bytes = buffer.ToArray();
                if (ContainsEmbeddedSecret(normalizedPath, bytes))
                    return SkillUploadResult.Failed($"Potential embedded secret detected: {entry.FullName}");
                if (normalizedPath.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
                {
                    skillMarkdown = bytes;
                    continue;
                }
                if (!ResourcePath.TryCreate(normalizedPath, out var resourcePath))
                    return SkillUploadResult.Failed($"Invalid resource path: {entry.FullName}");
                var unixMode = (entry.ExternalAttributes >> 16) & SkillArchiveBuilder.PermissionBitsMask;
                resources.Add(new SkillArchiveResource(resourcePath.Value, bytes, unixMode));
            }

            if (skillMarkdown is null)
                return SkillUploadResult.Failed("ZIP must contain SKILL.md at its root.");
            var manifestIndex = resources.FindIndex(resource =>
                resource.Path.Value.Equals("manifest.yaml", StringComparison.OrdinalIgnoreCase));
            if (manifestIndex < 0)
                return SkillUploadResult.Failed("ZIP must contain manifest.yaml at its root.");
            var manifest = resources[manifestIndex];
            var manifestError = ValidateManifest(manifest.Content, name.Value, version.Value, category ?? "general",
                out var permissions, out var manifestRiskLevel);
            if (manifestError is not null)
                return SkillUploadResult.Failed(manifestError);
            if (!string.Equals(manifestRiskLevel, expectedRiskLevel, StringComparison.Ordinal)
                || !string.Equals(permissions.Network.Policy, expectedNetworkPolicy, StringComparison.Ordinal))
                return SkillUploadResult.Failed("Form risk level and network policy must match manifest.yaml.");
            using var skillStream = new MemoryStream(skillMarkdown, writable: false);
            var uploads = resources.Select(resource => new SkillResourceUpload(
                resource.Path,
                new MemoryStream(resource.Content, writable: false),
                resource.UnixMode)).ToList();
            try
            {
                var upload = await _uploadService.UploadSkillWithResourcesAsync(name, version, skillStream, uploads, category, ct);
                return upload.Success
                    ? upload with
                    {
                        EnterprisePermissions = permissions,
                        EnterpriseManifestJson = JsonSerializer.Serialize(
                            permissions, AgentSkillStoreJsonContext.Default.EnterprisePermissions),
                        EnterpriseRiskLevel = manifestRiskLevel
                    }
                    : upload;
            }
            finally
            {
                foreach (var upload in uploads)
                    await upload.Content.DisposeAsync();
            }
        }
        catch (InvalidDataException exception)
        {
            return SkillUploadResult.Failed($"Invalid ZIP archive: {exception.Message}");
        }
    }

    private static bool ContainsEmbeddedSecret(string path, byte[] content)
    {
        if (content.Length > 2 * 1024 * 1024)
            return false;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".md" or ".yaml" or ".yml" or ".json" or ".txt" or ".env" or ".ps1" or ".sh" or ".py"))
            return false;
        var text = System.Text.Encoding.UTF8.GetString(content);
        return text.Contains("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal)
               || text.Contains("-----BEGIN RSA PRIVATE KEY-----", StringComparison.Ordinal)
               || text.Contains("AWS_SECRET_ACCESS_KEY=", StringComparison.OrdinalIgnoreCase)
               || text.Contains("AGENTSKILLSTORE__BOOTSTRAPAPIKEY=", StringComparison.OrdinalIgnoreCase)
               || text.Contains("AGENT_SKILL_STORE_API_KEY=", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ValidateManifest(
        byte[] content,
        string expectedName,
        string expectedVersion,
        string expectedNamespace,
        out EnterprisePermissions permissions,
        out string riskLevel)
    {
        permissions = new EnterprisePermissions();
        riskLevel = "";
        try
        {
            var yaml = new YamlStream();
            using var reader = new StringReader(System.Text.Encoding.UTF8.GetString(content));
            yaml.Load(reader);
            if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root)
                return "manifest.yaml must contain one mapping document.";
            if (Scalar(root, "apiVersion") != "skills.agent-skill-store.dev/v1alpha1" || Scalar(root, "kind") != "AgentSkill")
                return "manifest.yaml apiVersion or kind is invalid.";
            var metadata = Mapping(root, "metadata");
            if (metadata is null || Scalar(metadata, "name") != expectedName
                || Scalar(metadata, "version") != expectedVersion
                || Scalar(metadata, "namespace") != expectedNamespace)
                return "manifest.yaml name, namespace and version must match the publication request.";
            if (string.IsNullOrWhiteSpace(Scalar(metadata, "owner")))
                return "manifest.yaml metadata.owner is required.";
            var spec = Mapping(root, "spec");
            var permissionNode = spec is null ? null : Mapping(spec, "permissions");
            if (spec is null || permissionNode is null)
                return "manifest.yaml spec.permissions is required.";
            var risk = Scalar(spec, "riskLevel");
            if (risk is not ("low" or "medium" or "high" or "critical"))
                return "manifest.yaml spec.riskLevel is invalid.";
            var filesystem = Mapping(permissionNode, "filesystem");
            var network = Mapping(permissionNode, "network");
            var networkPolicy = network is null ? "deny-all" : Scalar(network, "policy") ?? "deny-all";
            if (networkPolicy is not ("deny-all" or "allow-list" or "unrestricted"))
                return "manifest.yaml network policy is invalid.";
            permissions = new EnterprisePermissions
            {
                Filesystem = new EnterpriseFilesystemPermissions
                {
                    Read = filesystem is null ? [] : Sequence(filesystem, "read"),
                    Write = filesystem is null ? [] : Sequence(filesystem, "write")
                },
                Network = new EnterpriseNetworkPermissions
                {
                    Policy = networkPolicy,
                    Allow = network is null ? [] : Sequence(network, "allow")
                },
                Commands = Sequence(permissionNode, "commands"),
                Secrets = Sequence(permissionNode, "secrets")
            };
            riskLevel = risk;
            return null;
        }
        catch (YamlDotNet.Core.YamlException exception)
        {
            return $"manifest.yaml is invalid: {exception.Message}";
        }
    }

    private static YamlMappingNode? Mapping(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value as YamlMappingNode : null;

    private static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? (value as YamlScalarNode)?.Value : null;

    private static IReadOnlyList<string> Sequence(YamlMappingNode node, string key)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out var value)
            || value is not YamlSequenceNode sequence)
            return [];
        return sequence.Children.OfType<YamlScalarNode>()
            .Select(item => item.Value)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToList();
    }
}
