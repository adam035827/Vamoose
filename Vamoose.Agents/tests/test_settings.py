import os

from vamoose_agents import parameters
from vamoose_agents.app import startup_warnings
from vamoose_agents.config import Settings, load_local_env
from vamoose_agents.search import search_provider_name

BASE = Settings(aws_region="us-east-2", planner_model_id=None, mock_agents=False)


def test_local_env_file_fills_missing_settings_but_never_overrides(monkeypatch, tmp_path):
    env_file = tmp_path / ".env"
    env_file.write_text("VAMOOSE_TEST_A=from-file\nVAMOOSE_TEST_B=from-file\n", encoding="utf-8")
    monkeypatch.delenv("VAMOOSE_TEST_A", raising=False)
    monkeypatch.setenv("VAMOOSE_TEST_B", "from-shell")

    assert load_local_env(env_file)

    assert os.environ["VAMOOSE_TEST_A"] == "from-file"
    assert os.environ["VAMOOSE_TEST_B"] == "from-shell"
    monkeypatch.delenv("VAMOOSE_TEST_A")


def test_missing_local_env_file_is_fine(tmp_path):
    assert load_local_env(tmp_path / ".env") is False


def test_env_key_wins_over_parameter_store(monkeypatch):
    monkeypatch.setattr(parameters, "read_secure_parameter", lambda name, region: "from-ssm")
    settings = Settings(**{**BASE.__dict__, "tavily_api_key": "from-env", "tavily_parameter": "/vamoose/tavily-api-key"})

    assert parameters.tavily_api_key(settings) == "from-env"


def test_key_is_read_from_parameter_store(monkeypatch):
    calls = []
    monkeypatch.setattr(parameters, "read_secure_parameter", lambda name, region: calls.append((name, region)) or "from-ssm")
    settings = Settings(**{**BASE.__dict__, "tavily_parameter": "/vamoose/tavily-api-key"})

    assert parameters.tavily_api_key(settings) == "from-ssm"
    assert calls == [("/vamoose/tavily-api-key", "us-east-2")]
    assert "Parameter Store /vamoose/tavily-api-key" in search_provider_name(settings)


def test_unreadable_parameter_falls_back_and_warns(monkeypatch):
    def denied(name: str, region: str) -> str:
        raise PermissionError("AccessDenied")

    monkeypatch.setattr(parameters, "read_secure_parameter", denied)
    monkeypatch.setenv("AWS_ACCESS_KEY_ID", "AKIAEXAMPLE")
    monkeypatch.setenv("AWS_SECRET_ACCESS_KEY", "example")
    monkeypatch.delenv("AWS_CA_BUNDLE", raising=False)
    settings = Settings(**{**BASE.__dict__, "tavily_parameter": "/vamoose/tavily-api-key"})

    assert parameters.tavily_api_key(settings) is None
    assert any("/vamoose/tavily-api-key" in w for w in startup_warnings(settings))
