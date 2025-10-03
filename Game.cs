using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace WindowEngine
{
    public class Game
    {
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

        public void Tick()
        {
            // 1) Fill background dark blue
            Array.Fill(screen.pixels, unchecked((int)0xFF202060));

            // 2) Draw centered 300×300 solid blue square
            int square = 300;
            int startX = (screen.width - square) / 2;
            int startY = (screen.height - square) / 2;

            for (int y = 0; y < square; y++)
            {
                int sy = startY + y;
                if (sy < 0 || sy >= screen.height) continue;

                for (int x = 0; x < square; x++)
                {
                    int sx = startX + x;
                    if (sx < 0 || sx >= screen.width) continue;

                    // Solid blue (ARGB: A=255, R=0, G=0, B=255)
                    int color = (255 << 24) | (255);
                    int location = sx + sy * screen.width;
                    screen.pixels[location] = color;
                }
            }

            // 3) Upload pixels to texture and draw quad (same as before)
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
        }
    }
}
