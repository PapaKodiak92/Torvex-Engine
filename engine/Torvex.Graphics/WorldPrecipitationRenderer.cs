using System.Numerics;
using Silk.NET.OpenGL;

namespace Torvex.Graphics;

public sealed unsafe class WorldPrecipitationRenderer : IDisposable
{
    private readonly GL _gl;

    private uint _vertexArray;
    private uint _vertexBuffer;
    private uint _shaderProgram;

    private int _viewLocation;
    private int _projectionLocation;
    private int _pointSizeLocation;
    private int _isSnowLocation;

    private const int FloatsPerVertex = 7;

    public WorldPrecipitationRenderer(GL gl)
    {
        _gl = gl;
    }

    public void Initialize()
    {
        CreateShader();
        CreateVertexObjects();
    }

    public void Render(
        Vector3 cameraPosition,
        Vector3 cameraForward,
        Matrix4x4 view,
        Matrix4x4 projection,
        float precipitationIntensity,
        bool isSnow,
        Vector2 windDirection,
        float windStrength,
        float weatherTime)
    {
        if (precipitationIntensity <= 0.01f)
        {
            return;
        }

        List<float> vertices = [];

        BuildWorldPrecipitationVertices(
            vertices,
            cameraPosition,
            precipitationIntensity,
            isSnow,
            windDirection,
            windStrength,
            weatherTime
        );

        if (vertices.Count == 0)
        {
            return;
        }

        float[] vertexArray = vertices.ToArray();

        _gl.UseProgram(_shaderProgram);
        SetMatrix4(_viewLocation, view);
        SetMatrix4(_projectionLocation, projection);

        _gl.Uniform1(_pointSizeLocation, isSnow ? 9.0f : 1.1f);
        _gl.Uniform1(_isSnowLocation, isSnow ? 1.0f : 0.0f);

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

        if (isSnow)
        {
            _gl.DrawArrays(PrimitiveType.Points, 0, (uint)(vertexArray.Length / FloatsPerVertex));
        }
        else
        {
            _gl.LineWidth(1.25f);
            _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(vertexArray.Length / FloatsPerVertex));
        }

        _gl.BindVertexArray(0);
    }

    private static void BuildWorldPrecipitationVertices(
        List<float> vertices,
        Vector3 cameraPosition,
        float precipitationIntensity,
        bool isSnow,
        Vector2 windDirection,
        float windStrength,
        float weatherTime)
    {
        float cellSize = isSnow ? 3.2f : 4.0f;
        int radiusCells = isSnow ? 9 : 10;

        float topOffset = 28.0f;
        float bottomOffset = -7.0f;
        float heightRange = topOffset - bottomOffset;

        int baseCellX = (int)MathF.Floor(cameraPosition.X / cellSize);
        int baseCellZ = (int)MathF.Floor(cameraPosition.Z / cellSize);

        float fallSpeed = isSnow ? 0.55f : 4.25f;
        float windPush = isSnow ? 5.0f : 13.0f;
        float alpha = isSnow
            ? 0.98f * precipitationIntensity
            : 0.38f * precipitationIntensity;

        Vector3 precipColor = isSnow
            ? new Vector3(0.92f, 0.97f, 1.0f)
            : new Vector3(0.56f, 0.64f, 0.74f);

        Vector3 fallDirection = Vector3.Normalize(new Vector3(
            windDirection.X * windStrength * 0.65f,
            -1.0f,
            windDirection.Y * windStrength * 0.65f
        ));

        for (int z = -radiusCells; z <= radiusCells; z++)
        {
            for (int x = -radiusCells; x <= radiusCells; x++)
            {
                int cellX = baseCellX + x;
                int cellZ = baseCellZ + z;

                float densityRoll = Hash01(cellX, cellZ, 9);

                if (densityRoll > precipitationIntensity)
                {
                    continue;
                }

                int dropsInCell = isSnow
                    ? 5
                    : 3;

                for (int i = 0; i < dropsInCell; i++)
                {
                    float rx = Hash01(cellX, cellZ, 31 + i * 11);
                    float rz = Hash01(cellX, cellZ, 71 + i * 13);
                    float phase = Hash01(cellX, cellZ, 101 + i * 17);

                    float worldX = (cellX + rx) * cellSize;
                    float worldZ = (cellZ + rz) * cellSize;

                    float fall = Fract(phase + weatherTime * fallSpeed * 0.12f);
                    float worldY = cameraPosition.Y + topOffset - fall * heightRange;

                    Vector3 windDrift = new(
                        windDirection.X * windStrength * fall * windPush,
                        0.0f,
                        windDirection.Y * windStrength * fall * windPush
                    );

                    if (isSnow)
                    {
                        float swirl = MathF.Sin(weatherTime * 1.7f + phase * MathF.Tau) * 0.65f;
                        windDrift += new Vector3(swirl, 0.0f, -swirl * 0.35f);

                        Vector3 position = new Vector3(worldX, worldY, worldZ) + windDrift;

                        AddVertex(vertices, position, precipColor, alpha);
                    }
                    else
                    {
                        Vector3 start = new Vector3(worldX, worldY, worldZ) + windDrift;
                        Vector3 end = start + fallDirection * 1.85f;

                        AddVertex(vertices, start, precipColor, alpha);
                        AddVertex(vertices, end, precipColor, 0.05f);
                    }
                }
            }
        }
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
        uniform float uPointSize;

        out vec4 vertexColor;

        void main()
        {
            vertexColor = aColor;
            gl_Position = uProjection * uView * vec4(aPosition, 1.0);
            gl_PointSize = uPointSize;
        }
        """;

        const string fragmentShaderSource = """
        #version 330 core

        in vec4 vertexColor;

        uniform float uIsSnow;

        out vec4 FragColor;

        void main()
        {
            vec4 color = vertexColor;

            if (uIsSnow > 0.5)
            {
                vec2 point = gl_PointCoord * 2.0 - 1.0;
                float distanceFromCenter = dot(point, point);

                if (distanceFromCenter > 1.0)
                {
                    discard;
                }

                float softEdge = 1.0 - smoothstep(0.45, 1.0, distanceFromCenter);
                color.a *= softEdge;
            }

            FragColor = color;
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
            throw new InvalidOperationException($"World precipitation shader link failed: {infoLog}");
        }

        _gl.DetachShader(_shaderProgram, vertexShader);
        _gl.DetachShader(_shaderProgram, fragmentShader);
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        _viewLocation = _gl.GetUniformLocation(_shaderProgram, "uView");
        _projectionLocation = _gl.GetUniformLocation(_shaderProgram, "uProjection");
        _pointSizeLocation = _gl.GetUniformLocation(_shaderProgram, "uPointSize");
        _isSnowLocation = _gl.GetUniformLocation(_shaderProgram, "uIsSnow");
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

    private static void AddVertex(List<float> vertices, Vector3 position, Vector3 color, float alpha)
    {
        vertices.Add(position.X);
        vertices.Add(position.Y);
        vertices.Add(position.Z);

        vertices.Add(color.X);
        vertices.Add(color.Y);
        vertices.Add(color.Z);
        vertices.Add(alpha);
    }

    private static float Hash01(int x, int z, int salt)
    {
        unchecked
        {
            uint n = (uint)(x * 374761393 + z * 668265263 + salt * 982451653);
            n = (n ^ (n >> 13)) * 1274126177u;
            n ^= n >> 16;

            return (n & 0x00FFFFFF) / 16777216.0f;
        }
    }

    private static float Fract(float value)
    {
        return value - MathF.Floor(value);
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






