# s&box — Getting Started

End-to-end: install s&box, set up your dev environment, and create the first project.

## System requirements

| | Minimum | Recommended |
|---|---|---|
| OS | Windows 10 (64-bit) | Windows 11 (64-bit) |
| CPU | Intel Core i5-7500 / AMD Ryzen 5 1600 | Intel Core i7-9700K / AMD Ryzen 7 3700 |
| RAM | 8 GB | 16 GB |
| GPU | GTX 1050 / RX 570 (4 GB VRAM) | RTX 2060 / RX 6600 XT (8 GB VRAM) |
| Storage | 12 GB | 50 GB |
| Network | Broadband | Broadband |
| VR | OpenXR | OpenXR |

**Important:** Integrated **Intel** graphics are not supported. AMD APU integrated graphics (e.g. Ryzen APUs) do work.

## Install

s&box ships as **two separate Steam apps**: the **game** runtime and the **editor**. You need *both*. The editor is **not** accessible from inside the game — it's its own app.

1. Open Steam.
2. Library tab → search `s&box`.
3. Install **both** apps. After install your library should show **`s&box`** *and* **`s&box editor`** as separate entries.

One-click install link: <steam://run/2129370>

## Launch the editor

Three equivalent ways:

- **Steam** → Library → click Play on **`s&box editor`** (the entry separate from the game).
- **Shortcut to `sbox-dev.exe`** — found at `…\steamapps\common\sbox\bin\win64\sbox-dev.exe`. Pin it to taskbar or desktop.
- **Open a `.sbproj` file** — once you have a project, double-clicking it opens the editor on that project.

> **Known bug:** The Steam shortcut for the editor sometimes resolves to the wrong folder and fails to find `sbox-dev.exe` ([sbox-public#10086](https://github.com/Facepunch/sbox-public/issues/10086)). Workaround: launch `sbox-dev.exe` manually from the game directory.

## Dev-environment setup

s&box itself ships everything you need to *run* the editor and play the testbed. To *write game code* comfortably you also want:

### Required for game code

- **An IDE that handles C#.** When you create a custom Component from the editor, the engine writes a `.cs` file and opens it externally. By default this is **Visual Studio** (Community edition is free). If you'd rather use **JetBrains Rider** or **VS Code with the C# Dev Kit**, install your preference and set it as the default `.cs` handler in Windows.

- **.NET SDK.** The editor bundles the runtime it needs for hotload. You generally do not need to install a separate .NET SDK to *make a game* — only if you intend to build the engine itself from source.

### Optional

- **Git.** Recommended for version-controlling your project folder.
- **GitHub Desktop / `gh` CLI** if you want to clone sample projects like [sbox-scenestaging](https://github.com/Facepunch/sbox-scenestaging).

### Building the engine from source (advanced — skip for now)

The [Facepunch/sbox-public](https://github.com/Facepunch/sbox-public) repo is for contributing to the **engine itself**, not for making games. If you ever go down that path you'll need:

- Git
- Visual Studio 2026
- .NET 10 SDK
- Then run `Bootstrap.bat` to fetch dependencies and build.

For making games, **ignore this and use the Steam editor.**

## Create your first project

1. Launch the s&box editor.
2. On the welcome screen, click **New Project**.
3. Pick the **Minimal Game** template.
4. The editor opens with an empty scene.

### Add a controllable GameObject

1. **Right-click** the scene tree on the left → choose an object type to create.
2. Select your GameObject. In the inspector, click **Add Component** and type a name for a new component (e.g. `PlayerController`).
3. The new `.cs` file opens in your IDE.

### Make it move

In your component, override the lifecycle methods and read input each frame. Pseudo-shape:

```csharp
public sealed class PlayerController : Component
{
    [Property] public float Speed { get; set; } = 200f;

    protected override void OnUpdate()
    {
        var move = Vector3.Zero;
        if ( Input.Down( "Forward" ) )  move += Vector3.Forward;
        if ( Input.Down( "Backward" ) ) move += Vector3.Backward;
        if ( Input.Down( "Left" ) )     move += Vector3.Left;
        if ( Input.Down( "Right" ) )    move += Vector3.Right;

        WorldPosition += move * Speed * Time.Delta;
    }
}
```

Save the file. Hotload picks it up in milliseconds — no editor restart.

To follow the player with the camera, parent the camera GameObject to your player, or set its position directly:

```csharp
Scene.Camera.WorldPosition = WorldPosition + new Vector3( 0, 0, 100 );
```

## Where to go next

- [engine-overview.md](engine-overview.md) — the scene/GameObject/Component model, in depth.
- [cheat-sheet.md](cheat-sheet.md) — common C# snippets you'll use constantly.
- Live docs sections worth bookmarking:
  - [Input](https://sbox.game/dev/doc/gameplay/input/index.md) — read keys, mouse, controller.
  - [Code Basics](https://sbox.game/dev/doc/code/code-basics/index.md) — math types, console variables, API whitelist.
  - [Editor](https://sbox.game/dev/doc/editor/index.md) — model editor, mapping, custom tools.
  - [Networking](https://sbox.game/dev/doc/networking/index.md) — multiplayer, RPCs, dedicated servers.

### Sample to learn from

Clone the testbed and open it in the editor:

```bash
git clone https://github.com/Facepunch/sbox-scenestaging.git
```

Then double-click the `.sbproj` file inside.
