#ifndef FLOCKING
#define FLOCKING
struct FoliageChunk {
    float4x4 grassMat;
    int meshIndex;
};

StructuredBuffer<FoliageChunk> _GrassMat;
StructuredBuffer<int> _GrassTriangles;
StructuredBuffer<float3> _GrassPositions;
StructuredBuffer<float3> _GrassNormals;
StructuredBuffer<float2> _GrassUVs;
uniform uint _GrassIndexCount;

void GetGrassInstance(uint vertexID, uint instanceID, out float4x4 grassMat, out float3 localPosition, out float3 localNormal, out float2 uv) {
    int vertIndex = _GrassTriangles[vertexID+_GrassMat[instanceID].meshIndex*_GrassIndexCount];
    localPosition = _GrassPositions[vertIndex];
    localNormal = _GrassNormals[vertIndex];
    uv = _GrassUVs[vertIndex];
    grassMat = _GrassMat[instanceID].grassMat;
}

void GetGrassInstance_float(float vertexID, float instanceID, out float4x4 grassMat, out float3 localPosition, out float3 localNormal, out float2 uv) {
    GetGrassInstance(vertexID, instanceID, grassMat, localPosition, localNormal, uv);
}

#endif
