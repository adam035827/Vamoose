from vamoose_agents.search import SearchSession


def test_search_records_returned_urls_and_fetch_time():
    session = SearchSession(lambda q: [{"title": "A", "url": "https://example.com/a", "snippet": "x"}])

    result = session.tool("hotels lisbon")

    assert result["results"][0]["url"] == "https://example.com/a"
    assert result["fetchedAt"] == session.sources["https://example.com/a"].isoformat()


def test_search_failure_gives_no_timestamp_to_copy():
    def failing(_: str) -> list[dict]:
        raise ConnectionError("dns")

    result = SearchSession(failing).tool("hotels lisbon")

    assert "error" in result and "fetchedAt" not in result


def test_search_is_capped_per_request():
    calls: list[str] = []
    session = SearchSession(lambda q: calls.append(q) or [], max_searches=2)

    results = [session.tool(f"q{i}") for i in range(3)]

    assert len(calls) == 2
    assert "error" in results[2]
