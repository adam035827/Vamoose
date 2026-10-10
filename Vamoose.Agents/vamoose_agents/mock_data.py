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


def _usd(amount: float) -> Money:
    return Money(amount=amount, currency="USD")


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
                    PlanOption(title="Morning nonstop", provider="TAP Air Portugal", price=_usd(540), start_time="08:10"),
                    PlanOption(title="Evening nonstop", provider="United", price=_usd(610), start_time="18:45"),
                    PlanOption(title="One-stop saver", provider="Iberia", price=_usd(420), start_time="06:30"),
                ],
            ),
            PlanSlot(
                key="hotel-main",
                category="Hotel",
                title="Hotel",
                day=1,
                selected_index=0,
                options=[
                    PlanOption(title=f"Old-town apartment in {place}", description="Walkable, 3 bedrooms", price=_usd(360)),
                    PlanOption(title=f"Boutique hotel in central {place}", description="Breakfast included", price=_usd(480)),
                    PlanOption(title="Budget hostel private rooms", description="Further out, lively", price=_usd(190)),
                ],
            ),
            PlanSlot(
                key="dinner-day-1",
                category="Restaurant",
                title="Welcome dinner",
                day=1,
                selected_index=0,
                options=[
                    PlanOption(title="Food market dinner", description="Lots of choice, casual", price=_usd(35), start_time="19:30"),
                    PlanOption(title="Seafood restaurant", description="Classic local seafood", price=_usd(70), start_time="20:00"),
                ],
            ),
            PlanSlot(
                key="activity-day-2",
                category="Activity",
                title="Day 2 outing",
                day=2,
                selected_index=0,
                options=[
                    PlanOption(title="Guided day trip to the hills", price=_usd(55), start_time="08:30", end_time="17:30"),
                    PlanOption(title="Beach day", price=_usd(20), start_time="10:00", end_time="17:00"),
                    PlanOption(title="City walking tour + museum", price=_usd(30), start_time="10:00", end_time="15:00"),
                ],
            ),
        ]
    )


def mock_chat(invocation: AgentInvocation) -> ChatReply:
    destination = invocation.trip.destination if invocation.trip else "your destination"
    if not invocation.can_edit_plan:
        if invocation.nudge:
            n = invocation.nudge
            return ChatReply(
                reply=f"(Mock) Just a heads-up: {n.majority_count} of {n.total_responses} chose {n.majority_choice} for "
                f"{n.slot_title}. Your pick is still yours, but switching would help the group agree."
            )
        return ChatReply(reply="(Mock agent) The plan is set. Ask me anything about it.")
    if invocation.plan and invocation.plan.slots:
        return ChatReply(reply=f"(Mock agent) Noted. The current plan for {destination} still fits; no changes made.")
    return ChatReply(
        reply=f"(Mock agent) Here's a first plan for {destination} with alternatives for each item. "
        "Tell me what to change.",
        plan=mock_plan(destination),
    )


def mock_questions(invocation: AgentInvocation) -> QuestionSet:
    slots = invocation.plan.slots if invocation.plan else []
    return QuestionSet(
        questions=[
            Question(slot_key=s.key, text=f"Does {s.options[s.selected_index].title} work for you for {s.title.lower()}?")
            for s in slots
        ]
    )


def mock_analysis(invocation: AgentInvocation) -> Analysis:
    r = invocation.responses
    works = r is not None and r.denied == 0 and r.pending == 0
    summary = (
        "(Mock) Everyone has approved the plan."
        if works
        else f"(Mock) {r.approved if r else 0} approved, {r.denied if r else 0} denied, {r.pending if r else 0} pending."
    )
    return Analysis(works_for_all=works, summary=summary)


def mock_nudge(invocation: AgentInvocation) -> AgentMessage:
    n = invocation.nudge
    return AgentMessage(
        message=f"(Mock) {n.majority_count} of {n.total_responses} travelers chose {n.majority_choice} for {n.slot_title}, "
        f"while you picked {n.user_choice}. Would you consider switching so everyone is on the same plan? It's your call."
    )


def mock_retain(invocation: AgentInvocation) -> AgentMessage:
    name = invocation.trip.name if invocation.trip else "this trip"
    return AgentMessage(
        message=f"(Mock) Sorry the plan isn't working for you. The group would miss you on {name}, and you can still "
        "pick alternatives for anything you don't like. If you'd still rather leave, that's okay."
    )
