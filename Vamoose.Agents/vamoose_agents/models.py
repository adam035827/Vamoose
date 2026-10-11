import functools
from dataclasses import dataclass

from strands.models import BedrockModel, Model
from strands.models.model import CacheConfig

from .config import DEFAULT_FAST_MODEL_ID, DEFAULT_PLANNER_MODEL_ID, Settings


@dataclass(frozen=True)
class Models:
    """planner: builds and edits plans (Sonnet). fast: short, well-defined tasks and research (Haiku)."""

    planner: Model
    fast: Model


def as_models(model: Model | Models) -> Models:
    return model if isinstance(model, Models) else Models(planner=model, fast=model)


@functools.lru_cache(maxsize=8)
def _bedrock(region: str, model_id: str, max_tokens: int) -> BedrockModel:
    # Cached per process: building the boto3 client per request costs time, and the model holds no request state.
    return BedrockModel(
        region_name=region,
        model_id=model_id,
        max_tokens=max_tokens,
        temperature=0.4,
        cache_config=CacheConfig(strategy="auto"),
    )


def build_models(settings: Settings) -> Models:
    return Models(
        planner=_bedrock(settings.aws_region, settings.planner_model_id or DEFAULT_PLANNER_MODEL_ID, 16000),
        fast=_bedrock(settings.aws_region, settings.fast_model_id or DEFAULT_FAST_MODEL_ID, 4096),
    )


def model_id(model: Model) -> str:
    config = model.get_config() or {}
    return str(config.get("model_id", type(model).__name__)) if isinstance(config, dict) else type(model).__name__
