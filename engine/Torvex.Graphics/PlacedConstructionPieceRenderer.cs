using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGL;

namespace Torvex.Graphics;

public readonly record struct PlacedConstructionPiece(
    Vector3 Position,
    float YawRadians
);

public sealed unsafe class PlacedConstructionPieceRenderer : IDisposable
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

    public PlacedConstructionPieceRenderer(GL gl)
    {
        _gl = gl;
    }

    public void Initialize()
    {
        CreateShader();
        CreateTimberBeamMesh();
    }

    public void Render(Matrix4x4 view, Matrix4x4 projection, IReadOnlyList<PlacedConstructionPiece> pieces)
    {
        if (pieces.Count == 0)
        {
            return;
        }

        _gl.UseProgram(_shaderProgram);

        SetMatrix4(_viewLocation, view);
        SetMatrix4(_projectionLocation, projection);

        _gl.Uniform3(_colorLocation, 0.48f, 0.29f, 0.11f);

        _gl.BindVertexArray(_vertexArray);

        foreach (PlacedConstructionPiece piece in pieces)
        {
            Matrix4x4 model =
                Matrix4x4.CreateRotationY(piece.YawRadians) *
                Matrix4x4.CreateTranslation(piece.Position + new Vector3(0.0f, 0.035f, 0.0f));

            SetMatrix4(_modelLocation, model);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_vertexCount);
        }

        _gl.BindVertexArray(0);
    }

    private void CreateTimberBeamMesh()
    {
        const float halfLength = 1.55f;
        const float halfWidth = 0.24f;
        const float height = 0.34f;

        Vector3 p000 = new(-halfLength, 0.0f, -halfWidth);
        Vector3 p100 = new( halfLength, 0.0f, -halfWidth);
        Vector3 p110 = new( halfLength, height, -halfWidth);
        Vector3 p010 = new(-halfLength, height, -halfWidth);

        Vector3 p001 = new(-halfLength, 0.0f,  halfWidth);
        Vector3 p101 = new( halfLength, 0.0f,  halfWidth);
        Vector3 p111 = new( halfLength, height,  halfWidth);
        Vector3 p011 = new(-halfLength, height,  halfWidth);

        List<float> vertices = [];

        AddQuad(vertices, p000, p100, p110, p010, new Vector3(0.0f, 0.0f, -1.0f));
        AddQuad(vertices, p101, p001, p011, p111, new Vector3(0.0f, 0.0f, 1.0f));
        AddQuad(vertices, p010, p110, p111, p011, new Vector3(0.0f, 1.0f, 0.0f));
        AddQuad(vertices, p001, p101, p100, p000, new Vector3(0.0f, -1.0f, 0.0f));
        AddQuad(vertices, p100, p101, p111, p110, new Vector3(1.0f, 0.0f, 0.0f));
        AddQuad(vertices, p001, p000, p010, p011, new Vector3(-1.0f, 0.0f, 0.0f));

        _vertexCount = vertices.Count / 6;

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

        const uint stride = 6 * sizeof(float);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);

        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindVertexArray(0);
    }

    private static void AddQuad(List<float> vertices, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
    {
        AddVertex(vertices, a, normal);
        AddVertex(vertices, b, normal);
        AddVertex(vertices, c, normal);

        AddVertex(vertices, a, normal);
        AddVertex(vertices, c, normal);
        AddVertex(vertices, d, normal);
    }

    private static void AddVertex(List<float> vertices, Vector3 position, Vector3 normal)
    {
        vertices.Add(position.X);
        vertices.Add(position.Y);
        vertices.Add(position.Z);

        vertices.Add(normal.X);
        vertices.Add(normal.Y);
        vertices.Add(normal.Z);
    }

    private void CreateShader()
    {
        const string vertexShaderSource = """
        #version 330 core

        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aNormal;

        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProjection;

        out vec3 vertexNormal;
        out vec3 vertexWorldPosition;

        void main()
        {
            vec4 worldPosition = uModel * vec4(aPosition, 1.0);
            vertexWorldPosition = worldPosition.xyz;
            vertexNormal = mat3(uModel) * aNormal;

            gl_Position = uProjection * uView * worldPosition;
        }
        """;

        const string fragmentShaderSource = """
        #version 330 core

        uniform vec3 uColor;

        in vec3 vertexNormal;
        in vec3 vertexWorldPosition;

        out vec4 FragColor;

        void main()
        {
            vec3 normal = normalize(vertexNormal);
            vec3 lightDirection = normalize(vec3(-0.35, 0.85, -0.25));

            float directLight = max(dot(normal, lightDirection), 0.0);
            float light = 0.30 + directLight * 0.70;

            float subtleWoodBand =
                0.5 +
                sin(vertexWorldPosition.x * 8.0 + vertexWorldPosition.z * 2.0) * 0.08 +
                sin(vertexWorldPosition.y * 16.0) * 0.05;

            vec3 color = uColor * light * subtleWoodBand;

            FragColor = vec4(color, 1.0);
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
            throw new InvalidOperationException($"Placed construction shader link failed: {infoLog}");
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
