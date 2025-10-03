using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace WindowEngine
{
    // Main entry point and OpenGL window setup
    class Program
    {
        static void Main(string[] args)
        {
            var nativeWindowSettings = new NativeWindowSettings()
            {
                Size = new Vector2i(800, 600),
                Title = "OpenTK Graphics Tutorial",
                WindowBorder = WindowBorder.Resizable,   // allow resizing
                Profile = ContextProfile.Core,
                APIVersion = new Version(3, 3)
            };

            using (var window = new GameWindow(GameWindowSettings.Default, nativeWindowSettings))
            {
                var game = new Game(window.ClientSize.X, window.ClientSize.Y);

                window.Load += () =>
                {
                    game.Init();
                    game.Resize(window.ClientSize.X, window.ClientSize.Y);
                };

                // --- Keyboard-driven pan/zoom ---
                window.UpdateFrame += (FrameEventArgs e) =>
                {
                    var kb = window.KeyboardState;
                    float dt = (float)e.Time;

                    // Close on ESC
                    if (kb.IsKeyDown(Keys.Escape))
                        window.Close();

                    // Tunable controls
                    float panPerSecondWorld = 2.0f;       // world units per second before zoom scaling
                    float pan = panPerSecondWorld * dt;

                    // Pan with arrows (scaled by current zoom inside Game.Pan)
                    if (kb.IsKeyDown(Keys.Left)) game.Pan(-pan, 0f);
                    if (kb.IsKeyDown(Keys.Right)) game.Pan(+pan, 0f);
                    if (kb.IsKeyDown(Keys.Up)) game.Pan(0f, +pan);
                    if (kb.IsKeyDown(Keys.Down)) game.Pan(0f, -pan);

                    // Zoom with Z (in) and X (out)
                    // Exponential step so it feels smooth across frame rates
                    float zoomRate = 1.8f;                      // ~80% per second
                    float zoomStep = MathF.Pow(zoomRate, dt);   // per-frame factor
                    if (kb.IsKeyDown(Keys.Z)) game.ZoomBy(zoomStep);
                    if (kb.IsKeyDown(Keys.X)) game.ZoomBy(1f / zoomStep);

                    // Optional: Reset camera with R
                    if (kb.IsKeyPressed(Keys.R)) game.ResetCamera();
                };

                // Render loop
                window.RenderFrame += (FrameEventArgs e) =>
                {
                    game.Tick();
                    window.SwapBuffers();
                };

                // Resize handlers
                window.Resize += (ResizeEventArgs e) =>
                {
                    game.Resize(e.Width, e.Height);
                };
                window.FramebufferResize += (FramebufferResizeEventArgs e) =>
                {
                    game.Resize(e.Width, e.Height);
                };

                window.Run();
            }
        }
    }
}
