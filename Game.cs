using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace WindowEngine
{
    public class Game
    {
        // Camera (world center + zoom)
        private float _centerX = 0f, _centerY = 0f;
        private float _zoom = 1f;

        // Base world range your old TX/TY assumed (-2..2)
        private const float BaseMin = -2f;
        private const float BaseMax = 2f;

        private long frameCount = 0;
        private float angle = 0f;

        private static int CreateColor(int r, int g, int b)
        {
            return (255 << 24) | (r << 16) | (g << 8) | b;
        }

        // 2D pixel surface
        private Surface screen;

        // GL stuff for drawing a textured quad
        private int _vao, _vbo, _ebo, _prog, _tex;

        // Simple passthrough shaders
        private const string VS = @"
#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUV;
out vec2 vUV;
void main(){
    vUV = aUV;
    gl_Position = vec4(aPos, 0.0, 1.0);
}";
        private const string FS = @"
#version 330 core
in vec2 vUV;
out vec4 FragColor;
uniform sampler2D uTex;
void main(){
    FragColor = texture(uTex, vUV);
}";

        public Game(int width, int height)
        {
            screen = new Surface(width, height);
        }

        public void Init()
        {
            GL.ClearColor(0f, 0f, 0.2f, 1f);

            // ---- compile shaders
            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, VS);
            GL.CompileShader(vs);
            GL.GetShader(vs, ShaderParameter.CompileStatus, out int vsOk);
            if (vsOk == 0) throw new Exception("VS: " + GL.GetShaderInfoLog(vs));

            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, FS);
            GL.CompileShader(fs);
            GL.GetShader(fs, ShaderParameter.CompileStatus, out int fsOk);
            if (fsOk == 0) throw new Exception("FS: " + GL.GetShaderInfoLog(fs));

            _prog = GL.CreateProgram();
            GL.AttachShader(_prog, vs);
            GL.AttachShader(_prog, fs);
            GL.LinkProgram(_prog);
            GL.GetProgram(_prog, GetProgramParameterName.LinkStatus, out int linkOk);
            if (linkOk == 0) throw new Exception("Link: " + GL.GetProgramInfoLog(_prog));
            GL.DeleteShader(vs);
            GL.DeleteShader(fs);

            // ---- fullscreen quad (two triangles)
            float[] verts = {
                // pos      // uv
                -1f, -1f,   0f, 0f,
                 1f, -1f,   1f, 0f,
                 1f,  1f,   1f, 1f,
                -1f,  1f,   0f, 1f
            };
            uint[] idx = { 0, 1, 2, 0, 2, 3 };

            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            _ebo = GL.GenBuffer();

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Length * sizeof(uint), idx, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);

            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));

            GL.BindVertexArray(0);

            // ---- texture to blit the pixel buffer
            _tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _tex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, screen.width, screen.height, 0,
                          PixelFormat.Bgra, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);

            // Initial viewport to current size
            GL.Viewport(0, 0, screen.width, screen.height);
        }

        // --- NEW: call this from your window's resize event
        public void Resize(int width, int height)
        {
            if (width <= 0 || height <= 0) return;

            // 1) Resize CPU pixel buffer
            screen = new Surface(width, height);

            // 2) Reallocate GPU texture storage to the new size
            GL.BindTexture(TextureTarget.Texture2D, _tex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0,
                          PixelFormat.Bgra, PixelType.UnsignedByte, IntPtr.Zero);
            GL.BindTexture(TextureTarget.Texture2D, 0);

            // 3) Update viewport
            GL.Viewport(0, 0, width, height);
        }
        public void Pan(float dxWorldPerSec, float dyWorldPerSec)
        {
            // Scale pan by 1/zoom so panning feels the same at all zoom levels
            float scale = 1f / MathF.Max(_zoom, 1e-6f);
            _centerX += dxWorldPerSec * scale;
            _centerY += dyWorldPerSec * scale;
        }
        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public void ZoomBy(float factor)
        {
            _zoom = Clamp(_zoom * factor, 0.25f, 50f);
        }

        public void ResetCamera()
        {
            _centerX = 0f; _centerY = 0f; _zoom = 1f;
        }

        // Transform world X coordinate (-2 to 2) to screen X coordinate (0 to width)
        // Map world X to pixel X using current center/zoom
        private int TX(float x)
        {
            float halfW = 0.5f * (BaseMax - BaseMin) / MathF.Max(_zoom, 1e-6f);
            float vminX = _centerX - halfW;
            float vmaxX = _centerX + halfW;

            float t = (x - vminX) / (vmaxX - vminX);
            return (int)MathF.Round(t * (screen.width - 1));
        }

        // Map world Y to pixel Y (inverted, top-left origin)
        private int TY(float y)
        {
            float halfH = 0.5f * (BaseMax - BaseMin) / MathF.Max(_zoom, 1e-6f);
            float vminY = _centerY - halfH;
            float vmaxY = _centerY + halfH;

            float t = (y - vminY) / (vmaxY - vminY);
            return (int)MathF.Round((1f - t) * (screen.height - 1));
        }


        public void Tick()
        {
            frameCount++;
            angle += 0.0015f;

            // Fill background
            Array.Fill(screen.pixels, unchecked((int)0xFF202060));

            // Pulse & rotate a square
            float sizeScale = 1.0f + 0.3f * MathF.Sin(angle);
            float size = 1.0f * sizeScale;
            float[] cornerX = { -size, size, size, -size };
            float[] cornerY = { -size, -size, size, size };

            float cosA = MathF.Cos(angle);
            float sinA = MathF.Sin(angle);

            int[] sx = new int[4];
            int[] sy = new int[4];

            for (int i = 0; i < 4; i++)
            {
                float rx = cornerX[i] * cosA - cornerY[i] * sinA;
                float ry = cornerX[i] * sinA + cornerY[i] * cosA;
                sx[i] = TX(rx);
                sy[i] = TY(ry);
            }

            int white = unchecked((int)0xFFFFFFFF);
            screen.FillPolygon(sx, sy, 4, white);

            // Upload pixels to texture and draw quad
            GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            GL.BindTexture(TextureTarget.Texture2D, _tex);
            var handle = GCHandle.Alloc(screen.pixels, GCHandleType.Pinned);
            try
            {
                GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, screen.width, screen.height,
                                 PixelFormat.Bgra, PixelType.UnsignedByte, handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }

            GL.UseProgram(_prog);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _tex);
            int loc = GL.GetUniformLocation(_prog, "uTex");
            if (loc >= 0) GL.Uniform1(loc, 0);

            GL.BindVertexArray(_vao);
            GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);

            GL.BindVertexArray(0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.UseProgram(0);
        }

        // Simple pixel surface
        public class Surface
        {
            public int[] pixels;
            public int width, height;
            public Surface(int width, int height)
            {
                this.width = width;
                this.height = height;
                pixels = new int[width * height];
            }

            public void Line(int x0, int y0, int x1, int y1, int color)
            {
                int dx = Math.Abs(x1 - x0);
                int dy = Math.Abs(y1 - y0);
                int sx = x0 < x1 ? 1 : -1;
                int sy = y0 < y1 ? 1 : -1;
                int err = dx - dy;

                while (true)
                {
                    if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
                        pixels[x0 + y0 * width] = color;

                    if (x0 == x1 && y0 == y1) break;

                    int e2 = 2 * err;
                    if (e2 > -dy) { err -= dy; x0 += sx; }
                    if (e2 < dx) { err += dx; y0 += sy; }
                }
            }

            public void FillPolygon(int[] x, int[] y, int numVerts, int color)
            {
                if (numVerts < 3) return;

                int minY = int.MaxValue, maxY = int.MinValue;
                for (int i = 0; i < numVerts; i++)
                {
                    if (y[i] < minY) minY = y[i];
                    if (y[i] > maxY) maxY = y[i];
                }

                minY = Math.Max(0, minY);
                maxY = Math.Min(height - 1, maxY);

                for (int scanY = minY; scanY <= maxY; scanY++)
                {
                    int[] intersections = new int[numVerts];
                    int intersectCount = 0;

                    for (int i = 0; i < numVerts; i++)
                    {
                        int next = (i + 1) % numVerts;
                        int y0 = y[i];
                        int y1 = y[next];

                        if ((y0 <= scanY && scanY < y1) || (y1 <= scanY && scanY < y0))
                        {
                            int x0 = x[i];
                            int x1 = x[next];
                            int intersectX = x0 + (scanY - y0) * (x1 - x0) / (y1 - y0);
                            intersections[intersectCount++] = intersectX;
                        }
                    }

                    Array.Sort(intersections, 0, intersectCount);

                    for (int i = 0; i < intersectCount; i += 2)
                    {
                        if (i + 1 < intersectCount)
                        {
                            int startX = Math.Max(0, intersections[i]);
                            int endX = Math.Min(width - 1, intersections[i + 1]);

                            for (int fillX = startX; fillX <= endX; fillX++)
                                pixels[fillX + scanY * width] = color;
                        }
                    }
                }
            }
        }
    }

}
