# Starter app

A minimal build-on for the FnaWindow engine. Copy this folder into a fresh repo and you have
a complete, runnable custom-chrome desktop app in about twenty lines.

## Set it up

```sh
# 1. new repo for your app
mkdir my-app && cd my-app && git init

# 2. copy this folder's contents in (Program.cs, StarterGame.cs, StarterApp.csproj)

# 3. add the engine as a submodule at engine/
git submodule add <engine-repo-url> engine

# 4. run
dotnet run
```

The `.csproj` references `engine\FnaWindow.csproj`. That one reference pulls in the engine
library, FNA, the native libs, and the bundled fonts. There's nothing else to wire up.

## Update the engine later

```sh
cd engine && git pull origin main && cd ..
git add engine            # pins your app to the new engine commit
git commit -m "Update engine"
```

Your app stays pinned to a specific engine commit until you do this, so a base change never
moves your app underneath you.

## Make it yours

- `StarterGame.cs` - subclass of `WindowGame`; override `BuildUi` to set the title, menu,
  content, and status bar.
- Add your own `Widget` subclasses for content (see `docs/EXTENDING.md` in the engine).
- Change the window title/size in the `base(...)` call.
