using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Torvex.Graphics;

public sealed unsafe class TorvexRenderer : IDisposable
{
    private readonly IWindow _window;
    private GL? _gl;

    private uint _vertexArray;
    private uint _vertexBuffer;
    private uint _shaderProgram;

    public TorvexRenderer(IWindow window)
    {
        _window = window;
    }

    public void Initialize()
    {
        _gl = GL.GetApi(_window);

        SetViewport(_window.FramebufferSize);
        _window.FramebufferResize += SetViewport;

        _gl.ClearColor(0.06f, 0.07f, 0.09f, 1.0f);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.DepthTest);

        CreateTriangle();

        Console.WriteLine($"Graphics initialized. Framebuffer: {_window.FramebufferSize.X}x{_window.FramebufferSize.Y}");
    }

    public void Render(double deltaTime)
    {
        if (_gl is null)
        {
            return;
        }

        _gl.Clear(ClearBufferMask.ColorBufferBit);

        _gl.UseProgram(_shaderProgram);
        _gl.BindVertexArray(_vertexArray);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);
    }

    private void SetViewport(Vector2D<int> size)
    {
        if (_gl is null)
        {
            return;
        }

        _gl.Viewport(0, 0, (uint)Math.Max(1, size.X), (uint)Math.Max(1, size.Y));
    }

    private void CreateTriangle()
    {
        if (_gl is null)
        {
            throw new InvalidOperationException("OpenGL has not been initialized.");
        }

        float[] vertices =
        [
             // position          // color
             0.0f,  0.75f, 0.0f,  1.0f, 0.75f, 0.15f,
            -0.75f, -0.65f, 0.0f,  0.15f, 0.85f, 0.35f,
             0.75f, -0.65f, 0.0f,  0.25f, 0.45f, 1.0f,
        ];

        const string vertexShaderSource = """
        #version 330 core

        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aColor;

        out vec3 vertexColor;

        void main()
        {
            vertexColor = aColor;
            gl_Position = vec4(aPosition, 1.0);
        }
        """;

        const string fragmentShaderSource = """
        #version 330 core

        in vec3 vertexColor;
        out vec4 FragColor;

        void main()
        {
            FragColor = vec4(vertexColor, 1.0);
        }
        """;

        uint vertexShader = CompileShader(ShaderType.VertexShader, vertexShaderSource);
        uint fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentShaderSource);

        _shaderProgram = _gl.CreateProgram();
        _gl.AttachShader(_shaderProgram, vertexShader);
        _gl.AttachShader(_shaderProgram, fragmentShader);
        _gl.LinkProgram(_shaderProgram);

        _gl.GetProgram(_shaderProgram, ProgramPropertyARB.LinkStatus, out int linkStatus);
        if (linkStatus == 0)
        {
            string infoLog = _gl.GetProgramInfoLog(_shaderProgram);
            throw new InvalidOperationException($"Shader program link failed: {infoLog}");
        }

        _gl.DetachShader(_shaderProgram, vertexShader);
        _gl.DetachShader(_shaderProgram, fragmentShader);
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        _vertexArray = _gl.GenVertexArray();
        _vertexBuffer = _gl.GenBuffer();

        _gl.BindVertexArray(_vertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);

        fixed (float* vertexData = vertices)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(vertices.Length * sizeof(float)),
                vertexData,
                BufferUsageARB.StaticDraw
            );
        }

        const uint stride = 6 * sizeof(float);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(
            0,
            3,
            VertexAttribPointerType.Float,
            false,
            stride,
            (void*)0
        );

        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(
            1,
            3,
            VertexAttribPointerType.Float,
            false,
            stride,
            (void*)(3 * sizeof(float))
        );

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindVertexArray(0);
    }

    private uint CompileShader(ShaderType shaderType, string source)
    {
        if (_gl is null)
        {
            throw new InvalidOperationException("OpenGL has not been initialized.");
        }

        uint shader = _gl.CreateShader(shaderType);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            string infoLog = _gl.GetShaderInfoLog(shader);
            throw new InvalidOperationException($"{shaderType} compile failed: {infoLog}");
        }

        return shader;
    }

    public void Dispose()
    {
        if (_gl is null)
        {
            return;
        }

        _window.FramebufferResize -= SetViewport;

        if (_vertexBuffer != 0)
        {
            _gl.DeleteBuffer(_vertexBuffer);
        }

        if (_vertexArray != 0)
        {
            _gl.DeleteVertexArray(_vertexArray);
        }

        if (_shaderProgram != 0)
        {
            _gl.DeleteProgram(_shaderProgram);
        }

        _gl.Dispose();
    }
}
