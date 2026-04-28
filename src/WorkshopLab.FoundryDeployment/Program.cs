using Azure.AI.Projects;
using Azure.Identity;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Reflection;
using YamlDotNet.Serialization;
using System.Text.Json;

var arguments = ParseArguments(args);

string projectEndpoint = GetRequiredValue(arguments, "project-endpoint", "AZURE_AI_PROJECT_ENDPOINT");
string agentName = GetOptionalValue(arguments, "agent-name", null) ?? Path.GetRandomFileName().Replace(".", "");
string? manifestPath = GetOptionalValue(arguments, "manifest", null);
string? agentDefinitionJson = GetOptionalValue(arguments, "agent-definition", null);
string? agentId = GetOptionalValue(arguments, "agent-id", "FOUNDRY_AGENT_ID");

var credential = new DefaultAzureCredential();
var projectClient = new AIProjectClient(new Uri(projectEndpoint), credential);

// Get or create the agent definition
string finalDefinitionJson;
if (!string.IsNullOrWhiteSpace(agentDefinitionJson))
{
    // Use provided agent definition directly
    finalDefinitionJson = agentDefinitionJson;
    Console.WriteLine($"Using provided agent definition for agent '{agentName}'");
    Console.WriteLine($"finalDefinitionJson: {finalDefinitionJson}");
}
else if (!string.IsNullOrWhiteSpace(manifestPath))
{
    // Read and process manifest file
    string manifest = File.ReadAllText(manifestPath);

    foreach (string replacement in GetMultiValue(arguments, "set"))
    {
        string[] parts = replacement.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
        {
            throw new ArgumentException($"Invalid --set value '{replacement}'. Use NAME=VALUE.");
        }

        manifest = manifest
            .Replace($"${{{parts[0]}}}", parts[1], StringComparison.Ordinal)
            .Replace($"{{{{{parts[0]}}}}}", parts[1], StringComparison.Ordinal);
    }

    // Convert YAML to JSON if necessary
    finalDefinitionJson = NormalizeManifestForApi(manifest, manifestPath);
    Console.WriteLine($"Using agent definition from manifest '{manifestPath}'");
}
else
{
    throw new InvalidOperationException("Either --agent-definition or --manifest must be provided.");
}

// Parse and validate the definition
JsonDocument docDef = JsonDocument.Parse(finalDefinitionJson);
string agentKind = docDef.RootElement.TryGetProperty("kind", out JsonElement kindEl) 
    ? kindEl.GetString() ?? "unknown" 
    : "unknown";

Console.WriteLine($"Agent Kind: {agentKind}");
BinaryContent definitionContent = BinaryContent.Create(BinaryData.FromString(finalDefinitionJson));

MethodInfo getAgentsClientMethod = typeof(AIProjectClient).GetMethod(
    "GetAIProjectAgentsOperationsClient",
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("The Azure AI Projects SDK does not expose the agents operations client in this build.");

object agentsClient = getAgentsClientMethod.Invoke(projectClient, null)
    ?? throw new InvalidOperationException("Failed to create the agents operations client from AIProjectClient.");

var requestOptions = new RequestOptions();

if (string.IsNullOrWhiteSpace(agentId))
{
    // Try create with definition if no agent-id provided
    if (agentKind == "hosted")
    {
        await InvokeCreateHostedAgentAsync(agentsClient, agentName, finalDefinitionJson, requestOptions);
        Console.WriteLine($"Created hosted agent '{agentName}'.");
    }
    else
    {
        await InvokeManifestMethodAsync(agentsClient, "CreateAgentFromManifestAsync", definitionContent, requestOptions);
        Console.WriteLine($"Created Foundry agent from definition.");
    }
}
else
{
    await InvokeManifestMethodAsync(agentsClient, "UpdateAgentFromManifestAsync", agentId, definitionContent, requestOptions);
    Console.WriteLine($"Updated Foundry agent '{agentId}' from definition.");
}

Console.WriteLine("Next step: start the hosted agent container and verify status in the Foundry portal or through Foundry MCP tools.");

static async Task InvokeCreateHostedAgentAsync(object agentsClient, string agentName, string definitionJson, RequestOptions requestOptions)
{
    // CreateAgentVersionAsync expects the AgentVersionCreationOptions envelope: {"definition": {...}}
    // Wrap the raw definition JSON before sending.
    string wrappedJson = $"{{\"definition\":{definitionJson}}}";
    BinaryContent wrappedContent = BinaryContent.Create(BinaryData.FromString(wrappedJson));

    // Resolve the (string, BinaryContent, RequestOptions) overload specifically.
    MethodInfo method = agentsClient.GetType().GetMethod(
        "CreateAgentVersionAsync",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        null,
        new[] { typeof(string), typeof(BinaryContent), typeof(RequestOptions) },
        null)
        ?? throw new InvalidOperationException("The agents operations client does not expose CreateAgentVersionAsync(string, BinaryContent, RequestOptions).");

    object? result = method.Invoke(agentsClient, new object[] { agentName, wrappedContent, requestOptions });
    if (result is Task task)
    {
        await task;
        return;
    }

    throw new InvalidOperationException("The method 'CreateAgentVersionAsync' did not return a Task as expected.");
}

static string NormalizeManifestForApi(string manifestContent, string manifestPath)
{
    string extension = Path.GetExtension(manifestPath);
    if (!extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
        && !extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
    {
        return manifestContent;
    }

    var deserializer = new DeserializerBuilder().Build();
    object yamlModel = deserializer.Deserialize(new StringReader(manifestContent))
        ?? throw new InvalidOperationException($"Manifest '{manifestPath}' is empty or invalid YAML.");

    var serializer = new SerializerBuilder().JsonCompatible().Build();
    return serializer.Serialize(yamlModel);
}

static Dictionary<string, List<string>> ParseArguments(string[] args)
{
    var parsed = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    for (int index = 0; index < args.Length; index++)
    {
        string current = args[index];
        if (!current.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        string key = current[2..];
        string value = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[++index]
            : "true";

        if (!parsed.TryGetValue(key, out List<string>? values))
        {
            values = [];
            parsed[key] = values;
        }

        values.Add(value);
    }

    return parsed;
}

static string GetRequiredValue(Dictionary<string, List<string>> args, string argName, string envName)
{
    string? value = GetOptionalValue(args, argName, envName);
    return !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException($"Missing required value '--{argName}' or environment variable '{envName}'.");
}

static string? GetOptionalValue(Dictionary<string, List<string>> args, string argName, string? envName)
{
    if (args.TryGetValue(argName, out List<string>? values) && values.Count > 0)
    {
        return values[^1];
    }

    return envName is null ? null : Environment.GetEnvironmentVariable(envName);
}

static IReadOnlyList<string> GetMultiValue(Dictionary<string, List<string>> args, string argName)
{
    return args.TryGetValue(argName, out List<string>? values) ? values : [];
}

static async Task InvokeManifestMethodAsync(object target, string methodName, params object[] parameters)
{
    MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"The agents operations client does not expose '{methodName}'.");

    object? result = method.Invoke(target, parameters);
    if (result is Task task)
    {
        await task;
        return;
    }

    throw new InvalidOperationException($"The method '{methodName}' did not return a Task as expected.");
}