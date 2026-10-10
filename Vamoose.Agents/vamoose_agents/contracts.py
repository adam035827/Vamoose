"""Wire contracts between Vamoose.Api and the agent service. camelCase on the wire."""

import datetime as dt
from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, StringConstraints
from pydantic.alias_generators import to_camel

Id = Annotated[str, StringConstraints(pattern=r"^[a-z0-9][a-z0-9-]{2,63}$")]
LocalTime = Annotated[str, StringConstraints(pattern=r"^([01][0-9]|2[0-3]):[0-5][0-9]$")]

AgentAction = Literal["Draft", "Questions", "Analyze", "Chat", "Price", "Finalize", "Nudge", "Retain"]
AgentStep = Literal[
    "Drafting",
    "Pricing",
    "DesigningQuestions",
    "Evaluating",
    "Recommending",
    "Finalizing",
    "Chatting",
    "Nudging",
    "Retaining",
]
PlanCategory = Literal["Flight", "Hotel", "Restaurant", "Activity"]


class Contract(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True, extra="forbid")

    def to_wire(self) -> dict:
        return self.model_dump(mode="json", by_alias=True, exclude_none=True)


class Money(Contract):
    amount: float = Field(ge=0)
    currency: Annotated[str, StringConstraints(pattern=r"^[A-Z]{3}$")]


class DraftItem(Contract):
    day: int = Field(ge=1, le=21, description="1-based trip day.")
    date: dt.date | None = Field(default=None, description="Only when the organizer gave exact dates.")
    start_time: LocalTime | None = None
    end_time: LocalTime | None = None
    category: Literal["Flight", "Lodging", "Activity", "Dining", "Transit", "Free"]
    title: str = Field(min_length=1, max_length=120)
    description: str | None = Field(default=None, max_length=600)
    location_name: str | None = Field(default=None, max_length=200)
    estimated_cost_per_person: Money | None = None
    energy_level: Literal["Low", "Medium", "High"] | None = None


class DraftItinerary(Contract):
    """Structured output of the Drafter agent."""

    title: str = Field(min_length=1, max_length=120)
    destination: str = Field(min_length=1, max_length=200)
    start_date: dt.date | None = None
    end_date: dt.date | None = None
    group_size: int | None = Field(default=None, ge=1, le=30)
    rationale: str = Field(max_length=1500, description="Short 'here's why' explaining the plan's tradeoffs.")
    items: list[DraftItem] = Field(min_length=1, max_length=60)
    assumptions: list[Annotated[str, StringConstraints(max_length=200)]] = Field(default_factory=list, max_length=10)
    open_questions: list[Annotated[str, StringConstraints(max_length=200)]] = Field(
        default_factory=list,
        max_length=10,
        description="Unknowns worth asking the group about.",
    )


class PlanOption(Contract):
    title: str = Field(min_length=1, max_length=200)
    description: str | None = Field(default=None, max_length=600)
    provider: str | None = Field(default=None, max_length=120, description="Airline, hotel brand, venue or tour operator.")
    location_name: str | None = Field(default=None, max_length=200)
    price: Money | None = Field(default=None, description="Per person, from search results when available.")
    url: str | None = Field(default=None, max_length=1000, description="Source link found by web search.")
    start_time: LocalTime | None = None
    end_time: LocalTime | None = None


class PlanSlot(Contract):
    key: Id = Field(description="Stable kebab-case id, e.g. 'flight-outbound' or 'dinner-day-2'.")
    category: PlanCategory
    title: str = Field(min_length=1, max_length=120, description="What this slot is, e.g. 'Outbound flight'.")
    day: int | None = Field(default=None, ge=1, le=21)
    selected_index: int = Field(ge=0, description="Index into options of the recommended choice.")
    options: list[PlanOption] = Field(
        min_length=1,
        max_length=4,
        description="The recommended option plus alternatives the group can switch to later.",
    )


class TripPlan(Contract):
    slots: list[PlanSlot] = Field(max_length=40)


class TripContext(Contract):
    name: str = Field(max_length=120)
    destination: str = Field(max_length=200)
    start_date: dt.date | None = None
    end_date: dt.date | None = None
    notes: str | None = Field(default=None, max_length=2000)
    traveler_count: int = Field(ge=1, le=100)
    status: Literal["Planning", "Agreement", "Locked", "Cancelled"]


class HistoryMessage(Contract):
    role: Literal["user", "agent"]
    text: str = Field(max_length=8000)


class NudgeContext(Contract):
    slot_title: str = Field(max_length=120)
    user_choice: str = Field(max_length=200)
    majority_choice: str = Field(max_length=200)
    user_choice_count: int = Field(ge=0)
    majority_count: int = Field(ge=0)
    total_responses: int = Field(ge=1)
    nudge_number: int = Field(ge=1, le=2)


class OptionTally(Contract):
    option: str = Field(max_length=200)
    count: int = Field(ge=0)


class SlotTally(Contract):
    key: str = Field(max_length=64)
    title: str = Field(max_length=120)
    selected: str = Field(max_length=200)
    tallies: list[OptionTally] = Field(max_length=4)


class Responses(Contract):
    approved: int = Field(ge=0)
    denied: int = Field(ge=0)
    pending: int = Field(ge=0)
    slots: list[SlotTally] = Field(max_length=40)


class ChatReply(Contract):
    """Structured output of the planner chat."""

    reply: str = Field(min_length=1, max_length=4000, description="Conversational answer shown in the chat.")
    plan: TripPlan | None = Field(
        default=None,
        description="The complete updated plan when it changed, otherwise null. Always null when editing is not allowed.",
    )


class Question(Contract):
    slot_key: str = Field(max_length=64)
    text: str = Field(min_length=1, max_length=300)


class QuestionSet(Contract):
    """Structured output of the question designer."""

    questions: list[Question] = Field(max_length=40)


class Analysis(Contract):
    """Structured output of the plan evaluator."""

    works_for_all: bool
    summary: str = Field(min_length=1, max_length=1500)


class AgentMessage(Contract):
    """Structured output for nudges and retention messages."""

    message: str = Field(min_length=1, max_length=1500)


class AgentInvocation(Contract):
    request_id: Id
    action: AgentAction
    message: str | None = Field(
        default=None,
        min_length=1,
        max_length=4000,
        description="Draft: the organizer's brain dump. Chat: the user's message. Untrusted input.",
    )
    trip: TripContext | None = None
    plan: TripPlan | None = None
    history: list[HistoryMessage] | None = Field(default=None, max_length=40)
    can_edit_plan: bool | None = None
    nudge: NudgeContext | None = None
    responses: Responses | None = None


class AgentEvent(Contract):
    request_id: Id
    sequence: int = Field(ge=0)
    type: Literal["Progress", "Text", "Result", "Error"]
    step: AgentStep | None = None
    message: str | None = Field(default=None, max_length=2000)
    draft: DraftItinerary | None = None
    reply: str | None = Field(default=None, max_length=4000)
    plan: TripPlan | None = None
    questions: list[Question] | None = None
    analysis: Analysis | None = None
