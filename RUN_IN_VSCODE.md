# Running Vamoose in Visual Studio Code

Vamoose has three parts that run side by side:

| Part | Folder | URL |
| --- | --- | --- |
| Agent service (Python, AWS Strands Agents SDK, Claude on Amazon Bedrock) | `Vamoose.Agents` | http://localhost:8080 |
| API (.NET 9) | `Vamoose.Api` | http://localhost:5019 |
| Web app (Angular) | `Vamoose.Web` | http://localhost:4200 |

The agents are built with the **AWS Strands Agents SDK** and call **Claude on Amazon Bedrock**, with live web search. This guide runs the real agents. They need the AWS setup in step 5. A **mock mode** with canned answers and no AWS account is described at the end of step 7, for quick UI work or demos.

Commands are shown for **Windows (PowerShell, the VS Code default terminal)** and **Mac (zsh)**. Where only one block is shown, it works on both.

For deploying to AWS (EC2, AgentCore, RDS), see [DEPLOYMENT.md](DEPLOYMENT.md).

## 1. Install the tools (one time)

| Tool | Windows | Mac |
| --- | --- | --- |
| [VS Code](https://code.visualstudio.com/) | Installer from the website | Installer from the website |
| Git | `winget install Git.Git` | `brew install git` |
| .NET 9 SDK | `winget install Microsoft.DotNet.SDK.9` | `brew install --cask dotnet-sdk` |
| Node.js 20.19 or newer | `winget install OpenJS.NodeJS.LTS` | `brew install node` |
| Python 3.13 (not 3.14 or 3.15; some dependencies have no builds for them yet) | `py install 3.13` | `brew install python@3.13` |
| AWS CLI (only for real Claude) | `winget install Amazon.AWSCLI` | `brew install awscli` |
| SQL Server | SQL Server Express **LocalDB** (from the SQL Server Express installer, or with Visual Studio) | None locally; LocalDB doesn't run on macOS. Use the shared RDS database (step 6). |

Recommended VS Code extensions: **C# Dev Kit**, **Angular Language Service**, **Python**.

**Fully restart VS Code after installing** (close every window), so new terminals can find the tools.

## 2. Get the code

```bash
git clone https://github.com/adam035827/Vamoose.git
```

Open the `Vamoose` folder with **File > Open Folder...**, then open a terminal (`` Ctrl+` ``). All paths below are relative to this folder.

## 3. Set up the agent service (one time)

**Windows:**

```powershell
cd Vamoose.Agents
py -3.13 -m venv .venv
.venv\Scripts\python -m pip install -e ".[dev]"
```

**Mac:**

```bash
cd Vamoose.Agents
python3.13 -m venv .venv
.venv/bin/python -m pip install -e ".[dev]"
```

## 4. Set up the web app (one time)

```bash
cd Vamoose.Web
npm install
```

## 5. Connect to AWS (one time, required for the real agents)

The Strands agents call Claude on Amazon Bedrock in the team's AWS account (`us-east-2`). You need an access key from the AWS account owner.

**5a. Add your access key.** Enter the key and secret you were sent, region `us-east-2`, output `json`. The key is stored in `~/.aws/credentials`. Never put it in the repo or in a `.env` file.

```bash
aws configure --profile vamoose
```

**5b. Set the environment variables.** These are required. The Claude models are set in code, so don't set `VAMOOSE_PLANNER_MODEL_ID`; if you set it earlier, remove it, or you'll keep using the older model.

**Windows** (run once; applies to every terminal after you restart VS Code):

```powershell
[Environment]::SetEnvironmentVariable('AWS_PROFILE', 'vamoose', 'User')
[Environment]::SetEnvironmentVariable('AWS_REGION', 'us-east-2', 'User')
[Environment]::SetEnvironmentVariable('VAMOOSE_PLANNER_MODEL_ID', $null, 'User')   # removes an old override
```

**Mac** (add these lines to `~/.zshrc`, delete any `VAMOOSE_PLANNER_MODEL_ID` line there, then open a new terminal):

```bash
export AWS_PROFILE=vamoose
export AWS_REGION=us-east-2
```

Restart VS Code (Windows) or open a new terminal (Mac).

**5c. Only on a network with TLS inspection (for example Zscaler).** Skip this unless you hit `CERTIFICATE_VERIFY_FAILED` in 5d. It creates a certificate bundle that includes your company's root certificate, so the AWS SDK can get through the proxy.

**Windows** (from `Vamoose.Agents`):

```powershell
$c = Get-ChildItem Cert:\LocalMachine\Root, Cert:\CurrentUser\Root | Where-Object Subject -like '*CN=Zscaler Root CA*' | Select-Object -First 1
"-----BEGIN CERTIFICATE-----`n$([Convert]::ToBase64String($c.RawData,'InsertLineBreaks'))`n-----END CERTIFICATE-----" | Set-Content -Encoding ascii "$HOME\zscaler-root.crt"
Get-Content (& .venv\Scripts\python -m certifi), "$HOME\zscaler-root.crt" | Set-Content -Encoding ascii "$HOME\aws-ca-bundle.pem"
[Environment]::SetEnvironmentVariable('AWS_CA_BUNDLE', "$HOME\aws-ca-bundle.pem", 'User')
```

**Mac** (from `Vamoose.Agents`):

```bash
security find-certificate -a -c "Zscaler Root CA" -p > ~/zscaler-root.crt
cat "$(.venv/bin/python -m certifi)" ~/zscaler-root.crt > ~/aws-ca-bundle.pem
echo 'export AWS_CA_BUNDLE=~/aws-ca-bundle.pem' >> ~/.zshrc
```

Then restart VS Code (Windows) or open a new terminal (Mac).

**5d. Verify.** This should print `...:user/vamoose-<name>`:

```bash
aws sts get-caller-identity
```

Then run the live tests from `Vamoose.Agents`. They all pass, take 1–2 minutes and cost a few cents.

**Windows:**

```powershell
$env:VAMOOSE_LIVE_TESTS='1'; .venv\Scripts\python -m pytest -q -m live
```

**Mac:**

```bash
VAMOOSE_LIVE_TESTS=1 .venv/bin/python -m pytest -q -m live
```

## 6. Database (one time)

The API reads the connection string `ConnectionStrings:Vamoose` and creates or updates the database automatically when it starts.

- **Windows:** nothing to do. The default in `appsettings.Development.json` uses LocalDB (Windows on ARM is handled automatically).
- **Mac (or to use RDS from Windows):** set your own database on the shared RDS instance with user-secrets, so the password never goes into git:

```bash
cd Vamoose.Api
dotnet user-secrets set "ConnectionStrings:Vamoose" "Server=<host>,1433;Database=vamoose_<you>;User Id=<login>;Password=<password>;TrustServerCertificate=True"
```

## 7. Run the app (every time, three terminals)

Start them in this order. Use the **+** button in the terminal panel for each new terminal.

**Terminal 1: agent service with the real Strands agents** (http://localhost:8080). Needs step 5.

**Windows:**

```powershell
cd Vamoose.Agents
$env:VAMOOSE_MOCK_AGENTS='0'; .venv\Scripts\python -m vamoose_agents.app
```

**Mac:**

```bash
cd Vamoose.Agents
VAMOOSE_MOCK_AGENTS=0 .venv/bin/python -m vamoose_agents.app
```

**Check that it's using real Claude.** The first line must read:

```text
Vamoose agent service on http://127.0.0.1:8080 - Bedrock planner=global.anthropic.claude-sonnet-4-6 fast=global.anthropic.claude-haiku-4-5-20251001-v1:0 (us-east-2, profile=vamoose), search=...
```

- It must say `Bedrock`, the Sonnet 4.6 planner, and `profile=vamoose`, with no `WARNING` lines.
- If it says `mock`, mock mode is on: run the command above exactly as shown (with `VAMOOSE_MOCK_AGENTS` set to `0`), and delete the file `Vamoose.Agents\.mock` if it exists.
- If it says "Strands default" or `profile=default`, step 5b didn't take effect: restart VS Code or open a new terminal.
- Check the service at http://localhost:8080/ping, which returns `Healthy`.

Real agent replies take several seconds to a minute, because the agent searches the web and calls Claude. Each message costs a little in AWS usage.

**Terminal 2: API** (http://localhost:5019):

```bash
cd Vamoose.Api
dotnet run
```

To debug instead, open **Run and Debug** (`Ctrl+Shift+D`), choose **C#: Vamoose.Api**, and press `F5`.

**Terminal 3: web app** (http://localhost:4200):

```bash
cd Vamoose.Web
npx ng serve
```

The dev server forwards `/api` and `/hubs` (live notifications) to the API (see `Vamoose.Web/proxy.conf.json`).

### Optional: mock mode (no AWS)

Mock mode returns fixed, canned answers instead of calling Claude, so the whole app works without an AWS account or key. Use it for UI work, or as a fallback if Bedrock fails during a demo. Start terminal 1 with `1` instead of `0`:

**Windows:**

```powershell
cd Vamoose.Agents
$env:VAMOOSE_MOCK_AGENTS='1'; .venv\Scripts\python -m vamoose_agents.app
```

**Mac:**

```bash
cd Vamoose.Agents
VAMOOSE_MOCK_AGENTS=1 .venv/bin/python -m vamoose_agents.app
```

The first line then ends with `- mock`, and agent replies start with "(Mock)". To switch a running agent service to mock mode without restarting it, create an empty file named `.mock` in `Vamoose.Agents`; delete the file to switch back to real Claude.

## 8. Use the app

- Open http://localhost:4200. There is no login: type where you want to go, then answer "Who are you?" and "Who all is going?".
- Everyone gets into a trip with their own **magic link**. No email is actually sent in development: open http://localhost:4200/dev/outbox to see the emails the app would have sent.
- To try the group flow, **Finalize & send** your trip, then open a traveler's link from the outbox in a private window.
- If a reply starts with "(Mock)", the agent service is in mock mode. See step 7 to run the real agents.

## 9. Tests

| Tests | Windows | Mac |
| --- | --- | --- |
| Agent service (offline, no AWS) | `cd Vamoose.Agents` then `.venv\Scripts\python -m pytest` | `cd Vamoose.Agents` then `.venv/bin/python -m pytest` |
| Agent service (live Bedrock, a few cents) | `$env:VAMOOSE_LIVE_TESTS='1'; .venv\Scripts\python -m pytest -q -m live` | `VAMOOSE_LIVE_TESTS=1 .venv/bin/python -m pytest -q -m live` |
| API (in-memory database and fake agent, no SQL Server or AWS) | `dotnet test Vamoose.Api.Tests` | `dotnet test Vamoose.Api.Tests` |

## Troubleshooting

The real agent error is always in terminal 1. The API and web app only show a friendly message.

| You see | Fix |
| --- | --- |
| "The Vamoose agent is not reachable right now" | Terminal 1 isn't running. Start it and check http://localhost:8080/ping. |
| "The Vamoose agent hit a problem" | Look at terminal 1 and use the rows below. |
| `WARNING: No AWS credentials found` / `NoCredentialsError` | Steps 5a–5b, then restart VS Code. |
| `AccessDeniedException ... aws-marketplace` | `VAMOOSE_PLANNER_MODEL_ID` isn't set (step 5b). |
| `AccessDeniedException ... InvokeModel` | Ask the AWS account owner to add you to `vamoose-devs`. |
| `CERTIFICATE_VERIFY_FAILED` | Step 5c. |
| `InvalidClientTokenId` / `SignatureDoesNotMatch` | Key mistyped; rerun step 5a. |
| `ThrottlingException` | The account's per-minute limit; wait a minute. |
| No source links in plans | Web search is blocked on some corporate networks; try another network. |
| `aws`, `py` or `dotnet` not recognized | Restart VS Code after installing. |
| Agent fails with `[Errno 10048]` (port 8080 in use) | An old agent service is still running. Close its terminal or stop it with `Ctrl+C`. |
| API fails at startup with a SQL connection error | Windows: check LocalDB with `sqllocaldb info`. Mac: set the connection string (step 6). |
| `ng serve` fails with a Node version error | Update Node.js to 20.19 or newer. |
| Port 4200 or 5019 already in use | Stop the other process, or change the port (`ng serve --port 4300`; the API port is in `Vamoose.Api/Properties/launchSettings.json`, and update `proxy.conf.json` to match). |
