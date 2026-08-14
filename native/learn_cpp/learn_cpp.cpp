#include "learn_cpp.h"

#include <cstring>
#include <limits>
#include <new>
#include <string>
#include <vector>

struct learn_cpp_counter
{
    explicit learn_cpp_counter(const int32_t initial_value)
        : value(initial_value), history{initial_value}
    {
    }

    int32_t value;
    std::vector<int32_t> history;
};

int32_t LEARN_CPP_CALL learn_cpp_counter_create(
    const int32_t initial_value,
    learn_cpp_counter** result) noexcept
{
    if (result == nullptr)
    {
        return LEARN_CPP_INVALID_ARGUMENT;
    }

    *result = nullptr;
    try
    {
        *result = new learn_cpp_counter(initial_value);
        return LEARN_CPP_OK;
    }
    catch (...)
    {
        return LEARN_CPP_FAILURE;
    }
}

int32_t LEARN_CPP_CALL learn_cpp_counter_add(
    learn_cpp_counter* counter,
    const int32_t delta,
    int32_t* value) noexcept
{
    if (counter == nullptr || value == nullptr)
    {
        return LEARN_CPP_INVALID_ARGUMENT;
    }

    const int64_t candidate = static_cast<int64_t>(counter->value) + delta;
    if (candidate < std::numeric_limits<int32_t>::min() ||
        candidate > std::numeric_limits<int32_t>::max())
    {
        return LEARN_CPP_OUT_OF_RANGE;
    }

    try
    {
        counter->history.push_back(static_cast<int32_t>(candidate));
        counter->value = static_cast<int32_t>(candidate);
        *value = counter->value;
        return LEARN_CPP_OK;
    }
    catch (...)
    {
        return LEARN_CPP_FAILURE;
    }
}

int32_t LEARN_CPP_CALL learn_cpp_counter_copy_history(
    const learn_cpp_counter* counter,
    int32_t* output,
    const int32_t capacity,
    int32_t* written) noexcept
{
    if (counter == nullptr || output == nullptr || capacity < 0 || written == nullptr)
    {
        return LEARN_CPP_INVALID_ARGUMENT;
    }

    const auto required = static_cast<int32_t>(counter->history.size());
    *written = required;
    if (capacity < required)
    {
        return LEARN_CPP_BUFFER_TOO_SMALL;
    }

    std::memcpy(output, counter->history.data(), counter->history.size() * sizeof(int32_t));
    return LEARN_CPP_OK;
}

int32_t LEARN_CPP_CALL learn_cpp_counter_describe_utf8(
    const learn_cpp_counter* counter,
    char* output,
    const int32_t capacity,
    int32_t* written) noexcept
{
    if (counter == nullptr || output == nullptr || capacity <= 0 || written == nullptr)
    {
        return LEARN_CPP_INVALID_ARGUMENT;
    }

    try
    {
        const std::string description =
            "Counter(value=" + std::to_string(counter->value) +
            ", history=" + std::to_string(counter->history.size()) + ")";
        const auto required = static_cast<int32_t>(description.size());
        *written = required;
        if (capacity <= required)
        {
            return LEARN_CPP_BUFFER_TOO_SMALL;
        }

        std::memcpy(output, description.data(), description.size());
        output[description.size()] = '\0';
        return LEARN_CPP_OK;
    }
    catch (...)
    {
        return LEARN_CPP_FAILURE;
    }
}

void LEARN_CPP_CALL learn_cpp_counter_destroy(learn_cpp_counter* counter) noexcept
{
    delete counter;
}
