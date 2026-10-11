# Vamoose deployment setup

Record of how the AWS environment was set up. Commands are copy-pasteable. Secrets and keys are never stored here.

## Status

| Piece | Where | State |
| --- | --- | --- |
| Angular frontend | EC2 + Nginx | Done, serving over HTTP |
| .NET 9 API | EC2, systemd service on `127.0.0.1:5000` | Done (hello endpoint only) |
| Agent service (`Vamoose.Agents`) | Bedrock AgentCore Runtime | Deployed in mock mode with the merged `main` code (`VamooseAgents`, stack `AgentCore-VamooseAgents-default`) |
| API agent client | `Vamoose.Api` (`AgentCoreAgentClient`) | Coded on `server-setup`, not deployed to the server yet |
| Database | Amazon RDS for SQL Server Express (`vamoosedb`) | Creating; security group rules, migrations and connection string pending |
| EC2 IAM role to call AgentCore | AWS IAM | Done (`VamooseApiEc2Role`, policy `VamooseInvokeAgentRuntime`, attached to the instance) |
| HTTPS / domain / Elastic IP | EC2 | Not started |

---

## 1. Web server: EC2 + Nginx + .NET API

### 1.1 Instance

- Region `us-east-2`, Ubuntu Server 24.04 LTS (x86-64), `t3.small`, 8 GiB gp3 root volume.
- Security group inbound: 22 (SSH), 80 (HTTP), 443 (HTTPS). SSH is currently open to `0.0.0.0/0`; restrict it to your own IP later.
- The public IP is auto-assigned, so it **changes if the instance is stopped and started**. Allocate an Elastic IP if a stable address is needed.

### 1.2 Connecting

- Local SSH from the work laptop failed with `Connection reset` right after key exchange (the network inspects traffic).
- Use **EC2 Instance Connect** instead: EC2 console, select the instance, Connect, EC2 Instance Connect, Connect (a browser terminal, user `ubuntu`).
- Paste one command block at a time. Pasting the same block twice runs it twice and concatenates lines.

### 1.3 Updates, disk, swap

```bash
sudo apt update && sudo apt upgrade -y
sudo reboot            # when "System restart required" is shown; the public IP is kept
```

Grow the root volume to 20 GiB: EC2 console, Volumes, select the volume, **Modify** (button at the top right), Size 20. Then on the server:

```bash
lsblk                                   # nvme0n1 should show 20G
sudo growpart /dev/nvme0n1 1
sudo resize2fs /dev/nvme0n1p1
df -h /                                 # about 19G
```

Add 2 GiB of swap so `ng build` fits in 2 GiB of RAM:

```bash
sudo fallocate -l 2G /swapfile
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

### 1.4 Install tooling

.NET 9 SDK (not in Ubuntu 24.04's archive, so use Microsoft's script; the SDK includes the ASP.NET Core runtime):

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
sudo ./dotnet-install.sh --channel 9.0 --install-dir /usr/share/dotnet
sudo ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
dotnet --list-sdks && dotnet --list-runtimes
```

