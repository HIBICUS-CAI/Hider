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

    if (failures == 0) {
        printf("test_frame_time: all checks passed\n");
    }
    return failures == 0 ? 0 : 1;
}
