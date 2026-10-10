import os
from dataclasses import dataclass


@dataclass(frozen=True)
class Settings:
    aws_region: str
    planner_model_id: str | None
    mock_agents: bool

    @classmethod
    def from_env(cls) -> "Settings":
        return cls(
            aws_region=os.getenv("AWS_REGION", "us-east-2"),
            planner_model_id=os.getenv("VAMOOSE_PLANNER_MODEL_ID") or None,
            mock_agents=os.getenv("VAMOOSE_MOCK_AGENTS", "0") == "1",
        )
