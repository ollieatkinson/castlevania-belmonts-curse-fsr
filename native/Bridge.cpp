#include <windows.h>
#include <d3d11_4.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#include <cstdio>
#include <cstring>
#include <initializer_list>
#include "Bridge.h"
#include "ffx_api.h"
#include "dx12/ffx_api_dx12.h"
#include "ffx_upscale.h"

template <class T> struct Com
{
    T* p = nullptr;

    ~Com()
    {
        reset();
    }

    void reset()
    {
        if (p)
        {
            p->Release();
            p = nullptr;
        }
    }

    T** put()
    {
        reset();
        return &p;
    }

    T* operator->() const
    {
        return p;
    }

    operator T*() const
    {
        return p;
    }

    Com() = default;
    Com(const Com&) = delete;
    Com& operator=(const Com&) = delete;
};

struct Failure
{
    char text[512];
};

static void check(HRESULT code, const char* stage)
{
    if (FAILED(code))
    {
        Failure f{};
        snprintf(f.text, sizeof(f.text), "%s failed: 0x%08lx", stage, (unsigned long)code);
        throw f;
    }
}

static void api(ffxReturnCode_t code, const char* stage)
{
    if (code)
    {
        Failure f{};
        snprintf(f.text, sizeof(f.text), "%s failed: FFX code %u", stage, code);
        throw f;
    }
}

#define HR(x) check((x), #x)
static SRWLOCK statusLock = SRWLOCK_INIT;
static Status status{};
static char message[512] = "FSR 4.1.1 bridge not loaded";
static HMODULE sdk = nullptr;
static PfnFfxCreateContext createContext = nullptr;
static PfnFfxDestroyContext destroyContext = nullptr;
static PfnFfxQuery query = nullptr;
static PfnFfxDispatch dispatch = nullptr;
static wchar_t logPath[MAX_PATH]{};

static void report(const char* text, bool fail = false)
{
    AcquireSRWLockExclusive(&statusLock);
    if (fail)
        status.failed = 1;
    snprintf(message, sizeof(message), "%s", text);
    ReleaseSRWLockExclusive(&statusLock);
    if (FILE* file = _wfopen(logPath, L"a"))
    {
        fprintf(file, "%s\n", text);
        fclose(file);
    }
}

static void sdkMessage(uint32_t, const wchar_t* text)
{
    char buffer[512]{};
    WideCharToMultiByte(CP_UTF8, 0, text, -1, buffer, sizeof(buffer) - 1, nullptr, nullptr);
    report(buffer);
}

struct SharedTexture
{
    Com<ID3D11Texture2D> dx11;
    Com<ID3D12Resource> dx12;

    void create(ID3D11Device* d11, ID3D12Device* d12, uint32_t w, uint32_t h, DXGI_FORMAT format)
    {
        D3D11_TEXTURE2D_DESC desc{};
        desc.Width = w;
        desc.Height = h;
        desc.MipLevels = desc.ArraySize = 1;
        desc.Format = format;
        desc.SampleDesc.Count = 1;
        desc.Usage = D3D11_USAGE_DEFAULT;
        desc.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_UNORDERED_ACCESS;
        desc.MiscFlags = D3D11_RESOURCE_MISC_SHARED | D3D11_RESOURCE_MISC_SHARED_NTHANDLE;
        HR(d11->CreateTexture2D(&desc, nullptr, dx11.put()));
        Com<IDXGIResource1> resource;
        HR(dx11->QueryInterface(IID_PPV_ARGS(resource.put())));
        HANDLE handle = nullptr;
        HR(resource->CreateSharedHandle(nullptr, DXGI_SHARED_RESOURCE_READ | DXGI_SHARED_RESOURCE_WRITE, nullptr,
                                        &handle));
        HRESULT result = d12->OpenSharedHandle(handle, IID_PPV_ARGS(dx12.put()));
        CloseHandle(handle);
        HR(result);
    }
};

