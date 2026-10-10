"""Wire contracts between Vamoose.Api and the agent service. camelCase on the wire."""

import datetime as dt
from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, StringConstraints
from pydantic.alias_generators import to_camel

Id = Annotated[str, StringConstraints(pattern=r"^[a-z0-9][a-z0-9-]{2,63}$")]
LocalTime = Annotated[str, StringConstraints(pattern=r"^([01][0-9]|2[0-3]):[0-5][0-9]$")]

AgentAction = Literal["Draft", "Questions", "Analyze", "Chat", "Price", "Finalize"]
AgentStep = Literal["Drafting", "Pricing", "DesigningQuestions", "Evaluating", "Recommending", "Finalizing", "Chatting"]


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


class AgentInvocation(Contract):
    request_id: Id
    action: AgentAction
    message: str | None = Field(
        default=None,
        min_length=1,
        max_length=4000,
        description="Draft: the organizer's brain dump. Chat: the organizer's message. Untrusted input.",
    )


class AgentEvent(Contract):
    request_id: Id
    sequence: int = Field(ge=0)
    type: Literal["Progress", "Text", "Result", "Error"]
    step: AgentStep | None = None
    message: str | None = Field(default=None, max_length=2000)
    draft: DraftItinerary | None = None
