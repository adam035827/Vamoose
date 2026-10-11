from collections import Counter

from .contracts import ItemAnalysis, Responses, SlotResponses, TieBreak


def leaders(slot: SlotResponses) -> list[str]:
    """Options with the most votes, in plan order. More than one means a tie the agent has to break."""
    if not slot.choices:
        return [slot.selected]
    votes = Counter(c.option for c in slot.choices)
    order = [slot.selected, *slot.alternatives, *(o for o in votes if o != slot.selected and o not in slot.alternatives)]
    top = max(votes.values())
    return [o for o in dict.fromkeys(order) if votes[o] == top]


def ties(responses: Responses | None) -> dict[str, list[str]]:
    """Slot key -> tied options, for every slot where two or more options share the most votes."""
    return {slot.key: tied for slot in (responses.slots if responses else []) if len(tied := leaders(slot)) > 1}


def tally(responses: Responses | None, tie_breaks: dict[str, TieBreak] | None = None) -> tuple[list[ItemAnalysis], bool]:
    """The group's decision per slot: the option most people chose. Pure counting, no model needed.

    Ties use the agent's pick from `tie_breaks` when it is one of the tied options, otherwise the current selection.
    """
    tie_breaks = tie_breaks or {}
    items = []
    for slot in responses.slots if responses else []:
        tied = leaders(slot)
        pick = tie_breaks.get(slot.key) if len(tied) > 1 else None
        if pick and pick.option in tied:
            decision = pick.option
        else:
            pick = None
            decision = slot.selected if slot.selected in tied else tied[0]
        options = {slot.selected, *slot.alternatives}
        items.append(
            ItemAnalysis(
                slot_key=slot.key,
                decision=decision,
                tie_break=len(tied) > 1,
                reason=pick.reason if pick else None,
                approvers=[c.participant for c in slot.choices if c.option == decision],
                dissenters=[c.participant for c in slot.choices if c.option != decision],
                alternatives=sorted({c.option for c in slot.choices if c.option != decision} & options),
            )
        )
    works = responses is not None and not responses.pending and not any(item.dissenters for item in items)
    return items, works
