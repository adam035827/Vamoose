import pytest

from tests.fakes import ScriptedToolModel
from vamoose_agents.config import Settings
from vamoose_agents.contracts import AgentEvent
from vamoose_agents.dispatch import handle
from vamoose_agents.mock_data import MOCK_DRAFT

REAL = Settings(aws_region="us-east-2", planner_model_id=None, mock_agents=False)
MOCK = Settings(aws_region="us-east-2", planner_model_id=None, mock_agents=True)
DRAFT_REQUEST = {"requestId": "req-test-1", "action": "Draft", "message": "Lisbon in May for 6 friends."}


async def collect(payload: object, settings: Settings, model_factory=None) -> list[AgentEvent]:
    kwargs = {"model_factory": model_factory} if model_factory else {}
    return [event async for event in handle(payload, settings, **kwargs)]


def scripted_factory(_: Settings) -> ScriptedToolModel:
    return ScriptedToolModel("DraftItinerary", MOCK_DRAFT.to_wire())


async def test_draft_streams_progress_then_result_from_strands_agent():
    events = await collect(DRAFT_REQUEST, REAL, scripted_factory)

    assert [e.type for e in events] == ["Progress", "Result"]
    assert [e.sequence for e in events] == [0, 1]
    assert events[-1].draft == MOCK_DRAFT


async def test_mock_mode_never_builds_a_model():
    def explode(_: Settings):
        raise AssertionError("mock mode must not touch Bedrock")

    events = await collect(DRAFT_REQUEST, MOCK, explode)

    assert events[-1].type == "Result"
    assert events[-1].draft == MOCK_DRAFT


async def test_model_failure_returns_friendly_error_without_internals():
    def broken(_: Settings):
        raise RuntimeError("AccessDeniedException: arn:aws:bedrock:secret-detail")

    events = await collect(DRAFT_REQUEST, REAL, broken)

    assert events[-1].type == "Error"
    assert events[-1].message == "The planner hit a problem. Please try again."


@pytest.mark.parametrize(
    ("payload", "expected"),
    [
        ({"action": "Draft"}, "Invalid invocation payload."),
        ({"requestId": "Bad Id!", "action": "Draft", "message": "x"}, "Invalid invocation payload."),
        ({"requestId": "req-test-2", "action": "Draft", "message": "x" * 4001}, "Invalid invocation payload."),
        ({"requestId": "req-test-3", "action": "Draft", "extra": 1}, "Invalid invocation payload."),
        ({"requestId": "req-test-4", "action": "Draft"}, "Tell me about the trip first."),
        ({"requestId": "req-test-5", "action": "Analyze"}, "Analyze is not available yet."),
    ],
)
async def test_rejected_requests_return_single_error(payload, expected):
    events = await collect(payload, MOCK)

    assert len(events) == 1
    assert events[0].type == "Error"
    assert events[0].message == expected
