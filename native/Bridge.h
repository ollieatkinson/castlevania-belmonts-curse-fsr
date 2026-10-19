#pragma once
#include <stdint.h>
#ifdef _WIN32
#define EXPORT extern "C" __declspec(dllexport)
#else
#define EXPORT extern "C"
#endif
// Blittable ABI shared with NativeBridge.cs. Pointers are ID3D11Texture2D*.
struct Frame {
    void *color, *depth, *motion, *output;
    uint32_t width, height, reset, frame;
    float jitterX, jitterY, deltaMs, nearPlane, farPlane, fov, sharpness;
};
struct Status { int32_t failed; uint32_t frames, width, height; double gpuMilliseconds; };
static_assert(sizeof(Frame) == 80, "Frame ABI size");
static_assert(sizeof(Status) == 24, "Status ABI size");
EXPORT int Fsr4_Load(const wchar_t* directory);
EXPORT void* Fsr4_Queue(const Frame* frame);
EXPORT void* Fsr4_GetRenderEvent();
EXPORT void Fsr4_GetStatus(Status* status, char* message, uint32_t capacity);
EXPORT void Fsr4_Shutdown();
