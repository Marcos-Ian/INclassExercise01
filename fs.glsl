#version 330 core
in vec3 vCol;
in vec3 vNormal;
in vec3 vWorldPos;

out vec4 FragColor;

// Simple Phong components (here we do Ambient + Diffuse):
//  - Ambient: constant base light so nothing is pure black.
//  - Diffuse (Lambert): depends on angle between normal and light direction.
//
// For a sunset vibe, use warm light color and cooler ambient.

uniform vec3 uLightDir;     // normalized world-space direction (toward the surface)
uniform vec3 uLightColor;   // warm sunset light, e.g. vec3(1.00, 0.58, 0.35)
uniform vec3 uAmbientColor; // cool ambient, e.g. vec3(0.08, 0.10, 0.18)
uniform float uAmbientK;    // ambient strength 0..1, e.g. 0.35

void main()
{
    vec3 N = normalize(vNormal);
    vec3 L = normalize(uLightDir);

    float NdotL = max(dot(N, L), 0.0);

    vec3 ambient = uAmbientK * uAmbientColor;
    vec3 diffuse = NdotL * uLightColor;

    // Combine with vertex color (acts like albedo)
    vec3 color = (ambient + diffuse) * vCol;

    FragColor = vec4(color, 1.0);
}