struct Bridge
{
    Com<ID3D11Device> d11;
    Com<ID3D11DeviceContext4> c11;
    Com<ID3D12Device> d12;
    Com<ID3D12CommandQueue> queue;
    Com<ID3D12CommandAllocator> allocator;
    Com<ID3D12GraphicsCommandList> list;
    Com<ID3D12Fence> fence12;
    Com<ID3D11Fence> fence11;
    SharedTexture color, depth, motion, output;
    HANDLE event = nullptr;
    uint64_t value = 0, submitted = 0;
    ffxContext context = nullptr;
    // SDK requires context descriptions to stay alive until destruction.
    ffxOverrideVersion version{};
    ffxCreateBackendDX12Desc backend{};
    ffxCreateContextDescUpscale description{};
    uint32_t width = 0, height = 0;

    ~Bridge()
    {
        if (context)
            destroyContext(&context, nullptr);
        if (event)
            CloseHandle(event);
    }

    void init(ID3D11Texture2D* input, uint32_t w, uint32_t h)
    {
        width = w;
        height = h;
        input->GetDevice(d11.put());
        Com<ID3D11DeviceContext> original;
        d11->GetImmediateContext(original.put());
        HR(original->QueryInterface(IID_PPV_ARGS(c11.put())));
        Com<IDXGIDevice> dxgi;
        HR(d11->QueryInterface(IID_PPV_ARGS(dxgi.put())));
        Com<IDXGIAdapter> adapter;
        HR(dxgi->GetAdapter(adapter.put()));
        HR(D3D12CreateDevice(adapter, D3D_FEATURE_LEVEL_12_0, IID_PPV_ARGS(d12.put())));
        D3D12_COMMAND_QUEUE_DESC q{};
        q.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
        HR(d12->CreateCommandQueue(&q, IID_PPV_ARGS(queue.put())));
        HR(d12->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(allocator.put())));
        HR(d12->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, allocator, nullptr, IID_PPV_ARGS(list.put())));
        HR(list->Close());
        HR(d12->CreateFence(0, D3D12_FENCE_FLAG_SHARED, IID_PPV_ARGS(fence12.put())));
        HANDLE handle = nullptr;
        HR(d12->CreateSharedHandle(fence12, nullptr, GENERIC_ALL, nullptr, &handle));
        Com<ID3D11Device5> d115;
        HR(d11->QueryInterface(IID_PPV_ARGS(d115.put())));
        HRESULT result = d115->OpenSharedFence(handle, IID_PPV_ARGS(fence11.put()));
        CloseHandle(handle);
        HR(result);
        event = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        if (!event)
            HR(HRESULT_FROM_WIN32(GetLastError()));
        color.create(d11, d12, w, h, DXGI_FORMAT_R16G16B16A16_FLOAT);
        depth.create(d11, d12, w, h, DXGI_FORMAT_R32_FLOAT);
        motion.create(d11, d12, w, h, DXGI_FORMAT_R16G16_FLOAT);
        output.create(d11, d12, w, h, DXGI_FORMAT_R16G16B16A16_FLOAT);
        uint64_t count = 0;
        ffxQueryDescGetVersions versions{};
        versions.header.type = FFX_API_QUERY_DESC_TYPE_GET_VERSIONS;
        versions.createDescType = FFX_API_CREATE_CONTEXT_DESC_TYPE_UPSCALE;
        versions.device = d12;
        versions.outputCount = &count;
        api(query(nullptr, &versions.header), "Enumerate FSR providers");
        if (count > 32)
            throw Failure{"Too many FSR providers"};
        uint64_t ids[32]{};
        const char* names[32]{};
        versions.versionIds = ids;
        versions.versionNames = names;
        count = 32;
        api(query(nullptr, &versions.header), "Read FSR providers");
        for (uint64_t i = 0; i < count; i++)
            if (names[i] && strncmp(names[i], "4.1.1", 5) == 0)
                version.versionId = ids[i];
        if (!version.versionId)
            throw Failure{"FSR 4.1.1 provider unavailable; refusing FSR 3 fallback"};
        version.header.type = FFX_API_DESC_TYPE_OVERRIDE_VERSION;
        backend.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_BACKEND_DX12;
        backend.header.pNext = &version.header;
        backend.device = d12;
        description.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_UPSCALE;
        description.header.pNext = &backend.header;
        description.maxRenderSize = description.maxUpscaleSize = {w, h};
        description.flags = FFX_UPSCALE_ENABLE_DEPTH_INVERTED | FFX_UPSCALE_ENABLE_AUTO_EXPOSURE;
        description.fpMessage = sdkMessage;
        api(createContext(&context, &description.header, nullptr), "Create FSR 4.1.1 context");
        ffxQueryGetProviderVersion active{};
        active.header.type = FFX_API_QUERY_DESC_TYPE_GET_PROVIDER_VERSION;
        api(query(&context, &active.header), "Verify FSR provider");
        if (!active.versionName || strncmp(active.versionName, "4.1.1", 5) != 0)
            throw Failure{"SDK selected an unexpected provider"};
        char text[256];
        snprintf(text, sizeof(text), "FSR %s Native AA context: %ux%u, DX11/DX12 shared textures and shared fence",
                 active.versionName, w, h);
        report(text);
    }

    void run(const Frame& frame)
    {
        c11->CopyResource(color.dx11, (ID3D11Texture2D*)frame.color);
        c11->CopyResource(depth.dx11, (ID3D11Texture2D*)frame.depth);
        c11->CopyResource(motion.dx11, (ID3D11Texture2D*)frame.motion);
        const uint64_t inputReady = ++value, outputReady = ++value;
        HR(c11->Signal(fence11, inputReady));
        c11->Flush();
        HR(queue->Wait(fence12, inputReady));
        HR(allocator->Reset());
        HR(list->Reset(allocator, nullptr));
        ffxDispatchDescUpscale desc{};
        desc.header.type = FFX_API_DISPATCH_DESC_TYPE_UPSCALE;
        desc.commandList = list;
        desc.color = ffxApiGetResourceDX12(color.dx12, FFX_API_RESOURCE_STATE_COMMON);
        desc.depth = ffxApiGetResourceDX12(depth.dx12, FFX_API_RESOURCE_STATE_COMMON);
        desc.motionVectors = ffxApiGetResourceDX12(motion.dx12, FFX_API_RESOURCE_STATE_COMMON);
        desc.output = ffxApiGetResourceDX12(output.dx12, FFX_API_RESOURCE_STATE_COMMON);
        desc.jitterOffset = {frame.jitterX, frame.jitterY};
        desc.motionVectorScale = {-(float)width, -(float)height};
        desc.renderSize = desc.upscaleSize = {width, height};
        desc.enableSharpening = frame.sharpness > 0;
        desc.sharpness = frame.sharpness;
        desc.frameTimeDelta = frame.deltaMs;
        desc.preExposure = 1;
        desc.reset = frame.reset != 0;
        desc.cameraNear = frame.nearPlane;
        desc.cameraFar = frame.farPlane;
        desc.cameraFovAngleVertical = frame.fov;
        desc.viewSpaceToMetersFactor = 1;
        api(dispatch(&context, &desc.header), "Dispatch FSR 4.1.1");
        // FFX restores imported resources to their supplied COMMON states.
        HR(list->Close());
        ID3D12CommandList* lists[] = {list};
        submitted = outputReady;
        queue->ExecuteCommandLists(1, lists);
        HR(queue->Signal(fence12, outputReady));
        // Wait before reusing the allocator and copying the result into Unity.
        // A bounded CPU wait keeps this first implementation simple and deterministic.
        HR(fence12->SetEventOnCompletion(outputReady, event));
        if (WaitForSingleObject(event, 5000) != WAIT_OBJECT_0)
            throw Failure{"Timed out waiting for the FSR GPU fence"};
        c11->CopyResource((ID3D11Texture2D*)frame.output, output.dx11);
    }
};

