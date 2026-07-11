cmake_minimum_required(VERSION 3.24)
project(RushframeNative VERSION 0.1.0 LANGUAGES CXX)

add_library(Rushframe.Native SHARED
    src/frame_buffer.cpp
    src/pixel_conversion.cpp)

target_include_directories(Rushframe.Native PUBLIC include)
target_compile_features(Rushframe.Native PRIVATE cxx_std_20)
target_compile_definitions(Rushframe.Native PRIVATE RUSHFRAME_NATIVE_EXPORTS)

if(MSVC)
    target_compile_options(Rushframe.Native PRIVATE /W4 /permissive- /EHsc)
else()
    target_compile_options(Rushframe.Native PRIVATE -Wall -Wextra -Wpedantic)
endif()

set_target_properties(Rushframe.Native PROPERTIES
    OUTPUT_NAME "Rushframe.Native"
    WINDOWS_EXPORT_ALL_SYMBOLS OFF)
