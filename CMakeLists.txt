cmake_minimum_required(VERSION 3.20)
project(BelmontFsr4Bridge LANGUAGES CXX)

if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "The bridge targets Windows x64. Use cmake/zig-windows.cmake when cross-compiling.")
endif()

set(FSR4_RUNTIME "${CMAKE_CURRENT_SOURCE_DIR}/vendor/fsr4/runtime/amd_fidelityfx_upscaler_dx12.dll")
file(SHA256 "${FSR4_RUNTIME}" FSR4_RUNTIME_SHA256)
if(NOT FSR4_RUNTIME_SHA256 STREQUAL "d0dcccc74a43c44ba435b7a369b456e0970d8a4464e4bd683119b374f2c9fb46")
    message(FATAL_ERROR "The bundled AMD FSR runtime checksum does not match SDK v2.3.0.")
endif()

add_library(BelmontFsr4Bridge SHARED native/Bridge.cpp)
target_compile_features(BelmontFsr4Bridge PRIVATE cxx_std_17)
target_include_directories(BelmontFsr4Bridge PRIVATE vendor/fsr4/api/include vendor/fsr4/upscalers/include)
target_link_libraries(BelmontFsr4Bridge PRIVATE d3d11 d3d12 dxgi dxguid)
target_compile_options(BelmontFsr4Bridge PRIVATE "$<$<CXX_COMPILER_ID:Clang>:-Wno-nullability-completeness>")
set_target_properties(BelmontFsr4Bridge PROPERTIES PREFIX "")

install(TARGETS BelmontFsr4Bridge RUNTIME DESTINATION .)
install(FILES "${FSR4_RUNTIME}" DESTINATION .)
install(FILES vendor/fsr4/LICENSE.md vendor/fsr4/PROVENANCE.md DESTINATION licenses/FSR4)
install(FILES LICENSE DESTINATION licenses/BelmontFsr4Bridge)
