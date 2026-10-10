import logging
from collections.abc import AsyncIterator, Callable

from pydantic import ValidationError
from strands.models import Model

from .config import Settings
from .contracts import AgentEvent, AgentInvocation, AgentStep, DraftItinerary
from .drafter import build_planner_model, draft_itinerary
from .mock_data import MOCK_DRAFT

logger = logging.getLogger(__name__)

ModelFactory = Callable[[Settings], Model]


class Emitter:
    """Builds sequenced events for one invocation."""

    def __init__(self, request_id: str) -> None:
        self._request_id = request_id
        self._sequence = 0

    def _next(self, **fields) -> AgentEvent:
        event = AgentEvent(request_id=self._request_id, sequence=self._sequence, **fields)
        self._sequence += 1
        return event

    def progress(self, step: AgentStep, message: str | None = None) -> AgentEvent:
        return self._next(type="Progress", step=step, message=message)

    def draft(self, draft: DraftItinerary) -> AgentEvent:
        return self._next(type="Result", draft=draft)

    def error(self, message: str) -> AgentEvent:
        return self._next(type="Error", message=message)


async def handle(
    payload: object,
    settings: Settings | None = None,
    model_factory: ModelFactory = build_planner_model,
) -> AsyncIterator[AgentEvent]:
    settings = settings or Settings.from_env()
    try:
        invocation = AgentInvocation.model_validate(payload)
    except ValidationError:
        yield Emitter("req-invalid").error("Invalid invocation payload.")
        return

    emit = Emitter(invocation.request_id)
    if invocation.action != "Draft":
        yield emit.error(f"{invocation.action} is not available yet.")
        return
    if not invocation.message:
        yield emit.error("Tell me about the trip first.")
        return

    yield emit.progress("Drafting", "Sketching a day-by-day plan")
    try:
        draft = MOCK_DRAFT if settings.mock_agents else await draft_itinerary(invocation.message, model_factory(settings))
    except Exception:
        # Details stay in server logs; callers only see a friendly message.
        logger.exception("Draft failed for request %s", invocation.request_id)
        yield emit.error("The planner hit a problem. Please try again.")
        return

    yield emit.draft(draft)