static Bridge* bridge = nullptr;

static bool releaseBridge()
{
    if (bridge && bridge->fence12 && bridge->fence12->GetCompletedValue() < bridge->submitted)
    {
        report("FSR GPU work is still pending; restart the game before retrying", true);
        return false;
    }
    delete bridge;
    bridge = nullptr;
    return true;
}

static void retain(const Frame& f)
{
    for (void* p : {f.color, f.depth, f.motion, f.output})
        ((IUnknown*)p)->AddRef();
}

static void release(const Frame& f)
{
    for (void* p : {f.color, f.depth, f.motion, f.output})
        ((IUnknown*)p)->Release();
}

static void __stdcall renderEvent(int eventId, void* data)
{
    if (eventId == 2 || eventId == 3)
    {
        if (releaseBridge())
        {
            AcquireSRWLockExclusive(&statusLock);
            status.width = status.height = 0;
            if (eventId == 3)
                status.failed = 0;
            ReleaseSRWLockExclusive(&statusLock);
            report(eventId == 3 ? "FSR 4.1.1 bridge ready for retry" : "FSR 4.1.1 GPU context released");
        }
        return;
    }
    if (eventId != 1 || !data)
        return;
    Frame* frame = (Frame*)data;
    try
    {
        Status current{};
        Fsr4_GetStatus(&current, nullptr, 0);
        if (current.failed)
            throw Failure{"FSR bridge is stopped"};
        if (bridge && (bridge->width != frame->width || bridge->height != frame->height) && !releaseBridge())
            throw Failure{"Cannot resize while FSR GPU work is pending"};
        if (!bridge)
        {
            bridge = new Bridge();
            bridge->init((ID3D11Texture2D*)frame->color, frame->width, frame->height);
        }
        LARGE_INTEGER start, end, freq;
        QueryPerformanceFrequency(&freq);
        QueryPerformanceCounter(&start);
        bridge->run(*frame);
        QueryPerformanceCounter(&end);
        AcquireSRWLockExclusive(&statusLock);
        status.frames++;
        status.width = frame->width;
        status.height = frame->height;
        status.gpuMilliseconds = (end.QuadPart - start.QuadPart) * 1000.0 / freq.QuadPart;
        ReleaseSRWLockExclusive(&statusLock);
        if (status.frames == 1)
            report("FSR 4.1.1 Native AA: first DX12 dispatch completed and copied back to DX11");
    }
    catch (const Failure& f)
    {
        report(f.text, true);
        // Ensure the queued Unity blit still receives this frame's colour.
        Com<ID3D11Device> device;
        ((ID3D11Texture2D*)frame->color)->GetDevice(device.put());
        Com<ID3D11DeviceContext> context;
        device->GetImmediateContext(context.put());
        context->CopyResource((ID3D11Texture2D*)frame->output, (ID3D11Texture2D*)frame->color);
    }
    catch (...)
    {
        report("Unexpected native bridge exception", true);
    }
    release(*frame);
    delete frame;
}

