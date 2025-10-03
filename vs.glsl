#version 330 core
// GPU pipeline: Vertex Shader -> Rasterizer -> Fragment Shader
// 1) Vertex Shader: transforms each input vertex (position, color) to clip space,
//    passing varyings (vCol) forward.
// 2) Rasterizer: turns triangles into fragments (pixels), interpolating varyings.
// 3) Fragment Shader: runs per-fragment, outputs final color.

layout(location = 0) in vec3 aPos;   // bound to position VBO
layout(location = 1) in vec3 aCol;   // bound to color VBO

uniform mat4 uMVP;                   // Model * View * Projection
out vec3 vCol;                       // varying goes to the fragment stage

void main()
{
    vCol = aCol;
    gl_Position = uMVP * vec4(aPos, 1.0);
}
