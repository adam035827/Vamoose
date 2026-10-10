from vamoose_agents.app import startup_warnings
from vamoose_agents.config import Settings

REAL = Settings(aws_region="us-east-2", planner_model_id=None, mock_agents=False)
MOCK = Settings(aws_region="us-east-2", planner_model_id=None, mock_agents=True)


def test_mock_mode_needs_no_aws_setup(monkeypatch):
    monkeypatch.setenv("AWS_CA_BUNDLE", "/nope/missing.pem")

    assert startup_warnings(MOCK) == []


def test_warns_when_no_aws_credentials(monkeypatch, tmp_path):
    for name in ("AWS_PROFILE", "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_SESSION_TOKEN", "AWS_CA_BUNDLE"):
        monkeypatch.delenv(name, raising=False)
    monkeypatch.setenv("AWS_SHARED_CREDENTIALS_FILE", str(tmp_path / "none"))
    monkeypatch.setenv("AWS_CONFIG_FILE", str(tmp_path / "none"))
    monkeypatch.setenv("AWS_EC2_METADATA_DISABLED", "true")

    assert any("No AWS credentials" in w for w in startup_warnings(REAL))


def test_warns_when_ca_bundle_file_is_missing(monkeypatch):
    monkeypatch.setenv("AWS_ACCESS_KEY_ID", "AKIAEXAMPLE")
    monkeypatch.setenv("AWS_SECRET_ACCESS_KEY", "example")
    monkeypatch.setenv("AWS_CA_BUNDLE", "/nope/missing.pem")

    assert startup_warnings(REAL) == ["AWS_CA_BUNDLE points to a missing file: /nope/missing.pem"]
