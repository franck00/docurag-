# Journal

Suivi jour par jour du plan [DocuRAG-PLAN-14-JOURS.md](DocuRAG-PLAN-14-JOURS.md).

## J1 — Fondations — 2026-09-07

- Repo git initialisé, `.gitignore` dotnet + entrées Azure (`.azure/`, `local.settings.json`).
- Solution `DocuRag.slnx` avec 4 projets : `DocuRag.Core` (classlib), `DocuRag.Ingestion` (Azure Function isolated, net10.0), `DocuRag.Worker` (BackgroundService), `DocuRag.Api` (ASP.NET Core Web API). Références de projet vers `DocuRag.Core` posées pour les trois autres.
- `Directory.Build.props` : `net10.0`, nullable, `TreatWarningsAsErrors`.
- Vulnérabilité NU1903 sur `Microsoft.OpenApi` 2.0.0 (transitif via `Microsoft.AspNetCore.OpenApi` 10.0.9) : épinglé en direct sur `2.12.2` (dernière version 2.x, compatible avec les générateurs de source d'AspNetCore.OpenApi — la 3.x casse la compilation, `IOpenApiMediaType.Example` devient read-only).
- README initial (problème, archi visée, stack, structure).
- `dotnet build` passe, 0 warning, 0 erreur.

**Reste à faire pour clore J1** : rassembler les 10-15 PDF publics dans `corpus/`.

**Blocage rencontré** : `dotnet add reference` est resté bloqué indéfiniment (process suspendu, aucune sortie, CPU quasi nul) — contourné en éditant les `.csproj` à la main pour ajouter les `<ProjectReference>`.
