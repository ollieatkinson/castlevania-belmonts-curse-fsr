// GPU regression checks for the exact DXBC embedded in the prototype.
#define COBJMACROS
#include <windows.h>
#include <d3d11.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#define OK(call) do { HRESULT hr=(call); if(FAILED(hr)){fprintf(stderr,"line %d: %08lx\n",__LINE__,hr);return 2;} }while(0)
static float current[8*8*4], previous[8*8*4], motion[8*8*4];
static ID3D11Device *device;
static ID3D11DeviceContext *ctx;
static int run_case(const char *name, ID3D11ComputeShader *shader, int reset, int expected) {
    ID3D11Texture2D *inputs[3], *output, *staging;
    ID3D11ShaderResourceView *views[3]; ID3D11UnorderedAccessView *uav;
    D3D11_TEXTURE2D_DESC desc={0}; desc.Width=desc.Height=8; desc.MipLevels=desc.ArraySize=1;
    desc.Format=DXGI_FORMAT_R32G32B32A32_FLOAT; desc.SampleDesc.Count=1;
    desc.Usage=D3D11_USAGE_IMMUTABLE; desc.BindFlags=D3D11_BIND_SHADER_RESOURCE;
    float *sources[]={current,previous,motion};
    for(int i=0;i<3;i++) {
        D3D11_SUBRESOURCE_DATA data={sources[i],8*4*sizeof(float),0};
        OK(ID3D11Device_CreateTexture2D(device,&desc,&data,&inputs[i]));
        OK(ID3D11Device_CreateShaderResourceView(device,(ID3D11Resource*)inputs[i],NULL,&views[i]));
    }
    desc.Format=DXGI_FORMAT_R8_UNORM; desc.Usage=D3D11_USAGE_DEFAULT; desc.BindFlags=D3D11_BIND_UNORDERED_ACCESS;
    OK(ID3D11Device_CreateTexture2D(device,&desc,NULL,&output));
    OK(ID3D11Device_CreateUnorderedAccessView(device,(ID3D11Resource*)output,NULL,&uav));
    desc.Usage=D3D11_USAGE_STAGING; desc.BindFlags=0; desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    OK(ID3D11Device_CreateTexture2D(device,&desc,NULL,&staging));
    float constants[]={.125f,.125f,.04f,8,0,0,(float)reset,0};
    D3D11_BUFFER_DESC bd={32,D3D11_USAGE_IMMUTABLE,D3D11_BIND_CONSTANT_BUFFER,0,0,0};
    D3D11_SUBRESOURCE_DATA data={constants,0,0}; ID3D11Buffer *buffer;
    OK(ID3D11Device_CreateBuffer(device,&bd,&data,&buffer));
    D3D11_SAMPLER_DESC sd={0}; sd.Filter=D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    sd.AddressU=sd.AddressV=sd.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP; sd.MaxLOD=D3D11_FLOAT32_MAX;
    ID3D11SamplerState *sampler; OK(ID3D11Device_CreateSamplerState(device,&sd,&sampler));
    ID3D11DeviceContext_CSSetShader(ctx,shader,NULL,0);
    ID3D11DeviceContext_CSSetShaderResources(ctx,0,3,views);
    ID3D11DeviceContext_CSSetUnorderedAccessViews(ctx,0,1,&uav,NULL);
    ID3D11DeviceContext_CSSetConstantBuffers(ctx,0,1,&buffer);
    ID3D11DeviceContext_CSSetSamplers(ctx,0,1,&sampler);
    ID3D11DeviceContext_Dispatch(ctx,1,1,1);
    ID3D11DeviceContext_CopyResource(ctx,(ID3D11Resource*)staging,(ID3D11Resource*)output);
    D3D11_MAPPED_SUBRESOURCE mapped; OK(ID3D11DeviceContext_Map(ctx,(ID3D11Resource*)staging,0,D3D11_MAP_READ,0,&mapped));
    int value=((unsigned char*)mapped.pData)[3*mapped.RowPitch+3];
    ID3D11DeviceContext_Unmap(ctx,(ID3D11Resource*)staging,0);
    ID3D11DeviceContext_ClearState(ctx);
    for(int i=0;i<3;i++) {ID3D11ShaderResourceView_Release(views[i]);ID3D11Texture2D_Release(inputs[i]);}
    ID3D11UnorderedAccessView_Release(uav); ID3D11Texture2D_Release(output); ID3D11Texture2D_Release(staging);
    ID3D11Buffer_Release(buffer);ID3D11SamplerState_Release(sampler);
    printf("%s: mask=%d, expected=%d\n",name,value,expected);
    return value==expected?0:1;
}
int main(int argc,char**argv) {
    if(argc!=2)return 2;
    FILE *f=fopen(argv[1],"rb");if(!f)return 2;
    fseek(f,0,SEEK_END);long n=ftell(f);rewind(f);void*code=malloc(n);fread(code,1,n,f);fclose(f);
    D3D_FEATURE_LEVEL level;
    OK(D3D11CreateDevice(NULL,D3D_DRIVER_TYPE_HARDWARE,NULL,0,NULL,0,D3D11_SDK_VERSION,&device,&level,&ctx));
    ID3D11ComputeShader *shader; OK(ID3D11Device_CreateComputeShader(device,code,n,NULL,&shader));free(code);
    int failures=0;
    failures+=run_case("Static image preserves history",shader,0,0);
    previous[(3*8+3)*4]=previous[(3*8+3)*4+1]=previous[(3*8+3)*4+2]=1;
    failures+=run_case("Disoccluded bright detail rejects history",shader,0,255);
    failures+=run_case("History reset skips uninitialised input",shader,1,0);
    memset(current,0,sizeof(current));memset(previous,0,sizeof(previous));
    current[(3*8+3)*4]=1;previous[(3*8+2)*4]=1;motion[(3*8+3)*4]=.125f;
    failures+=run_case("Correct forward motion preserves matching history",shader,0,0);
    memset(current,0,sizeof(current));
    failures+=run_case("Forward motion locates stale history at the previous position",shader,0,255);
    memset(motion,0,sizeof(motion));
    failures+=run_case("History outside the reprojection footprint is ignored",shader,0,0);
    ID3D11ComputeShader_Release(shader);ID3D11DeviceContext_Release(ctx);ID3D11Device_Release(device);
    return failures?1:0;
}
