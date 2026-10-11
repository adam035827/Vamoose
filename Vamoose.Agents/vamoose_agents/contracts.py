"""Wire contracts between Vamoose.Api and the agent service. camelCase on the wire."""

import datetime as dt
from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, StringConstraints
from pydantic.alias_generators import to_camel

Id = Annotated[str, StringConstraints(pattern=r"^[a-z0-9][a-z0-9-]{2,63}$")]
LocalTime = Annotated[str, StringConstraints(pattern=r"^([01][0-9]|2[0-3]):[0-5][0-9]$")]

AgentAction = Literal["Draft", "Questions", "Analyze", "Chat", "Research", "Price", "Finalize", "Nudge"]
AgentStep = Literal[
    "Drafting",
    "Researching",
    "Pricing",
    "DesigningQuestions",
    "Evaluating",
    "Recommending",
    "Finalizing",
    "Chatting",
    "Nudging",
]
# Same categories as DraftItem (requirements 3.4).
PlanCategory = Literal["Flight", "Lodging", "Activity", "Dining", "Transit", "Free"]
EnergyLevel = Literal["Low", "Medium", "High"]


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
    description: str | None = Field(default=None, max_length=600, description="One short sentence.")
    provider: str | None = Field(default=None, max_length=120, description="Airline, hotel brand, venue or tour operator.")
    location_name: str | None = Field(default=None, max_length=200)
    estimated_cost_per_person: Money | None = None
    source_url: str | None = Field(default=None, max_length=1000, description="Link found by web search. Never invented.")
    price_fetched_at: dt.datetime | None = Field(
        default=None, description="The fetchedAt time of the search result the price came from."
    )
    start_time: LocalTime | None = None
    end_time: LocalTime | None = None
    energy_level: EnergyLevel | None = None


