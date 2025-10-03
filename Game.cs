using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using StbImageSharp;

namespace WindowEngine
{
    public class Game
    {
        // ---------- Window ----------
        private int _width, _height;

        // ---------- Camera (orbit above target) ----------
        private float _camYaw = 0.0f;
        private float _camPitch = 0.6f;       // > 0 => above looking down
        private float _camDist = 8.0f;
        private Vector3 _target = Vector3.Zero;

        // Public controls used by Program.cs
        public void Pan(float dx, float dz)
        {
            float scale = MathF.Max(0.3f, _camDist * 0.15f);
            _target += new Vector3(dx * scale, 0f, -dz * scale);
        }
        public void ZoomBy(float factor) => _camDist = Clamp(_camDist * factor, 2.0f, 100.0f);
        public void ResetCamera() { _camYaw = 0f; _camPitch = 0.6f; _camDist = 8.0f; _target = Vector3.Zero; }

        // ---------- Terrain / mesh ----------
        private const int N = 128;                 // change to 512 to stress-test
        private readonly float[,] _h = new float[N, N];

        // Split buffers: positions & colors (initialized to empty to avoid CS8618)
        private float[] _positions = Array.Empty<float>();
        private float[] _colors = Array.Empty<float>();
        private int _vertexCount;

        // ---------- GL resources ----------
        private int _vao;
        private int _posVbo, _colVbo;
        private int _program;
        private int _uMvpLoc;

        // ---------- Matrices ----------
        private Matrix4 _proj;

        // ---------- FPS (optional) ----------
        private int _frames = 0;
        private DateTime _lastFps = DateTime.Now;

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

            // 1) Heightmap (or procedural fallback)
            LoadHeightmapOrFallback("heightmap.png");

            // 2) Build CPU arrays (positions & colors)
            BuildCpuArrays();

            // Log sizes (no screenshot required)
            Console.WriteLine("=== VBO upload ===");
            Console.WriteLine($"Vertices:    {_vertexCount}");
            Console.WriteLine($"Pos floats:  {_positions.Length} (~{_positions.Length * sizeof(float) / (1024f * 1024f):F2} MB)");
            Console.WriteLine($"Col floats:  {_colors.Length}    (~{_colors.Length * sizeof(float) / (1024f * 1024f):F2} MB)");
            Console.WriteLine("==================");

            // 3) VAO + VBOs
            _vao = GL.GenVertexArray();
            _posVbo = GL.GenBuffer();
            _colVbo = GL.GenBuffer();

            GL.BindVertexArray(_vao);

            // positions -> location 0
            GL.BindBuffer(BufferTarget.ArrayBuffer, _posVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _positions.Length * sizeof(float), _positions, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);

            // colors -> location 1
            GL.BindBuffer(BufferTarget.ArrayBuffer, _colVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _colors.Length * sizeof(float), _colors, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);

            GL.BindVertexArray(0);

            // 4) Shaders: load/compile/link external GLSL files
            _program = CreateProgramFromFiles("vs.glsl", "fs.glsl");
            _uMvpLoc = GL.GetUniformLocation(_program, "uMVP");
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

        // Call this every frame BEFORE SwapBuffers
        public void RenderGL(double dtSeconds)
        {
            _camYaw += (float)dtSeconds * 0.25f;

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // MVP
            var eye = OrbitEye(_target, _camDist, _camYaw, _camPitch);
            var view = Matrix4.LookAt(eye, _target, Vector3.UnitY);
            var model = Matrix4.CreateTranslation(0, -0.25f, 0);
            var mvp = model * view * _proj;

            // Use program, set uniform, bind VAO, draw arrays
            GL.UseProgram(_program);
            GL.UniformMatrix4(_uMvpLoc, false, ref mvp);

            GL.BindVertexArray(_vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _vertexCount);
            GL.BindVertexArray(0);

            GL.UseProgram(0);

            // simple FPS print
            _frames++;
            var now = DateTime.Now;
            if ((now - _lastFps).TotalSeconds >= 1.0)
            {
                Console.WriteLine($"FPS: {_frames}");
                _frames = 0;
                _lastFps = now;
            }
        }

        // ---------------- internals ----------------

        private static Vector3 OrbitEye(Vector3 target, float dist, float yaw, float pitch)
        {
            float cy = yaw, cp = pitch;
            return new Vector3(
                target.X + dist * MathF.Cos(cp) * MathF.Sin(cy),
                target.Y + dist * MathF.Sin(cp),
                target.Z + dist * MathF.Cos(cp) * MathF.Cos(cy)
            );
        }

