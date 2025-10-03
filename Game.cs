using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace WindowEngine
{

    public class Game

    {
        private long frameCount = 0;
        private float angle = 0f;

        private static int CreateColor(int r, int g, int b)
        {
            // Pack as AARRGGBB (A=255). With PixelFormat.Bgra upload this maps correctly in memory.
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
        }

        // Transform world X coordinate (-2 to 2) to screen X coordinate (0 to width)
        // Maps world space to pixel space with centering
        private int TX(float x)
        {
            return (int)((x + 2f) / 4f * screen.width);
        }

        // Transform world Y coordinate (-2 to 2) to screen Y coordinate (0 to height)
        // Y-INVERSION: Traditional screen coordinates have Y=0 at TOP, increasing downward.
        // OpenGL/math conventions have Y=0 at BOTTOM, increasing upward.
        // We negate y to flip: positive world Y goes UP on screen (toward pixel row 0).
        // This creates the intuitive Cartesian coordinate system users expect.
        private int TY(float y)
        {
            return (int)((-y + 2f) / 4f * screen.height);
        }

        public void Tick()
        {
            frameCount++;
            angle += 0.0005f; // Rotate 0.05 radians per frame

            // Fill background dark blue
            Array.Fill(screen.pixels, unchecked((int)0xFF202060));

            // Pulse size with sine wave: oscillates between 0.7 and 1.3
            float sizeScale = 1.0f + 0.3f * (float)Math.Sin(angle);

            // Define square corners in world space (before rotation and scaling)
            float size = 1.0f * sizeScale; // Base size in world coords, scaled by pulse
            float[] cornerX = { -size, size, size, -size };
            float[] cornerY = { -size, -size, size, size };

            // Rotate each corner using rotation matrix
            // [ cos(a)  -sin(a) ]   [x]   [x*cos(a) - y*sin(a)]
            // [ sin(a)   cos(a) ] * [y] = [x*sin(a) + y*cos(a)]
            float cosA = (float)Math.Cos(angle);
            float sinA = (float)Math.Sin(angle);

            int[] screenX = new int[4];
            int[] screenY = new int[4];

            for (int i = 0; i < 4; i++)
            {
                // Apply rotation transformation
                float rx = cornerX[i] * cosA - cornerY[i] * sinA;
                float ry = cornerX[i] * sinA + cornerY[i] * cosA;

                // Convert rotated world coordinates to screen coordinates
                screenX[i] = TX(rx);
                screenY[i] = TY(ry);
            }

            // Draw the square by connecting corners with lines
            int white = unchecked((int)0xFFFFFFFF);
            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4; // Wrap around to close the square
                screen.Line(screenX[i], screenY[i], screenX[next], screenY[next], white);
            }

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

            // Bresenham's line algorithm for efficient line drawing
            public void Line(int x0, int y0, int x1, int y1, int color)
            {
                int dx = Math.Abs(x1 - x0);
                int dy = Math.Abs(y1 - y0);
                int sx = x0 < x1 ? 1 : -1;
                int sy = y0 < y1 ? 1 : -1;
                int err = dx - dy;

                while (true)
                {
                    // Plot pixel if within bounds
                    if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
                    {
                        pixels[x0 + y0 * width] = color;
                    }

                    // Check if we've reached the end
                    if (x0 == x1 && y0 == y1) break;

                    // Calculate error and step
                    int e2 = 2 * err;
                    if (e2 > -dy)
                    {
                        err -= dy;
                        x0 += sx;
                    }
                    if (e2 < dx)
                    {
                        err += dx;
                        y0 += sy;
                    }
                }
            }
        }
    }
}