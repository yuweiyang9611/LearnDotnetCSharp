#include "learn_c.h"

int32_t LEARN_C_CALL learn_c_analyze_f64(
    const double* values,
    int32_t length,
    learn_c_stats* result)
{
    int32_t index;
    double sum = 0.0;

    if (values == 0 || length <= 0 || result == 0)
    {
        return LEARN_C_INVALID_ARGUMENT;
    }

    for (index = 0; index < length; ++index)
    {
        sum += values[index];
    }

    result->count = length;
    result->sum = sum;
    result->mean = sum / (double)length;
    return LEARN_C_OK;
}

int32_t LEARN_C_CALL learn_c_transform_sum_i32(
    const int32_t* values,
    int32_t length,
    learn_c_transform transform,
    void* context,
    int64_t* result)
{
    int32_t index;
    int64_t sum = 0;

    if (values == 0 || length < 0 || transform == 0 || result == 0)
    {
        return LEARN_C_INVALID_ARGUMENT;
    }

    for (index = 0; index < length; ++index)
    {
        sum += transform(values[index], context);
    }

    *result = sum;
    return LEARN_C_OK;
}
