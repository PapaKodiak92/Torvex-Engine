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
