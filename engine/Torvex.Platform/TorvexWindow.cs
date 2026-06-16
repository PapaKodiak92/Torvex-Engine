using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Torvex.Platform;

public sealed record TorvexWindowSettings(
    int Width,
    int Height,
    string Title,
    bool VSync = true
);

public sealed class TorvexWindow : IDisposable
{
    private IWindow? _window;
    private IInputContext? _input;

    public IWindow NativeWindow =>
        _window ?? throw new InvalidOperationException("Window has not been created yet.");

    public event Action? Loaded;
    public event Action<double>? Updated;
    public event Action<double>? Rendered;
    public event Action? Closing;

    public void Run(TorvexWindowSettings settings)
    {
        WindowOptions options = WindowOptions.Default;

        options.Size = new Vector2D<int>(settings.Width, settings.Height);
        options.Title = settings.Title;
        options.VSync = settings.VSync;

        options.API = new GraphicsAPI(
            ContextAPI.OpenGL,
            ContextProfile.Core,
            ContextFlags.ForwardCompatible,
            new APIVersion(3, 3)
        );

        _window = Window.Create(options);

        _window.Load += OnLoaded;
        _window.Update += deltaTime => Updated?.Invoke(deltaTime);
        _window.Render += deltaTime => Rendered?.Invoke(deltaTime);
        _window.Closing += () => Closing?.Invoke();

        _window.Run();
    }

    private void OnLoaded()
    {
        _input = NativeWindow.CreateInput();

        foreach (IKeyboard keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
        }

        Loaded?.Invoke();
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int keyCode)
    {
        if (key == Key.Escape)
        {
            NativeWindow.Close();
        }
    }

    public void Dispose()
    {
        _input?.Dispose();
        _window?.Dispose();
    }
}
