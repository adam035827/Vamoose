import os
from dataclasses import dataclass
from pathlib import Path

AGENTS_DIR = Path(__file__).resolve().parent.parent
# Creating this file switches a running service to mock mode without a restart (requirements 6.2).
MOCK_FLAG_FILE = AGENTS_DIR / ".mock"
# Developer-only settings and secrets (gitignored). AgentCore gets its settings from agentcore.json envVars.
LOCAL_ENV_FILE = AGENTS_DIR / ".env"

# Pinned so every machine and terminal runs the same models; both are enabled on the team's AWS account.
DEFAULT_PLANNER_MODEL_ID = "global.anthropic.claude-sonnet-4-6"
DEFAULT_FAST_MODEL_ID = "global.anthropic.claude-haiku-4-5-20251001-v1:0"


@dataclass(frozen=True)
class Settings:
    aws_region: str
    planner_model_id: str | None
    mock_agents: bool
    fast_model_id: str | None = None
    tavily_api_key: str | None = None
    use_os_truststore: bool = False
    tavily_parameter: str | None = None

    @classmethod
    def from_env(cls) -> "Settings":
        return cls(
            aws_region=os.getenv("AWS_REGION", "us-east-2"),
            planner_model_id=os.getenv("VAMOOSE_PLANNER_MODEL_ID") or DEFAULT_PLANNER_MODEL_ID,
            mock_agents=os.getenv("VAMOOSE_MOCK_AGENTS", "0") == "1" or MOCK_FLAG_FILE.exists(),
            fast_model_id=os.getenv("VAMOOSE_FAST_MODEL_ID") or DEFAULT_FAST_MODEL_ID,
            tavily_api_key=os.getenv("TAVILY_API_KEY") or None,
            use_os_truststore=os.getenv("VAMOOSE_USE_OS_TRUSTSTORE", "0") == "1",
            tavily_parameter=os.getenv("VAMOOSE_TAVILY_PARAMETER") or None,
        )


def load_local_env(path: Path = LOCAL_ENV_FILE) -> bool:
    """Loads Vamoose.Agents/.env if present. Real environment variables always win."""
    if not path.is_file():
        return False
    from dotenv import load_dotenv

    return load_dotenv(path, override=False)
