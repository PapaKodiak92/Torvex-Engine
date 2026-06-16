using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Torvex.Platform;

namespace Torvex.Graphics;

public sealed unsafe class TorvexRenderer : IDisposable
{
    private readonly IWindow _window;
    private GL? _gl;

    private uint _vertexArray;
    private uint _vertexBuffer;
    private uint _shaderProgram;

    private int _modelLocation;
    private int _viewLocation;
    private int _projectionLocation;

    private float _time;

    private Vector3 _cameraPosition = new(0f, 0f, 3f);
    private float _cameraYaw;
    private float _cameraPitch;

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
        _gl.Enable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);

        CreateMesh();

        Console.WriteLine($"Graphics initialized. Framebuffer: {_window.FramebufferSize.X}x{_window.FramebufferSize.Y}");
    }

    public void Render(double deltaTime)
    {
        if (_gl is null)
        {
            return;
        }

        _time += (float)deltaTime;

        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        float aspectRatio = MathF.Max(1f, _window.FramebufferSize.X) / MathF.Max(1f, _window.FramebufferSize.Y);

        Matrix4x4 model =
            Matrix4x4.CreateRotationX(_time * 0.65f) *
            Matrix4x4.CreateRotationY(_time * 0.95f);

        Vector3 cameraForward = GetCameraForward();

        Matrix4x4 view = Matrix4x4.CreateLookAt(
            _cameraPosition,
            _cameraPosition + cameraForward,
            Vector3.UnitY
        );

        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4f,
            aspectRatio,
            0.1f,
            100f
        );

        _gl.UseProgram(_shaderProgram);

        SetMatrix4(_modelLocation, model);
        SetMatrix4(_viewLocation, view);
        SetMatrix4(_projectionLocation, projection);

        _gl.BindVertexArray(_vertexArray);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 36);
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

    private void CreateMesh()
    {
        if (_gl is null)
        {
            throw new InvalidOperationException("OpenGL has not been initialized.");
        }

        float[] vertices =
        [
            // position              // color

            // front
            -0.5f, -0.5f,  0.5f,    0.90f, 0.62f, 0.25f,
             0.5f, -0.5f,  0.5f,    0.90f, 0.62f, 0.25f,
             0.5f,  0.5f,  0.5f,    0.90f, 0.62f, 0.25f,
             0.5f,  0.5f,  0.5f,    0.90f, 0.62f, 0.25f,
            -0.5f,  0.5f,  0.5f,    0.90f, 0.62f, 0.25f,
            -0.5f, -0.5f,  0.5f,    0.90f, 0.62f, 0.25f,

            // back
            -0.5f, -0.5f, -0.5f,    0.45f, 0.50f, 0.60f,
            -0.5f,  0.5f, -0.5f,    0.45f, 0.50f, 0.60f,
             0.5f,  0.5f, -0.5f,    0.45f, 0.50f, 0.60f,
             0.5f,  0.5f, -0.5f,    0.45f, 0.50f, 0.60f,
             0.5f, -0.5f, -0.5f,    0.45f, 0.50f, 0.60f,
            -0.5f, -0.5f, -0.5f,    0.45f, 0.50f, 0.60f,

            // left
            -0.5f,  0.5f,  0.5f,    0.55f, 0.72f, 0.38f,
            -0.5f,  0.5f, -0.5f,    0.55f, 0.72f, 0.38f,
            -0.5f, -0.5f, -0.5f,    0.55f, 0.72f, 0.38f,
            -0.5f, -0.5f, -0.5f,    0.55f, 0.72f, 0.38f,
            -0.5f, -0.5f,  0.5f,    0.55f, 0.72f, 0.38f,
            -0.5f,  0.5f,  0.5f,    0.55f, 0.72f, 0.38f,

            // right
             0.5f,  0.5f,  0.5f,    0.35f, 0.55f, 0.95f,
             0.5f, -0.5f, -0.5f,    0.35f, 0.55f, 0.95f,
             0.5f,  0.5f, -0.5f,    0.35f, 0.55f, 0.95f,
             0.5f, -0.5f, -0.5f,    0.35f, 0.55f, 0.95f,
             0.5f,  0.5f,  0.5f,    0.35f, 0.55f, 0.95f,
             0.5f, -0.5f,  0.5f,    0.35f, 0.55f, 0.95f,

            // top
            -0.5f,  0.5f, -0.5f,    0.85f, 0.72f, 0.28f,
            -0.5f,  0.5f,  0.5f,    0.85f, 0.72f, 0.28f,
             0.5f,  0.5f,  0.5f,    0.85f, 0.72f, 0.28f,
             0.5f,  0.5f,  0.5f,    0.85f, 0.72f, 0.28f,
             0.5f,  0.5f, -0.5f,    0.85f, 0.72f, 0.28f,
            -0.5f,  0.5f, -0.5f,    0.85f, 0.72f, 0.28f,

            // bottom
            -0.5f, -0.5f, -0.5f,    0.32f, 0.32f, 0.36f,
             0.5f, -0.5f,  0.5f,    0.32f, 0.32f, 0.36f,
            -0.5f, -0.5f,  0.5f,    0.32f, 0.32f, 0.36f,
             0.5f, -0.5f,  0.5f,    0.32f, 0.32f, 0.36f,
            -0.5f, -0.5f, -0.5f,    0.32f, 0.32f, 0.36f,
             0.5f, -0.5f, -0.5f,    0.32f, 0.32f, 0.36f,
        ];

        const string vertexShaderSource = """
        #version 330 core

        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aColor;

        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProjection;

        out vec3 vertexColor;

        void main()
        {
            vertexColor = aColor;
            gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
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

        _modelLocation = _gl.GetUniformLocation(_shaderProgram, "uModel");
        _viewLocation = _gl.GetUniformLocation(_shaderProgram, "uView");
        _projectionLocation = _gl.GetUniformLocation(_shaderProgram, "uProjection");

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

    private void SetMatrix4(int location, Matrix4x4 matrix)
    {
        if (_gl is null)
        {
            return;
        }

        float[] values =
        [
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44,
        ];

        fixed (float* pointer = values)
        {
            _gl.UniformMatrix4(location, 1, false, pointer);
        }
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

    public void Update(double deltaTime, TorvexWindow input)
    {
        float dt = (float)deltaTime;

        float lookSpeed = 1.8f;
        float moveSpeed = input.IsKeyDown(Key.ShiftLeft) || input.IsKeyDown(Key.ShiftRight)
            ? 7.5f
            : 3.5f;

        if (input.IsKeyDown(Key.Left))
        {
            _cameraYaw -= lookSpeed * dt;
        }

        if (input.IsKeyDown(Key.Right))
        {
            _cameraYaw += lookSpeed * dt;
        }

        if (input.IsKeyDown(Key.Up))
        {
            _cameraPitch += lookSpeed * dt;
        }

        if (input.IsKeyDown(Key.Down))
        {
            _cameraPitch -= lookSpeed * dt;
        }

        _cameraPitch = Math.Clamp(_cameraPitch, -1.45f, 1.45f);

        Vector3 forward = GetCameraForward();
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));

        if (input.IsKeyDown(Key.W))
        {
            _cameraPosition += forward * moveSpeed * dt;
        }

        if (input.IsKeyDown(Key.S))
        {
            _cameraPosition -= forward * moveSpeed * dt;
        }

        if (input.IsKeyDown(Key.D))
        {
            _cameraPosition += right * moveSpeed * dt;
        }

        if (input.IsKeyDown(Key.A))
        {
            _cameraPosition -= right * moveSpeed * dt;
        }

        if (input.IsKeyDown(Key.Space))
        {
            _cameraPosition += Vector3.UnitY * moveSpeed * dt;
        }

        if (input.IsKeyDown(Key.ControlLeft) || input.IsKeyDown(Key.ControlRight))
        {
            _cameraPosition -= Vector3.UnitY * moveSpeed * dt;
        }
    }

    private Vector3 GetCameraForward()
    {
        return Vector3.Normalize(new Vector3(
            MathF.Cos(_cameraPitch) * MathF.Sin(_cameraYaw),
            MathF.Sin(_cameraPitch),
            -MathF.Cos(_cameraPitch) * MathF.Cos(_cameraYaw)
        ));
    }
}
