using System.Numerics;
using Silk.NET.OpenGL;

namespace Torvex.Graphics;

public sealed unsafe class SnowSurfaceRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly Func<float, float, float> _heightProvider;
    private readonly Func<float, float, Vector3> _normalProvider;

    private uint _vertexArray;
    private uint _vertexBuffer;
    private uint _shaderProgram;

    private int _viewLocation;
    private int _projectionLocation;

    private const int FloatsPerVertex = 7;

    public SnowSurfaceRenderer(
        GL gl,
        Func<float, float, float> heightProvider,
        Func<float, float, Vector3> normalProvider)
    {
        _gl = gl;
        _heightProvider = heightProvider;
        _normalProvider = normalProvider;
    }

    public void Initialize()
    {
        CreateShader();
        CreateVertexObjects();
    }

    public void Render(Matrix4x4 view, Matrix4x4 projection, float snowAmount, float weatherTime)
    {
        if (snowAmount <= 0.015f)
        {
            return;
        }

        List<float> vertices = [];
        BuildSnowSurfaceVertices(vertices, snowAmount, weatherTime);

        if (vertices.Count == 0)
        {
            return;
        }

        float[] vertexArray = vertices.ToArray();

        _gl.UseProgram(_shaderProgram);
        SetMatrix4(_viewLocation, view);
        SetMatrix4(_projectionLocation, projection);

        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        _gl.BindVertexArray(_vertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);

        fixed (float* vertexData = vertexArray)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(vertexArray.Length * sizeof(float)),
                vertexData,
                BufferUsageARB.DynamicDraw
            );
        }

        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(vertexArray.Length / FloatsPerVertex));

        _gl.BindVertexArray(0);
    }

    private void BuildSnowSurfaceVertices(List<float> vertices, float snowAmount, float weatherTime)
    {
        int resolution = 120;
        float worldSize = 96f;
        float step = worldSize / resolution;
        float half = worldSize * 0.5f;

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float x0 = -half + x * step;
                float z0 = -half + z * step;
                float x1 = x0 + step;
                float z1 = z0 + step;

                AddSnowTriangle(vertices, x0, z0, x1, z0, x1, z1, snowAmount, weatherTime);
                AddSnowTriangle(vertices, x1, z1, x0, z1, x0, z0, snowAmount, weatherTime);
            }
        }
    }

    private void AddSnowTriangle(
        List<float> vertices,
        float ax,
        float az,
        float bx,
        float bz,
        float cx,
        float cz,
        float snowAmount,
        float weatherTime)
    {
        SnowVertex a = CreateSnowVertex(ax, az, snowAmount, weatherTime);
        SnowVertex b = CreateSnowVertex(bx, bz, snowAmount, weatherTime);
        SnowVertex c = CreateSnowVertex(cx, cz, snowAmount, weatherTime);

        float averageAlpha = (a.Color.W + b.Color.W + c.Color.W) / 3.0f;

        if (averageAlpha <= 0.025f)
        {
            return;
        }

        AddVertex(vertices, a);
        AddVertex(vertices, b);
        AddVertex(vertices, c);
    }

    private SnowVertex CreateSnowVertex(float x, float z, float snowAmount, float weatherTime)
    {
        float terrainHeight = _heightProvider(x, z);
        Vector3 normal = _normalProvider(x, z);

        float upwardFacing = Math.Clamp(normal.Y, 0.0f, 1.0f);

        float terrainNoise = HashNoise(x * 0.45f, z * 0.45f);
        float fineNoise = HashNoise(x * 1.7f + 19.0f, z * 1.7f - 31.0f);

        float slopeMask = SmoothStep(0.22f, 0.82f, upwardFacing);
        float breakup = Lerp(0.72f, 1.0f, SmoothStep(0.12f, 0.82f, terrainNoise));

        float snowPack = Math.Clamp(snowAmount * slopeMask * breakup, 0.0f, 1.0f);

        float snowDepth = Lerp(0.035f, 0.85f, snowAmount);
        float unevenDepth = Lerp(0.82f, 1.18f, fineNoise);

        float y = terrainHeight + 0.035f + snowPack * snowDepth * unevenDepth;

        float alpha = Math.Clamp(snowPack * 2.8f, 0.0f, 0.98f);

        float brightness = Lerp(0.88f, 1.05f, fineNoise);
        Vector3 color = new Vector3(0.90f, 0.94f, 0.97f) * brightness;

        return new SnowVertex(
            new Vector3(x, y, z),
            new Vector4(color.X, color.Y, color.Z, alpha)
        );
    }

    private void CreateVertexObjects()
    {
        _vertexArray = _gl.GenVertexArray();
        _vertexBuffer = _gl.GenBuffer();

        _gl.BindVertexArray(_vertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);

        const uint stride = FloatsPerVertex * sizeof(float);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);

        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindVertexArray(0);
    }

    private void CreateShader()
    {
        const string vertexShaderSource = """
        #version 330 core

        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec4 aColor;

        uniform mat4 uView;
        uniform mat4 uProjection;

        out vec4 vertexColor;

        void main()
        {
            vertexColor = aColor;
            gl_Position = uProjection * uView * vec4(aPosition, 1.0);
        }
        """;

        const string fragmentShaderSource = """
        #version 330 core

        in vec4 vertexColor;

        out vec4 FragColor;

        void main()
        {
            FragColor = vertexColor;
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
            throw new InvalidOperationException($"Snow surface shader link failed: {infoLog}");
        }

        _gl.DetachShader(_shaderProgram, vertexShader);
        _gl.DetachShader(_shaderProgram, fragmentShader);
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        _viewLocation = _gl.GetUniformLocation(_shaderProgram, "uView");
        _projectionLocation = _gl.GetUniformLocation(_shaderProgram, "uProjection");
    }

    private uint CompileShader(ShaderType shaderType, string source)
    {
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

    private void SetMatrix4(int location, Matrix4x4 matrix)
    {
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

    private static void AddVertex(List<float> vertices, SnowVertex vertex)
    {
        vertices.Add(vertex.Position.X);
        vertices.Add(vertex.Position.Y);
        vertices.Add(vertex.Position.Z);

        vertices.Add(vertex.Color.X);
        vertices.Add(vertex.Color.Y);
        vertices.Add(vertex.Color.Z);
        vertices.Add(vertex.Color.W);
    }

    private static float HashNoise(float x, float z)
    {
        return Fract(MathF.Sin(x * 12.9898f + z * 78.233f) * 43758.5453f);
    }

    private static float Lerp(float a, float b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return a + (b - a) * t;
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Fract(float value)
    {
        return value - MathF.Floor(value);
    }

    private readonly struct SnowVertex
    {
        public SnowVertex(Vector3 position, Vector4 color)
        {
            Position = position;
            Color = color;
        }

        public Vector3 Position { get; }
        public Vector4 Color { get; }
    }

    public void Dispose()
    {
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
    }
}
