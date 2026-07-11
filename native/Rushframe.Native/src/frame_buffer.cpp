#include "rushframe_native.h"
#include "native_error.hpp"

#include <algorithm>
#include <cstring>
#include <limits>
#include <memory>
#include <new>
#include <vector>

namespace {
struct frame_buffer {
    std::int32_t width;
    std::int32_t height;
    std::int32_t stride;
    std::vector<std::uint8_t> bytes;
};

bool calculate_layout(std::int32_t width, std::int32_t height, std::int32_t& stride, std::size_t& size) noexcept {
    if (width <= 0 || height <= 0 || width > std::numeric_limits<std::int32_t>::max() / 4) return false;
    stride = width * 4;
    const auto row = static_cast<std::size_t>(stride);
    const auto rows = static_cast<std::size_t>(height);
    if (rows > std::numeric_limits<std::size_t>::max() / row) return false;
    size = row * rows;
    return true;
}
}

extern "C" rf_result RF_CALL rf_create_frame_buffer(std::int32_t width, std::int32_t height, void** output_buffer) noexcept {
    if (output_buffer == nullptr) return rf_fail(RF_INVALID_ARGUMENT, "output_buffer is null");
    *output_buffer = nullptr;
    std::int32_t stride = 0;
    std::size_t size = 0;
    if (!calculate_layout(width, height, stride, size)) return rf_fail(RF_INVALID_ARGUMENT, "invalid frame dimensions");
    try {
        auto value = std::make_unique<frame_buffer>();
        value->width = width;
        value->height = height;
        value->stride = stride;
        value->bytes.resize(size);
        *output_buffer = value.release();
        rf_clear_error();
        return RF_OK;
    } catch (const std::bad_alloc&) {
        return rf_fail(RF_ALLOCATION_FAILED, "frame buffer allocation failed");
    } catch (...) {
        return rf_fail(RF_INTERNAL_ERROR, "unexpected native error");
    }
}

extern "C" void RF_CALL rf_destroy_frame_buffer(void* buffer) noexcept {
    delete static_cast<frame_buffer*>(buffer);
}

extern "C" rf_result RF_CALL rf_get_frame_buffer_info(void* buffer, std::uint8_t** data, std::int32_t* stride, std::size_t* size) noexcept {
    if (buffer == nullptr || data == nullptr || stride == nullptr || size == nullptr)
        return rf_fail(RF_INVALID_ARGUMENT, "buffer info argument is null");
    auto* value = static_cast<frame_buffer*>(buffer);
    *data = value->bytes.data();
    *stride = value->stride;
    *size = value->bytes.size();
    rf_clear_error();
    return RF_OK;
}

extern "C" std::size_t RF_CALL rf_get_last_error(char* destination, std::size_t destination_size) noexcept {
    const auto required = rf_last_error.size() + 1;
    if (destination != nullptr && destination_size > 0) {
        const auto count = std::min(rf_last_error.size(), destination_size - 1);
        std::memcpy(destination, rf_last_error.data(), count);
        destination[count] = '\0';
    }
    return required;
}
