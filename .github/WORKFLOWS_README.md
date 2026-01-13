# GitHub Actions: NuGet publishing

This repo includes a workflow at `.github/workflows/publish-nuget.yml`.

## How it works

- Triggers:
  - Manual: `workflow_dispatch`
  - Tag push: tags matching `v*` (example: `v0.5.0`)

- Steps:
  - Restore/build
  - `dotnet pack` the `BugViewer/BugViewer.csproj` project into `./artifacts`
  - Push `.nupkg` and `.snupkg` to NuGet.org

## Setup

1. Create a NuGet API key on nuget.org
2. Add a GitHub Actions secret named `NUGET_API_KEY` in your repository settings

## Release by tag

Example:

- `git tag v0.5.0`
- `git push origin v0.5.0`

## Local scripts

- `scripts/pack.ps1` -> builds + packs into `./artifacts`
- `scripts/push-nuget.ps1 -ApiKey <key>` -> pushes packages from `./artifacts`
