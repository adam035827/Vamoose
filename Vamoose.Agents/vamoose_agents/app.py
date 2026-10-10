from collections.abc import AsyncIterator

from bedrock_agentcore.runtime import BedrockAgentCoreApp

from .dispatch import handle

app = BedrockAgentCoreApp()


@app.entrypoint
async def invoke(payload: dict) -> AsyncIterator[dict]:
    async for event in handle(payload):
        yield event.to_wire()


if __name__ == "__main__":
    app.run()
