# Usage Guide

## What you need

- **Visual Studio 2022** (or .NET SDK + .NET Framework 4.7.2 Developer Pack)
- **Git for Windows** — https://git-scm.com/download/win
- A TFS account with read access to the target project

---

## Before the first run — edit the source

Open `tfs-fast-export\TfsChangeSet.cs` and adjust if needed:

- **`_SpecialCommands`** — add an entry for any changeset where a TFS branch folder was
  renamed, so the internal branch map stays correct after that point.
- **`Program.cs` → `_SkipCommits`** — add changeset IDs to skip entirely (e.g. TFS
  administration or build-template checkins that have no code).

Rebuild after any change (see step 1).

---

## Step 1 — Build

In a Developer Command Prompt, from the repo root:

```cmd
dotnet publish tfs-fast-export\tfs-fast-export.csproj -c Release
```

Output folder (contains the exe and all required DLLs):

```
tfs-fast-export\bin\Release\net472\publish\
```

---

## Step 2 — Create an empty Git repository

```cmd
mkdir C:\git-migration\MyProject
cd C:\git-migration\MyProject
git init
```

---

## Step 3 — Run the export

From inside the git repository folder:

```cmd
C:\path\to\publish\tfs-fast-export.exe ^
    http://tfsserver:8080/tfs/Collection ^
    $/MyProject ^
    DOMAIN\username ^
    | git fast-import
```

You will be prompted for your password. The export begins immediately after.

To pass the password non-interactively:

```cmd
C:\path\to\publish\tfs-fast-export.exe ^
    http://tfsserver:8080/tfs/Collection ^
    $/MyProject ^
    DOMAIN\username ^
    MyPassword ^
    | git fast-import
```

---

## Step 4 — Check out branches

```cmd
git branch -a
git checkout -b main refs/heads/MyProject
```
