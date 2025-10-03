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
                WindowBorder = WindowBorder.Resizable,   // <-- make the window resizable
                Profile = ContextProfile.Core,
                APIVersion = new Version(3, 3)
            };

            using (var window = new GameWindow(GameWindowSettings.Default, nativeWindowSettings))
            {
                // Create the game with the initial client size
                var game = new Game(window.ClientSize.X, window.ClientSize.Y);

                window.Load += () =>
                {
                    game.Init();

                    // Ensure GL viewport & buffers match actual drawable size on load
                    game.Resize(window.ClientSize.X, window.ClientSize.Y);
                };

                // Render loop
                window.RenderFrame += (FrameEventArgs e) =>
                {
                    game.Tick();
                    window.SwapBuffers();
                };

                // Handle logical window resizing (most cases)
                window.Resize += (ResizeEventArgs e) =>
                {
                    game.Resize(e.Width, e.Height);
                };

                // Handle framebuffer resize (HiDPI / scaling changes)
                window.FramebufferResize += (FramebufferResizeEventArgs e) =>
                {
                    // Prefer framebuffer size if available
                    game.Resize(e.Width, e.Height);
                };

                // Close on ESC
                window.UpdateFrame += (FrameEventArgs e) =>
                {
                    if (window.KeyboardState.IsKeyDown(Keys.Escape))
                        window.Close();
                };

                window.Run();
            }
        }
    }
}
