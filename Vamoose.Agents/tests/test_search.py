from vamoose_agents.search import CachedSearch, FallbackSearch, SearchSession, parse_tavily_result


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


def test_parse_tavily_structured_results():
    result = {"structuredContent": {"results": [{"title": "Hotel", "url": "https://example.com/h", "content": "$120/night"}]}}

    assert parse_tavily_result(result) == [{"title": "Hotel", "url": "https://example.com/h", "snippet": "$120/night"}]


def test_parse_tavily_text_results():
    text = "Detailed Results:\n\nTitle: Hotel A\nURL: https://example.com/a\nContent: Nice.\n\nTitle: Hotel B\nURL: https://example.com/b\nContent: Cheap."
    result = {"content": [{"text": text}]}

    assert [r["url"] for r in parse_tavily_result(result)] == ["https://example.com/a", "https://example.com/b"]
    assert parse_tavily_result(result)[1]["snippet"] == "Cheap."


def test_cached_search_reuses_results_for_same_query():
    calls: list[str] = []
    search = CachedSearch(lambda q: calls.append(q) or [{"url": "https://example.com"}])

    search("Hotels  Lisbon")
    search("hotels lisbon")

    assert calls == ["Hotels  Lisbon"]


def test_fallback_search_uses_next_provider_on_failure():
    def broken(_: str) -> list[dict]:
        raise ConnectionError("blocked")

    search = FallbackSearch([broken, lambda q: [{"url": "https://example.com/backup"}]])

    assert search("q") == [{"url": "https://example.com/backup"}]
