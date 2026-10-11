from .contracts import ItemAnalysis, Responses


def tally(responses: Responses | None) -> tuple[list[ItemAnalysis], bool]:
    """Per-slot approvers/dissenters and whether the plan works for everyone. Pure counting, no model needed."""
    items = [
        ItemAnalysis(
            slot_key=slot.key,
            approvers=[c.participant for c in slot.choices if c.option == slot.selected],
            dissenters=[c.participant for c in slot.choices if c.option != slot.selected],
            alternatives=sorted({c.option for c in slot.choices if c.option != slot.selected} & set(slot.alternatives)),
        )
        for slot in (responses.slots if responses else [])
    ]
    works = (
        responses is not None
        and not responses.denied
        and not responses.pending
        and not any(item.dissenters for item in items)
    )
    return items, works
