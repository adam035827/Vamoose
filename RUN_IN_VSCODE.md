# Running Vamoose in Visual Studio Code

Vamoose has three parts that run side by side:

| Part | Folder | URL |
| --- | --- | --- |
| Agent service (Python, Strands) | `Vamoose.Agents` | http://localhost:8080 |
| API (.NET 9) | `Vamoose.Api` | http://localhost:5019 |
| Web app (Angular) | `Vamoose.Web` | http://localhost:4200 |

## Prerequisites

- [Visual Studio Code](https://code.visualstudio.com/)
- [.NET 9 SDK](https://dotnet.microsoft.com/download) (`dotnet --version`)
- [Node.js](https://nodejs.org/) 20.19 or newer (`node --version`)
- [Python](https://www.python.org/downloads/) 3.13 (`python --version`)
- A SQL Server database:
  - **Windows:** SQL Server Express LocalDB (installed with Visual Studio, or from the SQL Server Express installer). The default connection string uses it, so nothing to configure.
  - **Mac:** LocalDB doesn't run on macOS. Point the API at the shared RDS instance (see "Database" below).

Recommended VS Code extensions: **C# Dev Kit**, **Angular Language Service**, **Python**.

## 1. One-time setup

Open the repo root with **File > Open Folder...**, then open a terminal (`` Ctrl+` ``).

Agent service:

```bash
cd Vamoose.Agents
python -m venv .venv
.venv/Scripts/python -m pip install -e ".[dev]"
```

On Mac, use `.venv/bin/python` instead of `.venv/Scripts/python`.

Web app:

```bash
cd Vamoose.Web
npm install
```

## 2. Start the agent service (terminal 1)

Mock mode returns canned answers, so the whole app works without AWS:

```bash
cd Vamoose.Agents
VAMOOSE_MOCK_AGENTS=1 .venv/Scripts/python -m vamoose_agents.app
```

In PowerShell: `$env:VAMOOSE_MOCK_AGENTS='1'; .venv\Scripts\python -m vamoose_agents.app`

To use real Claude on Amazon Bedrock with live web search, set up AWS credentials with Bedrock access (for example `AWS_PROFILE`), and start it with `VAMOOSE_MOCK_AGENTS=0`. See `Vamoose.Agents/.env.example`.

## 3. Start the API (terminal 2)

```bash
cd Vamoose.Api
dotnet run
```

On first start in Development, the API creates or updates the database automatically. **To debug instead:** open **Run and Debug** (`Ctrl+Shift+D`), choose **C#: Vamoose.Api**, and press `F5`.

## 4. Start the web app (terminal 3)

```bash
cd Vamoose.Web
npx ng serve
```

Open http://localhost:4200 and log in with any email address. The dev server forwards `/api` and `/hubs` (live notifications) to the API (see `Vamoose.Web/proxy.conf.json`).

To try the group flow, use a second browser profile or a private window and log in with another email. Add that email to your trip, then **Finalize & send**.

## Database

The API reads `ConnectionStrings:Vamoose`. The default in `appsettings.Development.json` is LocalDB. To use RDS or another SQL Server without committing a password, use user-secrets:

```bash
cd Vamoose.Api
dotnet user-secrets set "ConnectionStrings:Vamoose" "Server=<host>,1433;Database=vamoose_<you>;User Id=<login>;Password=<password>;TrustServerCertificate=True"
```

## Tests

```bash
cd Vamoose.Agents
.venv/Scripts/python -m pytest
```

## Troubleshooting

| Problem | Fix |
| --- | --- |
| "The Vamoose agent is not reachable right now" | Start the agent service on port 8080. |
| API fails at startup with a SQL connection error | Check that LocalDB is installed (`sqllocaldb info`), or set a connection string as above. Windows on ARM is handled automatically. |
| `ng serve` fails with a Node version error | Update Node.js to 20.19 or newer. |
| Port already in use | Stop the other process, or change the port (`ng serve --port 4300`; API port is in `Vamoose.Api/Properties/launchSettings.json`, and update `proxy.conf.json` to match). |
