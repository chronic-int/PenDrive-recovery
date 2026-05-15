# Pendrive Rescue

Pendrive Rescue is a Windows desktop recovery assistant for USB flash drives. It is built with C#/.NET 8/WPF and focuses on recovering files safely before attempting any repair operation.

## Requirements

- Windows 10 or Windows 11
- .NET 8 SDK for development
- Administrator privileges for raw disk access, Deep Scan on physical devices, DiskPart repair, and CHKDSK repair

## Build

```powershell
dotnet restore PendriveRescue.sln
dotnet build PendriveRescue.sln
```

If this workspace reports a failed build with no compiler errors, run MSBuild with one worker. This avoids project-output file locks seen in some local/sandboxed environments:

```powershell
dotnet build PendriveRescue.sln --no-restore -maxcpucount:1
```

If restore fails because the user-level NuGet config cannot be read, check access to:

```text
%AppData%\NuGet\NuGet.Config
```

The project can often still compile with existing restored assets:

```powershell
dotnet build PendriveRescue.sln --no-restore
```

## Test

```powershell
dotnet test PendriveRescue.sln
```

For already-restored dependencies:

```powershell
dotnet test PendriveRescue.sln --no-restore
```

If the test project fails without diagnostics, use the same single-worker build setting:

```powershell
dotnet test PendriveRescue.sln --no-restore -maxcpucount:1
```

## Run

From Visual Studio, set `PendriveRescue.App` as the startup project.

From the command line:

```powershell
dotnet run --project src\PendriveRescue.App\PendriveRescue.App.csproj
```

Run as Administrator when using raw disk reads or repair actions. Normal Quick Scan of mounted drives may work without elevation.

## Safety Model

- Scan and recovery should never write to the source pendrive.
- Recovery blocks writing to the same source drive letter.
- Deep Scan reads physical devices in read-only mode.
- Safe Repair is non-destructive but CHKDSK can modify filesystem metadata.
- Erase and Repair is destructive and must remain strongly confirmed by the user.

## Recommended User Flow

1. Diagnose the USB drive.
2. Recover files to another disk.
3. Verify recovered files.
4. Try Safe Repair only after recovery if Windows still cannot mount the drive.
5. Use Erase and Repair only after recovery is no longer needed.

## Project Layout

- `src\PendriveRescue.App` - WPF UI, view models, dependency injection, startup logging.
- `src\PendriveRescue.Domain` - entities, enums, and service contracts.
- `src\PendriveRescue.Infrastructure` - Windows integration, scans, recovery, repair, reports.
- `src\PendriveRescue.Application` - orchestration layer for use cases.
- `tests\PendriveRescue.Tests` - xUnit tests.

## Publish

Do not publish during normal development unless you intentionally need a distributable build.

When needed, create a single self-contained Windows build:

```powershell
dotnet publish src\PendriveRescue.App\PendriveRescue.App.csproj -c Release -r win-x64 --self-contained true
```

Avoid creating multiple executable variants from ad-hoc local changes.
