import os

import pytest

from vamoose_agents.config import Settings, load_local_env
from vamoose_agents.parameters import tavily_api_key
from vamoose_agents.search import TavilyMcpSearch

load_local_env()

pytestmark = [
    pytest.mark.live,
    pytest.mark.skipif(
        os.getenv("VAMOOSE_LIVE_TESTS") != "1"
        or not (os.getenv("TAVILY_API_KEY") or os.getenv("VAMOOSE_TAVILY_PARAMETER")),
        reason="set VAMOOSE_LIVE_TESTS=1 and TAVILY_API_KEY (or VAMOOSE_TAVILY_PARAMETER) to call Tavily",
    ),
]


def test_tavily_mcp_search_returns_real_links():
    settings = Settings.from_env()
    search = TavilyMcpSearch(tavily_api_key(settings), settings.use_os_truststore)

    results = search("best BBQ restaurants Nashville")

    assert results and all(r["url"].startswith("http") for r in results)
