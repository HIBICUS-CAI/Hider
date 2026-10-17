/* Unit test of hidercap.c's argument checks and error paths without a PipeWire daemon: the API before start and after
 * stop, a second start, a stream whose remote hangs up (stream error) or never answers (timeout). Needs libpipewire's
 * client modules (libpipewire-0.3-modules, pipewire-bin), not a running daemon. Run by "make test". */
#define _GNU_SOURCE
#include "hidercap.h"

#include <errno.h>
#include <fcntl.h>
#include <stdio.h>
#include <string.h>
#include <sys/socket.h>
#include <time.h>
#include <unistd.h>

static int failures;

#define CHECK(_cond) \
    do { \
        if (!(_cond)) { \
            fprintf(stderr, "%s:%d: CHECK(%s) failed\n", __FILE__, __LINE__, #_cond); \
            failures++; \
        } \
    } while (0)

static int64_t now_ms(void)
{
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (int64_t)ts.tv_sec * 1000 + ts.tv_nsec / 1000000;
}

static int is_closed(int _fd)
{
    return fcntl(_fd, F_GETFD) == -1 && errno == EBADF;
}

/* A frame description filled with garbage, so that "zeroed first" can be seen. */
static hidercap_frame dirty(void)
{
    hidercap_frame f;
    memset(&f, 0xAB, sizeof(f));
    return f;
}

static int all_zero(const hidercap_frame *_f)
{
    static const hidercap_frame zero;
    return memcmp(_f, &zero, sizeof(zero)) == 0;
}

/* Not started: every call is safe and says so. */
static void test_not_started(void)
{
    uint8_t dst[16];
    hidercap_frame f = dirty();
    CHECK(hidercap_grab_after(0, 0, 0, 1, dst, sizeof(dst), &f) == HIDERCAP_NOT_STARTED);
    CHECK(all_zero(&f));
    CHECK(hidercap_grab_after(0, 0, 0, 1, dst, sizeof(dst), NULL) == HIDERCAP_NOT_STARTED);
    CHECK(hidercap_grab_after(0, 0, 0, 1, NULL, 0, NULL) == HIDERCAP_NOT_STARTED);
    CHECK(hidercap_last_sequence() == 0);
    hidercap_stop();
    hidercap_stop();
    CHECK(hidercap_last_sequence() == 0);
}

/* The remote hangs up: the stream goes unconnected and a grab reports a stream error at once (not after its timeout). */
static void test_remote_hangs_up(void)
{
    int sv[2];
    CHECK(socketpair(AF_UNIX, SOCK_STREAM | SOCK_CLOEXEC, 0, sv) == 0);
    close(sv[1]);
    CHECK(hidercap_start(sv[0], 42) == HIDERCAP_OK);

    /* A grab without out is refused even while started. */
    uint8_t dst[16];
    CHECK(hidercap_grab_after(0, 100, 0, 1, dst, sizeof(dst), NULL) == HIDERCAP_NOT_STARTED);

    hidercap_frame f = dirty();
    int64_t t0 = now_ms();
    int r = hidercap_grab_after(0, 5000, 0, 1, dst, sizeof(dst), &f);
    CHECK(r == HIDERCAP_STREAM_ERROR);
    CHECK(now_ms() - t0 < 4000);
    CHECK(f.sequence == 0 && f.width == 0 && f.height == 0 && f.pts_ns == 0);
    /* The error stays until stop. */
    f = dirty();
    CHECK(hidercap_grab_after(0, 0, 0, 1, dst, sizeof(dst), &f) == HIDERCAP_STREAM_ERROR);
    CHECK(f.sequence == 0 && f.width == 0);
    CHECK(hidercap_last_sequence() == 0);
    hidercap_stop();

    /* After stop: not started again. */
    f = dirty();
    CHECK(hidercap_grab_after(0, 0, 0, 1, dst, sizeof(dst), &f) == HIDERCAP_NOT_STARTED);
    CHECK(all_zero(&f));
}

/* The remote never answers: no frame and no error, so a grab times out after about timeout_ms. A second start is refused
 * and closes the fd it was given (ownership passes also on failure). */
static void test_silent_remote_times_out(void)
{
    int sv[2];
    CHECK(socketpair(AF_UNIX, SOCK_STREAM | SOCK_CLOEXEC, 0, sv) == 0);
    CHECK(hidercap_start(sv[0], 42) == HIDERCAP_OK);

    int extra[2];
    CHECK(socketpair(AF_UNIX, SOCK_STREAM | SOCK_CLOEXEC, 0, extra) == 0);
    CHECK(hidercap_start(extra[0], 43) == HIDERCAP_ALREADY_STARTED);
    CHECK(is_closed(extra[0]));
    close(extra[1]);

    uint8_t dst[16];
    hidercap_frame f = dirty();
    int64_t t0 = now_ms();
    CHECK(hidercap_grab_after(0, 200, 0, 1, dst, sizeof(dst), &f) == HIDERCAP_TIMEOUT);
    int64_t waited = now_ms() - t0;
    CHECK(waited >= 150);
    CHECK(f.sequence == 0 && f.width == 0 && f.height == 0 && f.stride == 0 && f.pts_from_meta == 0);

    /* A zero and a negative timeout return at once: each call well under a second (a loaded runner may be slow, but
     * nowhere near that), measured on its own. */
    t0 = now_ms();
    CHECK(hidercap_grab_after(0, 0, 0, 1, dst, sizeof(dst), &f) == HIDERCAP_TIMEOUT);
    CHECK(now_ms() - t0 < 1000);
    t0 = now_ms();
    CHECK(hidercap_grab_after(0, -5, 0, 1, dst, sizeof(dst), &f) == HIDERCAP_TIMEOUT);
    CHECK(now_ms() - t0 < 1000);
    CHECK(hidercap_last_sequence() == 0);
    hidercap_stop();
    close(sv[1]);
}

/* A started helper can be started again after stop (pw_init / pw_deinit pair up). */
static void test_restart(void)
{
    for (int i = 0; i < 3; i++) {
        int sv[2];
        CHECK(socketpair(AF_UNIX, SOCK_STREAM | SOCK_CLOEXEC, 0, sv) == 0);
        CHECK(hidercap_start(sv[0], (uint32_t)i) == HIDERCAP_OK);
        hidercap_frame f = dirty();
        CHECK(hidercap_grab_after(0, 0, 0, 1, NULL, 0, &f) == HIDERCAP_TIMEOUT);
        hidercap_stop();
        close(sv[1]);
    }
}

/* An invalid fd: the connection fails and the start reports a stream error (and cleans up: a new start works). */
static void test_invalid_fd(void)
{
    CHECK(hidercap_start(-1, 42) == HIDERCAP_STREAM_ERROR);
    hidercap_frame f = dirty();
    CHECK(hidercap_grab_after(0, 0, 0, 1, NULL, 0, &f) == HIDERCAP_NOT_STARTED);
    CHECK(hidercap_last_sequence() == 0);

    int sv[2];
    CHECK(socketpair(AF_UNIX, SOCK_STREAM | SOCK_CLOEXEC, 0, sv) == 0);
    CHECK(hidercap_start(sv[0], 42) == HIDERCAP_OK);
    hidercap_stop();
    close(sv[1]);
}

int main(void)
{
    test_not_started();
    test_remote_hangs_up();
    test_silent_remote_times_out();
    test_restart();
    test_invalid_fd();
    if (failures == 0) {
        printf("test_hidercap: all checks passed\n");
    }
    return failures == 0 ? 0 : 1;
}
