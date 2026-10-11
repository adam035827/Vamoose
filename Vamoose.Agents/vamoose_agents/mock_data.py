import datetime as dt

from .contracts import (
    AgentInvocation,
    AgentMessage,
    Analysis,
    ChatReply,
    DraftItem,
    DraftItinerary,
    Money,
    PlanOption,
    PlanSlot,
    Question,
    QuestionSet,
    TripPlan,
)
from .voting import tally

# Canned Drafter output so the API/UI can integrate without AWS credentials.
MOCK_DRAFT = DraftItinerary(
    title="Lisbon Long Weekend",
    destination="Lisbon, Portugal",
    start_date=dt.date(2027, 5, 14),
    end_date=dt.date(2027, 5, 18),
    group_size=6,
    rationale="Food-forward plan with Sintra early while energy is high, the beach on a slow day, "
    "and the big night out after the beach so the last morning can start late.",
    items=[
        DraftItem(day=1, start_time="14:00", category="Flight", title="Arrive in Lisbon (LIS)"),
        DraftItem(
            day=1,
            start_time="16:00",
            category="Lodging",
            title="Check in: Alfama apartment",
            estimated_cost_per_person=Money(amount=360, currency="USD"),
        ),
        DraftItem(
            day=1,
            start_time="19:30",
            end_time="21:30",
            category="Dining",
            title="Dinner at Time Out Market",
            estimated_cost_per_person=Money(amount=35, currency="USD"),
            energy_level="Low",
        ),
        DraftItem(
            day=2,
            start_time="08:30",
            end_time="17:30",
            category="Activity",
            title="Sintra day trip: Pena Palace + Quinta da Regaleira",
            estimated_cost_per_person=Money(amount=55, currency="USD"),
            energy_level="High",
        ),
        DraftItem(
            day=3,
            start_time="10:00",
            end_time="17:00",
            category="Activity",
            title="Beach day in Cascais",
            estimated_cost_per_person=Money(amount=20, currency="USD"),
            energy_level="Low",
        ),
        DraftItem(
            day=3,
            start_time="22:30",
            category="Activity",
            title="Big night out in Bairro Alto",
            estimated_cost_per_person=Money(amount=70, currency="USD"),
            energy_level="High",
        ),
        DraftItem(day=5, start_time="17:00", category="Flight", title="Depart Lisbon"),
    ],
    assumptions=["Flights are not included in per-item estimates."],
    open_questions=["Is everyone up for a late night out?", "What's each person's all-in budget?"],
)


# Fixed timestamp keeps mock output repeatable (requirements 6.1).
MOCK_FETCHED_AT = dt.datetime(2026, 10, 1, 12, 0, tzinfo=dt.UTC)


def _option(title: str, cost: float, source: str, **fields) -> PlanOption:
    return PlanOption(
        title=title,
        estimated_cost_per_person=Money(amount=cost, currency="USD"),
        source_url=f"https://example.com/{source}",
        price_fetched_at=MOCK_FETCHED_AT,
        **fields,
    )


def mock_plan(destination: str) -> TripPlan:
    """Canned plan with alternatives so the agreement flow can be exercised without Bedrock."""
    place = destination.split(",")[0].strip() or "the destination"
    return TripPlan(
        slots=[
            PlanSlot(
                key="flight-outbound",
                category="Flight",
                title=f"Flight to {place}",
                day=1,
                selected_index=0,
                options=[
                    _option("Morning nonstop", 540, "flight-am", provider="TAP Air Portugal", start_time="08:10", energy_level="Medium"),
                    _option("Evening nonstop", 610, "flight-pm", provider="United", start_time="18:45", energy_level="Low"),
                    _option("One-stop saver", 420, "flight-saver", provider="Iberia", start_time="06:30", energy_level="High"),
                ],
            ),
            PlanSlot(
                key="lodging-main",
                category="Lodging",
                title="Where we stay",
                day=1,
                selected_index=0,
                options=[
                    _option(f"Old-town apartment in {place}", 360, "apartment", description="Walkable, 3 bedrooms."),
                    _option(f"Boutique hotel in central {place}", 480, "boutique", description="Breakfast included."),
                    _option("Budget hostel private rooms", 190, "hostel", description="Further out, lively."),
                ],
            ),
            PlanSlot(
                key="dinner-day-1",
                category="Dining",
                title="Welcome dinner",
                day=1,
                selected_index=0,
                options=[
                    _option("Food market dinner", 35, "market", description="Lots of choice, casual.", start_time="19:30", energy_level="Low"),
                    _option("Seafood restaurant", 70, "seafood", description="Classic local seafood.", start_time="20:00", energy_level="Low"),
                ],
            ),
            PlanSlot(
                key="activity-day-2",
                category="Activity",
                title="Day 2 outing",
                day=2,
                selected_index=0,
                options=[
                    _option("Guided day trip to the hills", 55, "day-trip", start_time="08:30", end_time="17:30", energy_level="High"),
                    _option("Beach day", 20, "beach", start_time="10:00", end_time="17:00", energy_level="Low"),
                    _option("City walking tour + museum", 30, "walking-tour", start_time="10:00", end_time="15:00", energy_level="Medium"),
                ],
            ),
        ]
    )


