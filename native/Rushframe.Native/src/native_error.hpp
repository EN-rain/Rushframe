#pragma once

#include "rushframe_native.h"

#include <string>

inline thread_local std::string rf_last_error;

inline rf_result rf_fail(rf_result code, const char* message) noexcept {
    rf_last_error = message;
    return code;
}

inline void rf_clear_error() noexcept {
    rf_last_error.clear();
}
