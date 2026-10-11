#define _GNU_SOURCE
#include "hidercap.h"

#include <errno.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

#include <pipewire/pipewire.h>
#include <spa/param/video/format-utils.h>
#include <spa/param/buffers.h>
#include <spa/param/param.h>
#include <spa/buffer/meta.h>
#include <spa/pod/builder.h>

static struct {
    struct pw_thread_loop *loop;
    struct pw_context *context;
    struct pw_core *core;
    struct pw_stream *stream;
    struct spa_hook stream_listener;
    struct spa_video_info_raw format;
    int has_format;
    int error;

    uint8_t *frame; /* latest frame, BGRA, width * 4 bytes per row */
    size_t frame_capacity;
    int32_t width;
    int32_t height;
    int64_t pts_ns;
    int pts_from_meta;
    uint64_t sequence;
} g;

static int64_t now_ns(void)
{
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (int64_t)ts.tv_sec * 1000000000LL + ts.tv_nsec;
}

static void copy_frame(const uint8_t *src, int32_t src_stride, int32_t width, int32_t height, uint32_t format)
{
    size_t needed = (size_t)width * (size_t)height * 4;
    if (needed > g.frame_capacity) {
        uint8_t *grown = realloc(g.frame, needed);
        if (grown == NULL) {
            g.error = HIDERCAP_STREAM_ERROR;
            return;
        }
        g.frame = grown;
        g.frame_capacity = needed;
    }
    int swap_rb = format == SPA_VIDEO_FORMAT_RGBx || format == SPA_VIDEO_FORMAT_RGBA;
    for (int32_t y = 0; y < height; y++) {
        const uint8_t *s = src + (size_t)y * (size_t)src_stride;
        uint8_t *d = g.frame + (size_t)y * (size_t)width * 4;
        if (!swap_rb) {
            memcpy(d, s, (size_t)width * 4);
            for (int32_t x = 0; x < width; x++) {
                d[x * 4 + 3] = 255; /* screen content is opaque; BGRx carries garbage in the 4th byte */
            }
        } else {
            for (int32_t x = 0; x < width; x++) {
                d[x * 4 + 0] = s[x * 4 + 2];
                d[x * 4 + 1] = s[x * 4 + 1];
                d[x * 4 + 2] = s[x * 4 + 0];
                d[x * 4 + 3] = 255;
            }
        }
    }
    g.width = width;
    g.height = height;
}

static void on_process(void *data)
{
    (void)data;
    struct pw_buffer *latest = NULL;
    struct pw_buffer *b;
    /* Only the newest buffer matters; return older ones right away. */
    while ((b = pw_stream_dequeue_buffer(g.stream)) != NULL) {
        if (latest != NULL) {
            pw_stream_queue_buffer(g.stream, latest);
        }
        latest = b;
    }
    if (latest == NULL) {
        return;
    }
    struct spa_buffer *buf = latest->buffer;
    struct spa_data *d = &buf->datas[0];
    if (g.has_format && d->data != NULL && d->chunk != NULL && d->chunk->size > 0
        && (d->chunk->flags & SPA_CHUNK_FLAG_CORRUPTED) == 0) {
        struct spa_meta_header *header = spa_buffer_find_meta_data(buf, SPA_META_Header, sizeof(*header));
        int64_t pts = now_ns();
        int from_meta = 0;
        if (header != NULL && header->pts > 0 && (header->flags & SPA_META_HEADER_FLAG_CORRUPTED) == 0) {
            pts = header->pts;
            from_meta = 1;
        }
        int32_t stride = d->chunk->stride > 0 ? d->chunk->stride : (int32_t)g.format.size.width * 4;
        const uint8_t *src = (const uint8_t *)d->data + d->chunk->offset;
        copy_frame(src, stride, (int32_t)g.format.size.width, (int32_t)g.format.size.height, g.format.format);
        g.pts_ns = pts;
        g.pts_from_meta = from_meta;
        g.sequence++;
        pw_thread_loop_signal(g.loop, false);
    }
    pw_stream_queue_buffer(g.stream, latest);
}

