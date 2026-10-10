import json
from collections.abc import AsyncIterator
from typing import Any

from strands.models import Model


class ScriptedToolModel(Model):
    """Offline stand-in for Bedrock: answers by calling the named tool with a scripted payload."""

    def __init__(self, tool_name: str, tool_input: dict[str, Any]) -> None:
        self._tool_name = tool_name
        self._tool_input = tool_input
        self.calls: list[dict[str, Any]] = []

    def update_config(self, **model_config: Any) -> None:
        pass

    def get_config(self) -> dict[str, Any]:
        return {}

    async def structured_output(self, output_model, prompt, system_prompt=None, **kwargs):
        raise NotImplementedError("Agents use the structured-output tool path.")
        yield  # pragma: no cover

    async def stream(self, messages, tool_specs=None, system_prompt=None, **kwargs) -> AsyncIterator[dict]:
        self.calls.append({"messages": messages, "tool_specs": tool_specs or [], "system_prompt": system_prompt})
        offered = {spec["name"] for spec in tool_specs or []}
        already_answered = any("toolResult" in block for block in messages[-1]["content"])

        yield {"messageStart": {"role": "assistant"}}
        if self._tool_name in offered and not already_answered:
            yield {"contentBlockStart": {"start": {"toolUse": {"toolUseId": "tool-1", "name": self._tool_name}}}}
            yield {"contentBlockDelta": {"delta": {"toolUse": {"input": json.dumps(self._tool_input)}}}}
            yield {"contentBlockStop": {}}
            yield {"messageStop": {"stopReason": "tool_use"}}
        else:
            yield {"contentBlockDelta": {"delta": {"text": "Done."}}}
            yield {"contentBlockStop": {}}
            yield {"messageStop": {"stopReason": "end_turn"}}