class PlanSlot(Contract):
    key: Id = Field(description="Stable kebab-case id, e.g. 'flight-outbound' or 'dinner-day-2'.")
    category: PlanCategory
    title: str = Field(min_length=1, max_length=120, description="What this slot is, e.g. 'Outbound flight'.")
    day: int | None = Field(default=None, ge=1, le=21, description="1-based trip day.")
    date: dt.date | None = Field(default=None, description="Only when exact dates are known.")
    selected_index: int = Field(ge=0, description="Index into options of the recommended choice.")
    options: list[PlanOption] = Field(
        min_length=1,
        max_length=4,
        description="The recommended option plus the alternatives considered, which the group can switch to later.",
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
    kind: Literal["Stay"] = Field(description="Stay: the user asked to leave the trip (4.5).")


class ParticipantChoice(Contract):
    participant: str = Field(max_length=40, description="Anonymous label such as 'traveler-2'.")
    option: str = Field(max_length=200)


class SlotResponses(Contract):
    key: str = Field(max_length=64)
    title: str = Field(max_length=120)
    selected: str = Field(max_length=200)
    alternatives: list[str] = Field(max_length=4)
    choices: list[ParticipantChoice] = Field(max_length=100)


class Responses(Contract):
    approved: list[str] = Field(max_length=100, description="Participants who approved the plan.")
    denied: list[str] = Field(max_length=100)
    pending: list[str] = Field(max_length=100)
    slots: list[SlotResponses] = Field(max_length=40)


class ChatReply(Contract):
    """Structured output of the planner chat."""

    reply: str = Field(min_length=1, max_length=4000, description="Conversational answer shown in the chat.")
    plan: TripPlan | None = Field(
        default=None,
        description="The complete updated plan when it changed, otherwise null. Always null when editing is not allowed.",
    )
    destination: str | None = Field(
        default=None, max_length=200, description="Start of a trip only: the destination, e.g. 'Lisbon, Portugal'."
    )
    trip_name: str | None = Field(
        default=None, max_length=120, description="Start of a trip only: a short trip name, e.g. 'Lisbon long weekend'."
    )


class Question(Contract):
    slot_key: str = Field(max_length=64)
    type: Literal["SingleChoice"] = Field(description="Travelers pick exactly one option for the item.")
    prompt: str = Field(min_length=1, max_length=300)
    options: list[Annotated[str, StringConstraints(min_length=1, max_length=200)]] = Field(
        min_length=1, max_length=4, description="One label per plan option of the slot, in the same order."
    )


class QuestionSet(Contract):
    """Structured output of the question designer."""

    questions: list[Question] = Field(max_length=40)


class ItemAnalysis(Contract):
    slot_key: str = Field(max_length=64)
    decision: str = Field(max_length=200, description="The option the group goes with: the one most people chose.")
    tie_break: bool = Field(default=False, description="Two or more options had the most votes and the agent picked.")
    reason: str | None = Field(default=None, max_length=300, description="Tie breaks only: why the agent picked it.")
    approvers: list[str] = Field(max_length=100, description="Participants who chose the decision.")
    dissenters: list[str] = Field(max_length=100, description="Participants who chose something else.")
    alternatives: list[str] = Field(max_length=4, description="Other options the dissenters chose.")


class Analysis(Contract):
    """Structured output of the plan evaluator: the group's decision per slot, applied by the app."""

    works_for_all: bool
    summary: str = Field(min_length=1, max_length=1500)
    items: list[ItemAnalysis] = Field(default_factory=list, max_length=40)


class AgentMessage(Contract):
    """Structured output for nudges."""

    message: str = Field(min_length=1, max_length=1500)


# Internal structured outputs: produced by models inside the agent service, never sent over the wire.


class SlotDraft(Contract):
    """A slot as the planner writes it: options come from research refs and/or are written out."""

    category: PlanCategory
    title: str = Field(min_length=1, max_length=120)
    day: int | None = Field(default=None, ge=1, le=21)
    date: dt.date | None = None
    selected_index: int = Field(default=0, ge=0, description="Index into the combined options (refs first).")
    option_refs: list[Annotated[str, StringConstraints(max_length=20)]] = Field(
        default_factory=list, max_length=4, description="`ref` values of options returned by research."
    )
    options: list[PlanOption] = Field(
        default_factory=list, max_length=4, description="Only for options that did not come from research."
    )


class PlanChange(Contract):
    op: Literal["upsert", "remove", "select"] = Field(
        description="upsert: add or replace a whole slot. remove: delete a slot. select: change selected_index only."
    )
    slot_key: Id
    slot: SlotDraft | None = Field(default=None, description="upsert only: the complete slot.")
    selected_index: int | None = Field(default=None, ge=0, description="select only.")


class PlannerReply(Contract):
    """What the planner model returns; the agent applies `changes` to the current plan."""

    reply: str = Field(min_length=1, max_length=4000, description="Conversational answer shown in the chat.")
    changes: list[PlanChange] = Field(
        default_factory=list, max_length=40, description="Only slots that change. Empty when just asking or answering."
    )


class TieBreak(Contract):
    slot_key: str = Field(max_length=64)
    option: str = Field(max_length=200, description="Exactly one of the tied options for this slot.")
    reason: str = Field(min_length=1, max_length=300, description="One short sentence on why it suits the group.")


class Resolution(Contract):
    """What the evaluator model returns: picks for tied slots and a summary for the organizer."""

    summary: str = Field(min_length=1, max_length=1500)
    tie_breaks: list[TieBreak] = Field(default_factory=list, max_length=40)


class ResearchResult(Contract):
    """What a research assistant returns for one slot."""

    options: list[PlanOption] = Field(min_length=1, max_length=4)
    note: str | None = Field(default=None, max_length=300, description="Caveats, e.g. prices are estimates.")


class AgentInvocation(Contract):
    request_id: Id
    action: AgentAction
    message: str | None = Field(
        default=None,
        min_length=1,
        max_length=4000,
        description="Draft: the organizer's brain dump. Chat: the user's message. Untrusted input.",
    )
    trip: TripContext | None = Field(default=None, description="Chat without a trip is the start of a new trip (3.1).")
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
    destination: str | None = None
    trip_name: str | None = None
    questions: list[Question] | None = None
    analysis: Analysis | None = None
