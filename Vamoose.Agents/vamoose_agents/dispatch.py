import logging
from collections.abc import AsyncIterator, Awaitable, Callable
from dataclasses import dataclass

from pydantic import ValidationError
from strands.models import Model

from . import mock_data, planner
from .config import Settings
from .contracts import (
    AgentEvent,
    AgentInvocation,
    AgentMessage,
    AgentStep,
    Analysis,
    ChatReply,
    DraftItinerary,
    QuestionSet,
)
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

    def result(self, output: ChatReply | QuestionSet | Analysis | AgentMessage) -> AgentEvent:
        match output:
            case ChatReply():
                return self._next(type="Result", reply=output.reply, plan=output.plan)
            case QuestionSet():
                return self._next(type="Result", questions=output.questions)
            case Analysis():
                return self._next(type="Result", analysis=output)
            case AgentMessage():
                return self._next(type="Result", reply=output.message)

    def error(self, message: str) -> AgentEvent:
        return self._next(type="Error", message=message)


@dataclass(frozen=True)
class Route:
    step: AgentStep
    progress: str
    requires: tuple[str, ...]
    run: Callable[[AgentInvocation, Model], Awaitable[ChatReply | QuestionSet | Analysis | AgentMessage]]
    mock: Callable[[AgentInvocation], ChatReply | QuestionSet | Analysis | AgentMessage]


ROUTES: dict[str, Route] = {
    "Chat": Route("Chatting", "Thinking it over", ("message", "trip"), planner.chat, mock_data.mock_chat),
    "Questions": Route(
        "DesigningQuestions", "Writing questions for the group", ("trip", "plan"), planner.questions, mock_data.mock_questions
    ),
    "Analyze": Route(
        "Evaluating", "Checking whether the plan works for everyone", ("trip", "plan", "responses"),
        planner.analyze, mock_data.mock_analysis,
    ),
    "Nudge": Route("Nudging", "Writing a note", ("trip", "plan", "nudge"), planner.nudge, mock_data.mock_nudge),
    "Retain": Route("Retaining", "Writing a note", ("trip",), planner.retain, mock_data.mock_retain),
}


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
    if invocation.action == "Draft":
        async for event in _draft(invocation, emit, settings, model_factory):
            yield event
        return

    route = ROUTES.get(invocation.action)
    if route is None:
        yield emit.error(f"{invocation.action} is not available yet.")
        return
    missing = [field for field in route.requires if getattr(invocation, field) is None]
    if missing:
        yield emit.error(f"{invocation.action} needs: {', '.join(missing)}.")
        return

    yield emit.progress(route.step, route.progress)
    try:
        output = route.mock(invocation) if settings.mock_agents else await route.run(invocation, model_factory(settings))
    except Exception:
        logger.exception("%s failed for request %s", invocation.action, invocation.request_id)
        yield emit.error("The Vamoose agent hit a problem. Please try again.")
        return

    yield emit.result(output)


async def _draft(
    invocation: AgentInvocation, emit: Emitter, settings: Settings, model_factory: ModelFactory
) -> AsyncIterator[AgentEvent]:
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
