using Torvex.Core;
using Torvex.Graphics;
using Torvex.Platform;

Console.WriteLine($"{EngineInfo.Name} booting...");
Console.WriteLine("Loading Stonewright...");

using TorvexWindow window = new();

TorvexRenderer? renderer = null;

window.Loaded += () =>
{
    Console.WriteLine("Platform initialized.");

    renderer = new TorvexRenderer(window.NativeWindow);
    renderer.Initialize();

    Console.WriteLine("Stonewright loaded.");
    Console.WriteLine("Controls: WASD move | Mouse look | F1 toggle mouse | V walk/fly | Space jump/fly up | Ctrl fly down | Shift fast | T fast-forward day/moon/season | B build mode | X vertical/horizontal | Tab cycle socket | N snap on/off | R rotate 90 | Q/E rotate | Z/C raise/lower | G reset height | Left Click place | Backspace undo | F2-F7 weather | F8/F9 temp | F10/F11 snowpack | F12 seasonal climate | ESC close");
};

window.Updated += deltaTime =>
{
    renderer?.Update(deltaTime, window);
};

window.Rendered += deltaTime =>
{
    renderer?.Render(deltaTime);
};

window.Closing += () =>
{
    renderer?.Dispose();
    Console.WriteLine("Torvex shutdown complete.");
};

window.Run(new TorvexWindowSettings(
    Width: 1280,
    Height: 720,
    Title: "Stonewright - Powered by Torvex Engine"
));






