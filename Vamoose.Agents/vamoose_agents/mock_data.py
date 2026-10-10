import datetime as dt

from .contracts import DraftItem, DraftItinerary, Money

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
