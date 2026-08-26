# VaderConsulting.Orbus

C# .NET 4.0 class library that loads Orbus iServer SQL drawings, services, servers, and HA into a Dependency collection. `iServer` implements `VaderConsulting.DataLayer.IDataLayer` / `DataLayer`: `Initialise` opens a caller-supplied SQL connection via `VaderConsulting.Database.SQLServer`, reads the iServer database version (233.07 or 334.00; other versions prompt with a WinForms warning), and `GetBusinessApplications` runs version-specific queries against `vwObject`/`Object`/`AttributeValue*` to attach Visio page previews, focused business applications (tier, runbook order, catalogue flags, comments), component relationships, HA groups, physical sites (PDC/SDC/QBE/External), and recovery streams A–E. Empty `SaveDrawing`, `SaveBusinessApplication`, and `SaveServer` stubs are present; `Attribute.cs` is on disk but not in the `.csproj` Compile list. Debug traces and `Announcement` events report drawing/runbook issues such as self-dependencies, out-of-scope mandatory components, and invalid stream/site combinations.

**Source last updated:** 2015-09-29 · **Language:** C# · **Target:** .NET Framework 4.0 · **Output:** class library (`Library`)

## Solution structure

| Project | Language | Type | Purpose |
|---------|----------|------|---------|
| `VaderConsulting.Orbus` (`VaderConsulting.Orbus.csproj`) | C# | class library (`net40`, WinForms) | Orbus iServer SQL data layer: drawings, business applications, servers, HA, recovery streams. |

## How to open

Open `VaderConsulting.Orbus.csproj` in Visual Studio 2013 or later (ToolsVersion 12.0). There is no `.sln` in this folder. The project references sibling folders `..\DataLayer\VaderConsulting.DataLayer.csproj`, `..\DependencyCollection\VaderConsulting.Dependency.csproj`, `..\VaderConsulting.CommandLine\VaderConsulting.CommandLine.csproj`, `..\VaderConsulting.Database\VaderConsulting.Database.csproj`, `..\VaderConsulting.Helper\VaderConsulting.Helper.csproj`, and `..\VaderConsulting.SystemCenter\VaderConsulting.SystemCenter.csproj`. `Attribute.cs` is present but not listed in the `.csproj` Compile items.

## Attribution and provenance

Working copy from Dave Robinson's OneDrive Historical Dev folder `VaderConsulting.Orbus`. Assembly title/product `VaderConsulting.Orbus`; copyright `Copyright ©  2015`; company empty. Namespace `VaderConsulting.Orbus`. `packages.config` lists AsyncBridge 0.1.1 (referenced by the `.csproj`). `App.config` has leftover Entity Framework 6 LocalDB section.

## License

MIT © 2026 VaderConsulting. See `LICENSE`.