static void on_state_changed(void *data, enum pw_stream_state old, enum pw_stream_state state, const char *error)
{
    (void)data;
    (void)old;
    (void)error;
    if (state == PW_STREAM_STATE_ERROR || state == PW_STREAM_STATE_UNCONNECTED) {
        g.error = HIDERCAP_STREAM_ERROR;
        pw_thread_loop_signal(g.loop, false);
    }
}

static void on_param_changed(void *data, uint32_t id, const struct spa_pod *param)
{
    (void)data;
    if (param == NULL || id != SPA_PARAM_Format) {
        return;
    }
    uint32_t media_type, media_subtype;
    if (spa_format_parse(param, &media_type, &media_subtype) < 0
        || media_type != SPA_MEDIA_TYPE_video || media_subtype != SPA_MEDIA_SUBTYPE_raw) {
        return;
    }
    if (spa_format_video_raw_parse(param, &g.format) < 0) {
        return;
    }
    g.has_format = 1;

    /* Ask for CPU-mappable buffers and the header meta that carries the frame timestamp. */
    uint8_t buffer[1024];
    struct spa_pod_builder builder = SPA_POD_BUILDER_INIT(buffer, sizeof(buffer));
    const struct spa_pod *params[2];
    params[0] = spa_pod_builder_add_object(&builder,
        SPA_TYPE_OBJECT_ParamBuffers, SPA_PARAM_Buffers,
        SPA_PARAM_BUFFERS_buffers, SPA_POD_CHOICE_RANGE_Int(4, 2, 16),
        SPA_PARAM_BUFFERS_dataType, SPA_POD_CHOICE_FLAGS_Int((1 << SPA_DATA_MemFd) | (1 << SPA_DATA_MemPtr)));
    params[1] = spa_pod_builder_add_object(&builder,
        SPA_TYPE_OBJECT_ParamMeta, SPA_PARAM_Meta,
        SPA_PARAM_META_type, SPA_POD_Id(SPA_META_Header),
        SPA_PARAM_META_size, SPA_POD_Int(sizeof(struct spa_meta_header)));
    pw_stream_update_params(g.stream, params, 2);
}

static const struct pw_stream_events stream_events = {
    PW_VERSION_STREAM_EVENTS,
    .state_changed = on_state_changed,
    .param_changed = on_param_changed,
    .process = on_process,
};

