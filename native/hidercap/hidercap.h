/* libhidercap: PipeWire ScreenCast helper used by PipeWireGrabber (design §5.3, Step 8; API of proposal P-2).
 *
 * Subscribes to the monitor stream handed out by org.freedesktop.portal.ScreenCast and keeps the newest buffer (no copy
 * per frame). hidercap_grab_after copies it, converted to premultiplied BGRA8888 (opaque), once its timestamp is late
 * enough. Timestamps are CLOCK_MONOTONIC nanoseconds, the clock .NET's Stopwatch uses on Linux. */
#ifndef HIDERCAP_H
#define HIDERCAP_H

#include <stdint.h>

#define HIDERCAP_API __attribute__((visibility("default")))

#define HIDERCAP_OK 0
#define HIDERCAP_BUFFER_TOO_SMALL 1
#define HIDERCAP_TIMEOUT -1
#define HIDERCAP_NOT_STARTED -2
#define HIDERCAP_STREAM_ERROR -3
#define HIDERCAP_ALREADY_STARTED -4

/* Without the compositor's frame time (SPA_META_Header), a frame counts from its receive time minus this margin
 * (two frames at 60 Hz): it may have been rendered that much earlier (design §5.3, "固定待ち"). */
#define HIDERCAP_RECEIVE_MARGIN_NS 33333333LL

/* A frame time from SPA_META_Header is trusted only when it lies in the last 500 ms by CLOCK_MONOTONIC. */
#define HIDERCAP_PTS_MAX_AGE_NS 500000000LL

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

/* Waits up to timeout_ms for a frame shown at or after not_before_ns and copies it into dst.
 * Returns HIDERCAP_OK, HIDERCAP_BUFFER_TOO_SMALL (out holds the size needed, nothing copied) or a negative error.
 * out->sequence is also filled on timeout with the last received frame's sequence. */
HIDERCAP_API int hidercap_grab_after(int64_t not_before_ns, int32_t timeout_ms, uint8_t *dst, int32_t dst_size, hidercap_frame *out);

/* Sequence number of the newest received frame (0 when none yet). */
HIDERCAP_API uint64_t hidercap_last_sequence(void);

/* Stops the stream and releases everything. Safe to call when not started. */
HIDERCAP_API void hidercap_stop(void);

#endif
