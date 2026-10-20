#include <windows.h>
#include <d3d11.h>
#include <cstdio>
#include <cmath>
#include "Bridge.h"

static bool ok(HRESULT result, const char* name)
{
    if (FAILED(result))
    {
        printf("FAIL %s: %08lx\n", name, (unsigned long)result);
        return false;
    }
    return true;
}

static ID3D11Texture2D* texture(ID3D11Device* device, uint32_t w, uint32_t h, DXGI_FORMAT format)
{
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = w;
    desc.Height = h;
    desc.MipLevels = desc.ArraySize = 1;
    desc.Format = format;
    desc.SampleDesc.Count = 1;
    desc.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_UNORDERED_ACCESS;
    ID3D11Texture2D* tex = nullptr;
    if (!ok(device->CreateTexture2D(&desc, nullptr, &tex), "texture"))
        return nullptr;
    return tex;
}

static void clear(ID3D11Device* device, ID3D11DeviceContext* context, ID3D11Texture2D* texture, const float (&color)[4])
{
    ID3D11UnorderedAccessView* view = nullptr;
    device->CreateUnorderedAccessView(texture, nullptr, &view);
    context->ClearUnorderedAccessViewFloat(view, color);
    view->Release();
}

int main(int argc, char**)
{
    wchar_t path[MAX_PATH];
    GetCurrentDirectoryW(MAX_PATH, path);
    if (!Fsr4_Load(path))
        return 1;
    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* context = nullptr;
    if (!ok(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION, &device,
                              nullptr, &context),
            "D3D11CreateDevice"))
        return 1;
    uint32_t w = argc > 1 ? 5120 : 1280, h = argc > 1 ? 2160 : 720;
    Frame f{};
    f.width = w;
    f.height = h;
    f.color = texture(device, w, h, DXGI_FORMAT_R16G16B16A16_FLOAT);
    f.output = texture(device, w, h, DXGI_FORMAT_R16G16B16A16_FLOAT);
    f.depth = texture(device, w, h, DXGI_FORMAT_R32_FLOAT);
    f.motion = texture(device, w, h, DXGI_FORMAT_R16G16_FLOAT);
    if (!f.color || !f.output || !f.depth || !f.motion)
        return 1;
    f.nearPlane = .1f;
    f.farPlane = 1000;
    f.fov = 1.0472f;
    f.deltaMs = 16.6667f;
    f.sharpness = .2f;
    clear(device, context, (ID3D11Texture2D*)f.depth, {.5f, 0, 0, 0});
    clear(device, context, (ID3D11Texture2D*)f.motion, {0, 0, 0, 0});
    auto callback = (void(__stdcall*)(int, void*))Fsr4_GetRenderEvent();
    unsigned short previous[4]{};
    for (uint32_t i = 0; i < 6; i++)
    {
        if (i == 3)
        {
            callback(2, nullptr);
            callback(3, nullptr);
            printf("Released context and requested retry\n");
        }
        f.frame = i;
        f.reset = (i == 0 || i == 3);
        clear(device, context, (ID3D11Texture2D*)f.color, {i < 3 ? .25f : .75f, .5f, .25f, 1});
        callback(1, Fsr4_Queue(&f));
        Status s{};
        char text[512];
        Fsr4_GetStatus(&s, text, sizeof(text));
        printf("Frame %u: %ux%u count=%u time=%.2fms, %s\n", i, s.width, s.height, s.frames, s.gpuMilliseconds, text);
        if (s.failed || s.frames != i + 1)
            return 2;
        D3D11_TEXTURE2D_DESC desc{};
        ((ID3D11Texture2D*)f.output)->GetDesc(&desc);
        desc.Usage = D3D11_USAGE_STAGING;
        desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        desc.BindFlags = 0;
        ID3D11Texture2D* read = nullptr;
        if (!ok(device->CreateTexture2D(&desc, nullptr, &read), "readback"))
            return 3;
        context->CopyResource(read, (ID3D11Texture2D*)f.output);
        D3D11_MAPPED_SUBRESOURCE map{};
        if (!ok(context->Map(read, 0, D3D11_MAP_READ, 0, &map), "map"))
            return 3;
        auto pixel = (unsigned short*)((char*)map.pData + (h / 2) * map.RowPitch) + (w / 2) * 4;
        printf("Output centre RGBA16F: %04x %04x %04x %04x\n", pixel[0], pixel[1], pixel[2], pixel[3]);
        bool valid =
            pixel[0] > 0 && pixel[0] < 0x7c00 && pixel[1] > 0 && pixel[1] < 0x7c00 && pixel[2] > 0 && pixel[2] < 0x7c00;
        if (i == 2)
            for (int j = 0; j < 4; j++)
                previous[j] = pixel[j];
        if (i == 5)
            valid = valid && pixel[0] > previous[0];
        context->Unmap(read, 0);
        read->Release();
        if (!valid)
        {
            printf("FAIL invalid or stale output\n");
            return 4;
        }
    }
    Fsr4_Shutdown();
    printf("PASS: six FSR 4.1.1 dispatches, reset and changed output through DX11/DX12 bridge\n");
    return 0;
}
