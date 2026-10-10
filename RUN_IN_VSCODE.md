# Running Vamoose in Visual Studio Code

## Prerequisites

- [Visual Studio Code](https://code.visualstudio.com/)
- [.NET 9 SDK](https://dotnet.microsoft.com/download) (`dotnet --version`)
- [Node.js](https://nodejs.org/) 20.19 or newer (`node --version`)

Recommended VS Code extensions:

- **C# Dev Kit** (`ms-dotnettools.csdevkit`)
- **Angular Language Service** (`Angular.ng-template`)

## 1. Open the project

Open the repo root (the folder containing `Vamoose.Api` and `Vamoose.Web`) with **File > Open Folder...**.

## 2. Install Angular dependencies (first time only)

Open a terminal with **Terminal > New Terminal** (`` Ctrl+` ``) and run:

```bash
cd Vamoose.Web
npm install
```

## 3. Start the API

In the same terminal, run:

```bash
cd Vamoose.Api
dotnet run
```

The API listens on http://localhost:5019. Check it in a browser at http://localhost:5019/api/hello. You should see:

```json
{ "message": "Hello, World from Vamoose API!" }
```

Leave this terminal running.

**To debug instead:** open the **Run and Debug** view (`Ctrl+Shift+D`), choose **C#: Vamoose.Api** (or accept the prompt to generate a launch configuration), and press `F5`.

## 4. Start the Angular app

Open a second terminal with the **+** button in the terminal panel, and run:

```bash
cd Vamoose.Web
npx ng serve
```

Open http://localhost:4200. The page should show "Vamoose" and "Hello, World from Vamoose API!".

The dev server forwards requests for `/api` to http://localhost:5019 (see `Vamoose.Web/proxy.conf.json`), so the API must be running first.

## Troubleshooting

| Problem | Fix |
| --- | --- |
| Page shows "Could not reach the API" | Make sure the API is running on port 5019. |
| `ng serve` fails with a Node version error | Update Node.js to 20.19 or newer. |
| Port 4200 or 5019 already in use | Stop the other process, or change the port (`ng serve --port 4300`; API port is in `Vamoose.Api/Properties/launchSettings.json`, and update `proxy.conf.json` to match). |
| `'ng' is not recognized` | Use `npx ng serve`, or run `npm install` in `Vamoose.Web` first. |
