# Usage Guide

This tool exports a TFS version control repository — including all branches and full history —
into the `git fast-import` stream format, which Git can then load into a local repository.

## Prerequisites

Install on the Windows 11 machine before starting:

- [Visual Studio 2022](https://visualstudio.microsoft.com/) (includes the .NET SDK and .NET Framework 4.7.2 build tools)
  — or — [.NET SDK](https://dotnet.microsoft.com/download) + [.NET Framework 4.7.2 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework/net472) if building without Visual Studio
- [Git for Windows](https://git-scm.com/download/win)
- A TFS account with **read access** to the target project (domain account or local TFS account)

The tool targets **.NET Framework 4.7.2**, which ships built-in with Windows 10 and 11 — no
runtime installation is needed on the machine that runs the export.

---

## Step 1 — Publish the tool

Open a Developer Command Prompt (or any terminal with the .NET SDK on the PATH) in the
repository root and run:

```cmd
dotnet publish tfs-fast-export\tfs-fast-export.csproj -c Release
```

This collects the executable and all required DLLs into a single output folder:

```
tfs-fast-export\bin\Release\net472\publish\
```

Copy that entire folder to wherever you want to run the export from.
The folder is self-contained — no installation step is needed on the target machine beyond
.NET Framework 4.7.2 (already present on Windows 10/11).

> **Note:** .NET Framework does not support single-file executables. The publish folder will
> contain `tfs-fast-export.exe` alongside ~50 TFS client DLLs — keep them together.

---

## Step 2 — Identify the TFS arguments

The tool accepts two required arguments and two optional credential arguments:

| Argument | Required | Meaning | Example |
|----------|----------|---------|---------|
| `<collection-url>` | Yes | URL of the TFS **collection** (everything before the project name) | `http://tfsserver:8080/tfs/Collection` |
| `<tfs-root-path>` | Yes | TFS server path to the project root | `$/MyProject` |
| `DOMAIN\username` | No | Account to authenticate with | `DOMAIN\username` |
| `password` | No | Password — if omitted, prompted securely | |

**How to find your collection URL:**

Given a TFS web UI URL such as `http://tfsserver:8080/tfs/Collection/MyProject/_versionControl`:

- Collection URL → **`http://tfsserver:8080/tfs/Collection`**
  (strip everything from the project name onward — the web UI appends those)
- Root path → **`$/MyProject`**
  (the `$/` prefix is how TFS addresses all server-side paths)

---

## Step 3 — Create an empty Git repository

The export must be imported into a **bare, empty** repository. Choose a folder with enough
free disk space to hold the entire repository history.

```cmd
mkdir C:\git-migration\MyProject
cd C:\git-migration\MyProject
git init
```

---

## Step 4 — Run the export

Pipe the tool's output directly into `git fast-import`. Run this from inside the git repository
folder created in Step 3.

Supply your credentials as the third argument (`DOMAIN\username`). The password is prompted
securely on screen so it is never stored in your command history:

```cmd
C:\path\to\publish\tfs-fast-export.exe ^
    http://tfsserver:8080/tfs/Collection ^
    $/MyProject ^
    DOMAIN\username ^
    | git fast-import
```

You will see `Password for DOMAIN\username:` on screen. Type your password and press Enter —
characters are not echoed. The export then begins immediately.

If you prefer to supply the password non-interactively (e.g. in a script), pass it as the
fourth argument:

```cmd
tfs-fast-export.exe ^
    http://tfsserver:8080/tfs/Collection ^
    $/MyProject ^
    DOMAIN\username ^
    MyPassword ^
    | git fast-import
```

> **Note:** Passing a password on the command line leaves it visible in process listings and
> shell history. Use the interactive prompt when working on a shared machine.

If the machine is already joined to the domain and you are logged in as the TFS account,
the credentials arguments can be omitted entirely:

```cmd
tfs-fast-export.exe http://tfsserver:8080/tfs/Collection $/MyProject | git fast-import
```

Progress is printed as `progress N/Total` and shown by `git fast-import` as it runs.
A large repository with thousands of changesets may take several hours because every version
of every file is downloaded from the TFS server.

**Tip:** For very long exports, save to a file first so you can re-import without re-downloading:

```cmd
tfs-fast-export.exe ^
    http://tfsserver:8080/tfs/Collection ^
    $/MyProject ^
    DOMAIN\username ^
    MyPassword ^
    > export.bundle
git fast-import < export.bundle
```

> **Note:** Do not open `export.bundle` in a text editor — it contains raw binary file
> content and will appear corrupt if opened as text.

---

## Step 5 — Check out the branches

After `git fast-import` completes, the commits exist in the repository but the working tree is
empty (the export uses `refs/heads/<branchname>` directly). Create local tracking branches:

```cmd
git checkout -b main refs/heads/MyProject
```

To see all imported branches:

```cmd
git branch -a
```

Check out each branch you need:

```cmd
git checkout -b <local-name> refs/heads/<tfs-branch-name>
```

---

## Customisation (edit before building)

Three places in `TfsChangeSet.cs` and `Program.cs` let you adjust the export for quirks in
your specific TFS history. Rebuild after any change.

### Skip specific changesets — `Program.cs`

Add changeset IDs to `_SkipCommits` to omit them from the export entirely. Useful for
build-template or TFS administration checkins that have no meaningful code:

```csharp
private static HashSet<int> _SkipCommits = new()
{
    1042,   // TFS workspace policy checkin, not code
    8801,   // accidental duplicate of 8800
};
```

### Stop at a changeset for debugging — `Program.cs`

Add a changeset ID to `_BreakCommits` to attach a debugger at that point and inspect
internal state (`_Branches`, `_Commits`, etc.):

```csharp
private static HashSet<int> _BreakCommits = new()
{
    4500,
};
```

### Handle branch renames and other anomalies — `TfsChangeSet.cs`

Add entries to `_SpecialCommands` to run arbitrary code at a specific changeset before it is
processed. The most common use is renaming a branch in the internal tracking dictionary when
TFS records the branch folder itself being renamed:

```csharp
private static Dictionary<int, Func<CommitCommand?>> _SpecialCommands = new()
{
    // Changeset 9301 renamed $/MyProject/Release to $/MyProject/Release-2023.
    // Update the internal branch map so subsequent changesets resolve correctly.
    { 9301, () =>
        {
            _Branches["$/MyProject/Release-2023/"] = _Branches["$/MyProject/Release/"];
            _Branches.Remove("$/MyProject/Release/");
            return null;   // return null to emit no git commit for this changeset
        } },
};
```

Return `null` to skip emitting a commit for that changeset (branch rename only), or return a
`CommitCommand` to emit a custom commit.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| `The type or namespace 'Microsoft.TeamFoundation' could not be found` | Built on Linux or without NuGet restore | Run on Windows; run `dotnet restore` first |
| `TF30063: You are not authorized to access` or 401 error | Wrong credentials or account lacks read permission | Double-check the `DOMAIN\username` and password; confirm the account has TFS read access to the project |
| `git fast-import` reports `fatal: Corrupt patch at line N` | stdout was opened in text mode and line endings were converted | Ensure you pipe directly (`\|`) rather than writing to a text file; if using a file, use `> export.bundle` (cmd) not PowerShell `Set-Content` |
| Export runs but some branches are missing | The branch root changeset was in `_SkipCommits`, so the branch was never registered | Remove the changeset from `_SkipCommits`, or add a `_SpecialCommands` entry to register the branch manually |
| A changeset crashes with `IndexOutOfRangeException` on rename | The file was created and renamed in a single combined `Branch\|Rename` changeset | Add the changeset to `_SkipCommits` or handle it in `_SpecialCommands` |
