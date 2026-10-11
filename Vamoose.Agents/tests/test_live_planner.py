import os

import pytest

from vamoose_agents.config import Settings, load_local_env
from vamoose_agents.contracts import AgentInvocation
from vamoose_agents.mock_data import mock_plan
from vamoose_agents.models import build_models
from vamoose_agents.planner import analyze, chat, nudge, questions

load_local_env()

pytestmark = [
    pytest.mark.live,
    pytest.mark.skipif(os.getenv("VAMOOSE_LIVE_TESTS") != "1", reason="set VAMOOSE_LIVE_TESTS=1 to call Bedrock"),
]

TRIP = {"name": "Lisbon weekend", "destination": "Lisbon, Portugal", "travelerCount": 3, "status": "Agreement"}
PLAN = mock_plan("Lisbon").to_wire()


def invocation(**fields) -> AgentInvocation:
    return AgentInvocation.model_validate({"requestId": "req-live-1", **fields})


@pytest.fixture
def model():
    return build_models(Settings.from_env())


async def test_intro_against_real_bedrock(model):
    reply = await chat(invocation(action="Chat", message="Lisbon in May with friends"), model)

    assert reply.destination and "lisbon" in reply.destination.lower()


async def test_planning_chat_returns_plan_with_alternatives(model):
    reply = await chat(
        invocation(
            action="Chat",
            trip={**TRIP, "status": "Planning"},
            canEditPlan=True,
            message="3 friends from New York, May 14-17 2027, mid-range budget. Please draft the full plan now.",
        ),
        model,
    )

    assert reply.plan is not None and all(len(s.options) >= 2 for s in reply.plan.slots)


async def test_quick_edit_changes_only_one_slot(model):
    reply = await chat(
        invocation(
            action="Chat",
            trip={**TRIP, "status": "Planning"},
            plan=PLAN,
            canEditPlan=True,
            message="Switch the welcome dinner to the seafood restaurant.",
        ),
        model,
    )

    assert reply.plan is not None
    selected = {s.key: s.selected_index for s in reply.plan.slots}
    assert selected["dinner-day-1"] == 1
    assert all(selected[s["key"]] == s["selectedIndex"] for s in PLAN["slots"] if s["key"] != "dinner-day-1")


async def test_questions_against_real_bedrock(model):
    result = await questions(invocation(action="Questions", trip=TRIP, plan=PLAN), model)

    assert {q.slot_key for q in result.questions} == {s["key"] for s in PLAN["slots"]}


async def test_analyze_against_real_bedrock(model):
    responses = {
        "approved": ["traveler-1"],
        "denied": ["traveler-2"],
        "pending": [],
        "slots": [
            {
                "key": "lodging-main",
                "title": "Where we stay",
                "selected": "Old-town apartment in Lisbon",
                "alternatives": ["Budget hostel private rooms"],
                "choices": [
                    {"participant": "traveler-1", "option": "Old-town apartment in Lisbon"},
                    {"participant": "traveler-2", "option": "Budget hostel private rooms"},
                ],
            }
        ],
    }

    result = await analyze(invocation(action="Analyze", trip=TRIP, plan=PLAN, responses=responses), model)

    assert result.works_for_all is False


async def test_stay_nudge_against_real_bedrock(model):
    result = await nudge(invocation(action="Nudge", trip=TRIP, plan=PLAN, nudge={"kind": "Stay"}), model)

    assert result.message
