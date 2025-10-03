using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using StbImageSharp;

namespace WindowEngine
{
    public class Game
    {
        // ------- Window
        private int _width, _height;

        // ------- Camera (orbit above target)
        private float _camYaw = 0f;            // radians
        private float _camPitch = 0.6f;        // positive => above looking down
        private float _camDist = 6.0f;         // radius
        private Vector3 _target = Vector3.Zero;

        public void Pan(float dx, float dz)
        {
            // Pan in world XZ, scaled by distance so it feels consistent
            float scale = MathF.Max(0.3f, _camDist * 0.15f);
            _target += new Vector3(dx * scale, 0f, -dz * scale);
        }
        public void ZoomBy(float factor) => _camDist = Clamp(_camDist * factor, 2.0f, 30.0f);
        public void ResetCamera()
        {
            _camYaw = 0f; _camPitch = 0.6f; _camDist = 6.0f; _target = Vector3.Zero;
        }

        // ------- Terrain
        private const int N = 128;          // heightmap resolution
        private float[,] _h = new float[N, N];

        // Packed vertex array (pos + color interleaved)
        // size = (N-1)*(N-1)*2 triangles * 3 verts/tri * (pos3+col3) floats
        private float[] _vertexData;
        private int _vertexCount;

        // ------- GL objects
        private int _vao, _vbo, _prog, _uMvp;
        private Matrix4 _proj;

        public Game(int width, int height)
        {
            _width = Math.Max(1, width);
            _height = Math.Max(1, height);
        }

        public void Init()
        {
            GL.ClearColor(0.06f, 0.08f, 0.12f, 1f);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Lequal);

            _proj = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(60f),
                (float)_width / Math.Max(1, _height),
                0.05f, 100f);

            // 1) Load heights
            LoadHeightmapOrFallback("heightmap.png");

            // 2) Build VBO data
            BuildPackedVertexData();

            // ---- NEW: log stats
            int bytes = _vertexData.Length * sizeof(float);
            Console.WriteLine("=== Terrain VBO Info ===");
            Console.WriteLine($"Floats:   {_vertexData.Length}");
            Console.WriteLine($"Vertices: {_vertexCount}");
            Console.WriteLine($"Bytes:    {bytes} (~{bytes / (1024f * 1024f):F2} MB)");
            Console.WriteLine("========================");

