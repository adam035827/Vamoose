"""Pre-demo check against real AWS: `python -m vamoose_agents.smoke`. Costs a few cents; exits 1 on any failure."""

import asyncio
import json
import sys
import time
import urllib.request
from collections.abc import Awaitable, Callable

from .app import configure_logging, describe, startup_warnings
from .config import Settings, load_local_env
from .contracts import AgentInvocation
from .mock_data import mock_plan
from .models import build_models
from .parameters import tavily_api_key
from .planner import chat
from .search import TavilyMcpSearch, _ddgs_search

TRIP = {"name": "Smoke test", "destination": "Lisbon, Portugal", "travelerCount": 4, "status": "Planning"}


def _check_running_service() -> str:
    try:
        with urllib.request.urlopen("http://127.0.0.1:8080/ping", timeout=3) as response:
            return f"running: {json.load(response).get('status')}"
    except OSError:
        raise RuntimeError("no agent service on 127.0.0.1:8080 (start it, or ignore when testing locally)")


async def _check_fast_model(settings: Settings) -> str:
    reply = await chat(
        AgentInvocation.model_validate({"requestId": "req-smoke-1", "action": "Chat", "message": "Lisbon in May"}),
        build_models(settings),
    )
    return f"intro ok ({reply.destination})"


async def _check_planner(settings: Settings) -> str:
    reply = await chat(
        AgentInvocation.model_validate(
            {
                "requestId": "req-smoke-2",
                "action": "Chat",
                "trip": TRIP,
                "plan": mock_plan("Lisbon").to_wire(),
                "canEditPlan": True,
                "message": "Switch the welcome dinner to the seafood restaurant.",
            }
        ),
        build_models(settings),
    )
    selected = {s.key: s.selected_index for s in (reply.plan.slots if reply.plan else [])}
    if selected.get("dinner-day-1") != 1:
        raise RuntimeError("planner did not apply the edit")
    return "quick edit applied"


def _check_tavily(settings: Settings) -> str:
    key = tavily_api_key(settings)
    if not key:
        return "SKIP: no Tavily key configured (DuckDuckGo fallback)"
    results = TavilyMcpSearch(key, settings.use_os_truststore)("best BBQ restaurants Nashville")
    if not results:
        raise RuntimeError("no results")
    return f"{len(results)} results"


def _check_duckduckgo(_: Settings) -> str:
    return f"{len(_ddgs_search('best BBQ restaurants Nashville'))} results"


async def main() -> int:
    load_local_env()
    configure_logging()
    settings = Settings.from_env()
    print(f"Settings: {describe(settings)}")
    if settings.mock_agents:
        print("Mock mode is on (VAMOOSE_MOCK_AGENTS=1 or a .mock file); turn it off to smoke-test AWS.")
        return 1

    checks: list[tuple[str, bool, Callable[[Settings], str | Awaitable[str]]]] = [
        ("Setup", True, lambda s: "; ".join(startup_warnings(s)) or "ok"),
        ("Agent service", False, lambda s: _check_running_service()),
        ("Fast model (Haiku)", True, _check_fast_model),
        ("Planner (Sonnet)", True, _check_planner),
        ("Tavily MCP search", True, _check_tavily),
        ("DuckDuckGo fallback", False, _check_duckduckgo),
    ]
    failed = False
    for name, required, check in checks:
        started = time.perf_counter()
        try:
            outcome = check(settings)
            detail = await outcome if asyncio.iscoroutine(outcome) else outcome
            status = "FAIL" if name == "Setup" and detail != "ok" else "SKIP" if detail.startswith("SKIP") else "PASS"
            detail = detail.removeprefix("SKIP: ")
        except Exception as ex:
            detail, status = f"{type(ex).__name__}: {ex}"[:200], "FAIL" if required else "WARN"
        failed |= status == "FAIL"
        print(f"{status:4}  {name:20} {time.perf_counter() - started:5.1f}s  {detail}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))