        private void LoadHeightmapOrFallback(string path)
        {
            if (!File.Exists(path))
            {
                // Procedural fallback so you always see something
                var rnd = new Random(123);
                float noise = 0.06f;
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        float nx = x / (float)(N - 1) * 2f - 1f;
                        float ny = y / (float)(N - 1) * 2f - 1f;
                        float r = MathF.Sqrt(nx * nx + ny * ny);
                        float island = MathF.Max(0f, 1f - r);
                        float n = (float)rnd.NextDouble() * 2f - 1f;
                        _h[x, y] = Clamp(island * 0.9f + n * noise, 0f, 1f);
                    }
                }
                return;
            }

            using var fs = File.OpenRead(path);
            var img = ImageResult.FromStream(fs, ColorComponents.Grey); // greyscale

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

        private void BuildCpuArrays()
        {
            float span = 8.0f;                 // world size in XZ
            float step = span / (N - 1);
            float heightScale = 2.0f;

            int quads = (N - 1) * (N - 1);
            _vertexCount = quads * 6;          // 2 triangles per quad, 3 verts per tri

            _positions = new float[_vertexCount * 3];
            _colors = new float[_vertexCount * 3];

            int ip = 0, ic = 0;

            for (int y = 0; y < N - 1; y++)
            {
                for (int x = 0; x < N - 1; x++)
                {
                    var v0 = new Vector3(-span * 0.5f + x * step, _h[x, y] * heightScale, -span * 0.5f + y * step);
                    var v1 = new Vector3(-span * 0.5f + x * step, _h[x, y + 1] * heightScale, -span * 0.5f + (y + 1) * step);
                    var v2 = new Vector3(-span * 0.5f + (x + 1) * step, _h[x + 1, y] * heightScale, -span * 0.5f + y * step);
                    var v3 = new Vector3(-span * 0.5f + (x + 1) * step, _h[x + 1, y + 1] * heightScale, -span * 0.5f + (y + 1) * step);

                    var c0 = HeightColor(v0.Y);
                    var c1 = HeightColor(v1.Y);
                    var c2 = HeightColor(v2.Y);
                    var c3 = HeightColor(v3.Y);

                    // tri1: v0,v1,v2
                    ip = WriteVec3(_positions, ip, v0); ic = WriteVec3(_colors, ic, c0);
                    ip = WriteVec3(_positions, ip, v1); ic = WriteVec3(_colors, ic, c1);
                    ip = WriteVec3(_positions, ip, v2); ic = WriteVec3(_colors, ic, c2);

                    // tri2: v2,v1,v3
                    ip = WriteVec3(_positions, ip, v2); ic = WriteVec3(_colors, ic, c2);
                    ip = WriteVec3(_positions, ip, v1); ic = WriteVec3(_colors, ic, c1);
                    ip = WriteVec3(_positions, ip, v3); ic = WriteVec3(_colors, ic, c3);
                }
            }
        }

        private static int WriteVec3(float[] arr, int idx, in Vector3 v)
        {
            arr[idx++] = v.X; arr[idx++] = v.Y; arr[idx++] = v.Z;
            return idx;
        }

        private static Vector3 HeightColor(float y)
        {
            if (y < 0.15f) return new Vector3(0.80f, 0.74f, 0.55f); // sand
            else if (y < 0.45f) return new Vector3(0.20f, 0.65f, 0.30f); // grass
            else return new Vector3(0.55f, 0.55f, 0.55f); // rock
        }

        private static int CreateProgramFromFiles(string vsPath, string fsPath)
        {
            string vs = ReadTextStrict(vsPath);
            string fs = ReadTextStrict(fsPath);

            int vsId = CompileShader(ShaderType.VertexShader, vs);
            int fsId = CompileShader(ShaderType.FragmentShader, fs);

            int prog = GL.CreateProgram();
            GL.AttachShader(prog, vsId);
            GL.AttachShader(prog, fsId);
            GL.LinkProgram(prog);
            GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int ok);
            if (ok == 0) throw new Exception("Link: " + GL.GetProgramInfoLog(prog));

            GL.DeleteShader(vsId);
            GL.DeleteShader(fsId);
            return prog;
        }

        private static int CompileShader(ShaderType type, string src)
        {
            int id = GL.CreateShader(type);
            GL.ShaderSource(id, src);
            GL.CompileShader(id);
            GL.GetShader(id, ShaderParameter.CompileStatus, out int ok);
            if (ok == 0) throw new Exception($"{type} compile: " + GL.GetShaderInfoLog(id));
            return id;
        }

        private static string ReadTextStrict(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Shader file not found: {path}");
            return File.ReadAllText(path);
        }

        // ---- helpers (avoid version-specific Math.Clamp) ----
        private static int ClampInt(int v, int mn, int mx) => v < mn ? mn : (v > mx ? mx : v);
        private static float Clamp(float v, float mn, float mx) => v < mn ? mn : (v > mx ? mx : v);
    }
}
