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

    private uint _testVertexArray;
    private uint _testVertexBuffer;

    private uint _terrainVertexArray;
    private uint _terrainVertexBuffer;
    private int _terrainVertexCount;

    private uint _shaderProgram;

    private int _modelLocation;
    private int _viewLocation;
    private int _projectionLocation;
    private int _lightDirectionLocation;
    private int _ambientLightLocation;
    private int _sunStrengthLocation;

    private Vector3 _cameraPosition = new(0f, 3.5f, 8f);
    private float _cameraYaw;
    private float _cameraPitch = -0.22f;

    private float _time;

    public TorvexRenderer(IWindow window)
    {
        _window = window;
    }

    public void Initialize()
    {
        _gl = GL.GetApi(_window);

        SetViewport(_window.FramebufferSize);
        _window.FramebufferResize += SetViewport;

        _gl.ClearColor(0.42f, 0.58f, 0.78f, 1.0f);
        _gl.Enable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);

        CreateShader();
        CreateTestMesh();
        CreateTerrainMesh();

        Console.WriteLine($"Graphics initialized. Framebuffer: {_window.FramebufferSize.X}x{_window.FramebufferSize.Y}");
        Console.WriteLine($"Terrain mesh generated. Vertices: {_terrainVertexCount}");
        Console.WriteLine("Sun lighting enabled.");
    }

    public void Update(double deltaTime, TorvexWindow input)
    {
        float dt = (float)deltaTime;

        Vector2 mouseDelta = input.ConsumeMouseDelta();

        const float mouseSensitivity = 0.0022f;

        _cameraYaw += mouseDelta.X * mouseSensitivity;
        _cameraPitch -= mouseDelta.Y * mouseSensitivity;

        float lookSpeed = 1.8f;
        float moveSpeed = input.IsKeyDown(Key.ShiftLeft) || input.IsKeyDown(Key.ShiftRight)
            ? 12.0f
            : 6.0f;

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

    public void Render(double deltaTime)
    {
        if (_gl is null)
        {
            return;
        }

        _time += (float)deltaTime;

        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        float aspectRatio = MathF.Max(1f, _window.FramebufferSize.X) / MathF.Max(1f, _window.FramebufferSize.Y);

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
            1000f
        );

        Vector3 lightDirection = Vector3.Normalize(new Vector3(-0.35f, -0.85f, -0.45f));

        _gl.UseProgram(_shaderProgram);

        SetMatrix4(_viewLocation, view);
        SetMatrix4(_projectionLocation, projection);

        _gl.Uniform3(_lightDirectionLocation, lightDirection.X, lightDirection.Y, lightDirection.Z);
        _gl.Uniform1(_ambientLightLocation, 0.34f);
        _gl.Uniform1(_sunStrengthLocation, 0.82f);

        DrawTerrain();
        DrawTestMesh();
    }

    private void DrawTerrain()
    {
        if (_gl is null)
        {
            return;
        }

        SetMatrix4(_modelLocation, Matrix4x4.Identity);

        _gl.BindVertexArray(_terrainVertexArray);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_terrainVertexCount);
        _gl.BindVertexArray(0);
    }

    private void DrawTestMesh()
    {
        if (_gl is null)
        {
            return;
        }

        float markerX = 0f;
        float markerZ = 0f;
        float markerY = GetTerrainHeight(markerX, markerZ) + 0.65f;

        Matrix4x4 model =
            Matrix4x4.CreateScale(1.2f, 1.0f, 1.2f) *
            Matrix4x4.CreateRotationY(_time * 0.45f) *
            Matrix4x4.CreateTranslation(markerX, markerY, markerZ);

        SetMatrix4(_modelLocation, model);

        _gl.BindVertexArray(_testVertexArray);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 36);
        _gl.BindVertexArray(0);
    }

    private Vector3 GetCameraForward()
    {
        return Vector3.Normalize(new Vector3(
            MathF.Cos(_cameraPitch) * MathF.Sin(_cameraYaw),
            MathF.Sin(_cameraPitch),
            -MathF.Cos(_cameraPitch) * MathF.Cos(_cameraYaw)
        ));
    }

    private float GetTerrainHeight(float x, float z)
    {
        float rolling =
            MathF.Sin(x * 0.055f) * 1.35f +
            MathF.Cos(z * 0.050f) * 1.20f;

        float secondary =
            MathF.Sin((x + z) * 0.115f) * 0.45f +
            MathF.Cos((x - z) * 0.095f) * 0.35f;

        float detail =
            MathF.Sin(x * 0.31f + z * 0.14f) * 0.10f +
            MathF.Cos(z * 0.27f - x * 0.11f) * 0.08f;

        float valley = -MathF.Exp(-(x * x + z * z) * 0.0018f) * 0.9f;

        return rolling + secondary + detail + valley;
    }

    private Vector3 GetTerrainNormal(float x, float z)
    {
        const float sampleDistance = 0.75f;

        float left = GetTerrainHeight(x - sampleDistance, z);
        float right = GetTerrainHeight(x + sampleDistance, z);
        float down = GetTerrainHeight(x, z - sampleDistance);
        float up = GetTerrainHeight(x, z + sampleDistance);

        return Vector3.Normalize(new Vector3(
            left - right,
            sampleDistance * 2.0f,
            down - up
        ));
    }

    private Vector3 GetTerrainColor(float height)
    {
        Vector3 lowGrass = new(0.24f, 0.34f, 0.20f);
        Vector3 grass = new(0.34f, 0.46f, 0.25f);
        Vector3 highGrass = new(0.42f, 0.48f, 0.28f);
        Vector3 dirt = new(0.42f, 0.36f, 0.28f);
        Vector3 stone = new(0.42f, 0.42f, 0.40f);

        if (height < -0.7f)
        {
            return Lerp(lowGrass, grass, SmoothStep(-1.8f, -0.7f, height));
        }

        if (height < 0.55f)
        {
            return Lerp(grass, highGrass, SmoothStep(-0.7f, 0.55f, height));
        }

        if (height < 1.45f)
        {
            return Lerp(highGrass, dirt, SmoothStep(0.55f, 1.45f, height));
        }

        return Lerp(dirt, stone, SmoothStep(1.45f, 2.4f, height));
    }

    private void SetViewport(Vector2D<int> size)
    {
        if (_gl is null)
        {
            return;
        }

        _gl.Viewport(0, 0, (uint)Math.Max(1, size.X), (uint)Math.Max(1, size.Y));
    }

    private void CreateShader()
    {
        if (_gl is null)
        {
            throw new InvalidOperationException("OpenGL has not been initialized.");
        }

        const string vertexShaderSource = """
        #version 330 core

        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aNormal;
        layout (location = 2) in vec3 aColor;

        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProjection;

        out vec3 vertexNormal;
        out vec3 vertexColor;

        void main()
        {
            vertexColor = aColor;
            vertexNormal = normalize(mat3(uModel) * aNormal);
            gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
        }
        """;

        const string fragmentShaderSource = """
        #version 330 core

        in vec3 vertexNormal;
        in vec3 vertexColor;

        uniform vec3 uLightDirection;
        uniform float uAmbientLight;
        uniform float uSunStrength;

        out vec4 FragColor;

        void main()
        {
            vec3 normal = normalize(vertexNormal);
            float sunlight = max(dot(normal, -uLightDirection), 0.0);

            float lightAmount = uAmbientLight + sunlight * uSunStrength;
            vec3 finalColor = vertexColor * lightAmount;

            FragColor = vec4(finalColor, 1.0);
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
        _lightDirectionLocation = _gl.GetUniformLocation(_shaderProgram, "uLightDirection");
        _ambientLightLocation = _gl.GetUniformLocation(_shaderProgram, "uAmbientLight");
        _sunStrengthLocation = _gl.GetUniformLocation(_shaderProgram, "uSunStrength");
    }

    private void CreateTerrainMesh()
    {
        int resolution = 160;
        float worldSize = 96f;
        float step = worldSize / resolution;
        float half = worldSize * 0.5f;

        List<float> vertices = [];

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float x0 = -half + x * step;
                float z0 = -half + z * step;
                float x1 = x0 + step;
                float z1 = z0 + step;

                AddTerrainTriangle(vertices, x0, z0, x1, z0, x1, z1);
                AddTerrainTriangle(vertices, x1, z1, x0, z1, x0, z0);
            }
        }

        _terrainVertexCount = vertices.Count / 9;

        CreateVertexObjects(vertices.ToArray(), out _terrainVertexArray, out _terrainVertexBuffer);
    }

    private void AddTerrainTriangle(List<float> vertices, float ax, float az, float bx, float bz, float cx, float cz)
    {
        AddTerrainVertex(vertices, ax, az);
        AddTerrainVertex(vertices, bx, bz);
        AddTerrainVertex(vertices, cx, cz);
    }

    private void AddTerrainVertex(List<float> vertices, float x, float z)
    {
        float y = GetTerrainHeight(x, z);
        Vector3 normal = GetTerrainNormal(x, z);
        Vector3 color = GetTerrainColor(y);

        AddVertex(vertices, new Vector3(x, y, z), normal, color);
    }

    private void CreateTestMesh()
    {
        List<float> vertices = [];

        Vector3 brown = new(0.68f, 0.48f, 0.30f);
        Vector3 tan = new(0.78f, 0.62f, 0.38f);
        Vector3 green = new(0.46f, 0.58f, 0.36f);
        Vector3 blueGray = new(0.34f, 0.40f, 0.52f);
        Vector3 dark = new(0.18f, 0.18f, 0.20f);

        AddFace(vertices,
            new Vector3(-0.5f, -0.5f, 0.5f),
            new Vector3(0.5f, -0.5f, 0.5f),
            new Vector3(0.5f, 0.5f, 0.5f),
            new Vector3(-0.5f, 0.5f, 0.5f),
            new Vector3(0f, 0f, 1f),
            tan);

        AddFace(vertices,
            new Vector3(0.5f, -0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, -0.5f),
            new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(0.5f, 0.5f, -0.5f),
            new Vector3(0f, 0f, -1f),
            dark);

        AddFace(vertices,
            new Vector3(-0.5f, -0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, 0.5f),
            new Vector3(-0.5f, 0.5f, 0.5f),
            new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(-1f, 0f, 0f),
            green);

        AddFace(vertices,
            new Vector3(0.5f, -0.5f, 0.5f),
            new Vector3(0.5f, -0.5f, -0.5f),
            new Vector3(0.5f, 0.5f, -0.5f),
            new Vector3(0.5f, 0.5f, 0.5f),
            new Vector3(1f, 0f, 0f),
            blueGray);

        AddFace(vertices,
            new Vector3(-0.5f, 0.5f, 0.5f),
            new Vector3(0.5f, 0.5f, 0.5f),
            new Vector3(0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(0f, 1f, 0f),
            tan);

        AddFace(vertices,
            new Vector3(-0.5f, -0.5f, -0.5f),
            new Vector3(0.5f, -0.5f, -0.5f),
            new Vector3(0.5f, -0.5f, 0.5f),
            new Vector3(-0.5f, -0.5f, 0.5f),
            new Vector3(0f, -1f, 0f),
            brown);

        CreateVertexObjects(vertices.ToArray(), out _testVertexArray, out _testVertexBuffer);
    }

    private static void AddFace(
        List<float> vertices,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 normal,
        Vector3 color)
    {
        AddVertex(vertices, a, normal, color);
        AddVertex(vertices, b, normal, color);
        AddVertex(vertices, c, normal, color);

        AddVertex(vertices, c, normal, color);
        AddVertex(vertices, d, normal, color);
        AddVertex(vertices, a, normal, color);
    }

    private static void AddVertex(List<float> vertices, Vector3 position, Vector3 normal, Vector3 color)
    {
        vertices.Add(position.X);
        vertices.Add(position.Y);
        vertices.Add(position.Z);

        vertices.Add(normal.X);
        vertices.Add(normal.Y);
        vertices.Add(normal.Z);

        vertices.Add(color.X);
        vertices.Add(color.Y);
        vertices.Add(color.Z);
    }

    private void CreateVertexObjects(float[] vertices, out uint vertexArray, out uint vertexBuffer)
    {
        if (_gl is null)
        {
            throw new InvalidOperationException("OpenGL has not been initialized.");
        }

        vertexArray = _gl.GenVertexArray();
        vertexBuffer = _gl.GenBuffer();

        _gl.BindVertexArray(vertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vertexBuffer);

        fixed (float* vertexData = vertices)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(vertices.Length * sizeof(float)),
                vertexData,
                BufferUsageARB.StaticDraw
            );
        }

        const uint stride = 9 * sizeof(float);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);

        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));

        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));

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

    private static Vector3 Lerp(Vector3 a, Vector3 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return a + (b - a) * t;
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public void Dispose()
    {
        if (_gl is null)
        {
            return;
        }

        _window.FramebufferResize -= SetViewport;

        if (_testVertexBuffer != 0)
        {
            _gl.DeleteBuffer(_testVertexBuffer);
        }

        if (_testVertexArray != 0)
        {
            _gl.DeleteVertexArray(_testVertexArray);
        }

        if (_terrainVertexBuffer != 0)
        {
            _gl.DeleteBuffer(_terrainVertexBuffer);
        }

        if (_terrainVertexArray != 0)
        {
            _gl.DeleteVertexArray(_terrainVertexArray);
        }

        if (_shaderProgram != 0)
        {
            _gl.DeleteProgram(_shaderProgram);
        }

        _gl.Dispose();
    }
}
