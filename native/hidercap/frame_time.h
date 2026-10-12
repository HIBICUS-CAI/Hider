/* Frame times of libhidercap, kept apart from PipeWire so that the unit test (test_frame_time.c) can run them. */
#ifndef HIDERCAP_FRAME_TIME_H
#define HIDERCAP_FRAME_TIME_H

#include <stdint.h>

/* Without the compositor's frame time (SPA_META_Header), a frame counts from its receive time minus this margin
 * (two frames at 60 Hz): it may have been rendered that much earlier (design §5.3, "固定待ち"). */
#define RECEIVE_MARGIN_NS 33333333LL

/* A frame time from SPA_META_Header is trusted only when it lies in the last 500 ms by CLOCK_MONOTONIC. */
#define PTS_MAX_AGE_NS 500000000LL

typedef struct {
    int64_t pts_ns;   /* CLOCK_MONOTONIC ns: the header's PTS, or the receive time */
    int from_meta;    /* pts_ns came from the header */
} frame_time;

/* The time of a frame received at _now_ns. Mutter stamps frames with CLOCK_MONOTONIC; a producer with another clock
 * (GStreamer's running time, for one) gives values far from now, which would make every frame look too old or too new:
 * the receive time is used then, and for a missing, zero or corrupted header. */
static inline frame_time frame_time_pick(int _has_header, int64_t _header_pts, int _corrupted, int64_t _now_ns)
{
    frame_time t = { _now_ns, 0 };
    if (_has_header && _header_pts > 0 && !_corrupted && _header_pts <= _now_ns && _now_ns - _header_pts < PTS_MAX_AGE_NS) {
        t.pts_ns = _header_pts;
        t.from_meta = 1;
    }
    return t;
}

/* The time the frame was shown, as far as it is known. */
static inline int64_t frame_shown_at(frame_time _t)
{
    return _t.from_meta ? _t.pts_ns : _t.pts_ns - RECEIVE_MARGIN_NS;
}

/* Frames shown before not_before still show the old window contents (design §5.3, CT-90): a grab waits for a newer one. */
static inline int frame_is_new_enough(frame_time _t, int64_t _not_before_ns)
{
    return frame_shown_at(_t) >= _not_before_ns;
}

#endif
