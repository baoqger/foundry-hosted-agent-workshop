using System.ClientModel.Primitives;
using System.ComponentModel;
using Azure.AI.AgentServer.AgentFramework.Extensions;
using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using WorkshopLab.Core;

var settings = LoadSettingsFromDotEnv();
var projectEndpoint = GetRequiredSetting(settings, "AZURE_AI_PROJECT_ENDPOINT");
var deploymentName = GetOptionalSetting(settings, "MODEL_DEPLOYMENT_NAME", "gpt-4.1-mini");

Console.WriteLine($"WorkshopLab Agent Host starting for project: {projectEndpoint}");
Console.WriteLine($"Using deployment: {deploymentName}");

var credential = new DefaultAzureCredential();
var projectClient = new AIProjectClient(new Uri(projectEndpoint), credential);
var connection = projectClient.GetConnection(typeof(AzureOpenAIClient).FullName!);

if (!connection.TryGetLocatorAsUri(out Uri? openAiEndpoint) || openAiEndpoint is null)
{
	throw new InvalidOperationException("Failed to resolve the Azure OpenAI connection from the Foundry project.");
}

openAiEndpoint = new Uri($"https://{openAiEndpoint.Host}");

Console.WriteLine($"Resolved Azure OpenAI endpoint: {openAiEndpoint}");

var chatClient = new AzureOpenAIClient(openAiEndpoint, credential)
	.GetChatClient(deploymentName)
	.AsIChatClient()
	.AsBuilder()
	.UseOpenTelemetry(sourceName: "WorkshopLab.Agent", configure: options => options.EnableSensitiveData = false)
	.Build();

var advisor = new HostedAgentAdvisor();

[Description("Recommend whether a team should start with a hosted agent and explain the implementation shape to use.")]
string RecommendImplementationShape(
	[Description("The business goal for the agent.")] string goal,
	[Description("Whether the team needs custom server-side code such as deterministic logic or enterprise integrations. Use yes or no.")] string needsCustomCode,
	[Description("Whether the team needs external tools, MCP integrations, or private APIs. Use yes or no.")] string needsExternalTools,
	[Description("Whether the team expects multi-step orchestration or workflow handoffs. Use yes or no.")] string needsWorkflow)
{
	return advisor.RecommendImplementationShape(goal, needsCustomCode, needsExternalTools, needsWorkflow);
}

[Description("Create a practical launch checklist for a Microsoft Foundry Hosted Agent pilot.")]
string BuildLaunchChecklist(
	[Description("The short agent name.")] string agentName,
	[Description("The target environment such as dev, pilot, or production.")] string environment)
{
	return advisor.BuildLaunchChecklist(agentName, environment);
}

[Description("Suggest troubleshooting guidance for a common hosted-agent symptom.")]
string TroubleshootHostedAgent(
	[Description("A short symptom or error description from the team.")] string symptom)
{
	return advisor.TroubleshootHostedAgent(symptom);
}

var agent = new ChatClientAgent(
	chatClient,
	name: "HostedAgentReadinessCoach",
	instructions:
	"""
	You are a Microsoft Foundry Hosted Agent readiness coach.

	Help teams choose when to use a hosted agent, prepare a safe pilot, and troubleshoot early setup issues.

	When a user asks for implementation guidance:
	1. Clarify the business goal when it is vague.
	2. Use RecommendImplementationShape for architecture decisions.
	3. Use BuildLaunchChecklist for concrete onboarding steps.
	4. Use TroubleshootHostedAgent for setup or runtime symptoms.
	5. Keep answers practical, concise, and aimed at teams getting started with Microsoft Foundry Hosted Agents.
	""",
	tools:
	[
		AIFunctionFactory.Create(RecommendImplementationShape),
		AIFunctionFactory.Create(BuildLaunchChecklist),
		AIFunctionFactory.Create(TroubleshootHostedAgent)
	])
	.AsBuilder()
	.UseOpenTelemetry(sourceName: "WorkshopLab.Agent", configure: options => options.EnableSensitiveData = false)
	.Build();

Console.WriteLine("Hosted Agent Readiness Coach is listening on http://localhost:8088!");

await agent.RunAIAgentAsync(telemetrySourceName: "WorkshopLab.Agent");

static string GetRequiredSetting(IReadOnlyDictionary<string, string> settings, string key)
{
	return settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
		? value
		: throw new InvalidOperationException($"{key} is not set in the local .env file.");
}

static string GetOptionalSetting(IReadOnlyDictionary<string, string> settings, string key, string defaultValue)
{
	return settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
		? value
		: defaultValue;
}

static IReadOnlyDictionary<string, string> LoadSettingsFromDotEnv()
{
	var dotEnvPath = FindDotEnvFile()
		?? throw new InvalidOperationException("No .env file was found for WorkshopLab.AgentHost.");

	var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	foreach (var rawLine in File.ReadAllLines(dotEnvPath))
	{
		var line = rawLine.Trim();
		if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
		{
			continue;
		}

		if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
		{
			line = line["export ".Length..].Trim();
		}

		var separatorIndex = line.IndexOf('=');
		if (separatorIndex <= 0)
		{
			continue;
		}

		var key = line[..separatorIndex].Trim();
		var value = line[(separatorIndex + 1)..].Trim();

		if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
		{
			value = value[1..^1];
		}

		if (value.Length >= 2 && value.StartsWith('\'') && value.EndsWith('\''))
		{
			value = value[1..^1];
		}

		settings[key] = value;
	}

	Console.WriteLine($"Loaded settings from {dotEnvPath}");

	return settings;
}

static string? FindDotEnvFile()
{
	var startDirectories = new[]
	{
		Directory.GetCurrentDirectory(),
		AppContext.BaseDirectory
	};

	foreach (var startDirectory in startDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
	{
		foreach (var directory in EnumerateDirectoryAndParents(startDirectory))
		{
			var candidate = Path.Combine(directory, ".env");
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}
	}

	return null;
}

static IEnumerable<string> EnumerateDirectoryAndParents(string startDirectory)
{
	var directory = new DirectoryInfo(startDirectory);
	while (directory is not null)
	{
		yield return directory.FullName;
		directory = directory.Parent;
	}
}
