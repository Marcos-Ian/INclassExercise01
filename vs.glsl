#version 330 core
// Modern pipeline summary:
// Vertex Shader -> Rasterizer -> Fragment Shader
// Vertex: transform position to clip space, pass color & normal to the next stage.
// Rasterizer: makes fragments, interpolates varyings across the triangle.
// Fragment: computes final color per-pixel (ambient + diffuse here).

layout(location = 0) in vec3 aPos;      // position from VBO
layout(location = 1) in vec3 aCol;      // vertex color (albedo/tint)
layout(location = 2) in vec3 aNormal;   // per-vertex normal (object space)

uniform mat4 uMVP;      // Model * View * Projection
uniform mat4 uModel;    // Model (for proper normal transform if scaled/rotated)

out vec3 vCol;          // passed to fragment
out vec3 vNormal;       // normal in world space (or view space—must match light space)
out vec3 vWorldPos;     // optional (not strictly needed for this diffuse but nice to have)

void main()
{
    // Transform to world space (model is translation in our sample, but keep it general)
    vec3 worldPos = vec3(uModel * vec4(aPos, 1.0));
    mat3 normalMat = mat3(transpose(inverse(uModel)));
    vec3 worldN = normalize(normalMat * aNormal);

    vCol = aCol;
    vNormal = worldN;
    vWorldPos = worldPos;

    gl_Position = uMVP * vec4(aPos, 1.0);
}
