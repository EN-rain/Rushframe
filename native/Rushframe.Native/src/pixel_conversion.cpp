#include "rushframe_native.h"
#include "native_error.hpp"

#include <algorithm>
#include <cmath>
#include <cstdint>
extern "C" rf_result RF_CALL rf_scale_bgra(
    const std::uint8_t* source,
    std::int32_t source_width,
    std::int32_t source_height,
    std::int32_t source_stride,
    std::uint8_t* destination,
    std::int32_t destination_width,
    std::int32_t destination_height,
    std::int32_t destination_stride) noexcept {
    if (source == nullptr || destination == nullptr)
        return rf_fail(RF_INVALID_ARGUMENT, "source or destination is null");
    if (source_width <= 0 || source_height <= 0 || destination_width <= 0 || destination_height <= 0)
        return rf_fail(RF_INVALID_ARGUMENT, "invalid frame dimensions");
    if (source_stride < source_width * 4 || destination_stride < destination_width * 4)
        return rf_fail(RF_BUFFER_TOO_SMALL, "frame stride is too small");

    try {
        const double x_scale = static_cast<double>(source_width) / destination_width;
        const double y_scale = static_cast<double>(source_height) / destination_height;

        for (std::int32_t y = 0; y < destination_height; ++y) {
            const double source_y = (y + 0.5) * y_scale - 0.5;
            const auto y0 = std::clamp(static_cast<std::int32_t>(std::floor(source_y)), 0, source_height - 1);
            const auto y1 = std::min(y0 + 1, source_height - 1);
            const double fy = std::clamp(source_y - std::floor(source_y), 0.0, 1.0);
            auto* output_row = destination + static_cast<std::size_t>(y) * destination_stride;

            for (std::int32_t x = 0; x < destination_width; ++x) {
                const double source_x = (x + 0.5) * x_scale - 0.5;
                const auto x0 = std::clamp(static_cast<std::int32_t>(std::floor(source_x)), 0, source_width - 1);
                const auto x1 = std::min(x0 + 1, source_width - 1);
                const double fx = std::clamp(source_x - std::floor(source_x), 0.0, 1.0);

                const auto* p00 = source + static_cast<std::size_t>(y0) * source_stride + x0 * 4;
                const auto* p10 = source + static_cast<std::size_t>(y0) * source_stride + x1 * 4;
                const auto* p01 = source + static_cast<std::size_t>(y1) * source_stride + x0 * 4;
                const auto* p11 = source + static_cast<std::size_t>(y1) * source_stride + x1 * 4;
                auto* output = output_row + x * 4;

                for (int channel = 0; channel < 4; ++channel) {
                    const double top = p00[channel] + (p10[channel] - p00[channel]) * fx;
                    const double bottom = p01[channel] + (p11[channel] - p01[channel]) * fx;
                    output[channel] = static_cast<std::uint8_t>(std::clamp(std::lround(top + (bottom - top) * fy), 0L, 255L));
                }
            }
        }
        rf_clear_error();
        return RF_OK;
    } catch (...) {
        return rf_fail(RF_INTERNAL_ERROR, "unexpected native scaling error");
    }
}
