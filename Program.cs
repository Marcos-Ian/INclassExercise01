using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace WindowEngine
{
    class Program
    {
        static void Main(string[] args)
        {
            var nativeWindowSettings = new NativeWindowSettings
            {
                ClientSize = new Vector2i(800, 600),
                Title = "OpenGL Terrain (VBO draw arrays)",
                WindowBorder = WindowBorder.Resizable,
                Profile = ContextProfile.Core,
                APIVersion = new Version(3, 3)
            };

            using var window = new GameWindow(GameWindowSettings.Default, nativeWindowSettings);
            var game = new Game(window.ClientSize.X, window.ClientSize.Y);

            window.Load += () =>
            {
                game.Init();
                game.Resize(window.ClientSize.X, window.ClientSize.Y);
            };

            // Keyboard: arrows pan, Z in / X out, R reset
            window.UpdateFrame += (FrameEventArgs e) =>
            {
                var kb = window.KeyboardState;
                float dt = (float)e.Time;

                if (kb.IsKeyDown(Keys.Escape)) window.Close();

                float panSpeed = 1.5f * dt;
                if (kb.IsKeyDown(Keys.Left)) game.Pan(-panSpeed, 0f);
                if (kb.IsKeyDown(Keys.Right)) game.Pan(+panSpeed, 0f);
                if (kb.IsKeyDown(Keys.Up)) game.Pan(0f, +panSpeed);
                if (kb.IsKeyDown(Keys.Down)) game.Pan(0f, -panSpeed);

                float zoomRate = 1.8f;               // exponential zoom feels better
                float step = MathF.Pow(zoomRate, dt);
                if (kb.IsKeyDown(Keys.Z)) game.ZoomBy(step);
                if (kb.IsKeyDown(Keys.X)) game.ZoomBy(1f / step);

                if (kb.IsKeyPressed(Keys.R)) game.ResetCamera();
            };

            window.RenderFrame += (FrameEventArgs e) =>
            {
                game.RenderGL(e.Time);
                window.SwapBuffers();
            };

            window.Resize += (ResizeEventArgs e) => game.Resize(e.Width, e.Height);
            window.FramebufferResize += (FramebufferResizeEventArgs e) => game.Resize(e.Width, e.Height);

            window.Run();
        }
    }
}
