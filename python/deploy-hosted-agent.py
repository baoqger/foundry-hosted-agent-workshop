import os
from dotenv import load_dotenv
from azure.identity import DefaultAzureCredential, get_bearer_token_provider
from azure.ai.projects import AIProjectClient
from azure.ai.projects.models import HostedAgentDefinition, ProtocolVersionRecord, AgentProtocol

# Take environment variables from .env
load_dotenv(override=True)

credential = DefaultAzureCredential()

bearer_token_provider = get_bearer_token_provider(
    credential, "https://management.azure.com/.default"
)

FOUNDRY_ENDPOINT = os.environ["FOUNDRY_PROJECT_ENDPOINT"]
MODEL_DEPLOYMENT = os.environ.get("FOUNDRY_MODEL_DEPLOYMENT_NAME")

project_client = AIProjectClient(
    endpoint=FOUNDRY_ENDPOINT, credential=credential, allow_preview=True
)

# Create a hosted agent version
agent = project_client.agents.create_version(
    agent_name="my-hosted-agent",
    definition=HostedAgentDefinition(
        kind = "hosted",
        container_protocol_versions=[
            ProtocolVersionRecord(protocol=AgentProtocol.RESPONSES, version="1.0.0")
        ],
        cpu="1",
        memory="2Gi",
        image="workshoplabazureagentjorombwwhuat4.azurecr.io/workshoplab-agent:lab4",
        environment_variables={
            "AZURE_AI_PROJECT_ENDPOINT": FOUNDRY_ENDPOINT,
            "AZURE_AI_MODEL_DEPLOYMENT": MODEL_DEPLOYMENT
        }
    )
)

print(f"Agent created: {agent.name}, version: {agent.version}")