int hidercap_start(int fd, uint32_t node_id)
{
    if (g.loop != NULL) {
        close(fd);
        return HIDERCAP_ALREADY_STARTED;
    }
    pw_init(NULL, NULL);
    memset(&g, 0, sizeof(g));
    g.loop = pw_thread_loop_new("hidercap", NULL);
    if (g.loop == NULL) {
        close(fd);
        return HIDERCAP_STREAM_ERROR;
    }
    g.context = pw_context_new(pw_thread_loop_get_loop(g.loop), NULL, 0);
    if (g.context == NULL || pw_thread_loop_start(g.loop) < 0) {
        close(fd);
        hidercap_stop();
        return HIDERCAP_STREAM_ERROR;
    }

    pw_thread_loop_lock(g.loop);
    g.core = pw_context_connect_fd(g.context, fd, NULL, 0);
    if (g.core == NULL) {
        pw_thread_loop_unlock(g.loop);
        hidercap_stop();
        return HIDERCAP_STREAM_ERROR;
    }
    g.stream = pw_stream_new(g.core, "hidercap",
        pw_properties_new(PW_KEY_MEDIA_TYPE, "Video", PW_KEY_MEDIA_CATEGORY, "Capture", PW_KEY_MEDIA_ROLE, "Screen", NULL));
    if (g.stream == NULL) {
        pw_thread_loop_unlock(g.loop);
        hidercap_stop();
        return HIDERCAP_STREAM_ERROR;
    }
    pw_stream_add_listener(g.stream, &g.stream_listener, &stream_events, NULL);

    /* Raw BGR(A)/RGB(A) without modifiers, so the compositor sends shared memory instead of DMA-BUF. */
    uint8_t buffer[1024];
    struct spa_pod_builder builder = SPA_POD_BUILDER_INIT(buffer, sizeof(buffer));
    const struct spa_pod *params[1];
    params[0] = spa_pod_builder_add_object(&builder,
        SPA_TYPE_OBJECT_Format, SPA_PARAM_EnumFormat,
        SPA_FORMAT_mediaType, SPA_POD_Id(SPA_MEDIA_TYPE_video),
        SPA_FORMAT_mediaSubtype, SPA_POD_Id(SPA_MEDIA_SUBTYPE_raw),
        SPA_FORMAT_VIDEO_format, SPA_POD_CHOICE_ENUM_Id(5, SPA_VIDEO_FORMAT_BGRx, SPA_VIDEO_FORMAT_BGRx,
            SPA_VIDEO_FORMAT_BGRA, SPA_VIDEO_FORMAT_RGBx, SPA_VIDEO_FORMAT_RGBA),
        SPA_FORMAT_VIDEO_size, SPA_POD_CHOICE_RANGE_Rectangle(&SPA_RECTANGLE(1920, 1080), &SPA_RECTANGLE(1, 1), &SPA_RECTANGLE(16384, 16384)),
        SPA_FORMAT_VIDEO_framerate, SPA_POD_CHOICE_RANGE_Fraction(&SPA_FRACTION(60, 1), &SPA_FRACTION(0, 1), &SPA_FRACTION(1000, 1)));

    int res = pw_stream_connect(g.stream, PW_DIRECTION_INPUT, node_id,
        PW_STREAM_FLAG_AUTOCONNECT | PW_STREAM_FLAG_MAP_BUFFERS, params, 1);
    pw_thread_loop_unlock(g.loop);
    if (res < 0) {
        hidercap_stop();
        return HIDERCAP_STREAM_ERROR;
    }
    return HIDERCAP_OK;
}

int hidercap_grab_after(int64_t not_before_ns, int32_t timeout_ms, uint8_t *dst, int32_t dst_size, hidercap_frame *out)
{
    if (g.loop == NULL || out == NULL) {
        return HIDERCAP_NOT_STARTED;
    }
    pw_thread_loop_lock(g.loop);
    struct timespec deadline;
    pw_thread_loop_get_time(g.loop, &deadline, (int64_t)timeout_ms * SPA_NSEC_PER_MSEC);
    int result = HIDERCAP_OK;
    while (g.error == 0 && (g.sequence == 0 || g.pts_ns < not_before_ns)) {
        if (pw_thread_loop_timed_wait_full(g.loop, &deadline) != 0) {
            result = HIDERCAP_TIMEOUT;
            break;
        }
    }
    if (g.error != 0) {
        result = g.error;
    }
    out->sequence = g.sequence;
    if (result == HIDERCAP_OK) {
        out->width = g.width;
        out->height = g.height;
        out->stride = g.width * 4;
        out->pts_ns = g.pts_ns;
        out->pts_from_meta = g.pts_from_meta;
        size_t needed = (size_t)g.width * (size_t)g.height * 4;
        if (dst == NULL || (size_t)dst_size < needed) {
            result = HIDERCAP_BUFFER_TOO_SMALL;
        } else {
            memcpy(dst, g.frame, needed);
        }
    }
    pw_thread_loop_unlock(g.loop);
    return result;
}

uint64_t hidercap_last_sequence(void)
{
    if (g.loop == NULL) {
        return 0;
    }
    pw_thread_loop_lock(g.loop);
    uint64_t sequence = g.sequence;
    pw_thread_loop_unlock(g.loop);
    return sequence;
}

void hidercap_stop(void)
{
    if (g.loop != NULL) {
        pw_thread_loop_stop(g.loop);
    }
    if (g.stream != NULL) {
        pw_stream_destroy(g.stream);
    }
    if (g.core != NULL) {
        pw_core_disconnect(g.core);
    }
    if (g.context != NULL) {
        pw_context_destroy(g.context);
    }
    if (g.loop != NULL) {
        pw_thread_loop_destroy(g.loop);
    }
    free(g.frame);
    memset(&g, 0, sizeof(g));
}
