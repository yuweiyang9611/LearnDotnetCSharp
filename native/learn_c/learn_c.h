#ifndef LEARN_C_H
#define LEARN_C_H

#include <stdint.h>

#if defined(_WIN32)
#define LEARN_C_API __declspec(dllexport)
#define LEARN_C_CALL __cdecl
#else
#define LEARN_C_API __attribute__((visibility("default")))
#define LEARN_C_CALL
#endif

#if defined(__cplusplus)
extern "C" {
#endif

enum learn_c_status
{
    LEARN_C_OK = 0,
    LEARN_C_INVALID_ARGUMENT = 1,
};

typedef struct learn_c_stats
{
    int32_t count;
    double sum;
    double mean;
} learn_c_stats;

typedef int32_t(LEARN_C_CALL* learn_c_transform)(int32_t value, void* context);

LEARN_C_API int32_t LEARN_C_CALL learn_c_analyze_f64(
    const double* values,
    int32_t length,
    learn_c_stats* result);

LEARN_C_API int32_t LEARN_C_CALL learn_c_transform_sum_i32(
    const int32_t* values,
    int32_t length,
    learn_c_transform transform,
    void* context,
    int64_t* result);

#if defined(__cplusplus)
}
#endif

#endif
