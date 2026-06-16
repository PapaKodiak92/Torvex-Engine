using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGL;

namespace Torvex.Graphics;

public readonly record struct ConstructionSocketDebugPoint(
    Vector3 Position,
    Vector3 Color
);

public sealed unsafe class ConstructionSocketDebugRenderer : IDisposable
{
    private readonly GL _gl;

    private uint _vertexArray;
    private uint _vertexBuffer;
    private uint _shaderProgram;

    private int _viewLocation;
    private int _projectionLocation;

    public ConstructionSocketDebugRenderer(GL gl)
    {
        _gl = gl;
    }

    public void Initialize()
    {
        CreateShader();

        _vertexArray = _gl.GenVertexArray();
        _vertexBuffer = _gl.GenBuffer();

        _gl.BindVertexArray(_vertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);

        const uint stride = 6 * sizeof(float);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);

        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindVertexArray(0);
    }

    public void Render(Matrix4x4 view, Matrix4x4 projection, IReadOnlyList<ConstructionSocketDebugPoint> points)
    {
        if (points.Count == 0)
        {
            return;
        }

        float[] vertices = new float[points.Count * 6];

        for (int i = 0; i < points.Count; i++)
        {
            int offset = i * 6;
            ConstructionSocketDebugPoint point = points[i];

            vertices[offset + 0] = point.Position.X;
            vertices[offset + 1] = point.Position.Y;
            vertices[offset + 2] = point.Position.Z;

            vertices[offset + 3] = point.Color.X;
            vertices[offset + 4] = point.Color.Y;
            vertices[offset + 5] = point.Color.Z;
        }

        _gl.BindVertexArray(_vertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);

        fixed (float* vertexData = vertices)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(vertices.Length * sizeof(float)),
                vertexData,
                BufferUsageARB.DynamicDraw
            );
        }

        _gl.UseProgram(_shaderProgram);
        SetMatrix4(_viewLocation, view);
        SetMatrix4(_projectionLocation, projection);

        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(false);

        _gl.DrawArrays(PrimitiveType.Points, 0, (uint)points.Count);

        _gl.DepthMask(true);
        _gl.Enable(EnableCap.DepthTest);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindVertexArray(0);
    }

    private void CreateShader()
    {
        const string vertexShaderSource = """
        #version 330 core

        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aColor;

        uniform mat4 uView;
        uniform mat4 uProjection;

        out vec3 vertexColor;

        void main()
        {
            vertexColor = aColor;
            gl_Position = uProjection * uView * vec4(aPosition, 1.0);
            gl_PointSize = 15.0;
        }
        """;

        const string fragmentShaderSource = """
        #version 330 core

        in vec3 vertexColor;

        out vec4 FragColor;

        void main()
        {
            vec2 point = gl_PointCoord - vec2(0.5);
            float distanceSquared = dot(point, point);

            if (distanceSquared > 0.25)
            {
                discard;
            }

            float edge = smoothstep(0.25, 0.16, distanceSquared);
            FragColor = vec4(vertexColor, 0.95 * edge);
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
            throw new InvalidOperationException($"Construction socket debug shader link failed: {infoLog}");
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
