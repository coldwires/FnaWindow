# Starter app

A complete build-on for the FnaWindow engine. Copy this folder into a fresh repo and you have a
runnable custom-chrome desktop app, with the repo plumbing already right.

Replace this file with your own app's README once you have copied it.

## Start a new app

```sh
mkdir my-app && cd my-app && git init -b main

# copy everything in this folder to here, including the dotfiles

git submodule add https://github.com/coldwires/FnaWindow engine
setup.cmd
dotnet run
```

Then rename to taste: `StarterApp.csproj`, `StarterGame.cs`, and the class name and window title
inside it. Set `<AssemblyName>` and `<RootNamespace>` in the csproj if you want the exe named
something other than the file.

## What each file is for

| File | Why it is here |
|---|---|
| `StarterApp.csproj` | `WinExe`, not `Exe`. `Exe` is the console subsystem, so Windows opens a terminal alongside your window. One `ProjectReference` to `engine\FnaWindow.csproj` pulls in the engine, FNA, the native libraries and the fonts. |
| `Program.cs` / `StarterGame.cs` | The whole app: subclass `WindowGame`, override `BuildUi`, fill the frame. |
| `setup.cmd` | Run once per clone. Fetches the engine submodule, and enables `.githooks` if your app has any. Without it a fresh clone has an empty `engine/` and fails to build with confusing errors about `Microsoft.Xna` not existing. |
| `.gitattributes` | LF in the repo, but CRLF for `.cmd`/`.bat`/`.ps1` - batch files misbehave with LF-only endings. Marks `.png`/`.dll`/`.ico` binary. |
| `.gitignore` | Build output only. Everything else should be committed, so a clone builds. |

## Working on it from another machine

```sh
git clone <your-app-repo>
cd my-app
setup.cmd
dotnet build
```

That is the whole flow. `setup.cmd` is safe to re-run.

## Update the engine later

```sh
cd engine && git pull origin main && cd ..
git add engine            # pins your app to the new engine commit
git commit -m "Update engine"
```

Your app stays pinned to a specific engine commit until you do this, so a base change never moves
your app underneath you.

## Make it yours

- `StarterGame.cs` - subclass of `WindowGame`; override `BuildUi` to set the title, menu, content
  and status bar.
- Add your own `Widget` subclasses for content, or use the engine's `TextArea` if you need an
  editable text view.
- An app icon is two separate things: `<ApplicationIcon>` in the csproj (a `.ico`, what Explorer
  shows) and `Content/appicon.png` (what the taskbar shows, picked up by the engine).
- See `docs/EXTENDING.md` in the engine for widgets, themes, skins, dialogs and text areas.
