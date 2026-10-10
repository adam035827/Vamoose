import json

import pytest
from starlette.testclient import TestClient

from vamoose_agents.app import app
from vamoose_agents.contracts import AgentEvent


@pytest.fixture
def client(monkeypatch) -> TestClient:
    monkeypatch.setenv("VAMOOSE_MOCK_AGENTS", "1")
    return TestClient(app)


def parse_sse(body: str) -> list[AgentEvent]:
    return [
        AgentEvent.model_validate(json.loads(line.removeprefix("data: ")))
        for line in body.splitlines()
        if line.startswith("data: ")
    ]


def test_ping_reports_healthy(client):
    response = client.get("/ping")

    assert response.status_code == 200
    assert response.json()["status"] == "Healthy"


def test_invocations_streams_contract_events_as_sse(client):
    response = client.post(
        "/invocations",
        json={"requestId": "req-http-1", "action": "Draft", "message": "Lisbon in May."},
    )

    assert response.status_code == 200
    assert response.headers["content-type"].startswith("text/event-stream")
    events = parse_sse(response.text)
    assert [e.type for e in events] == ["Progress", "Result"]
    assert events[-1].draft is not None


def test_invocations_wire_format_is_camel_case(client):
    response = client.post(
        "/invocations",
        json={"requestId": "req-http-2", "action": "Draft", "message": "Lisbon in May."},
    )

    data_lines = [line.removeprefix("data: ") for line in response.text.splitlines() if line.startswith("data: ")]
    result = json.loads(data_lines[-1])
    assert {"requestId", "sequence", "type", "draft"} <= result.keys()
    assert "openQuestions" in result["draft"]
