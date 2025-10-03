#version 330 core
in vec3 vCol;          // interpolated color from the vertex shader
out vec4 FragColor;    // final color written to the framebuffer

void main()
{
    FragColor = vec4(vCol, 1.0);
}