EXPORT int Fsr4_Load(const wchar_t* directory)
{
    if (sdk)
        return 1;
    swprintf(logPath, MAX_PATH, L"%ls/fsr4-bridge.log", directory);
    wchar_t path[MAX_PATH];
    swprintf(path, MAX_PATH, L"%ls/amd_fidelityfx_upscaler_dx12.dll", directory);
    sdk = LoadLibraryExW(path, nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if (!sdk)
    {
        report("Could not load amd_fidelityfx_upscaler_dx12.dll", true);
        return 0;
    }
    createContext = (PfnFfxCreateContext)GetProcAddress(sdk, "ffxCreateContext");
    destroyContext = (PfnFfxDestroyContext)GetProcAddress(sdk, "ffxDestroyContext");
    query = (PfnFfxQuery)GetProcAddress(sdk, "ffxQuery");
    dispatch = (PfnFfxDispatch)GetProcAddress(sdk, "ffxDispatch");
    if (!createContext || !destroyContext || !query || !dispatch)
    {
        report("FSR SDK exports missing", true);
        return 0;
    }
    report("FSR 4.1.1 SDK loaded; awaiting the Unity render event");
    return 1;
}

EXPORT void* Fsr4_Queue(const Frame* frame)
{
    if (!frame || !frame->color || !frame->depth || !frame->motion || !frame->output || !frame->width || !frame->height)
        return nullptr;
    Frame* job = new Frame(*frame);
    retain(*job);
    return job;
}

EXPORT void* Fsr4_GetRenderEvent()
{
    return (void*)renderEvent;
}

EXPORT void Fsr4_GetStatus(Status* target, char* text, uint32_t capacity)
{
    AcquireSRWLockShared(&statusLock);
    if (target)
        *target = status;
    if (text && capacity)
        snprintf(text, capacity, "%s", message);
    ReleaseSRWLockShared(&statusLock);
}

EXPORT void Fsr4_Shutdown()
{
    // Must be called only after Unity's queued plugin events have completed.
    if (!releaseBridge())
        return;
    if (sdk)
    {
        FreeLibrary(sdk);
        sdk = nullptr;
    }
}
