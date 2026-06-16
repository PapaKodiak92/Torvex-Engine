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

    private uint _terrainVertexArray;
    private uint _terrainVertexBuffer;
    private int _terrainVertexCount;

    private uint _skyVertexArray;
    private uint _skyVertexBuffer;

    private uint _terrainShaderProgram;
    private uint _skyShaderProgram;

    private int _modelLocation;
    private int _viewLocation;
    private int _projectionLocation;
    private int _lightDirectionLocation;
    private int _ambientLightLocation;
    private int _sunStrengthLocation;
    private int _sunColorLocation;
    private int _moonDirectionLocation;
    private int _moonColorLocation;
    private int _moonStrengthLocation;
    private int _fogColorLocation;
    private int _fogStartLocation;
    private int _fogEndLocation;

    private int _skyTopColorLocation;
    private int _skyHorizonColorLocation;
    private int _skySunColorLocation;
    private int _skySunPositionLocation;
    private int _skySunSizeLocation;
    private int _skySunVisibilityLocation;
    private int _skyMoonColorLocation;
    private int _skyMoonPositionLocation;
    private int _skyMoonVisibilityLocation;
    private int _skyMoonPhaseLocation;
    private int _skyAspectRatioLocation;

    private Vector3 _cameraPosition = new(0f, 3.5f, 8f);
    private float _cameraYaw;
    private float _cameraPitch = -0.22f;
    private const float FieldOfViewRadians = MathF.PI / 4f;
    private const float DayLengthSeconds = 180f;
    private const float MoonCycleDays = 8f;

    private float _timeOfDay = 0.38f;
    private float _moonCycle = 0.50f; // 0 new, 0.25 first quarter, 0.5 full, 0.75 last quarter

    public TorvexRenderer(IWindow window)
    {
        _window = window;
    }

    public void Initialize()
    {
        _gl = GL.GetApi(_window);

        SetViewport(_window.FramebufferSize);
        _window.FramebufferResize += SetViewport;

        _gl.ClearColor(0.58f, 0.72f, 0.88f, 1.0f);
        _gl.Enable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);

        CreateSkyShader();
        CreateSkyMesh();

        CreateTerrainShader();
        CreateTerrainMesh();

        Console.WriteLine($"Graphics initialized. Framebuffer: {_window.FramebufferSize.X}x{_window.FramebufferSize.Y}");
        Console.WriteLine($"Terrain mesh generated. Vertices: {_terrainVertexCount}");
        Console.WriteLine("Sky gradient and warm sunlight enabled.");
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

        float timeScale = input.IsKeyDown(Key.T)
            ? 20.0f
            : 1.0f;

        _timeOfDay = Wrap01(_timeOfDay + dt / DayLengthSeconds * timeScale);
        _moonCycle = Wrap01(_moonCycle + dt / (DayLengthSeconds * MoonCycleDays) * timeScale);
    }

    public void Render(double deltaTime)
    {
        if (_gl is null)
        {
            return;
        }

        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        float aspectRatio = MathF.Max(1f, _window.FramebufferSize.X) / MathF.Max(1f, _window.FramebufferSize.Y);

        Vector3 cameraForward = GetCameraForward();
        Vector3 cameraRight = Vector3.Normalize(Vector3.Cross(cameraForward, Vector3.UnitY));
        Vector3 cameraUp = Vector3.Normalize(Vector3.Cross(cameraRight, cameraForward));

        Matrix4x4 view = Matrix4x4.CreateLookAt(
            _cameraPosition,
            _cameraPosition + cameraForward,
            Vector3.UnitY
        );

        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            FieldOfViewRadians,
            aspectRatio,
            0.1f,
            1000f
        );

        Vector3 sunDirection = GetSunDirection();
        Vector3 moonDirection = GetMoonDirection();

        float sunElevation = sunDirection.Y;
        float moonElevation = moonDirection.Y;

        Vector3 lightDirection = -sunDirection;
        Vector3 sunColor = GetSunColor(sunElevation);
        Vector3 moonColor = new(0.42f, 0.50f, 0.68f);
        Vector3 fogColor = GetSkyHorizonColor(sunElevation);

        float daylightAmount = SmoothStep(-0.05f, 0.35f, sunElevation);
        float moonPhaseLight = GetMoonPhaseLight();

        float moonVisibility =
            SmoothStep(-0.03f, 0.22f, moonElevation) *
            (1.0f - daylightAmount) *
            moonPhaseLight;

        // Night stays dark. Full moon helps silhouettes, but torches still matter.
        float ambientLight = Lerp(0.012f, 0.35f, daylightAmount);
        float sunStrength = Lerp(0.0f, 0.90f, daylightAmount);
        float moonStrength = 0.075f * moonVisibility;

        DrawSky(
            cameraForward,
            cameraRight,
            cameraUp,
            sunDirection,
            moonDirection,
            sunColor,
            moonColor,
            moonVisibility,
            aspectRatio
        );

        _gl.UseProgram(_terrainShaderProgram);

        SetMatrix4(_viewLocation, view);
        SetMatrix4(_projectionLocation, projection);
        SetMatrix4(_modelLocation, Matrix4x4.Identity);

        _gl.Uniform3(_lightDirectionLocation, lightDirection.X, lightDirection.Y, lightDirection.Z);
        _gl.Uniform1(_ambientLightLocation, ambientLight);
        _gl.Uniform1(_sunStrengthLocation, sunStrength);
        _gl.Uniform3(_sunColorLocation, sunColor.X, sunColor.Y, sunColor.Z);

        _gl.Uniform3(_moonDirectionLocation, moonDirection.X, moonDirection.Y, moonDirection.Z);
        _gl.Uniform3(_moonColorLocation, moonColor.X, moonColor.Y, moonColor.Z);
        _gl.Uniform1(_moonStrengthLocation, moonStrength);

        _gl.Uniform3(_fogColorLocation, fogColor.X, fogColor.Y, fogColor.Z);
        _gl.Uniform1(_fogStartLocation, 42.0f);
        _gl.Uniform1(_fogEndLocation, 135.0f);

        DrawTerrain();
    }

    private void DrawSky(
    Vector3 cameraForward,
    Vector3 cameraRight,
    Vector3 cameraUp,
    Vector3 sunDirection,
    Vector3 moonDirection,
    Vector3 sunColor,
    Vector3 moonColor,
    float moonVisibility,
    float aspectRatio)
    {
        if (_gl is null)
        {
            return;
        }

        _gl.Disable(EnableCap.DepthTest);

        float sunElevation = sunDirection.Y;
        float sunVisibility = SmoothStep(-0.04f, 0.08f, sunElevation);

        Vector3 topColor = GetSkyTopColor(sunElevation);
        Vector3 horizonColor = GetSkyHorizonColor(sunElevation);

        Vector2 sunScreenPosition = ProjectDirectionToSkyUv(
            sunDirection,
            cameraForward,
            cameraRight,
            cameraUp,
            aspectRatio
        );

        Vector2 moonScreenPosition = ProjectDirectionToSkyUv(
            moonDirection,
            cameraForward,
            cameraRight,
            cameraUp,
            aspectRatio
        );

        _gl.UseProgram(_skyShaderProgram);

        _gl.Uniform3(_skyTopColorLocation, topColor.X, topColor.Y, topColor.Z);
        _gl.Uniform3(_skyHorizonColorLocation, horizonColor.X, horizonColor.Y, horizonColor.Z);

        _gl.Uniform3(_skySunColorLocation, sunColor.X, sunColor.Y, sunColor.Z);
        _gl.Uniform2(_skySunPositionLocation, sunScreenPosition.X, sunScreenPosition.Y);
        _gl.Uniform1(_skySunSizeLocation, 0.055f);
        _gl.Uniform1(_skySunVisibilityLocation, sunVisibility);

        _gl.Uniform3(_skyMoonColorLocation, moonColor.X, moonColor.Y, moonColor.Z);
        _gl.Uniform2(_skyMoonPositionLocation, moonScreenPosition.X, moonScreenPosition.Y);
        _gl.Uniform1(_skyMoonVisibilityLocation, moonVisibility);
        _gl.Uniform1(_skyMoonPhaseLocation, _moonCycle);
        _gl.Uniform1(_skyAspectRatioLocation, aspectRatio);

        _gl.BindVertexArray(_skyVertexArray);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);

        _gl.Enable(EnableCap.DepthTest);
    }

    private void DrawTerrain()
    {
        if (_gl is null)
        {
            return;
        }

        _gl.BindVertexArray(_terrainVertexArray);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_terrainVertexCount);
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

    private void CreateSkyShader()
    {
        if (_gl is null)
        {
            throw new InvalidOperationException("OpenGL has not been initialized.");
        }

        const string vertexShaderSource = """
        #version 330 core

        layout (location = 0) in vec2 aPosition;

        out vec2 screenUv;

        void main()
        {
            screenUv = aPosition * 0.5 + 0.5;
            gl_Position = vec4(aPosition, 0.0, 1.0);
        }
        """;

        const string fragmentShaderSource = """
        #version 330 core

        in vec2 screenUv;

        uniform vec3 uSkyTopColor;
        uniform vec3 uSkyHorizonColor;

        uniform vec3 uSkySunColor;
        uniform vec2 uSkySunPosition;
        uniform float uSkySunSize;
        uniform float uSkySunVisibility;

        uniform vec3 uSkyMoonColor;
        uniform vec2 uSkyMoonPosition;
        uniform float uSkyMoonVisibility;
        uniform float uSkyMoonPhase;

        uniform float uSkyAspectRatio;

        out vec4 FragColor;

        void main()
        {
            float heightBlend = smoothstep(0.0, 1.0, clamp(screenUv.y, 0.0, 1.0));
            vec3 skyColor = mix(uSkyHorizonColor, uSkyTopColor, heightBlend);

            vec2 sunDelta = screenUv - uSkySunPosition;
            sunDelta.x *= uSkyAspectRatio;

            float sunDistance = length(sunDelta);

            float sunDisc = 1.0 - smoothstep(uSkySunSize * 0.55, uSkySunSize, sunDistance);
            float sunCore = 1.0 - smoothstep(0.0, uSkySunSize * 0.55, sunDistance);
            float sunGlow = exp(-sunDistance * 12.0) * 0.55;
            float sunHalo = exp(-sunDistance * 3.6) * 0.10;

            sunDisc *= uSkySunVisibility;
            sunCore *= uSkySunVisibility;
            sunGlow *= uSkySunVisibility;
            sunHalo *= uSkySunVisibility;

            skyColor += uSkySunColor * sunGlow;
            skyColor += uSkySunColor * sunHalo;
            skyColor = mix(skyColor, vec3(1.0, 0.94, 0.72), sunDisc * 0.88);
            skyColor += vec3(1.0, 0.96, 0.82) * sunCore * 0.22;

            vec2 moonDelta = screenUv - uSkyMoonPosition;
            moonDelta.x *= uSkyAspectRatio;

            float moonDistance = length(moonDelta);

            float moonRadius = 0.034;
            float moonDisc = 1.0 - smoothstep(moonRadius * 0.86, moonRadius, moonDistance);
            float moonGlow = exp(-moonDistance * 16.0) * 0.13;

            float phaseAngle = uSkyMoonPhase * 6.28318530718;
            float litFraction = clamp(0.5 - 0.5 * cos(phaseAngle), 0.0, 1.0);

            float threshold = 1.0 - 2.0 * litFraction;
            float localX = moonDelta.x / moonRadius;

            float waxing = step(0.0, sin(phaseAngle));

            float waxingLit = smoothstep(threshold - 0.08, threshold + 0.08, localX);
            float waningLit = 1.0 - smoothstep(-threshold - 0.08, -threshold + 0.08, localX);

            float phaseMask = mix(waningLit, waxingLit, waxing);

            float moonLit = moonDisc * phaseMask * uSkyMoonVisibility;
            float moonDark = moonDisc * (1.0 - phaseMask) * uSkyMoonVisibility * 0.20;

            skyColor += uSkyMoonColor * moonGlow * uSkyMoonVisibility;
            skyColor = mix(skyColor, uSkyMoonColor, moonLit * 0.86);
            skyColor = mix(skyColor, uSkyMoonColor * 0.16, moonDark);

            FragColor = vec4(skyColor, 1.0);
        }
        """;

        uint vertexShader = CompileShader(ShaderType.VertexShader, vertexShaderSource);
        uint fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentShaderSource);

        _skyShaderProgram = _gl.CreateProgram();
        _gl.AttachShader(_skyShaderProgram, vertexShader);
        _gl.AttachShader(_skyShaderProgram, fragmentShader);
        _gl.LinkProgram(_skyShaderProgram);

        _gl.GetProgram(_skyShaderProgram, ProgramPropertyARB.LinkStatus, out int linkStatus);
        if (linkStatus == 0)
        {
            string infoLog = _gl.GetProgramInfoLog(_skyShaderProgram);
            throw new InvalidOperationException($"Sky shader program link failed: {infoLog}");
        }

        _gl.DetachShader(_skyShaderProgram, vertexShader);
        _gl.DetachShader(_skyShaderProgram, fragmentShader);
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        _skyTopColorLocation = _gl.GetUniformLocation(_skyShaderProgram, "uSkyTopColor");
        _skyHorizonColorLocation = _gl.GetUniformLocation(_skyShaderProgram, "uSkyHorizonColor");

        _skySunColorLocation = _gl.GetUniformLocation(_skyShaderProgram, "uSkySunColor");
        _skySunPositionLocation = _gl.GetUniformLocation(_skyShaderProgram, "uSkySunPosition");
        _skySunSizeLocation = _gl.GetUniformLocation(_skyShaderProgram, "uSkySunSize");
        _skySunVisibilityLocation = _gl.GetUniformLocation(_skyShaderProgram, "uSkySunVisibility");

        _skyMoonColorLocation = _gl.GetUniformLocation(_skyShaderProgram, "uSkyMoonColor");
        _skyMoonPositionLocation = _gl.GetUniformLocation(_skyShaderProgram, "uSkyMoonPosition");
        _skyMoonVisibilityLocation = _gl.GetUniformLocation(_skyShaderProgram, "uSkyMoonVisibility");
        _skyMoonPhaseLocation = _gl.GetUniformLocation(_skyShaderProgram, "uSkyMoonPhase");

        _skyAspectRatioLocation = _gl.GetUniformLocation(_skyShaderProgram, "uSkyAspectRatio");
    }

    private void CreateTerrainShader()
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
        out float vertexFogDepth;

        void main()
        {
            vec4 worldPosition = uModel * vec4(aPosition, 1.0);
            vec4 viewPosition = uView * worldPosition;

            vertexColor = aColor;
            vertexNormal = normalize(mat3(uModel) * aNormal);
            vertexFogDepth = length(viewPosition.xyz);

            gl_Position = uProjection * viewPosition;
        }
        """;

        const string fragmentShaderSource = """
        #version 330 core

        in vec3 vertexNormal;
        in vec3 vertexColor;
        in float vertexFogDepth;

        uniform vec3 uLightDirection;
        uniform float uAmbientLight;
        uniform float uSunStrength;
        uniform vec3 uSunColor;

        uniform vec3 uMoonDirection;
        uniform vec3 uMoonColor;
        uniform float uMoonStrength;

        uniform vec3 uFogColor;
        uniform float uFogStart;
        uniform float uFogEnd;

        out vec4 FragColor;

        void main()
        {
            vec3 normal = normalize(vertexNormal);

            float sunlight = max(dot(normal, -uLightDirection), 0.0);
            float moonlight = max(dot(normal, uMoonDirection), 0.0);

            vec3 ambient = vertexColor * uAmbientLight;
            vec3 directSun = vertexColor * uSunColor * sunlight * uSunStrength;
            vec3 directMoon = vertexColor * uMoonColor * moonlight * uMoonStrength;

            vec3 litColor = ambient + directSun + directMoon;

            float fogAmount = clamp((vertexFogDepth - uFogStart) / (uFogEnd - uFogStart), 0.0, 1.0);
            fogAmount = fogAmount * fogAmount * (3.0 - 2.0 * fogAmount);

            vec3 finalColor = mix(litColor, uFogColor, fogAmount);

            FragColor = vec4(finalColor, 1.0);
        }
        """;

        uint vertexShader = CompileShader(ShaderType.VertexShader, vertexShaderSource);
        uint fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentShaderSource);

        _terrainShaderProgram = _gl.CreateProgram();
        _gl.AttachShader(_terrainShaderProgram, vertexShader);
        _gl.AttachShader(_terrainShaderProgram, fragmentShader);
        _gl.LinkProgram(_terrainShaderProgram);

        _gl.GetProgram(_terrainShaderProgram, ProgramPropertyARB.LinkStatus, out int linkStatus);
        if (linkStatus == 0)
        {
            string infoLog = _gl.GetProgramInfoLog(_terrainShaderProgram);
            throw new InvalidOperationException($"Terrain shader program link failed: {infoLog}");
        }

        _gl.DetachShader(_terrainShaderProgram, vertexShader);
        _gl.DetachShader(_terrainShaderProgram, fragmentShader);
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        _modelLocation = _gl.GetUniformLocation(_terrainShaderProgram, "uModel");
        _viewLocation = _gl.GetUniformLocation(_terrainShaderProgram, "uView");
        _projectionLocation = _gl.GetUniformLocation(_terrainShaderProgram, "uProjection");

        _lightDirectionLocation = _gl.GetUniformLocation(_terrainShaderProgram, "uLightDirection");
        _ambientLightLocation = _gl.GetUniformLocation(_terrainShaderProgram, "uAmbientLight");
        _sunStrengthLocation = _gl.GetUniformLocation(_terrainShaderProgram, "uSunStrength");
        _sunColorLocation = _gl.GetUniformLocation(_terrainShaderProgram, "uSunColor");

        _moonDirectionLocation = _gl.GetUniformLocation(_terrainShaderProgram, "uMoonDirection");
        _moonColorLocation = _gl.GetUniformLocation(_terrainShaderProgram, "uMoonColor");
        _moonStrengthLocation = _gl.GetUniformLocation(_terrainShaderProgram, "uMoonStrength");

        _fogColorLocation = _gl.GetUniformLocation(_terrainShaderProgram, "uFogColor");
        _fogStartLocation = _gl.GetUniformLocation(_terrainShaderProgram, "uFogStart");
        _fogEndLocation = _gl.GetUniformLocation(_terrainShaderProgram, "uFogEnd");
    }

    private void CreateSkyMesh()
    {
        float[] vertices =
        [
            -1f, -1f,
             3f, -1f,
            -1f,  3f,
        ];

        if (_gl is null)
        {
            throw new InvalidOperationException("OpenGL has not been initialized.");
        }

        _skyVertexArray = _gl.GenVertexArray();
        _skyVertexBuffer = _gl.GenBuffer();

        _gl.BindVertexArray(_skyVertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _skyVertexBuffer);

        fixed (float* vertexData = vertices)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(vertices.Length * sizeof(float)),
                vertexData,
                BufferUsageARB.StaticDraw
            );
        }

        const uint stride = 2 * sizeof(float);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindVertexArray(0);
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

        CreateTerrainVertexObjects(vertices.ToArray());
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

    private void CreateTerrainVertexObjects(float[] vertices)
    {
        if (_gl is null)
        {
            throw new InvalidOperationException("OpenGL has not been initialized.");
        }

        _terrainVertexArray = _gl.GenVertexArray();
        _terrainVertexBuffer = _gl.GenBuffer();

        _gl.BindVertexArray(_terrainVertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _terrainVertexBuffer);

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

    private Vector3 GetSunDirection()
    {
        // timeOfDay:
        // 0.25 = sunrise in the east
        // 0.50 = noon
        // 0.75 = sunset in the west
        float angle = (_timeOfDay - 0.25f) * MathF.Tau;

        return Vector3.Normalize(new Vector3(
            MathF.Cos(angle),
            MathF.Sin(angle),
            -0.22f
        ));
    }

    private Vector3 GetMoonDirection()
    {
        // Moon angle is offset from the sun by the lunar phase.
        // 0.0 = new moon near sun
        // 0.5 = full moon opposite sun
        float sunAngle = (_timeOfDay - 0.25f) * MathF.Tau;
        float moonAngle = sunAngle + _moonCycle * MathF.Tau;

        return Vector3.Normalize(new Vector3(
            MathF.Cos(moonAngle),
            MathF.Sin(moonAngle),
            0.18f
        ));
    }

    private float GetMoonPhaseLight()
    {
        // 0 new moon, 1 full moon
        return 0.5f - 0.5f * MathF.Cos(_moonCycle * MathF.Tau);
    }

    private Vector2 ProjectDirectionToSkyUv(
        Vector3 worldDirection,
        Vector3 cameraForward,
        Vector3 cameraRight,
        Vector3 cameraUp,
        float aspectRatio)
    {
        float forwardDepth = Vector3.Dot(worldDirection, cameraForward);

        if (forwardDepth <= 0.001f)
        {
            return new Vector2(-10f, -10f);
        }

        float rightAmount = Vector3.Dot(worldDirection, cameraRight);
        float upAmount = Vector3.Dot(worldDirection, cameraUp);

        float projectionScale = 1.0f / MathF.Tan(FieldOfViewRadians * 0.5f);

        float ndcX = (rightAmount * projectionScale / aspectRatio) / forwardDepth;
        float ndcY = (upAmount * projectionScale) / forwardDepth;

        return new Vector2(
            ndcX * 0.5f + 0.5f,
            ndcY * 0.5f + 0.5f
        );
    }

    private static Vector3 GetSunColor(float elevation)
    {
        Vector3 sunriseColor = new(1.0f, 0.58f, 0.24f);
        Vector3 noonColor = new(1.0f, 0.88f, 0.62f);

        float noonAmount = SmoothStep(0.05f, 0.65f, elevation);

        return Lerp(sunriseColor, noonColor, noonAmount);
    }

    private static Vector3 GetSkyTopColor(float elevation)
    {
        Vector3 nightTop = new(0.015f, 0.025f, 0.055f);
        Vector3 dayTop = new(0.25f, 0.47f, 0.76f);

        float daylightAmount = SmoothStep(-0.08f, 0.35f, elevation);

        return Lerp(nightTop, dayTop, daylightAmount);
    }

    private static Vector3 GetSkyHorizonColor(float elevation)
    {
        Vector3 nightHorizon = new(0.035f, 0.045f, 0.075f);
        Vector3 sunriseHorizon = new(0.82f, 0.50f, 0.28f);
        Vector3 dayHorizon = new(0.62f, 0.74f, 0.86f);

        float daylightAmount = SmoothStep(-0.08f, 0.35f, elevation);
        float noonAmount = SmoothStep(0.15f, 0.65f, elevation);

        Vector3 dawnToDay = Lerp(sunriseHorizon, dayHorizon, noonAmount);

        return Lerp(nightHorizon, dawnToDay, daylightAmount);
    }

    private static float Lerp(float a, float b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return a + (b - a) * t;
    }

    private static float Wrap01(float value)
    {
        value %= 1.0f;

        if (value < 0.0f)
        {
            value += 1.0f;
        }

        return value;
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

        if (_terrainVertexBuffer != 0)
        {
            _gl.DeleteBuffer(_terrainVertexBuffer);
        }

        if (_terrainVertexArray != 0)
        {
            _gl.DeleteVertexArray(_terrainVertexArray);
        }

        if (_skyVertexBuffer != 0)
        {
            _gl.DeleteBuffer(_skyVertexBuffer);
        }

        if (_skyVertexArray != 0)
        {
            _gl.DeleteVertexArray(_skyVertexArray);
        }

        if (_terrainShaderProgram != 0)
        {
            _gl.DeleteProgram(_terrainShaderProgram);
        }

        if (_skyShaderProgram != 0)
        {
            _gl.DeleteProgram(_skyShaderProgram);
        }

        _gl.Dispose();
    }
}
