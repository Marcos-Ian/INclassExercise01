Terrain Lighting with Normals

This project renders a 3D terrain from a heightmap using OpenGL and GLSL 330. To get realistic lighting, each vertex needs a normal vector that tells the shader which way the surface is facing.

Normal calculation

Normals are computed on the CPU from the heightmap using central differences. For each grid point (x, y) we look at the neighboring heights:

dX = (h[x+1,y] - h[x-1,y]) / (2 * step)
dY = (h[x,y+1] - h[x,y-1]) / (2 * step)
N  = normalize( vec3(-dX, 1, -dY) )


This gives a smooth approximation of the slope, and the normals are expanded into the triangle vertex data.

Shader pipeline

Normals are sent to the GPU in a third VBO and bound to layout(location=2).

Vertex shader (snippet):

layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aCol;
layout(location=2) in vec3 aNormal;

out vec3 vCol;
out vec3 vNormal;

uniform mat4 uMVP;
uniform mat4 uModel;

void main() {
    mat3 normalMat = mat3(transpose(inverse(uModel)));
    vNormal = normalize(normalMat * aNormal);
    vCol = aCol;
    gl_Position = uMVP * vec4(aPos, 1.0);
}


Fragment shader (snippet):

in vec3 vCol;
in vec3 vNormal;
out vec4 FragColor;

uniform vec3 uLightDir;
uniform vec3 uLightColor;
uniform vec3 uAmbientColor;
uniform float uAmbientK;

void main() {
    float NdotL = max(dot(normalize(vNormal), normalize(uLightDir)), 0.0);
    vec3 ambient = uAmbientK * uAmbientColor;
    vec3 diffuse = NdotL * uLightColor;
    FragColor = vec4((ambient + diffuse) * vCol, 1.0);
}

Lighting model

We use a simple Phong-style model:

Ambient: adds a base light so nothing is fully dark.

Diffuse (Lambert): depends on the angle between the normal and light direction.

The light is set to a warm orange (vec3(1.0, 0.58, 0.35)) with a cool twilight ambient, giving a sunset look.
