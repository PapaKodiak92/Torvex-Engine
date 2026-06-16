using System.Numerics;
using Silk.NET.OpenGL;

namespace Torvex.Graphics;

public sealed unsafe class TerrainTargetMarkerRenderer : IDisposable
{
    private readonly GL _gl;

    private uint _vertexArray;
    private uint _vertexBuffer;
    private uint _shaderProgram;
    private int _vertexCount;

    private int _modelLocation;
    private int _viewLocation;
    private int _projectionLocation;
    private int _colorLocation;

    public TerrainTargetMarkerRenderer(GL gl)
    {
        _gl = gl;
    }

    public void Initialize()
    {
        CreateShader();
        CreateMarkerMesh();
    }

    public void Render(Matrix4x4 view, Matrix4x4 projection, Vector3 position, float timeSeconds)
    {
        float pulse = 0.5f + MathF.Sin(timeSeconds * 5.0f) * 0.5f;
        float alpha = 0.42f + pulse * 0.24f;

        Matrix4x4 model =
            Matrix4x4.CreateScale(1.0f + pulse * 0.035f) *
            Matrix4x4.CreateTranslation(position + new Vector3(0.0f, 0.045f, 0.0f));

        _gl.UseProgram(_shaderProgram);

        SetMatrix4(_modelLocation, model);
        SetMatrix4(_viewLocation, view);
        SetMatrix4(_projectionLocation, projection);

        _gl.Uniform4(_colorLocation, 1.0f, 0.78f, 0.24f, alpha);

        _gl.BindVertexArray(_vertexArray);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_vertexCount);
        _gl.BindVertexArray(0);
    }

    private void CreateMarkerMesh()
    {
        const int segments = 72;
        const float innerRadius = 0.28f;
        const float outerRadius = 0.39f;

        List<float> vertices = [];

        for (int i = 0; i < segments; i++)
        {
            float a0 = i / (float)segments * MathF.Tau;
            float a1 = (i + 1) / (float)segments * MathF.Tau;

            Vector3 inner0 = new(MathF.Cos(a0) * innerRadius, 0.0f, MathF.Sin(a0) * innerRadius);
            Vector3 outer0 = new(MathF.Cos(a0) * outerRadius, 0.0f, MathF.Sin(a0) * outerRadius);
            Vector3 inner1 = new(MathF.Cos(a1) * innerRadius, 0.0f, MathF.Sin(a1) * innerRadius);
            Vector3 outer1 = new(MathF.Cos(a1) * outerRadius, 0.0f, MathF.Sin(a1) * outerRadius);

            AddVertex(vertices, outer0);
            AddVertex(vertices, inner0);
            AddVertex(vertices, inner1);

            AddVertex(vertices, outer0);
            AddVertex(vertices, inner1);
            AddVertex(vertices, outer1);
        }

        _vertexCount = vertices.Count / 3;

        _vertexArray = _gl.GenVertexArray();
        _vertexBuffer = _gl.GenBuffer();

        _gl.BindVertexArray(_vertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);

        float[] vertexArray = vertices.ToArray();

        fixed (float* vertexData = vertexArray)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(vertexArray.Length * sizeof(float)),
                vertexData,
                BufferUsageARB.StaticDraw
            );
        }

        const uint stride = 3 * sizeof(float);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindVertexArray(0);
    }

    private static void AddVertex(List<float> vertices, Vector3 position)
    {
        vertices.Add(position.X);
        vertices.Add(position.Y);
        vertices.Add(position.Z);
    }

    private void CreateShader()
    {
        const string vertexShaderSource = """
        #version 330 core

        layout (location = 0) in vec3 aPosition;

        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProjection;

        void main()
        {
            gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
        }
        """;

        const string fragmentShaderSource = """
        #version 330 core

        uniform vec4 uColor;

        out vec4 FragColor;

        void main()
        {
            FragColor = uColor;
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
            throw new InvalidOperationException($"Terrain target marker shader link failed: {infoLog}");
        }

        _gl.DetachShader(_shaderProgram, vertexShader);
        _gl.DetachShader(_shaderProgram, fragmentShader);
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        _modelLocation = _gl.GetUniformLocation(_shaderProgram, "uModel");
        _viewLocation = _gl.GetUniformLocation(_shaderProgram, "uView");
        _projectionLocation = _gl.GetUniformLocation(_shaderProgram, "uProjection");
        _colorLocation = _gl.GetUniformLocation(_shaderProgram, "uColor");
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
