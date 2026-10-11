/* libhidercap: PipeWire ScreenCast helper used by PipeWireGrabber (design §5.3, Step 8; API of proposal P-2).
 *
 * Subscribes to the monitor stream handed out by org.freedesktop.portal.ScreenCast and keeps the newest buffer (no copy
 * per frame). hidercap_grab_after copies the rows asked for, converted to premultiplied BGRA8888 (opaque), once the
 * frame's timestamp is late enough. Timestamps are CLOCK_MONOTONIC nanoseconds, the clock .NET's Stopwatch uses on Linux.
 *
 * Threading: the functions may be called from any thread but not concurrently with each other (PipeWireGrabber holds a
 * lock around every call). */
#ifndef HIDERCAP_H
#define HIDERCAP_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define HIDERCAP_API __attribute__((visibility("default")))

#define HIDERCAP_OK (0)
#define HIDERCAP_BUFFER_TOO_SMALL (1)
#define HIDERCAP_TIMEOUT (-1)
#define HIDERCAP_NOT_STARTED (-2)
#define HIDERCAP_STREAM_ERROR (-3)
#define HIDERCAP_ALREADY_STARTED (-4)

typedef struct hidercap_frame {
    int32_t width;
    int32_t height;
    int32_t stride;        /* bytes per row in the caller's buffer (width * 4) */
    int32_t pts_from_meta; /* 1: pts is the compositor's frame time; 0: receive time (less precise) */
    int64_t pts_ns;        /* CLOCK_MONOTONIC */
    uint64_t sequence;     /* frames received since start */
} hidercap_frame;

/* Connects to the PipeWire remote fd from OpenPipeWireRemote and starts receiving node_id. Takes ownership of fd. */
HIDERCAP_API int hidercap_start(int fd, uint32_t node_id);

/* Waits up to timeout_ms for a frame shown at or after not_before_ns and copies its rows [row0, row1) (clamped to the
 * frame) into dst, each at its own place in a whole-frame buffer (row y at y * width * 4); other rows are not written.
 * Returns HIDERCAP_OK, HIDERCAP_BUFFER_TOO_SMALL (out holds the size needed, nothing copied) or a negative error.
 * out is zeroed first; out->sequence is also filled on timeout with the last received frame's sequence. */
HIDERCAP_API int hidercap_grab_after(int64_t not_before_ns, int32_t timeout_ms, int32_t row0, int32_t row1, uint8_t *dst,
    int32_t dst_size, hidercap_frame *out);

/* Sequence number of the newest received frame (0 when none yet). */
HIDERCAP_API uint64_t hidercap_last_sequence(void);

/* Stops the stream and releases everything. Safe to call when not started. */
HIDERCAP_API void hidercap_stop(void);

#ifdef __cplusplus
}
#endif

#endif