Node 22 (Angular 20 needs Node 20.19+ or 22.12+; Ubuntu's own package is too old), git and Nginx:

```bash
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash -
sudo apt install -y nodejs git nginx
node --version && npm --version && git --version
```

### 1.5 Source and first deploy

Layout on the server:

- `/home/ubuntu/Vamoose` holds the cloned source.
- `/var/www/vamoose/frontend` holds the Angular build, which Nginx serves.
- `/var/www/vamoose/api` holds the published .NET output.

```bash
git clone https://github.com/adam035827/Vamoose.git ~/Vamoose

cd ~/Vamoose/Vamoose.Web
npm ci
npm run build
sudo mkdir -p /var/www/vamoose/frontend /var/www/vamoose/api
sudo cp -r dist/Vamoose.Web/browser/. /var/www/vamoose/frontend/

cd ~/Vamoose/Vamoose.Api
dotnet publish -c Release -o ~/api-publish
sudo cp -r ~/api-publish/. /var/www/vamoose/api/
```

### 1.6 API as a systemd service

```bash
sudo tee /etc/systemd/system/vamoose-api.service > /dev/null <<'EOF'
[Unit]
Description=Vamoose API
After=network.target

[Service]
WorkingDirectory=/var/www/vamoose/api
ExecStart=/usr/bin/dotnet /var/www/vamoose/api/Vamoose.Api.dll
Restart=always
RestartSec=5
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:5000
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable --now vamoose-api
systemctl is-active vamoose-api
curl -s http://127.0.0.1:5000/api/hello      # {"message":"Hello, World from Vamoose API!"}
```

The API listens on localhost only. Port 5000 is not opened in the security group.

### 1.7 Nginx: serve Angular and proxy `/api`

```bash
sudo tee /etc/nginx/sites-available/vamoose > /dev/null <<'EOF'
server {
    listen 80 default_server;
    listen [::]:80 default_server;
    server_name _;

    root /var/www/vamoose/frontend;
    index index.html;

    location /api/ {
        proxy_pass http://127.0.0.1:5000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # SignalR notifications (WebSocket upgrade)
    location /hubs/ {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 1h;
    }

    location / {
        try_files $uri $uri/ /index.html;
    }
}
EOF
sudo ln -sf /etc/nginx/sites-available/vamoose /etc/nginx/sites-enabled/vamoose
sudo rm -f /etc/nginx/sites-enabled/default
sudo nginx -t && sudo systemctl reload nginx
```

Verify on the server and from a browser (use `http://`, not `https://`):

```bash
curl -s http://localhost/api/hello; echo
curl -s -o /dev/null -w "%{http_code}\n" http://localhost/some-angular-route    # 200 (SPA fallback)
```

### 1.8 Redeploy after pushing to `main`

```bash
cd ~/Vamoose && git pull
cd Vamoose.Web && npm ci && npm run build && sudo cp -r dist/Vamoose.Web/browser/. /var/www/vamoose/frontend/
cd ../Vamoose.Api && dotnet publish -c Release -o ~/api-publish && sudo cp -r ~/api-publish/. /var/www/vamoose/api/ && sudo systemctl restart vamoose-api
```

---

## 2. Agent service on Bedrock AgentCore Runtime

Run from the Windows dev machine, not the server. The EC2 server is only the caller (see section 3).

### 2.1 AWS CLI and sign-in

```powershell
winget install -e --id Amazon.AWSCLI
# VS Code terminals do not see new PATH entries until VS Code is restarted. Refresh in the current terminal:
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
aws login                       # opens the browser; region us-east-2
aws sts get-caller-identity
```

`aws login` stored temporary credentials for the account's **root user**. That works for now, but create and use an IAM admin user later. Never copy these credentials to the server.

### 2.2 Corporate TLS inspection (Zscaler)

The work network re-signs HTTPS traffic, so AWS, Node and `uv` tools fail with `CERTIFICATE_VERIFY_FAILED` or `UnknownIssuer`. Export the Zscaler root certificates from the Windows store and point each tool at them:

```powershell
New-Item -ItemType Directory -Force C:\certs | Out-Null
$certs = Get-ChildItem Cert:\LocalMachine\Root, Cert:\CurrentUser\Root, Cert:\LocalMachine\CA, Cert:\CurrentUser\CA | Where-Object { $_.Subject -like "*Zscaler*" } | Sort-Object Thumbprint -Unique
$pem = foreach ($c in $certs) { "-----BEGIN CERTIFICATE-----"; [Convert]::ToBase64String($c.RawData, 'InsertLineBreaks'); "-----END CERTIFICATE-----" }
$pem | Set-Content C:\certs\zscaler.pem -Encoding ascii

setx AWS_CA_BUNDLE "C:\certs\zscaler.pem"
setx NODE_EXTRA_CA_CERTS "C:\certs\zscaler.pem"
setx UV_SYSTEM_CERTS "1"
```

`setx` only affects **new** terminals; restart VS Code after setting.

### 2.3 AgentCore CLI, `uv` and Python 3.13

```powershell
npm install -g @aws/agentcore
agentcore --version
winget install -e --id astral-sh.uv        # the CLI uses uv to build the Python zip
```

Python 3.13 could not be downloaded the normal ways (`winget` and python.org were blocked, and `uv`'s own download dropped mid-transfer). NuGet's full CPython package worked:

```powershell
$v = (Invoke-RestMethod https://api.nuget.org/v3-flatcontainer/python/index.json).versions | Where-Object { $_ -like "3.13.*" -and $_ -notlike "*-*" } | Select-Object -Last 1
Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/python/$v/python.$v.nupkg" -OutFile "$env:TEMP\python313.zip"
Expand-Archive "$env:TEMP\python313.zip" C:\tools\python313 -Force
C:\tools\python313\tools\python.exe --version

setx UV_PYTHON "C:\tools\python313\tools\python.exe"
setx UV_PYTHON_DOWNLOADS "never"
```

The embeddable zip is not suitable (no `venv`, and its `._pth` file ignores virtual environments).

### 2.4 Project config (already in the repo)

- `agentcore/` is the CLI's config and CDK project, generated with `agentcore create --no-agent` and copied to the repo root.
- `agentcore/agentcore.json` has one runtime, `VamooseAgents`: `CodeZip`, entrypoint `main.py`, `codeLocation` `Vamoose.Agents/`, `PYTHON_3_13`, with env vars `AWS_REGION=us-east-2`, `VAMOOSE_MOCK_AGENTS=0` (real Claude) and `VAMOOSE_TAVILY_PARAMETER=/vamoose/tavily-api-key` (Tavily web search key, a SecureString in Parameter Store). The runtime role has the inline policy `VamooseReadParameters` (`ssm:GetParameter` on `parameter/vamoose/*`), added by hand in IAM.
- `agentcore/aws-targets.json` targets account `965472235570`, region `us-east-2`.
- `Vamoose.Agents/main.py` is a small launcher. `app.py` uses relative imports, so it cannot run as a plain script.
- Run `npm install` once in `agentcore/cdk` (the scaffold skipped it).

### 2.5 Test locally (mock mode)

```powershell
$env:VAMOOSE_MOCK_AGENTS = "1"
agentcore dev --logs
```

In a second terminal:

```powershell
curl.exe -s http://127.0.0.1:8080/ping
'{"requestId":"test-001","action":"Draft","message":"A long weekend in Lisbon for 4 friends"}' | Set-Content $env:TEMP\req.json -Encoding ascii
curl.exe -s -N -X POST http://127.0.0.1:8080/invocations -H "Content-Type: application/json" --data-binary "@$env:TEMP\req.json"
```

Expected: `{"status":"Healthy",...}`, then a `Progress` event and a `Result` event with the canned Lisbon draft.

### 2.6 Deploy

```powershell
agentcore deploy --dry-run       # stops at "AWS environment needs bootstrapping" the first time
agentcore deploy --yes           # bootstraps CDK, packages the zip, creates the runtime and its role
agentcore status                 # shows the runtime and its ARN
```

Deployed runtime ARN: `arn:aws:bedrock-agentcore:us-east-2:965472235570:runtime/VamooseAgents_VamooseAgents-M7CWbmE3bi`

Redeploy after changing `Vamoose.Agents` code with the same `agentcore deploy --yes`. Invoke the deployed runtime directly:

```powershell
aws bedrock-agentcore invoke-agent-runtime --agent-runtime-arn <arn> --runtime-session-id "vamoose-test-session-00000000000000001" --payload fileb://$env:TEMP\req.json $env:TEMP\out.txt
Get-Content $env:TEMP\out.txt
```

---

## 3. Remaining work

1. **EC2 IAM role:** done. `VamooseApiEc2Role` allows `bedrock-agentcore:InvokeAgentRuntime` on the runtime and its endpoints, and is attached to the instance. The agent's own Bedrock model access comes from its execution role, which the deploy created. No keys are on the server.
2. **`Vamoose.Api` agent client:** done in code. `AgentCoreAgentClient` calls the runtime with the AWS SDK when `Agents:RuntimeArn` is set; otherwise `HttpAgentClient` uses `Agents:BaseUrl` (local dev).
3. **Database:** RDS SQL Server Express `vamoosedb` is being created. Then: allow port 1433 from the EC2 security group and your own IP only (never `0.0.0.0/0`), set the connection string (user-secrets locally, systemd environment on the server, never in git), and apply the EF migrations once. Production does not auto-migrate; Development does (and seeds the demo trip).
4. **Server settings** (add to `vamoose-api.service`, then `daemon-reload` and restart):

   ```ini
   Environment=ConnectionStrings__Vamoose=Server=<rds-endpoint>,1433;Database=Vamoose;User Id=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=True
   Environment=App__BaseUrl=http://<public-address>
   Environment=Agents__RuntimeArn=arn:aws:bedrock-agentcore:us-east-2:965472235570:runtime/VamooseAgents_VamooseAgents-M7CWbmE3bi
   Environment=Agents__Region=us-east-2
   ```

   Also add the `/hubs/` block from section 1.7 to Nginx, then redeploy (section 1.8) and check `journalctl -u vamoose-api -n 50`.
5. **Mock mode off:** done in `agentcore.json`; takes effect on the next `agentcore deploy --yes`. Check the runtime logs for the startup line `Vamoose agent service on AgentCore - Bedrock planner=... search=Tavily MCP (Parameter Store ...)` with no `WARNING` after it.
6. **HTTPS and a stable address:** Elastic IP, hostname, Certbot.
7. **Security cleanup:** use an IAM user instead of root, restrict SSH to your own IP.

## 4. Troubleshooting notes

- **A Windows command is "not recognized" after installing it:** VS Code terminals keep the `PATH` from when VS Code started. Refresh `PATH` as shown in section 2.1 or restart VS Code.
- **`agentcore deploy` says "Subprocess exited with error 1":** run `node dist/bin/cdk.js` inside `agentcore\cdk` to see the real message. In our case it was `uv is required... on your PATH`.
- **`CERTIFICATE_VERIFY_FAILED` or `UnknownIssuer`:** the Zscaler setup in section 2.2 is missing in that terminal. For `uv` (`invalid peer certificate: UnknownIssuer` on pypi.org) set `UV_SYSTEM_CERTS=1`, `UV_PYTHON` and `UV_PYTHON_DOWNLOADS=never` in the current session; `setx` values do not always reach a terminal.
- **Pasting several commands at once:** commands can get joined on one line and produce confusing errors. Paste one block at a time.
