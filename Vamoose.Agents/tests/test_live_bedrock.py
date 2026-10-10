import datetime as dt
import os

import pytest

from vamoose_agents.config import Settings
from vamoose_agents.contracts import DraftItinerary
from vamoose_agents.drafter import build_planner_model, draft_itinerary

pytestmark = [
    pytest.mark.live,
    pytest.mark.skipif(os.getenv("VAMOOSE_LIVE_TESTS") != "1", reason="set VAMOOSE_LIVE_TESTS=1 to call Bedrock"),
]


async def test_drafter_against_real_bedrock():
    model = build_planner_model(Settings.from_env())

    draft = await draft_itinerary(
        "Lisbon, May 14-18 2027, 6 friends. Big on food, one beach day, one big night out, "
        "Sintra if possible. About $1,500 each including flights.",
        model,
    )

    assert isinstance(draft, DraftItinerary)
    assert "lisbon" in draft.destination.lower()
    assert any("sintra" in item.title.lower() for item in draft.items)


async def test_drafter_schedules_partial_dates_in_the_future():
    model = build_planner_model(Settings.from_env())

    draft = await draft_itinerary("Nashville, 3 days in March, 5 friends, live music and BBQ.", model)

    assert draft.start_date is not None and draft.start_date > dt.date.today()
