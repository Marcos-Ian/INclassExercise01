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
            // Define window settings
            var nativeWindowSettings = new NativeWindowSettings()
            {
                Size = new Vector2i(800, 600), // Modern resolution
                Title = "OpenTK Graphics Tutorial",
                WindowBorder = WindowBorder.Fixed,
                Profile = ContextProfile.Core, // Use modern OpenGL
                APIVersion = new Version(3, 3) // OpenGL 3.3 for compatibility
            };

            // Create window and game instance
            using (var window = new GameWindow(GameWindowSettings.Default, nativeWindowSettings))
            {
                var game = new Game(800, 600);

                // Initialize OpenGL on load
                window.Load += () =>
                {
                    game.Init();
                };

                // Update and render each frame
                window.RenderFrame += (FrameEventArgs e) =>
                {
                    game.Tick();
                    window.SwapBuffers(); // Double buffering for smooth rendering
                };

                // Handle window resizing (basic setup)
                window.Resize += (ResizeEventArgs e) =>
                {
                    GL.Viewport(0, 0, e.Width, e.Height);
                };

                // Close on ESC key
                window.UpdateFrame += (FrameEventArgs e) =>
                {
                    if (window.KeyboardState.IsKeyDown(Keys.Escape))
                    {
                        window.Close();
                    }
                };

                // Run at 60 FPS
                window.Run();
            }
        }
    }
}