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

        // ------- Camera
        private float _camYaw = 0f;
        private float _camPitch = 0.6f;    // above terrain
        private float _camDist = 8.0f;     // farther back for big grid
        private Vector3 _target = Vector3.Zero;

        public void Pan(float dx, float dz)
        {
            float scale = MathF.Max(0.3f, _camDist * 0.15f);
            _target += new Vector3(dx * scale, 0f, -dz * scale);
        }
        public void ZoomBy(float factor) => _camDist = Clamp(_camDist * factor, 2.0f, 100.0f);
        public void ResetCamera()
        {
            _camYaw = 0f; _camPitch = 0.6f; _camDist = 8.0f; _target = Vector3.Zero;
        }

        // ------- Terrain
        private const int N = 512;           // larger grid!
        private float[,] _h = new float[N, N];

        private float[] _vertexData;
        private int _vertexCount;

        // ------- GL objects
        private int _vao, _vbo, _prog, _uMvp;
        private Matrix4 _proj;

        // ------- FPS counter
        private int _frames = 0;
        private DateTime _lastFpsTime = DateTime.Now;

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
                0.05f, 500f);

            // 1) Load heights (or fallback)
            LoadHeightmapOrFallback("heightmap.png");

            // 2) Build vertex data once
            BuildPackedVertexData();

            // Log buffer info
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

            // 4) Compile shaders
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
                0.05f, 500f);
        }

        public void RenderGL(double dt)
        {
            _camYaw += (float)dt * 0.25f;

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Camera orbit
            float cy = _camYaw;
            float cp = _camPitch;
            var eye = new Vector3(
                _target.X + _camDist * MathF.Cos(cp) * MathF.Sin(cy),
                _target.Y + _camDist * MathF.Sin(cp),
                _target.Z + _camDist * MathF.Cos(cp) * MathF.Cos(cy)
            );
            var view = Matrix4.LookAt(eye, _target, Vector3.UnitY);

            var model = Matrix4.CreateTranslation(0, -0.25f, 0);
            var mvp = model * view * _proj;

            GL.UseProgram(_prog);
            GL.UniformMatrix4(_uMvp, false, ref mvp);

            GL.BindVertexArray(_vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _vertexCount);
            GL.BindVertexArray(0);

            GL.UseProgram(0);

            // FPS counter
            _frames++;
            var now = DateTime.Now;
            if ((now - _lastFpsTime).TotalSeconds >= 1.0)
            {
                Console.WriteLine($"FPS: {_frames}");
                _frames = 0;
                _lastFpsTime = now;
            }
        }

        // ---------------- internals ----------------

        private void LoadHeightmapOrFallback(string path)
        {
            if (!File.Exists(path))
            {
                // Simple gradient fallback
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                        _h[x, y] = (float)y / (N - 1);
                return;
            }

            using var fs = File.OpenRead(path);
            var img = ImageResult.FromStream(fs, ColorComponents.Grey);

            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    int sx = (int)((x / (float)(N - 1)) * (img.Width - 1));
                    int sy = (int)((y / (float)(N - 1)) * (img.Height - 1));
                    sx = ClampInt(sx, 0, img.Width - 1);
                    sy = ClampInt(sy, 0, img.Height - 1);
                    byte grey = img.Data[sy * img.Width + sx];
                    _h[x, y] = grey / 255f;
                }
            }
        }

        private void BuildPackedVertexData()
        {
            float span = 8.0f;   // larger span for bigger grid
            float step = span / (N - 1);
            float heightScale = 2.0f;

            int quadCount = (N - 1) * (N - 1);
            int floatsPerVertex = 6;
            int vertsPerQuad = 6;
            _vertexData = new float[quadCount * vertsPerQuad * floatsPerVertex];

            int i = 0;
            for (int y = 0; y < N - 1; y++)
            {
                for (int x = 0; x < N - 1; x++)
                {
                    Vector3 v0 = new(-span / 2 + x * step, _h[x, y] * heightScale, -span / 2 + y * step);
                    Vector3 v1 = new(-span / 2 + x * step, _h[x, y + 1] * heightScale, -span / 2 + (y + 1) * step);
                    Vector3 v2 = new(-span / 2 + (x + 1) * step, _h[x + 1, y] * heightScale, -span / 2 + y * step);
                    Vector3 v3 = new(-span / 2 + (x + 1) * step, _h[x + 1, y + 1] * heightScale, -span / 2 + (y + 1) * step);

                    Vector3 c0 = HeightColor(v0.Y);
                    Vector3 c1 = HeightColor(v1.Y);
                    Vector3 c2 = HeightColor(v2.Y);
                    Vector3 c3 = HeightColor(v3.Y);

                    i = WriteVert(_vertexData, i, v0, c0);
                    i = WriteVert(_vertexData, i, v1, c1);
                    i = WriteVert(_vertexData, i, v2, c2);

                    i = WriteVert(_vertexData, i, v2, c2);
                    i = WriteVert(_vertexData, i, v1, c1);
                    i = WriteVert(_vertexData, i, v3, c3);
                }
            }
            _vertexCount = i / 6;
        }

        private static int WriteVert(float[] arr, int idx, in Vector3 pos, in Vector3 col)
        {
            arr[idx++] = pos.X; arr[idx++] = pos.Y; arr[idx++] = pos.Z;
            arr[idx++] = col.X; arr[idx++] = col.Y; arr[idx++] = col.Z;
            return idx;
        }

        private static Vector3 HeightColor(float h)
        {
            if (h < 0.15f) return new Vector3(0.80f, 0.74f, 0.55f);
            else if (h < 0.45f) return new Vector3(0.20f, 0.65f, 0.30f);
            else return new Vector3(0.55f, 0.55f, 0.55f);
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

        private static int ClampInt(int v, int mn, int mx) => v < mn ? mn : (v > mx ? mx : v);
        private static float Clamp(float v, float mn, float mx) => v < mn ? mn : (v > mx ? mx : v);
    }
}