            // 3) Upload to GPU
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _vertexData.Length * sizeof(float), _vertexData, BufferUsageHint.StaticDraw);

            int stride = 6 * sizeof(float);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));

            GL.BindVertexArray(0);

            _prog = CompileProgram(VS, FS);
            _uMvp = GL.GetUniformLocation(_prog, "uMVP");
        }


        public void Resize(int width, int height)
        {
            _width = Math.Max(1, width);
            _height = Math.Max(1, height);
            GL.Viewport(0, 0, _width, _height);

            _proj = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(60f),
                (float)_width / Math.Max(1, _height),
                0.05f, 100f);
        }

        public void RenderGL(double dt)
        {
            // gentle autorotation
            _camYaw += (float)dt * 0.25f;

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Camera placement from orbit params
            float cy = _camYaw;
            float cp = _camPitch;
            var eye = new Vector3(
                _target.X + _camDist * MathF.Cos(cp) * MathF.Sin(cy),
                _target.Y + _camDist * MathF.Sin(cp),
                _target.Z + _camDist * MathF.Cos(cp) * MathF.Cos(cy)
            );
            var view = Matrix4.LookAt(eye, _target, Vector3.UnitY);

            // Slightly lower the whole terrain so origin is around center
            var model = Matrix4.CreateTranslation(0, -0.25f, 0);
            var mvp = model * view * _proj;

            GL.UseProgram(_prog);
            GL.UniformMatrix4(_uMvp, false, ref mvp);

            GL.BindVertexArray(_vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _vertexCount);
            GL.BindVertexArray(0);

            GL.UseProgram(0);
        }

        // ---------------- internals ----------------

        private void LoadHeightmapOrFallback(string path)
        {
            if (!File.Exists(path))
            {
                // Procedural fallback: simple “island” using radial gradient + noise
                var rnd = new Random(1234);
                float noiseAmp = 0.08f;

                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        float nx = (x / (float)(N - 1)) * 2f - 1f;
                        float ny = (y / (float)(N - 1)) * 2f - 1f;
                        float r = MathF.Sqrt(nx * nx + ny * ny);  // 0 center .. ~1.414 corner
                        float island = MathF.Max(0f, 1.0f - r);    // fade to water outward
                        float n = (float)rnd.NextDouble() * 2f - 1f;
                        _h[x, y] = Clamp(island * 0.9f + n * noiseAmp, 0f, 1f);
                    }
                }
                return;
            }

            using var fs = File.OpenRead(path);
            var img = ImageResult.FromStream(fs, ColorComponents.Grey); // force greyscale

            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    int sx = (int)((x / (float)(N - 1)) * (img.Width - 1));
                    int sy = (int)((y / (float)(N - 1)) * (img.Height - 1));
                    sx = ClampInt(sx, 0, img.Width - 1);
                    sy = ClampInt(sy, 0, img.Height - 1);
                    byte grey = img.Data[sy * img.Width + sx];
                    _h[x, y] = grey / 255f; // 0..1
                }
            }
        }

        private void BuildPackedVertexData()
        {
            // Terrain spans [-2..2] in XZ (span=4). Height scaled a bit.
            float span = 4.0f;
            float step = span / (N - 1);
            float heightScale = 1.2f;

            int quadCount = (N - 1) * (N - 1);
            int floatsPerVertex = 6;          // pos3 + col3
            int vertsPerQuad = 6;             // 2 tris
            _vertexData = new float[quadCount * vertsPerQuad * floatsPerVertex];

            int i = 0;

            for (int y = 0; y < N - 1; y++)
            {
                for (int x = 0; x < N - 1; x++)
                {
                    // Four corners
                    Vector3 v0 = new(-span * 0.5f + x * step, _h[x, y] * heightScale, -span * 0.5f + y * step);
                    Vector3 v1 = new(-span * 0.5f + x * step, _h[x, y + 1] * heightScale, -span * 0.5f + (y + 1) * step);
                    Vector3 v2 = new(-span * 0.5f + (x + 1) * step, _h[x + 1, y] * heightScale, -span * 0.5f + y * step);
                    Vector3 v3 = new(-span * 0.5f + (x + 1) * step, _h[x + 1, y + 1] * heightScale, -span * 0.5f + (y + 1) * step);

                    // Height-based colors (sand -> grass -> rock)
                    Vector3 c0 = HeightColor(v0.Y);
                    Vector3 c1 = HeightColor(v1.Y);
                    Vector3 c2 = HeightColor(v2.Y);
                    Vector3 c3 = HeightColor(v3.Y);

                    // tri 1: v0, v1, v2
                    i = WriteVert(_vertexData, i, v0, c0);
                    i = WriteVert(_vertexData, i, v1, c1);
                    i = WriteVert(_vertexData, i, v2, c2);

                    // tri 2: v2, v1, v3
                    i = WriteVert(_vertexData, i, v2, c2);
                    i = WriteVert(_vertexData, i, v1, c1);
                    i = WriteVert(_vertexData, i, v3, c3);
                }
            }

            _vertexCount = i / 6; // each vertex wrote 6 floats
        }

        private static int WriteVert(float[] arr, int idx, in Vector3 pos, in Vector3 col)
        {
            arr[idx++] = pos.X; arr[idx++] = pos.Y; arr[idx++] = pos.Z;
            arr[idx++] = col.X; arr[idx++] = col.Y; arr[idx++] = col.Z;
            return idx;
        }

        private static Vector3 HeightColor(float h)
        {
            if (h < 0.15f) return new Vector3(0.80f, 0.74f, 0.55f); // sand
            else if (h < 0.45f) return new Vector3(0.20f, 0.65f, 0.30f); // grass
            else return new Vector3(0.55f, 0.55f, 0.55f); // rock
        }

        private static int CompileProgram(string vsSrc, string fsSrc)
        {
            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, vsSrc);
            GL.CompileShader(vs);
            GL.GetShader(vs, ShaderParameter.CompileStatus, out int vsOK);
            if (vsOK == 0) throw new Exception("VS: " + GL.GetShaderInfoLog(vs));

            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, fsSrc);
            GL.CompileShader(fs);
            GL.GetShader(fs, ShaderParameter.CompileStatus, out int fsOK);
            if (fsOK == 0) throw new Exception("FS: " + GL.GetShaderInfoLog(fs));

            int prog = GL.CreateProgram();
            GL.AttachShader(prog, vs);
            GL.AttachShader(prog, fs);
            GL.LinkProgram(prog);
            GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int linkOK);
            if (linkOK == 0) throw new Exception("Link: " + GL.GetProgramInfoLog(prog));

            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
            return prog;
        }

        private const string VS = @"
#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aCol;
uniform mat4 uMVP;
out vec3 vCol;
void main(){
    vCol = aCol;
    gl_Position = uMVP * vec4(aPos,1.0);
}";

        private const string FS = @"
#version 330 core
in vec3 vCol;
out vec4 FragColor;
void main(){
    FragColor = vec4(vCol, 1.0);
}";

        // ---- small helpers (avoid Math.Clamp version issues)
        private static int ClampInt(int v, int mn, int mx)
        {
            if (v < mn) return mn;
            if (v > mx) return mx;
            return v;
        }
        private static float Clamp(float v, float mn, float mx)
        {
            if (v < mn) return mn;
            if (v > mx) return mx;
            return v;
        }
    }
}
