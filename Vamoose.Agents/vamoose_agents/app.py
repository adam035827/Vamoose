from collections.abc import AsyncIterator
import os
import sys

from bedrock_agentcore.runtime import BedrockAgentCoreApp

from .config import Settings
from .dispatch import handle

app = BedrockAgentCoreApp()


@app.entrypoint
async def invoke(payload: dict) -> AsyncIterator[dict]:
    async for event in handle(payload):
        yield event.to_wire()


def startup_warnings(settings: Settings) -> list[str]:
    """Local setup problems that would otherwise surface only as a generic agent error on every request."""
    if settings.mock_agents:
        return []
    import boto3

    warnings = []
    if boto3.Session().get_credentials() is None:
        warnings.append("No AWS credentials found. Set AWS_PROFILE, or start with VAMOOSE_MOCK_AGENTS=1.")
    bundle = os.getenv("AWS_CA_BUNDLE")
    if bundle and not os.path.isfile(bundle):
        warnings.append(f"AWS_CA_BUNDLE points to a missing file: {bundle}")
    return warnings


if __name__ == "__main__":
    settings = Settings.from_env()
    model = settings.planner_model_id or "Strands default"
    mode = "mock" if settings.mock_agents else f"Bedrock {model} ({settings.aws_region}, profile={os.getenv('AWS_PROFILE', 'default')})"
    print(f"Vamoose agent service on http://127.0.0.1:8080 - {mode}", file=sys.stderr, flush=True)
    for warning in startup_warnings(settings):
        print(f"WARNING: {warning}", file=sys.stderr, flush=True)
    app.run()
