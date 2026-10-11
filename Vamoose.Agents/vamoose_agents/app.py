from collections.abc import AsyncIterator
import logging
import os
import sys

from bedrock_agentcore.runtime import BedrockAgentCoreApp

from .config import Settings, load_local_env
from .dispatch import handle
from .parameters import tavily_api_key
from .search import search_provider_name

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
    if settings.tavily_parameter and not settings.tavily_api_key and tavily_api_key(settings) is None:
        warnings.append(f"Could not read {settings.tavily_parameter} from Parameter Store; search falls back to DuckDuckGo.")
    return warnings


def configure_logging() -> None:
    logging.basicConfig(level=logging.WARNING, format="%(levelname)s %(name)s: %(message)s")
    logging.getLogger("vamoose_agents").setLevel(logging.INFO)


def describe(settings: Settings) -> str:
    if settings.mock_agents:
        return "mock"
    return (
        f"Bedrock planner={settings.planner_model_id} fast={settings.fast_model_id} "
        f"({settings.aws_region}, profile={os.getenv('AWS_PROFILE', 'default')}), search={search_provider_name(settings)}"
    )


def log_startup(where: str) -> None:
    configure_logging()
    settings = Settings.from_env()
    print(f"Vamoose agent service {where} - {describe(settings)}", file=sys.stderr, flush=True)
    for warning in startup_warnings(settings):
        print(f"WARNING: {warning}", file=sys.stderr, flush=True)


if __name__ == "__main__":
    load_local_env()
    log_startup("on http://127.0.0.1:8080")
    app.run()
