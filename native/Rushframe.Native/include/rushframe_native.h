#pragma once

#include <cstddef>
#include <cstdint>

#if defined(_WIN32)
#  if defined(RUSHFRAME_NATIVE_EXPORTS)
#    define RF_API __declspec(dllexport)
#  else
#    define RF_API __declspec(dllimport)
#  endif
#  define RF_CALL __cdecl
#else
#  define RF_API __attribute__((visibility("default")))
#  define RF_CALL
#endif

extern "C" {

enum rf_result : std::int32_t {
    RF_OK = 0,
    RF_INVALID_ARGUMENT = 1,
    RF_ALLOCATION_FAILED = 2,
    RF_BUFFER_TOO_SMALL = 3,
    RF_INTERNAL_ERROR = 4
};

RF_API rf_result RF_CALL rf_create_frame_buffer(
    std::int32_t width,
    std::int32_t height,
    void** output_buffer) noexcept;

RF_API void RF_CALL rf_destroy_frame_buffer(void* buffer) noexcept;

RF_API rf_result RF_CALL rf_get_frame_buffer_info(
    void* buffer,
    std::uint8_t** data,
    std::int32_t* stride,
    std::size_t* size) noexcept;

RF_API rf_result RF_CALL rf_scale_bgra(
    const std::uint8_t* source,
    std::int32_t source_width,
    std::int32_t source_height,
    std::int32_t source_stride,
    std::uint8_t* destination,
    std::int32_t destination_width,
    std::int32_t destination_height,
    std::int32_t destination_stride) noexcept;

RF_API std::size_t RF_CALL rf_get_last_error(
    char* destination,
    std::size_t destination_size) noexcept;

}
