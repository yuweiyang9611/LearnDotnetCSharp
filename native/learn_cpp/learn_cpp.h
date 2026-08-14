#ifndef LEARN_CPP_H
#define LEARN_CPP_H

#include <stdint.h>

#if defined(_WIN32)
#define LEARN_CPP_API __declspec(dllexport)
#define LEARN_CPP_CALL __cdecl
#else
#define LEARN_CPP_API __attribute__((visibility("default")))
#define LEARN_CPP_CALL
#endif

#if defined(__cplusplus)
#define LEARN_CPP_NOEXCEPT noexcept
extern "C" {
#else
#define LEARN_CPP_NOEXCEPT
#endif

typedef struct learn_cpp_counter learn_cpp_counter;

enum learn_cpp_status
{
    LEARN_CPP_OK = 0,
    LEARN_CPP_INVALID_ARGUMENT = 1,
    LEARN_CPP_OUT_OF_RANGE = 2,
    LEARN_CPP_FAILURE = 3,
    LEARN_CPP_BUFFER_TOO_SMALL = 4,
};

LEARN_CPP_API int32_t LEARN_CPP_CALL learn_cpp_counter_create(
    int32_t initial_value,
    learn_cpp_counter** result) LEARN_CPP_NOEXCEPT;

LEARN_CPP_API int32_t LEARN_CPP_CALL learn_cpp_counter_add(
    learn_cpp_counter* counter,
    int32_t delta,
    int32_t* value) LEARN_CPP_NOEXCEPT;

LEARN_CPP_API int32_t LEARN_CPP_CALL learn_cpp_counter_copy_history(
    const learn_cpp_counter* counter,
    int32_t* output,
    int32_t capacity,
    int32_t* written) LEARN_CPP_NOEXCEPT;

LEARN_CPP_API int32_t LEARN_CPP_CALL learn_cpp_counter_describe_utf8(
    const learn_cpp_counter* counter,
    char* output,
    int32_t capacity,
    int32_t* written) LEARN_CPP_NOEXCEPT;

LEARN_CPP_API void LEARN_CPP_CALL learn_cpp_counter_destroy(
    learn_cpp_counter* counter) LEARN_CPP_NOEXCEPT;

#if defined(__cplusplus)
}
#endif

#endif
