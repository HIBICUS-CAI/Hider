/* Unit test of frame_time.h (CT-90): which frames a grab takes. No PipeWire needed; run by "make test" (CI native job). */
#include "frame_time.h"

#include <stdio.h>

static int failures;

#define CHECK(_cond) \
    do { \
        if (!(_cond)) { \
            fprintf(stderr, "%s:%d: CHECK(%s) failed\n", __FILE__, __LINE__, #_cond); \
            failures++; \
        } \
    } while (0)

#define MS 1000000LL

int main(void)
{
    const int64_t now = 10000 * MS;

    /* A valid header PTS is the frame's time: frames shown before not_before are older, from it on new enough. */
    frame_time meta = frame_time_pick(1, now - 20 * MS, 0, now);
    CHECK(meta.from_meta && meta.pts_ns == now - 20 * MS);
    CHECK(frame_shown_at(meta) == now - 20 * MS);
    CHECK(!frame_is_new_enough(meta, now - 20 * MS + 1));
    CHECK(frame_is_new_enough(meta, now - 20 * MS));
    CHECK(frame_is_new_enough(meta, now - 21 * MS));

    /* Without a header the receive time counts, less the margin: a frame received just after not_before may have been
     * rendered before it (strip still opaque), one received a margin later cannot. */
    frame_time received = frame_time_pick(0, 0, 0, now);
    CHECK(!received.from_meta && received.pts_ns == now);
    CHECK(frame_shown_at(received) == now - RECEIVE_MARGIN_NS);
    CHECK(!frame_is_new_enough(received, now - 10 * MS));
    CHECK(frame_is_new_enough(received, now - RECEIVE_MARGIN_NS));
    CHECK(frame_is_new_enough(received, now - 40 * MS));

    /* Headers that cannot be trusted fall back to the receive time: zero, corrupted, in the future, or too old (another
     * clock than CLOCK_MONOTONIC). */
    CHECK(!frame_time_pick(1, 0, 0, now).from_meta);
    CHECK(!frame_time_pick(1, now - 20 * MS, 1, now).from_meta);
    CHECK(!frame_time_pick(1, now + 1, 0, now).from_meta);
    CHECK(!frame_time_pick(1, now - PTS_MAX_AGE_NS, 0, now).from_meta);
    CHECK(frame_time_pick(1, now - PTS_MAX_AGE_NS + 1, 0, now).from_meta);
    CHECK(frame_time_pick(1, now, 0, now).from_meta);

    /* The constants the grab relies on: two frames at 60 Hz, half a second. */
    CHECK(RECEIVE_MARGIN_NS == 33333333LL);
    CHECK(PTS_MAX_AGE_NS == 500 * MS);

    /* frame_time_pick, one condition at a time against a valid header (now - 20 ms, not corrupted, has a header). */
    {
        const int64_t pts = now - 20 * MS;
        frame_time t;

        /* No header: the header's value is ignored, even a valid one. */
        t = frame_time_pick(0, pts, 0, now);
        CHECK(!t.from_meta && t.pts_ns == now);
        /* A header with zero or a negative PTS. */
        t = frame_time_pick(1, 0, 0, now);
        CHECK(!t.from_meta && t.pts_ns == now);
        t = frame_time_pick(1, -1, 0, now);
        CHECK(!t.from_meta && t.pts_ns == now);
        t = frame_time_pick(1, INT64_MIN, 0, now);
        CHECK(!t.from_meta && t.pts_ns == now);
        /* PTS 1 ns is positive, but older than PTS_MAX_AGE_NS here. */
        t = frame_time_pick(1, 1, 0, now);
        CHECK(!t.from_meta && t.pts_ns == now);
        /* ...and trusted when now is early enough (the clock just started). */
        t = frame_time_pick(1, 1, 0, 2);
        CHECK(t.from_meta && t.pts_ns == 1);
        /* Any non-zero flag counts as true. */
        t = frame_time_pick(2, pts, 0, now);
        CHECK(t.from_meta && t.pts_ns == pts);
        t = frame_time_pick(1, pts, 7, now);
        CHECK(!t.from_meta && t.pts_ns == now);
        t = frame_time_pick(1, pts, -1, now);
        CHECK(!t.from_meta && t.pts_ns == now);
        /* In the future by 1 ns and by a lot (another clock running ahead). */
        t = frame_time_pick(1, now + 1, 0, now);
        CHECK(!t.from_meta && t.pts_ns == now);
        t = frame_time_pick(1, INT64_MAX, 0, now);
        CHECK(!t.from_meta && t.pts_ns == now);
        /* Age limits: exactly PTS_MAX_AGE_NS is too old, 1 ns less is fine, far older (GStreamer running time) too old. */
        t = frame_time_pick(1, now - PTS_MAX_AGE_NS, 0, now);
        CHECK(!t.from_meta && t.pts_ns == now);
        t = frame_time_pick(1, now - PTS_MAX_AGE_NS + 1, 0, now);
        CHECK(t.from_meta && t.pts_ns == now - PTS_MAX_AGE_NS + 1);
        t = frame_time_pick(1, 3 * MS, 0, now);
        CHECK(!t.from_meta && t.pts_ns == now);
        /* Every condition false at once still gives the receive time. */
        t = frame_time_pick(0, 0, 1, now);
        CHECK(!t.from_meta && t.pts_ns == now);
        /* now = 0 (no valid header can exist: it would have to be > 0 and <= 0). */
        t = frame_time_pick(1, 0, 0, 0);
        CHECK(!t.from_meta && t.pts_ns == 0);
    }

    /* frame_shown_at: both branches, set directly. */
    {
        frame_time t = { 5 * MS, 1 };
        CHECK(frame_shown_at(t) == 5 * MS);
        t.from_meta = 0;
        CHECK(frame_shown_at(t) == 5 * MS - RECEIVE_MARGIN_NS);
        /* Early receive times go below zero rather than wrap. */
        t.pts_ns = 0;
        CHECK(frame_shown_at(t) == -RECEIVE_MARGIN_NS);
        /* Any non-zero from_meta counts as from the header. */
        t.pts_ns = 5 * MS;
        t.from_meta = 2;
        CHECK(frame_shown_at(t) == 5 * MS);
    }

    /* frame_is_new_enough: the boundary is inclusive for both kinds of time; not_before 0 (a prepare's first frame)
     * takes any frame, a negative one too. */
    {
        frame_time t = { 100 * MS, 1 };
        CHECK(frame_is_new_enough(t, 100 * MS));
        CHECK(!frame_is_new_enough(t, 100 * MS + 1));
        CHECK(frame_is_new_enough(t, 0));
        CHECK(frame_is_new_enough(t, INT64_MIN));
        CHECK(!frame_is_new_enough(t, INT64_MAX));
        t.from_meta = 0;
        CHECK(frame_is_new_enough(t, 100 * MS - RECEIVE_MARGIN_NS));
        CHECK(!frame_is_new_enough(t, 100 * MS - RECEIVE_MARGIN_NS + 1));
        CHECK(!frame_is_new_enough(t, 100 * MS));
        CHECK(frame_is_new_enough(t, 0));
        /* Received at 10 ms: shown "before" 0, so even not_before 0 waits... */
        t.pts_ns = 10 * MS;
        CHECK(!frame_is_new_enough(t, 0));
        /* ...but a header PTS at 10 ms is taken. */
        t.from_meta = 1;
        CHECK(frame_is_new_enough(t, 0));
    }

    if (failures == 0) {
        printf("test_frame_time: all checks passed\n");
    }
    return failures == 0 ? 0 : 1;
}
