from vamoose_agents.app import app, log_startup

if __name__ == "__main__":
    # AgentCore entrypoint: settings come from agentcore.json envVars, never a bundled .env file.
    log_startup("on AgentCore")
    app.run()
