// Minimal DX11 compiler driver. Run using Wine's d3dcompiler_47, no Unity Editor.
#define COBJMACROS
#include <windows.h>
#include <d3dcompiler.h>
#include <stdio.h>
#include <stdlib.h>
int main(int argc, char **argv) {
    if (argc != 3) return 2;
    FILE *f = fopen(argv[1], "rb"); if (!f) return 3;
    fseek(f, 0, SEEK_END); long n = ftell(f); rewind(f);
    char *source = malloc(n); fread(source, 1, n, f); fclose(f);
    HMODULE dll = LoadLibraryA("d3dcompiler_47.dll");
    typedef HRESULT (WINAPI *Compile)(LPCVOID, SIZE_T, LPCSTR, const D3D_SHADER_MACRO *, ID3DInclude *, LPCSTR, LPCSTR, UINT, UINT, ID3DBlob **, ID3DBlob **);
    Compile compile = (Compile)GetProcAddress(dll, "D3DCompile"); if (!compile) return 4;
    ID3DBlob *code = NULL, *errors = NULL;
    HRESULT hr = compile(source, n, argv[1], NULL, NULL, "CS", "cs_5_0", D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &code, &errors);
    if (errors) { fwrite(ID3D10Blob_GetBufferPointer(errors), 1, ID3D10Blob_GetBufferSize(errors), stderr); ID3D10Blob_Release(errors); }
    free(source); if (FAILED(hr)) return 5;
    f = fopen(argv[2], "wb"); if (!f) return 6;
    fwrite(ID3D10Blob_GetBufferPointer(code), 1, ID3D10Blob_GetBufferSize(code), f); fclose(f);
    ID3D10Blob_Release(code); return 0;
}
