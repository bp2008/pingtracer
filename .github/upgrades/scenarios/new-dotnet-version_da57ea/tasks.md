# Upgrade Tasks: PingTracer .NET Framework 4.6.2 → .NET 10.0

## Progress Dashboard

| Metric | Status |
|---|---|
| Total Tasks | 6 |
| Completed | 6 |
| In Progress | 0 |
| Failed | 0 |
| Not Started | 0 |

---

## Tasks

### [✓] TASK-001: Validate Prerequisites *(Completed: 2026-04-12 13:28)*
**Scope**: Environment and SDK validation
**References**: Plan: §Migration Strategy — Phase 0

**Actions:**
- [✓] (1) Verify .NET 10.0 SDK is installed on the machine
- [✓] (2) Verify the solution file loads correctly
- [✓] (3) Create `upgrade-to-NET10` branch from `master` if not already on it

---

### [✓] TASK-002: Convert Project to SDK-Style and Update Target Framework *(Completed: 2026-04-12 13:32)*
**Scope**: PingTracer.csproj
**References**: Plan: §Project-by-Project Plans — Step 1, Step 2

**Actions:**
- [✓] (1) Use `upgrade_convert_project_to_sdk_style` tool to convert `PingTracer.csproj` from classic format to SDK-style targeting `net10.0-windows`
- [✓] (2) Verify the converted project file contains `<TargetFramework>net10.0-windows</TargetFramework>` and `<UseWindowsForms>true</UseWindowsForms>`
- [✓] (3) Add `System.Configuration.ConfigurationManager` NuGet package (latest version compatible with net10.0) to the project
- [✓] (4) Update or remove `App.config` — remove the `<startup><supportedRuntime>` section that targets .NET Framework 4.6.2
- [✓] (5) Verify `dotnet restore` succeeds with no package conflicts

---

### [✓] TASK-003: Replace Legacy WinForms Controls — MainForm.Designer.cs *(Completed: 2026-04-12 13:35)*
**Scope**: PingTest\MainForm.Designer.cs
**References**: Plan: §Breaking Changes Catalog — #1 Removed Legacy WinForms Controls

**Actions:**
- [✓] (1) Replace all `System.Windows.Forms.MainMenu` declarations and instantiations with `System.Windows.Forms.MenuStrip`
- [✓] (2) Replace all `System.Windows.Forms.MenuItem` declarations and instantiations with `System.Windows.Forms.ToolStripMenuItem`
- [✓] (3) Replace `mainMenu1.MenuItems.AddRange(...)` with `menuStrip1.Items.AddRange(...)` for top-level items
- [✓] (4) Replace `menuItem.MenuItems.AddRange(...)` with `toolStripMenuItem.DropDownItems.AddRange(...)` for sub-menu items
- [✓] (5) Replace `this.Menu = this.mainMenu1` with `this.MainMenuStrip = this.menuStrip1` and add `this.Controls.Add(this.menuStrip1)`
- [✓] (6) Remove all `menuItem.Index = N` property assignments (not applicable to ToolStripMenuItem)
- [✓] (7) Update field declarations section: change all `MainMenu`/`MenuItem` field types to `MenuStrip`/`ToolStripMenuItem`

---

### [✓] TASK-004: Replace Legacy WinForms Controls — MainForm.cs *(Completed: 2026-04-12 13:37)*
**Scope**: PingTest\MainForm.cs
**References**: Plan: §Breaking Changes Catalog — #1 Removed Legacy WinForms Controls

**Actions:**
- [✓] (1) Read `MainForm.cs` and identify all references to `MainMenu`, `MenuItem`, or legacy menu APIs
- [✓] (2) Update any code-behind references to use `MenuStrip`/`ToolStripMenuItem` types
- [✓] (3) Verify event handler signatures remain unchanged (Click handlers should work as-is)

---

### [✓] TASK-005: Build Solution and Fix Compilation Errors *(Completed: 2026-04-12 13:41)*
**Scope**: Entire solution
**References**: Plan: §Project-by-Project Plans — Step 5

**Actions:**
- [✓] (1) Build the solution and capture all compilation errors
- [✓] (2) Fix all compilation errors related to the upgrade (residual legacy control references, missing usings, property differences)
- [✓] (3) Rebuild and verify: **Expected result: 0 errors**

---

### [✓] TASK-006: Final Validation and Commit *(Completed: 2026-04-12 13:44)*
**Scope**: Entire solution
**References**: Plan: §Success Criteria, §Source Control Strategy

**Actions:**
- [✓] (1) Verify project file is SDK-style with `net10.0-windows` target framework
- [✓] (2) Verify `System.Configuration.ConfigurationManager` package is referenced
- [✓] (3) Verify all `MainMenu`/`MenuItem` references are replaced
- [✓] (4) Verify solution builds with 0 errors
- [✓] (5) Commit all changes with message: `Upgrade PingTracer from .NET Framework 4.6.2 to .NET 10.0`

---

## Execution Log

*Execution log entries will be appended below as tasks are completed.*
