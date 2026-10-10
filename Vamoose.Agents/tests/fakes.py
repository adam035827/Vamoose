import json
from collections.abc import AsyncIterator, Sequence
from typing import Any

from strands.models import Model


class ScriptedToolModel(Model):
    """Offline stand-in for Bedrock: calls each `before` tool once, then answers with the named tool."""

    def __init__(
        self, tool_name: str, tool_input: dict[str, Any], before: Sequence[tuple[str, dict[str, Any]]] = ()
    ) -> None:
        self._tool_name = tool_name
        self._tool_input = tool_input
        self._script = [*before, (tool_name, tool_input)]
        self._next = 0
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
        if self._next < len(self._script) and self._script[self._next][0] in offered:
            name, payload = self._script[self._next]
            self._next += 1
            events = self._tool_use(name, payload)
        elif self._tool_name in offered and not already_answered:
            events = self._tool_use(self._tool_name, self._tool_input)
        else:
            events = [
                {"contentBlockDelta": {"delta": {"text": "Done."}}},
                {"contentBlockStop": {}},
                {"messageStop": {"stopReason": "end_turn"}},
            ]
        for event in events:
            yield event

    def _tool_use(self, name: str, payload: dict[str, Any]) -> list[dict]:
        tool_use_id = f"tool-{len(self.calls)}"
        return [
            {"contentBlockStart": {"start": {"toolUse": {"toolUseId": tool_use_id, "name": name}}}},
            {"contentBlockDelta": {"delta": {"toolUse": {"input": json.dumps(payload)}}}},
            {"contentBlockStop": {}},
            {"messageStop": {"stopReason": "tool_use"}},
        ]
