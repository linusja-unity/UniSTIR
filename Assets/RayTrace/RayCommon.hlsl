#ifndef __RAY_COMMON_RAYTRACE__
#define __RAY_COMMON_RAYTRACE__

#include "UnityShaderVariables.cginc"

#define K_PI                    3.1415926535f
#define K_HALF_PI               1.5707963267f
#define K_QUARTER_PI            0.7853981633f
#define K_TWO_PI                6.283185307f
#define K_T_MAX                 10000
#define K_RAY_ORIGIN_PUSH_OFF   0.002

uint WangHash(inout uint seed)
{
    seed = (seed ^ 61) ^ (seed >> 16);
    seed *= 9;
    seed = seed ^ (seed >> 4);
    seed *= 0x27d4eb2d;
    seed = seed ^ (seed >> 15);
    return seed;
}

float RandomFloat01(inout uint seed)
{
    return float(WangHash(seed)) / float(0xFFFFFFFF);
}

/* Generates a random vector in the UnitZ+ angle with max-angle offset in rad */
float3 RandomUnitVectorZ(inout uint state, in float max_angle)
{
    /*
     * Z is a value between [-1, 1], whilst A is an angle between [0, 2PI]
     * Using that Z value to determine which part/slice of unitsphere we are
     * along the Z axis, and the angle to determine the position on the slice
     *
     * By limiting the range of the Z values, we can determine the angle of
     * the cone, and by always generating it for Z-axis, we can always rotate
     * to align it with a new direction.
     */
    float z = 1.0 - (max_angle / K_PI) * RandomFloat01(state);
    float a = RandomFloat01(state) * K_TWO_PI;
    float r_slice = sqrt(1.0f - z * z);
    float x = r_slice * cos(a);
    float y = r_slice * sin(a);
    return float3(x, y, z);
}

float3 RandomUnitVector(inout uint state)
{
    return RandomUnitVectorZ(state, K_TWO_PI);
}

/* Generates a random vector in the UnitZ+ angle with max-angle offset in rad */
float3 RandomUnitVectorDir(inout uint state, in float max_angle, in float3 dir)
{
    float3 zVector = RandomUnitVectorZ(state, max_angle);
    float3 generatedAngle = cross(dir, float3(0.0, 0.0, 1.0));

    // If dir is effectivly the same as Z-axis, then we flip what we build our new base on
    generatedAngle = (length(generatedAngle) < 1e-5) ? float3(0.0, 1.0, 0.0) : generatedAngle;

    float3 baseX = normalize(cross(generatedAngle, dir));
    float3 baseY = normalize(cross(baseX, dir));
    float3 baseZ = normalize(dir);

    float3x3 zVectorToRotatedDir = float3x3(baseX, baseY, baseZ);
    return mul(zVector, zVectorToRotatedDir);
}

float3 RandomUnitVectorDirNormRange(inout uint state, in float normalized_range, in float3 dir)
{
    return RandomUnitVectorDir(state, lerp(K_PI, 0.0, normalized_range), dir);
}

float3 RandomUnitHemisphereVector(inout uint state, float3 dir)
{
    return RandomUnitVectorDir(state, K_PI, dir);
}

struct RayPayload
{
    // Inout
    uint rngState;

    // Output
    float3 albedo;
    float3 radiance;
    float3 worldPosition;
    float3 worldNormal;
    float3 worldReflection;
};

#endif // __RAY_COMMON_RAYTRACE__