def mock_chat(invocation: AgentInvocation) -> ChatReply:
    if invocation.trip is None:
        place = (invocation.message or "your destination").strip().rstrip(".!?")[:80]
        return ChatReply(
            reply=f"(Mock) {place} is a great pick for a group trip: good food, easy to walk, something for everyone.",
            destination=place,
            trip_name=f"{place.split(',')[0]} trip",
        )
    destination = invocation.trip.destination
    if not invocation.can_edit_plan:
        if invocation.nudge and invocation.nudge.kind == "Majority":
            n = invocation.nudge
            return ChatReply(
                reply=f"(Mock) Just a heads-up: {n.majority_count} of {n.total_responses} chose {n.majority_choice} for "
                f"{n.slot_title}. Your pick is still yours, but switching would help the group agree."
            )
        return ChatReply(reply="(Mock agent) Happy to help. Ask me anything about the plan.")
    if invocation.plan and invocation.plan.slots:
        return ChatReply(reply=f"(Mock agent) Noted. The current plan for {destination} still fits; no changes made.")
    if not invocation.history:
        return ChatReply(
            reply=f"(Mock agent) Let's plan {destination}! A few quick questions: what dates work, what's the budget "
            "per person, and where is everyone flying from? Any must-dos?"
        )
    return ChatReply(
        reply=f"(Mock agent) Here's a first plan for {destination} with alternatives for each item. "
        "Tell me what to change.",
        plan=mock_plan(destination),
    )


def mock_research(invocation: AgentInvocation) -> ChatReply:
    # The mock plan already has alternatives and links, so research has nothing to add.
    return ChatReply(reply="(Mock) The plan's options are already up to date.")


def mock_questions(invocation: AgentInvocation) -> QuestionSet:
    slots = invocation.plan.slots if invocation.plan else []
    return QuestionSet(
        questions=[
            Question(
                slot_key=s.key,
                type="SingleChoice",
                prompt=f"Does {s.options[s.selected_index].title} work for you for {s.title.lower()}?",
                options=[o.title for o in s.options],
            )
            for s in slots
        ]
    )


def mock_analysis(invocation: AgentInvocation) -> Analysis:
    r = invocation.responses
    items, works = tally(r)
    summary = (
        "(Mock) Everyone has approved the plan."
        if works
        else f"(Mock) {len(r.approved) if r else 0} approved, {len(r.denied) if r else 0} want changes, "
        f"{len(r.pending) if r else 0} haven't answered."
    )
    return Analysis(works_for_all=works, summary=summary, items=items)


def mock_nudge(invocation: AgentInvocation) -> AgentMessage:
    n = invocation.nudge
    if n.kind == "Stay":
        name = invocation.trip.name if invocation.trip else "this trip"
        return AgentMessage(
            message=f"(Mock) Sorry the plan isn't working for you. The group would miss you on {name}, and you can "
            "still pick alternatives for anything you don't like. If you'd still rather leave, that's okay."
        )
    return AgentMessage(
        message=f"(Mock) {n.majority_count} of {n.total_responses} travelers chose {n.majority_choice} for {n.slot_title}, "
        f"while you picked {n.user_choice}. Would you consider switching so everyone is on the same plan? It's your call."
    )
