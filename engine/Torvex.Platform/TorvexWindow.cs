using System.Numerics;
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
    private readonly HashSet<Key> _keysDown = [];
    private readonly HashSet<MouseButton> _mouseButtonsDown = [];

    private IWindow? _window;
    private IInputContext? _input;

    private Vector2 _lastMousePosition;
    private Vector2 _mouseDelta;
    private bool _firstMouseMove = true;
    private bool _mouseCaptured = true;

    public IWindow NativeWindow =>
        _window ?? throw new InvalidOperationException("Window has not been created yet.");

    public event Action? Loaded;
    public event Action<double>? Updated;
    public event Action<double>? Rendered;
    public event Action? Closing;

    public bool IsKeyDown(Key key)
    {
        return _keysDown.Contains(key);
    }

    public bool IsMouseButtonDown(MouseButton button)
    {
        return _mouseButtonsDown.Contains(button);
    }

    public Vector2 ConsumeMouseDelta()
    {
        Vector2 delta = _mouseDelta;
        _mouseDelta = Vector2.Zero;
        return delta;
    }

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
            keyboard.KeyUp += OnKeyUp;
        }

        foreach (IMouse mouse in _input.Mice)
        {
            mouse.MouseMove += OnMouseMove;
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
        }

        SetMouseCaptured(true);

        Loaded?.Invoke();
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int keyCode)
    {
        _keysDown.Add(key);

        if (key == Key.Escape)
        {
            NativeWindow.Close();
        }

        if (key == Key.F1)
        {
            SetMouseCaptured(!_mouseCaptured);
        }
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int keyCode)
    {
        _keysDown.Remove(key);
    }

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        _mouseButtonsDown.Add(button);
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        _mouseButtonsDown.Remove(button);
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        if (!_mouseCaptured)
        {
            return;
        }

        if (_firstMouseMove)
        {
            _lastMousePosition = position;
            _firstMouseMove = false;
            return;
        }

        _mouseDelta += position - _lastMousePosition;
        _lastMousePosition = position;
    }

    private void SetMouseCaptured(bool captured)
    {
        _mouseCaptured = captured;
        _firstMouseMove = true;
        _mouseDelta = Vector2.Zero;

        if (_input is null)
        {
            return;
        }

        foreach (IMouse mouse in _input.Mice)
        {
            mouse.Cursor.CursorMode = captured
                ? CursorMode.Raw
                : CursorMode.Normal;
        }

        Console.WriteLine(captured
            ? "Mouse captured. Press F1 to release."
            : "Mouse released. Press F1 to capture.");
    }

    public void Dispose()
    {
        _input?.Dispose();
        _window?.Dispose();
    }
}

