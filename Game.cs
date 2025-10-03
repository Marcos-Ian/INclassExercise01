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
        // -------- window size
        private int _width, _height;

        // -------- camera (orbit around target)
        private float _camYaw = 0f;            // radians, Y rotation
        private float _camPitch = 0.6f;       // radians, tilt down slightly
        private float _camDist = 6.0f;         // radius
        private Vector3 _target = Vector3.Zero;

        // public camera controls, used by Program.cs
        public void Pan(float dx, float dz)
        {
            // pan in XZ plane relative to world axes, scaled by current distance
            float scale = MathF.Max(0.3f, _camDist * 0.15f);
            _target += new Vector3(dx * scale, 0f, -dz * scale);
        }
        public void ZoomBy(float factor)
        {
            _camDist = Clamp(_camDist * factor, 2.0f, 30.0f);
        }
        public void ResetCamera()
        {
            _camYaw = 0f;
            _camPitch = -0.3f;
            _camDist = 6.0f;
            _target = Vector3.Zero;
        }

        // -------- terrain data
        private const int N = 128;
        private float[,] _h = new float[N, N];

        // -------- GL objects
        private int _vao, _vbo, _ebo, _prog, _uMvp;
        private int _indexCount;

        // -------- matrices
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

            LoadHeightmap("heightmap.png");
            BuildTerrainMesh();

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

        // render is called every frame
        public void RenderGL(double dt)
        {
            // gentle auto-rotation
            _camYaw += (float)dt * 0.25f;

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // camera position from yaw/pitch/dist
            float cy = _camYaw;
            float cp = _camPitch;
            var eye = new Vector3(
                _target.X + _camDist * MathF.Cos(cp) * MathF.Sin(cy),
                _target.Y + _camDist * MathF.Sin(cp),
                _target.Z + _camDist * MathF.Cos(cp) * MathF.Cos(cy)
            );
            var view = Matrix4.LookAt(eye, _target, Vector3.UnitY);

            var model =
                Matrix4.CreateTranslation(0, -0.25f, 0); // lower a bit

            var mvp = model * view * _proj;

            GL.UseProgram(_prog);
            GL.UniformMatrix4(_uMvp, false, ref mvp);

            GL.BindVertexArray(_vao);
            GL.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }

        // -------------------- internals --------------------

        private void LoadHeightmap(string path)
        {
            if (!File.Exists(path))
            {
                // flat fallback
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                        _h[x, y] = 0f;
                return;
            }

            using var fs = File.OpenRead(path);
            // force greyscale
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
                    _h[x, y] = grey / 256f; // 0..~1
                }
            }
        }

        private void BuildTerrainMesh()
        {
            // vertex: position (vec3) + color (vec3)
            float heightScale = 1.2f;
            float span = 4.0f;              // world size in XZ
            float step = span / (N - 1);

            var verts = new List<float>(N * N * 6);
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float px = -span * 0.5f + x * step;
                    float pz = -span * 0.5f + y * step;
                    float py = _h[x, y] * heightScale;

                    // height-based color
                    Vector3 col =
                        py < 0.15f ? new Vector3(0.80f, 0.74f, 0.55f) :
                        (py < 0.45f ? new Vector3(0.20f, 0.65f, 0.30f) :
                                      new Vector3(0.55f, 0.55f, 0.55f));

                    verts.Add(px); verts.Add(py); verts.Add(pz);
                    verts.Add(col.X); verts.Add(col.Y); verts.Add(col.Z);
                }
            }

            var idx = new List<uint>((N - 1) * (N - 1) * 6);
            for (int y = 0; y < N - 1; y++)
            {
                for (int x = 0; x < N - 1; x++)
                {
                    uint i0 = (uint)(y * N + x);
                    uint i1 = (uint)((y + 1) * N + x);
                    uint i2 = (uint)(y * N + (x + 1));
                    uint i3 = (uint)((y + 1) * N + (x + 1));

                    idx.Add(i0); idx.Add(i1); idx.Add(i2);
                    idx.Add(i2); idx.Add(i1); idx.Add(i3);
                }
            }
            _indexCount = idx.Count;

            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            _ebo = GL.GenBuffer();

            GL.BindVertexArray(_vao);

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, verts.Count * sizeof(float), verts.ToArray(), BufferUsageHint.StaticDraw);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Count * sizeof(uint), idx.ToArray(), BufferUsageHint.StaticDraw);

            int stride = 6 * sizeof(float);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));

            GL.BindVertexArray(0);
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

        // small helpers (avoid framework/version differences)
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
