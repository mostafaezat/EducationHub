# tools/claude

Helper scripts that let Claude (working from a cloud workspace without nuget.org or SQL Server)
build and verify EducationHub using this machine. They never modify source code.

| Script | What it does | Output |
|---|---|---|
| `prepare-offline.ps1` | Packs the solution's NuGet packages + `dotnet-ef` | `offline-parts\offline.zip.NNN` |
| `run-checks.ps1` | build, EF pending-model check, unit, integration, Angular build/tests, API smoke start | `logs\` (`SUMMARY.txt`, `*.log`, `*.trx`) |

`offline\`, `offline-parts\` and `logs\` are generated; do not commit them.
