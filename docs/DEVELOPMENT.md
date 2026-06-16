# Development guide

## Prerequisites

- Windows 11 for the desktop shell.
- .NET 8 SDK or newer.
- Visual Studio 2022 with WinUI / Windows App SDK workload for `Threadline.Windows`.
- Node.js 20+ for the browser extension.
- PowerShell 7+.

## Build core/service projects

```powershell
./eng/build.ps1
```

## Run tests

```powershell
./eng/test.ps1
```

## Run the local service

```powershell
dotnet run --project src/Threadline.Service/Threadline.Service.csproj
```

Then verify:

```powershell
Invoke-RestMethod http://localhost:5000/health
```

The actual port may vary based on ASP.NET Core launch settings until explicit local binding is added in Phase 2.

## Build browser extension

```powershell
cd adapters/browser-extension
npm install
npm run build
```

## Engineering rule

ThreadlineAI is security-sensitive. Do not add capture behavior without a matching privacy/control story: visibility, pause, preview, rule evaluation, and deletion.
